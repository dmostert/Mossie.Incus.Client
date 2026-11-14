using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Serialization;

namespace Mossie.Incus.Client.SimpleStreams;

/// <summary>
/// SimpleStreams image metadata repository. Mirrors the Go <c>shared/simplestreams.SimpleStreams</c> type.
/// </summary>
internal sealed class SimpleStreamsRepository
{
    private const string IndexPath = "streams/v1/index.json";
    private const string NameLayout = "yyyyMMdd";
    private const string EolLayout = "yyyy-MM-dd";

    private readonly HttpClient? _http;
    private readonly string _url;
    private readonly string? _userAgent;
    private readonly string? _cachePath;
    private readonly TimeSpan _cacheExpiry;
    private readonly Dictionary<string, SimpleStreamsProducts> _cachedProducts = new();
    private SimpleStreamsIndex? _cachedStream;
    private List<Image>? _cachedImages;
    private List<ExtendedAlias>? _cachedAliases;

    internal SimpleStreamsRepository(string url, HttpClient? http, string? userAgent, string? cachePath, TimeSpan cacheExpiry)
    {
        _url = url.TrimEnd('/');
        _http = http;
        _userAgent = userAgent;
        _cachePath = cachePath;
        _cacheExpiry = cacheExpiry == TimeSpan.Zero ? TimeSpan.FromHours(1) : cacheExpiry;
    }

    internal void InvalidateCache()
    {
        if (string.IsNullOrEmpty(_cachePath))
            return;

        try
        {
            Directory.Delete(_cachePath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private (byte[]? Body, bool Expired) ReadCache(string fileName)
    {
        if (string.IsNullOrEmpty(_cachePath))
            return (null, false);

        string cacheName = Path.Combine(_cachePath, fileName);
        if (!File.Exists(cacheName))
            return (null, false);

        try
        {
            FileInfo info = new(cacheName);
            byte[] body = File.ReadAllBytes(cacheName);
            bool expired = DateTimeOffset.UtcNow - info.LastWriteTimeUtc > _cacheExpiry;
            return (body, expired);
        }
        catch (IOException)
        {
            TryDelete(cacheName);
            return (null, false);
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(cacheName);
            return (null, false);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task<byte[]> CachedDownloadAsync(string path, CancellationToken cancellationToken)
    {
        string fileName = path.Split('/')[^1];

        if (_http is null)
        {
            byte[] body = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (body.Length == 0)
                throw new IncusException($"Empty index file \"{path}\"");

            return body;
        }

        (byte[]? cachedBody, bool expired) = ReadCache(fileName);
        if (cachedBody is not null && !expired)
            return cachedBody;

        string uri = JoinPath(_url, path);
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        if (!string.IsNullOrEmpty(_userAgent))
            request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);

        try
        {
            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                if (cachedBody is not null)
                    return cachedBody;

                throw new IncusException($"Unable to fetch {uri}: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (body.Length == 0)
                throw new IncusException($"No content in download from \"{uri}\"");

            if (!string.IsNullOrEmpty(_cachePath))
            {
                string cacheName = Path.Combine(_cachePath, fileName);
                TryDelete(cacheName);
                try
                {
                    await File.WriteAllBytesAsync(cacheName, body, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return body;
        }
        catch (HttpRequestException) when (cachedBody is not null)
        {
            return cachedBody;
        }
        catch (TaskCanceledException) when (cachedBody is not null && !cancellationToken.IsCancellationRequested)
        {
            return cachedBody;
        }
    }

    private async Task<SimpleStreamsIndex> ParseStreamAsync(CancellationToken cancellationToken)
    {
        if (_cachedStream is not null)
            return _cachedStream;

        byte[] body = await CachedDownloadAsync(IndexPath, cancellationToken).ConfigureAwait(false);
        string pathUrl = JoinPath(_url, IndexPath);

        SimpleStreamsIndex stream;
        try
        {
            stream = JsonSerializer.Deserialize<SimpleStreamsIndex>(body, IncusJson.Options)
                ?? throw new IncusException($"Failed decoding stream JSON from \"{pathUrl}\": empty document");
        }
        catch (JsonException ex)
        {
            throw new IncusException($"Failed decoding stream JSON from \"{pathUrl}\": {ex.Message}");
        }

        _cachedStream = stream;
        return stream;
    }

    private async Task<SimpleStreamsProducts> ParseProductsAsync(string path, CancellationToken cancellationToken)
    {
        if (_cachedProducts.TryGetValue(path, out SimpleStreamsProducts? cached))
            return cached;

        byte[] body = await CachedDownloadAsync(path, cancellationToken).ConfigureAwait(false);

        SimpleStreamsProducts products;
        try
        {
            products = JsonSerializer.Deserialize<SimpleStreamsProducts>(body, IncusJson.Options)
                ?? throw new IncusException($"Failed decoding products JSON from \"{path}\": empty document");
        }
        catch (JsonException ex)
        {
            throw new IncusException($"Failed decoding products JSON from \"{path}\": {ex.Message}");
        }

        _cachedProducts[path] = products;
        return products;
    }

    private sealed class ExtendedAlias
    {
        internal required string Name { get; init; }

        internal required ImageAliasesEntry Alias { get; init; }

        internal required string Type { get; init; }

        internal required string Architecture { get; init; }
    }

    private async Task<(List<Image> Images, List<ExtendedAlias> Aliases)> GetImagesAsync(CancellationToken cancellationToken)
    {
        if (_cachedImages is not null && _cachedAliases is not null)
            return (_cachedImages, _cachedAliases);

        List<Image> images = new();

        SimpleStreamsIndex stream;
        try
        {
            stream = await ParseStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not IncusException and not OperationCanceledException)
        {
            throw new IncusException($"Failed parsing stream: {ex.Message}");
        }

        foreach (SimpleStreamsIndexEntry entry in stream.Index.Values)
        {
            if (entry.DataType != "image-downloads")
                continue;

            if (entry.Products.Count == 0)
                continue;

            SimpleStreamsProducts products;
            try
            {
                products = await ParseProductsAsync(entry.Path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not IncusException and not OperationCanceledException)
            {
                throw new IncusException($"Failed parsing products: {ex.Message}");
            }

            images.AddRange(ToApi(products).Images);
        }

        (List<Image> aliasedImages, List<ExtendedAlias> aliases) = ApplyAliases(images);

        _cachedImages = aliasedImages;
        _cachedAliases = aliases;

        return (aliasedImages, aliases);
    }

    internal async Task<List<Image>> ListImagesAsync(CancellationToken cancellationToken)
    {
        (List<Image> images, _) = await GetImagesAsync(cancellationToken).ConfigureAwait(false);
        return images;
    }

    internal async Task<List<ImageAliasesEntry>> ListAliasesAsync(CancellationToken cancellationToken)
    {
        List<ExtendedAlias> aliasesList = (await GetImagesAsync(cancellationToken).ConfigureAwait(false)).Aliases;
        aliasesList.Sort(CompareAliases);

        List<ImageAliasesEntry> aliases = new();
        foreach (ExtendedAlias entry in aliasesList)
        {
            bool duplicate = aliases.Any(v => v.Name == entry.Name && v.Type == entry.Type);
            if (!duplicate)
                aliases.Add(entry.Alias);
        }

        return aliases;
    }

    internal async Task<ImageAliasesEntry?> GetAliasAsync(
        string imageType,
        string name,
        CancellationToken cancellationToken)
    {
        List<ExtendedAlias> aliasesList = (await GetImagesAsync(cancellationToken).ConfigureAwait(false)).Aliases;
        aliasesList.Sort(CompareAliases);

        ImageAliasesEntry? match = null;
        foreach (ExtendedAlias entry in aliasesList)
        {
            if (entry.Name != name)
                continue;

            if (entry.Type != imageType && imageType != "")
                continue;

            if (match is not null)
            {
                if (match.Type != entry.Type)
                    throw new IncusException($"More than one match for alias '{name}'");

                continue;
            }

            match = entry.Alias;
        }

        if (match is null)
            throw new IncusException($"Alias '{name}' doesn't exist");

        return match;
    }

    internal async Task<Dictionary<string, ImageAliasesEntry>> GetAliasArchitecturesAsync(
        string imageType,
        string name,
        CancellationToken cancellationToken)
    {
        Dictionary<string, ImageAliasesEntry> aliases = new();
        List<ExtendedAlias> aliasesList = (await GetImagesAsync(cancellationToken).ConfigureAwait(false)).Aliases;

        foreach (ExtendedAlias entry in aliasesList)
        {
            if (entry.Name != name)
                continue;

            if (entry.Type != imageType && imageType != "")
                continue;

            if (aliases.ContainsKey(entry.Architecture))
                throw new IncusException($"More than one match for alias '{name}'");

            aliases[entry.Architecture] = entry.Alias;
        }

        if (aliases.Count == 0)
            throw new IncusException($"Alias '{name}' doesn't exist");

        return aliases;
    }

    internal async Task<Image> GetImageAsync(string fingerprint, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(fingerprint))
            throw new IncusException("The fingerprint cannot be empty");

        List<Image> images = await ListImagesAsync(cancellationToken).ConfigureAwait(false);

        List<Image> matches = images
            .Where(image => image.Fingerprint.StartsWith(fingerprint, StringComparison.Ordinal))
            .ToList();

        return matches.Count switch
        {
            0 => throw new IncusException("The requested image couldn't be found"),
            > 1 => throw new IncusException("More than one match for the provided partial fingerprint"),
            _ => matches[0],
        };
    }

    internal async Task<Dictionary<string, SimpleStreamsDownloadableFile>> GetFilesAsync(
        string fingerprint,
        CancellationToken cancellationToken)
    {
        SimpleStreamsIndex stream = await ParseStreamAsync(cancellationToken).ConfigureAwait(false);

        foreach (SimpleStreamsIndexEntry entry in stream.Index.Values)
        {
            if (entry.DataType != "image-downloads")
                continue;

            if (entry.Products.Count == 0)
                continue;

            SimpleStreamsProducts products = await ParseProductsAsync(entry.Path, cancellationToken).ConfigureAwait(false);
            (List<Image> images, Dictionary<string, List<List<string>>> downloads) = ToApi(products);

            foreach (Image image in images)
            {
                if (!image.Fingerprint.StartsWith(fingerprint, StringComparison.Ordinal))
                    continue;

                Dictionary<string, SimpleStreamsDownloadableFile> files = new();
                foreach (List<string> path in downloads[image.Fingerprint])
                {
                    if (path.Count < 4)
                        throw new IncusException($"Invalid path content: \"{string.Join(",", path)}\"");

                    long size = long.Parse(path[3], System.Globalization.CultureInfo.InvariantCulture);
                    files[path[2]] = new SimpleStreamsDownloadableFile
                    {
                        Path = path[0],
                        Sha256 = path[1],
                        Size = size,
                    };
                }

                return files;
            }
        }

        throw new IncusException("Couldn't find the requested image");
    }

    private (List<Image> Images, Dictionary<string, List<List<string>>> Downloads) ToApi(SimpleStreamsProducts products)
    {
        Dictionary<string, List<List<string>>> downloads = new();
        List<Image> images = new();

        foreach (SimpleStreamsProduct product in products.Products.Values)
        {
            string architectureName;
            try
            {
                architectureName = IncusArchitecture.Name(IncusArchitecture.Id(product.Architecture));
            }
            catch (ArgumentException)
            {
                continue;
            }

            foreach ((string name, SimpleStreamsProductVersion version) in product.Versions)
            {
                if (name.Length < 8)
                    continue;

                if (!DateTime.TryParseExact(
                        name[..8], NameLayout, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out DateTime creationDate))
                {
                    continue;
                }

                DateTimeOffset createdAt = new(DateTime.SpecifyKind(creationDate, DateTimeKind.Utc));

                bool AddImage(SimpleStreamsProductVersionItem meta, SimpleStreamsProductVersionItem? root)
                {
                    List<SimpleStreamsProductVersionItem> deltas = new();
                    if (root is not null && root.FileType is "squashfs" or "disk-kvm.img")
                    {
                        foreach (SimpleStreamsProductVersionItem item in version.Items.Values)
                        {
                            if (item.FileType == $"{root.FileType}.vcdiff")
                                deltas.Add(item);
                        }
                    }

                    string fingerprint;
                    if (root is not null)
                    {
                        fingerprint = root.FileType switch
                        {
                            "root.tar.xz" => !string.IsNullOrEmpty(meta.CombinedSha256RootXz)
                                ? meta.CombinedSha256RootXz
                                : meta.CombinedSha256,
                            "squashfs" => meta.CombinedSha256SquashFs,
                            "disk-kvm.img" => meta.CombinedSha256DiskKvmImg,
                            "disk1.img" => meta.CombinedSha256DiskImg,
                            "uefi1.img" => meta.CombinedSha256DiskUefiImg,
                            _ => "",
                        };
                    }
                    else
                    {
                        fingerprint = meta.HashSha256 ?? "";
                    }

                    if (string.IsNullOrEmpty(fingerprint))
                        return false;

                    long size = meta.Size;
                    if (root is not null)
                        size += root.Size;

                    if (string.IsNullOrEmpty(meta.Path))
                        return false;

                    string[] fields = meta.Path.Split('/');
                    string filename = fields[^1];

                    string description = $"{product.OperatingSystem} {product.ReleaseTitle} {product.Architecture}";
                    if (!string.IsNullOrEmpty(product.Variant) && product.Variant != "default")
                        description = $"{description} ({product.Variant})";

                    if (!string.IsNullOrEmpty(version.Label))
                        description = $"{description} ({version.Label})";

                    description = $"{description} ({name})";

                    Image image = new()
                    {
                        Architecture = architectureName,
                        Public = true,
                        Size = size,
                        CreatedAt = createdAt,
                        UploadedAt = createdAt,
                        Filename = filename,
                        Fingerprint = fingerprint,
                        Properties = new Dictionary<string, string>
                        {
                            ["os"] = product.OperatingSystem,
                            ["release"] = product.Release,
                            ["version"] = product.Version ?? "",
                            ["architecture"] = product.Architecture,
                            ["label"] = version.Label ?? "",
                            ["serial"] = name,
                            ["description"] = description,
                        },
                    };

                    if (product.Requirements is not null)
                    {
                        foreach ((string key, string value) in product.Requirements)
                            image.Properties[$"requirements.{key}"] = value;
                    }

                    if (!string.IsNullOrEmpty(product.Variant))
                        image.Properties["variant"] = product.Variant;

                    image.Type = "container";

                    if (root is not null)
                    {
                        image.Properties["type"] = root.FileType;
                        if (root.FileType is "disk1.img" or "disk-kvm.img" or "uefi1.img")
                            image.Type = "virtual-machine";
                    }
                    else
                    {
                        image.Properties["type"] = "tar.gz";
                        if (meta.CombinedType == "virtual-machine")
                            image.Type = "virtual-machine";
                    }

                    foreach (string key in image.Properties.Where(pair => pair.Value == "").Select(pair => pair.Key).ToList())
                        image.Properties.Remove(key);

                    if (!string.IsNullOrEmpty(product.Aliases))
                    {
                        image.Aliases = product.Aliases
                            .Split(',')
                            .Select(entry => new ImageAlias { Name = entry })
                            .ToList();
                    }

                    image.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(0);
                    if (!string.IsNullOrEmpty(product.SupportedEol)
                        && DateTime.TryParseExact(
                            product.SupportedEol, EolLayout, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                            out DateTime eolDate))
                    {
                        image.ExpiresAt = new DateTimeOffset(DateTime.SpecifyKind(eolDate, DateTimeKind.Utc));
                    }

                    List<List<string>> imageDownloads;
                    if (root is null)
                    {
                        imageDownloads = [new() { meta.Path, meta.HashSha256 ?? "", "meta", meta.Size.ToString(System.Globalization.CultureInfo.InvariantCulture) }];
                    }
                    else
                    {
                        imageDownloads =
                        [
                            new() { meta.Path, meta.HashSha256 ?? "", "meta", meta.Size.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                            new() { root.Path, root.HashSha256 ?? "", "root", root.Size.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                        ];
                    }

                    foreach (SimpleStreamsProductVersionItem delta in deltas)
                    {
                        if (string.IsNullOrEmpty(delta.DeltaBase))
                            continue;

                        if (!product.Versions.TryGetValue(delta.DeltaBase, out SimpleStreamsProductVersion? sourceImage))
                            continue;

                        string? sourceFingerprint = null;
                        foreach (SimpleStreamsProductVersionItem item in sourceImage.Items.Values)
                        {
                            if (item.FileType != "incus.tar.xz")
                                continue;

                            sourceFingerprint = item.CombinedSha256SquashFs;
                            break;
                        }

                        if (string.IsNullOrEmpty(sourceFingerprint))
                            continue;

                        imageDownloads.Add(
                        [
                            delta.Path,
                            delta.HashSha256 ?? "",
                            $"root.delta-{sourceFingerprint}",
                            delta.Size.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ]);
                    }

                    downloads[fingerprint] = imageDownloads;
                    images.Add(image);

                    return true;
                }

                foreach (SimpleStreamsProductVersionItem item in version.Items.Values)
                {
                    if (item.FileType == "incus_combined.tar.gz")
                        AddImage(item, null);

                    if (item.FileType == "incus.tar.xz")
                    {
                        foreach (SimpleStreamsProductVersionItem subItem in version.Items.Values)
                        {
                            if (subItem.FileType is "disk1.img" or "disk-kvm.img" or "uefi1.img" or "root.tar.xz" or "squashfs")
                                AddImage(item, subItem);
                        }
                    }
                }
            }
        }

        return (images, downloads);
    }

    private static (List<Image> Images, List<ExtendedAlias> Aliases) ApplyAliases(List<Image> images)
    {
        List<ExtendedAlias> aliasesList = new();
        images.Sort(CompareImages);

        ImageAlias? AddAlias(string imageType, string architecture, string name, string fingerprint)
        {
            foreach (ExtendedAlias entry in aliasesList)
            {
                if (entry.Name == name && entry.Type == imageType && entry.Architecture == architecture)
                    return null;
            }

            ImageAliasesEntry alias = new()
            {
                Name = name,
                Target = fingerprint,
                Type = imageType,
            };

            aliasesList.Add(new ExtendedAlias
            {
                Name = name,
                Type = imageType,
                Alias = alias,
                Architecture = architecture,
            });

            return new ImageAlias { Name = name };
        }

        string localArchitecture = IncusArchitecture.LocalName();

        List<Image> newImages = new();
        foreach (Image image in images)
        {
            if (image.Aliases is not null)
            {
                List<ImageAlias> aliases = image.Aliases;
                image.Aliases = null;

                foreach (ImageAlias entry in aliases)
                {
                    ImageAlias? alias = AddAlias(image.Type, image.Architecture, entry.Name, image.Fingerprint);
                    if (alias is not null && localArchitecture == image.Architecture)
                    {
                        image.Aliases ??= new List<ImageAlias>();
                        image.Aliases.Add(alias);
                    }

                    string architecture = image.Properties?.TryGetValue("architecture", out string? value) == true
                        ? value
                        : "";

                    alias = AddAlias(image.Type, image.Architecture, $"{entry.Name}/{architecture}", image.Fingerprint);
                    if (alias is not null)
                    {
                        image.Aliases ??= new List<ImageAlias>();
                        image.Aliases.Add(alias);
                    }
                }
            }

            newImages.Add(image);
        }

        return (newImages, aliasesList);
    }

    private static int CompareImages(Image a, Image b)
    {
        string Prop(Image image, string key)
            => image.Properties?.TryGetValue(key, out string? value) == true ? value : "";

        foreach (string prop in (string[])["os", "release", "variant", "serial", "architecture"])
        {
            string aValue = Prop(a, prop);
            string bValue = Prop(b, prop);
            if (aValue == bValue)
                continue;

            if (aValue == "")
                return 1;

            if (bValue == "")
                return -1;

            return prop == "serial"
                ? string.CompareOrdinal(bValue, aValue)
                : string.CompareOrdinal(aValue, bValue);
        }

        string aType = Prop(a, "type");
        string bType = Prop(b, "type");
        if (aType != bType)
        {
            string[] preferences = ["squashfs", "root.tar.xz", "disk-kvm.img", "uefi1.img", "disk1.img"];
            int iScore = Array.IndexOf(preferences, aType);
            int jScore = Array.IndexOf(preferences, bType);
            if (iScore < 0)
                iScore = int.MaxValue;
            if (jScore < 0)
                jScore = int.MaxValue;

            return iScore.CompareTo(jScore);
        }

        return 0;
    }

    private static int CompareAliases(ExtendedAlias a, ExtendedAlias b)
    {
        string nativeName = IncusArchitecture.LocalName();

        static bool IsNative(string architecture, string nativeName) => architecture == nativeName;

        static bool IsPersonality(string architecture, string nativeName)
        {
            try
            {
                int archId = IncusArchitecture.Id(nativeName);
                foreach (int personality in IncusArchitecture.SupportedPersonalities(archId))
                {
                    if (IncusArchitecture.Name(personality) == architecture)
                        return true;
                }
            }
            catch (ArgumentException)
            {
            }

            return false;
        }

        if (a.Architecture == b.Architecture)
            return 0;

        if (IsNative(a.Architecture, nativeName))
            return -1;

        if (IsPersonality(a.Architecture, nativeName) && !IsNative(b.Architecture, nativeName))
            return -1;

        return 1;
    }

    internal static string JoinPath(string baseHost, string path)
    {
        if (path.StartsWith('/'))
        {
            Uri baseUri = new(baseHost.TrimEnd('/') + "/");
            UriBuilder builder = new(baseUri)
            {
                Path = path,
            };

            return builder.Uri.ToString();
        }

        return new Uri(baseHost.TrimEnd('/') + "/" + path).ToString();
    }
}
