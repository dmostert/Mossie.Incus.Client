using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;
using Mossie.Incus.Client.Serialization;

namespace Mossie.Incus.Client;

/// <summary>
/// Client for an OCI image registry. Mirrors the Go <c>incus.ProtocolOCI</c> client.
/// Requires <c>skopeo</c> (and <c>umoci</c> for downloading images) on the local system.
/// </summary>
public sealed class OciClient : IImageServer, IAsyncDisposable, IDisposable
{
    private const string Skopeo = "skopeo";

    private sealed class OciInfo
    {
        internal string Alias { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("Name")]
        internal string Name { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("Digest")]
        internal string Digest { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("Created")]
        internal DateTimeOffset Created { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("Architecture")]
        internal string Architecture { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("Layers")]
        internal List<string> Layers { get; set; } = new();

        [System.Text.Json.Serialization.JsonPropertyName("LayersData")]
        internal List<OciLayerData> LayersData { get; set; } = new();

        [System.Text.Json.Serialization.JsonPropertyName("Labels")]
        internal Dictionary<string, string> Labels { get; set; } = new();
    }

    private sealed class OciLayerData
    {
        [System.Text.Json.Serialization.JsonPropertyName("Size")]
        internal long Size { get; set; }
    }

    private readonly HttpClient _http;
    private readonly string _host;
    private readonly string _certificate;
    private readonly string? _tempPath;
    private readonly Dictionary<string, OciInfo> _cache = new();
    private readonly Dictionary<string, Exception> _errors = new();

    private OciClient(HttpClient http, string host, string certificate, string? tempPath)
    {
        _http = http;
        _host = host;
        _certificate = certificate;
        _tempPath = tempPath;
    }

    /// <summary>Connects to a remote OCI registry over HTTPS.</summary>
    public static Task<OciClient> ConnectAsync(
        string uri,
        IncusConnectionArgs? args = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        args ??= new IncusConnectionArgs();
        string host = uri.TrimEnd('/');

        (HttpMessageHandler handler, _, _) = IncusTransports.CreateHttpsHandler(args);
        Uri baseUri = new(host + "/");
        HttpClient http = IncusTransports.CreateClient(args, handler, baseUri);

        return Task.FromResult(new OciClient(http, host, args.TlsServerCert ?? "", args.TempPath));
    }

    /// <summary>Releases the HTTP client used by this connection.</summary>
    public void Dispose() => _http.Dispose();

    /// <summary>Releases the HTTP client used by this connection.</summary>
    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Returns the HTTP client used by this connection.</summary>
    public HttpClient GetHttpClient() => _http;

    /// <summary>Disconnects from the registry (no-op).</summary>
    public void Disconnect()
    {
    }

    /// <inheritdoc />
    public IncusConnectionInfo GetConnectionInfo() => new()
    {
        Addresses = [_host],
        Certificate = _certificate,
        Protocol = "oci",
        Url = _host,
    };

    /// <inheritdoc />
    public Task<List<Image>> GetImagesAsync(CancellationToken cancellationToken = default)
        => throw new IncusException("Can't list images from OCI registry");

    /// <inheritdoc />
    public Task<List<Image>> GetImagesAllProjectsAsync(CancellationToken cancellationToken = default)
        => throw new IncusException("Can't list images from OCI registry");

    /// <inheritdoc />
    public Task<List<Image>> GetImagesAllProjectsWithFilterAsync(
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
        => throw new IncusException("Can't list images from OCI registry");

    /// <inheritdoc />
    public Task<List<Image>> GetImagesWithFilterAsync(
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
        => throw new IncusException("Can't list images from OCI registry");

    /// <inheritdoc />
    public Task<List<string>> GetImageFingerprintsAsync(CancellationToken cancellationToken = default)
        => throw new IncusException("Can't list images from OCI registry");

    /// <inheritdoc />
    public Task<(Image Image, string ETag)> GetImageAsync(
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        if (!_cache.TryGetValue(fingerprint, out OciInfo? info))
        {
            if (!ExecutableExists(Skopeo))
                throw new IncusException("OCI container handling requires \"skopeo\" be present on the system");

            if (_errors.TryGetValue(fingerprint, out Exception? error))
                throw new IncusException(error.Message, error);

            throw new IncusException("Image not found");
        }

        Image image = new()
        {
            Public = true,
            Properties = new Dictionary<string, string>
            {
                ["architecture"] = info.Architecture,
                ["type"] = "oci",
                ["description"] = $"{info.Name} (OCI)",
                ["id"] = info.Alias,
            },
            Aliases = [new ImageAlias { Name = info.Alias }],
            Architecture = info.Architecture,
            Fingerprint = fingerprint,
            Type = "container",
            CreatedAt = info.Created,
            UploadedAt = info.Created,
        };

        foreach ((string key, string value) in info.Labels)
        {
            const string prefix = "org.opencontainers.image.";
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                image.Properties["oci." + key[prefix.Length..]] = value;
        }

        image.Size = info.LayersData.Sum(layer => layer.Size);

        return Task.FromResult((image, ""));
    }

    /// <inheritdoc />
    public async Task<ImageFileResponse> GetImageFileAsync(
        string fingerprint,
        ImageFileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_cache.TryGetValue(fingerprint, out OciInfo? info))
        {
            if (!ExecutableExists(Skopeo))
                throw new IncusException("OCI container handling requires \"skopeo\" be present on the system");

            if (_errors.TryGetValue(fingerprint, out Exception? error))
                throw new IncusException(error.Message, error);

            throw new IncusException("Image not found");
        }

        if (request.MetaFile is null && request.RootfsFile is null)
            throw new IncusException("No file requested");

        if (!OperatingSystem.IsLinux() || Environment.UserName != "root")
            throw new IncusException("OCI image export currently requires root access");

        string ociPath = CreateTempDirectory("incus-oci-");
        try
        {
            string layoutPath = Path.Combine(ociPath, "oci");
            string imagePath = Path.Combine(ociPath, "image");
            Directory.CreateDirectory(layoutPath);
            Directory.CreateDirectory(imagePath);

            const string imageTag = "latest";

            await RunSkopeoAsync(
                "copy",
                info.Alias,
                cancellationToken,
                "--remove-signatures",
                $"oci:{layoutPath}:{imageTag}").ConfigureAwait(false);

            await UnpackOciImageAsync(layoutPath, imageTag, imagePath, info.Alias, cancellationToken).ConfigureAwait(false);

            ImageMetadata metadata = new()
            {
                Architecture = info.Architecture,
                CreationDate = info.Created.ToUnixTimeSeconds(),
            };

            await File.WriteAllTextAsync(
                Path.Combine(imagePath, "metadata.yaml"), IncusJson.Serialize(metadata), cancellationToken).ConfigureAwait(false);

            ImageFileResponse response = new()
            {
                MetaName = "metadata.tar.gz",
                RootfsName = "rootfs.tar.gz",
            };

            if (request.MetaFile is not null)
            {
                response.MetaSize = await WriteTarGzAsync(
                    request.MetaFile,
                    imagePath,
                    ["config.json", "metadata.yaml"],
                    cancellationToken).ConfigureAwait(false);
            }

            if (request.RootfsFile is not null)
            {
                string rootfsPath = Path.Combine(imagePath, "rootfs");
                response.RootfsSize = await WriteTarGzAsync(
                    request.RootfsFile,
                    rootfsPath,
                    entries: null,
                    cancellationToken).ConfigureAwait(false);
            }

            return response;
        }
        finally
        {
            try
            {
                Directory.Delete(ociPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <inheritdoc />
    public Task<string> GetImageSecretAsync(string fingerprint, CancellationToken cancellationToken = default)
        => throw new IncusException("Private images aren't supported with OCI registry");

    /// <inheritdoc />
    public Task<(Image Image, string ETag)> GetPrivateImageAsync(
        string fingerprint,
        string secret,
        CancellationToken cancellationToken = default)
        => throw new IncusException("Private images aren't supported with OCI registry");

    /// <inheritdoc />
    public Task<ImageFileResponse> GetPrivateImageFileAsync(
        string fingerprint,
        string secret,
        ImageFileRequest request,
        CancellationToken cancellationToken = default)
        => throw new IncusException("Private images aren't supported with OCI registry");

    /// <inheritdoc />
    public Task<List<ImageAliasesEntry>> GetImageAliasesAsync(CancellationToken cancellationToken = default)
        => throw new IncusException("Can't list image aliases from OCI registry");

    /// <inheritdoc />
    public Task<List<string>> GetImageAliasNamesAsync(CancellationToken cancellationToken = default)
        => throw new IncusException("Can't list image aliases from OCI registry");

    /// <inheritdoc />
    public async Task<(ImageAliasesEntry Alias, string ETag)> GetImageAliasAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        // If the image is "IMAGE:TAG@HASH", drop ":TAG" so that skopeo uses the pinned hash instead.
        int hashIndex = name.IndexOf('@');
        if (hashIndex >= 0)
        {
            string imageWithoutHash = name[..hashIndex];
            string hash = name[(hashIndex + 1)..];
            int tagIndex = imageWithoutHash.IndexOf(':');
            string imageWithoutTag = tagIndex >= 0 ? imageWithoutHash[..tagIndex] : imageWithoutHash;
            name = $"{imageWithoutTag}@{hash}";
        }

        string stdout;
        try
        {
            stdout = await RunSkopeoAsync("inspect", name, cancellationToken, "--no-tags").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _errors[name] = ex;
            throw;
        }

        OciInfo info;
        try
        {
            info = JsonSerializer.Deserialize<OciInfo>(stdout, IncusJson.Options)
                ?? throw new IncusException("Invalid skopeo output");
        }
        catch (Exception ex) when (ex is JsonException or IncusException)
        {
            _errors[name] = ex;
            throw new IncusException($"Failed parsing skopeo output: {ex.Message}", ex);
        }

        info.Alias = name;
        info.Digest = ComputeFingerprint(info.Layers);

        try
        {
            info.Architecture = IncusArchitecture.Name(IncusArchitecture.Id(info.Architecture));
        }
        catch (ArgumentException ex)
        {
            _errors[name] = ex;
            throw new IncusException(ex.Message, ex);
        }

        _cache[info.Digest] = info;

        return (new ImageAliasesEntry
        {
            Target = info.Digest,
            Name = name,
            Type = "container",
        }, "");
    }

    /// <inheritdoc />
    public async Task<(ImageAliasesEntry Alias, string ETag)> GetImageAliasTypeAsync(
        string imageType,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (imageType == InstanceTypeExtensions.ToApiString(InstanceType.VirtualMachine))
            throw new IncusException("OCI images are only supported for containers");

        return await GetImageAliasAsync(name, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, ImageAliasesEntry>> GetImageAliasArchitecturesAsync(
        string imageType,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (imageType == InstanceTypeExtensions.ToApiString(InstanceType.VirtualMachine))
            throw new IncusException("OCI images are only supported for containers");

        (ImageAliasesEntry alias, _) = await GetImageAliasAsync(name, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, ImageAliasesEntry>
        {
            [IncusArchitecture.LocalName()] = alias,
        };
    }

    /// <inheritdoc />
    public Task<Operation> ExportImageAsync(
        string fingerprint,
        ImageExportPost image,
        CancellationToken cancellationToken = default)
        => throw new IncusException("Exporting images is not supported with OCI registry");

    private static string ComputeFingerprint(IEnumerable<string> layers)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string layer in layers)
            hash.AppendData(Encoding.UTF8.GetBytes(layer));

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private async Task<string> RunSkopeoAsync(
        string action,
        string image,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        Uri uri = new(_host);
        string? authFile = null;

        List<string> args = new() { "--insecure-policy", action };
        string target = $"docker://{uri.Host}{uri.AbsolutePath.TrimEnd('/')}/{image}";

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            string userInfo = Uri.UnescapeDataString(uri.UserInfo);
            int separator = userInfo.IndexOf(':');
            string username = separator >= 0 ? userInfo[..separator] : userInfo;
            string password = separator >= 0 ? userInfo[(separator + 1)..] : "";

            string credentials = JsonSerializer.Serialize(new
            {
                auths = new Dictionary<string, object>
                {
                    [$"{uri.Scheme}://{uri.Host}"] = new Dictionary<string, string>
                    {
                        ["auth"] = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")),
                    },
                },
            });

            authFile = Path.Combine(_tempPath ?? Path.GetTempPath(), $"incus_client_auth_{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(authFile, credentials, cancellationToken).ConfigureAwait(false);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(authFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        try
        {
            args.Add(target);
            args.AddRange(arguments);
            if (authFile is not null)
                args.Add($"--authfile={authFile}");

            ProcessStartInfo startInfo = new(Skopeo)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            foreach (string arg in args)
                startInfo.ArgumentList.Add(arg);

            string? proxy = Environment.GetEnvironmentVariable("HTTPS_PROXY")
                ?? Environment.GetEnvironmentVariable("https_proxy")
                ?? Environment.GetEnvironmentVariable("HTTP_PROXY")
                ?? Environment.GetEnvironmentVariable("http_proxy");

            if (!string.IsNullOrEmpty(proxy))
            {
                startInfo.Environment["HTTPS_PROXY"] = proxy;
                startInfo.Environment["HTTP_PROXY"] = proxy;
            }

            using Process process = Process.Start(startInfo)
                ?? throw new IncusException("Failed to start skopeo");

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
                throw new IncusException(string.IsNullOrEmpty(stderr) ? "skopeo failed" : stderr.Trim());

            return stdout;
        }
        finally
        {
            if (authFile is not null)
            {
                try
                {
                    File.Delete(authFile);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private async Task UnpackOciImageAsync(
        string layoutPath,
        string imageTag,
        string bundlePath,
        string imageName,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new("umoci")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("unpack");
        startInfo.ArgumentList.Add("--image");
        startInfo.ArgumentList.Add($"{layoutPath}:{imageTag}");
        startInfo.ArgumentList.Add(bundlePath);

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new IncusException("Failed to start umoci");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new IncusException("OCI image export requires \"umoci\" be present on the system", ex);
        }

        using (process)
        {
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            _ = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
                throw new IncusException(string.IsNullOrEmpty(stderr) ? "umoci failed" : stderr.Trim());
        }

        await StripVolumeMountsAsync(bundlePath, imageName, cancellationToken).ConfigureAwait(false);
    }

    private async Task StripVolumeMountsAsync(string bundlePath, string imageName, CancellationToken cancellationToken)
    {
        HashSet<string> volumes;
        try
        {
            string config = await RunSkopeoAsync("inspect", imageName, cancellationToken, "--config").ConfigureAwait(false);
            JsonNode? root = JsonNode.Parse(config);
            JsonNode? volumeNode = root?["config"]?["Volumes"];
            if (volumeNode is not JsonObject volumeObject || volumeObject.Count == 0)
                return;

            volumes = volumeObject.Select(pair => Path.GetFullPath(pair.Key).TrimEnd('/'))
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return;
        }

        string configPath = Path.Combine(bundlePath, "config.json");
        if (!File.Exists(configPath))
            return;

        JsonNode? spec;
        try
        {
            spec = JsonNode.Parse(await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false));
        }
        catch (JsonException)
        {
            return;
        }

        if (spec?["mounts"] is not JsonArray mounts)
            return;

        for (int i = mounts.Count - 1; i >= 0; i--)
        {
            JsonNode? mount = mounts[i];
            if (mount is null)
                continue;

            string type = mount["type"]?.GetValue<string>() ?? "";
            string destination = mount["destination"]?.GetValue<string>() ?? "";
            if (type == "tmpfs" && volumes.Contains(Path.GetFullPath(destination).TrimEnd('/')))
                mounts.RemoveAt(i);
        }

        await File.WriteAllTextAsync(
            configPath,
            spec.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> WriteTarGzAsync(
        Stream target,
        string sourceDirectory,
        IReadOnlyList<string>? entries,
        CancellationToken cancellationToken)
    {
        CountingWriteStream counter = new(target);

        await using (GZipStream gzip = new(counter, CompressionLevel.Optimal, leaveOpen: true))
        {
            await using (TarWriter tar = new(gzip, TarEntryFormat.Pax, leaveOpen: true))
            {
                if (entries is not null)
                {
                    foreach (string entry in entries)
                    {
                        string path = Path.Combine(sourceDirectory, entry);
                        if (File.Exists(path))
                            await tar.WriteEntryAsync(path, entry, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    await WriteDirectoryAsync(tar, new DirectoryInfo(sourceDirectory), "", cancellationToken).ConfigureAwait(false);
                }
            }
        }

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        return counter.BytesWritten;
    }

    private sealed class CountingWriteStream(Stream inner) : Stream
    {
        internal long BytesWritten { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            BytesWritten += buffer.Length;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            BytesWritten += count;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            BytesWritten += buffer.Length;
        }
    }

    private static async Task WriteDirectoryAsync(
        TarWriter tar,
        DirectoryInfo directory,
        string entryPrefix,
        CancellationToken cancellationToken)
    {
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            string entryName = string.IsNullOrEmpty(entryPrefix)
                ? entry.Name
                : $"{entryPrefix}/{entry.Name}";

            if (entry is DirectoryInfo subDirectory)
            {
                await tar.WriteEntryAsync(subDirectory.FullName, entryName, cancellationToken).ConfigureAwait(false);
                await WriteDirectoryAsync(tar, subDirectory, entryName, cancellationToken).ConfigureAwait(false);
            }
            else if (entry is FileInfo file)
            {
                await tar.WriteEntryAsync(file.FullName, entryName, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private string CreateTempDirectory(string prefix)
    {
        string basePath = _tempPath ?? Path.GetTempPath();
        string path = Path.Combine(basePath, prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool ExecutableExists(string name)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return false;

        foreach (string directory in path.Split(Path.PathSeparator))
        {
            if (File.Exists(Path.Combine(directory, name)))
                return true;
        }

        return false;
    }
}
