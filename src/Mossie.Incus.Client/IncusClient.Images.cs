using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;
using Mossie.Incus.Client.Serialization;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns all images.</summary>
    public async Task<List<Api.Image>> GetImagesAsync(CancellationToken cancellationToken = default)
    {
        (List<Api.Image> images, _) = await QueryStructAsync<List<Api.Image>>(
            HttpMethod.Get, "/images?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return images;
    }

    /// <summary>Returns all images across all projects.</summary>
    public async Task<List<Api.Image>> GetImagesAllProjectsAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("images_all_projects");

        (List<Api.Image> images, _) = await QueryStructAsync<List<Api.Image>>(
            HttpMethod.Get, "/images?recursion=1&all-projects=true", cancellationToken: cancellationToken).ConfigureAwait(false);

        return images;
    }

    /// <summary>Returns a filtered list of images across all projects.</summary>
    public async Task<List<Api.Image>> GetImagesAllProjectsWithFilterAsync(
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("images_all_projects");

        string path = $"/images?recursion=1&all-projects=true&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}";
        (List<Api.Image> images, _) = await QueryStructAsync<List<Api.Image>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return images;
    }

    /// <summary>Returns a filtered list of images.</summary>
    public async Task<List<Api.Image>> GetImagesWithFilterAsync(
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("api_filtering");

        string path = $"/images?recursion=1&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}";
        (List<Api.Image> images, _) = await QueryStructAsync<List<Api.Image>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return images;
    }

    /// <summary>Returns the fingerprints of all available images.</summary>
    public async Task<List<string>> GetImageFingerprintsAsync(CancellationToken cancellationToken = default)
    {
        (List<string> urls, _) = await QueryStructAsync<List<string>>(
            HttpMethod.Get, "/images", cancellationToken: cancellationToken).ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/images", urls);
    }

    /// <summary>Returns an image.</summary>
    public Task<(Api.Image Image, string ETag)> GetImageAsync(string fingerprint, CancellationToken cancellationToken = default)
        => GetPrivateImageAsync(fingerprint, "", cancellationToken);

    /// <summary>Downloads an image.</summary>
    public Task<ImageFileResponse> GetImageFileAsync(
        string fingerprint,
        ImageFileRequest request,
        CancellationToken cancellationToken = default)
        => GetPrivateImageFileAsync(fingerprint, "", request, cancellationToken);

    /// <summary>Requests a temporary download secret for an image.</summary>
    public async Task<string> GetImageSecretAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        Operation operation = await CreateImageSecretAsync(fingerprint, cancellationToken).ConfigureAwait(false);
        Api.Operation state = operation.GetOperation();

        if (state.Metadata is null || !state.Metadata.TryGetValue("secret", out object? value))
            throw new IncusException("Bad secret type");

        string? secret = value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => null,
        };

        return secret ?? throw new IncusException("Bad secret type");
    }

    /// <summary>Returns a private image using a download secret.</summary>
    public async Task<(Api.Image Image, string ETag)> GetPrivateImageAsync(
        string fingerprint,
        string secret,
        CancellationToken cancellationToken = default)
    {
        string path = $"/images/{Uri.EscapeDataString(fingerprint)}";
        if (!string.IsNullOrEmpty(secret))
            path = IncusQuery.SetParameter(path, "secret", secret);

        (Api.Image image, string etag) = await QueryStructAsync<Api.Image>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return (image, etag);
    }

    /// <summary>Downloads a private image using a download secret.</summary>
    public async Task<ImageFileResponse> GetPrivateImageFileAsync(
        string fingerprint,
        string secret,
        ImageFileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.MetaFile is null && request.RootfsFile is null)
            throw new IncusException("No file requested");

        string path = $"/images/{Uri.EscapeDataString(fingerprint)}/export";
        if (!string.IsNullOrEmpty(secret))
            path = IncusQuery.SetParameter(path, "secret", secret);

        string url = BuildQueryUrl(path);
        using HttpRequestMessage message = new(HttpMethod.Get, url);
        await AddClientHeadersAsync(message, cancellationToken).ConfigureAwait(false);

        using HttpResponseMessage response = await _http
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
            await ParseResponseAsync(response, url, cancellationToken).ConfigureAwait(false);

        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        string contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        string imageType = response.Headers.TryGetValues("X-Incus-Type", out IEnumerable<string>? values)
            ? values.FirstOrDefault() ?? "incus"
            : "incus";

        ImageFileResponse result = new();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        if (contentType == "multipart/form-data")
        {
            if (request.MetaFile is null || request.RootfsFile is null)
                throw new IncusException("Multi-part image but only one target file provided");

            string boundary = response.Content.Headers.ContentType?.Parameters
                .FirstOrDefault(p => p.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase))?.Value?.Trim('"')
                ?? throw new IncusException("Missing multipart boundary");

            IncusMultipartReader reader = new(boundary, body);

            if (!await reader.StartAsync(cancellationToken).ConfigureAwait(false))
                throw new IncusException("Invalid multipart image");

            Dictionary<string, string>? headers = await reader.ReadHeadersAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new IncusException("Invalid multipart image");

            if (!headers.TryGetValue("Content-Disposition", out string? disposition))
                throw new IncusException("Invalid multipart image");

            (string? name, string? fileName) = ParseContentDisposition(disposition);
            if (name != "metadata")
                throw new IncusException("Invalid multipart image");

            result.MetaName = fileName ?? "";
            await using (Stream part = reader.OpenBody())
            {
                result.MetaSize = await CopyHashedAsync(part, request.MetaFile, hash, request.ProgressHandler, cancellationToken)
                    .ConfigureAwait(false);
            }

            headers = await reader.ReadHeadersAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new IncusException("Invalid multipart image");

            if (!headers.TryGetValue("Content-Disposition", out disposition))
                throw new IncusException("Invalid multipart image");

            (name, fileName) = ParseContentDisposition(disposition);
            if (name is not ("rootfs" or "rootfs.img"))
                throw new IncusException("Invalid multipart image");

            result.RootfsName = fileName ?? "";
            await using (Stream part = reader.OpenBody())
            {
                result.RootfsSize = await CopyHashedAsync(part, request.RootfsFile, hash, request.ProgressHandler, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            if (request.MetaFile is null)
                throw new IncusException("No metadata file target provided");

            string? disposition = response.Content.Headers.ContentDisposition?.ToString();
            if (string.IsNullOrEmpty(disposition))
                throw new IncusException("No filename in Content-Disposition header");

            string filename = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                ?? "rootfs.tar.xz";

            result.MetaSize = await CopyHashedAsync(body, request.MetaFile, hash, request.ProgressHandler, cancellationToken)
                .ConfigureAwait(false);
            result.MetaName = Path.GetFileName(filename);
        }

        if (imageType != "oci")
        {
            string computed = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!computed.StartsWith(fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new IncusException($"Image fingerprint doesn't match. Got {computed} expected {fingerprint}");
        }

        return result;
    }

    private static (string? Name, string? FileName) ParseContentDisposition(string value)
    {
        string? name = null;
        string? fileName = null;

        foreach (string item in value.Split(';'))
        {
            int equals = item.IndexOf('=');
            if (equals <= 0)
                continue;

            string key = item[..equals].Trim();
            string entryValue = item[(equals + 1)..].Trim().Trim('"');

            if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                name = entryValue;
            else if (key.Equals("filename*", StringComparison.OrdinalIgnoreCase))
                fileName = Uri.UnescapeDataString(entryValue);
            else if (key.Equals("filename", StringComparison.OrdinalIgnoreCase))
                fileName ??= entryValue;
        }

        return (name, fileName);
    }

    private static async Task<long> CopyHashedAsync(
        Stream source,
        Stream destination,
        IncrementalHash hash,
        Action<long, long>? progress,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        long length = source.CanSeek ? source.Length : -1;

        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;
            progress?.Invoke(total, length);
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        return total;
    }

    /// <summary>Returns all image aliases.</summary>
    public async Task<List<Api.ImageAliasesEntry>> GetImageAliasesAsync(CancellationToken cancellationToken = default)
    {
        (List<Api.ImageAliasesEntry> aliases, _) = await QueryStructAsync<List<Api.ImageAliasesEntry>>(
            HttpMethod.Get, "/images/aliases?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return aliases;
    }

    /// <summary>Returns the names of all image aliases.</summary>
    public async Task<List<string>> GetImageAliasNamesAsync(CancellationToken cancellationToken = default)
    {
        (List<string> urls, _) = await QueryStructAsync<List<string>>(
            HttpMethod.Get, "/images/aliases", cancellationToken: cancellationToken).ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/images/aliases", urls);
    }

    /// <summary>Returns an image alias.</summary>
    public async Task<(Api.ImageAliasesEntry Alias, string ETag)> GetImageAliasAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        (Api.ImageAliasesEntry alias, string etag) = await QueryStructAsync<Api.ImageAliasesEntry>(
            HttpMethod.Get,
            $"/images/aliases/{Uri.EscapeDataString(name)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (alias, etag);
    }

    /// <summary>Returns an image alias for a specific image type.</summary>
    public async Task<(Api.ImageAliasesEntry Alias, string ETag)> GetImageAliasTypeAsync(
        string imageType,
        string name,
        CancellationToken cancellationToken = default)
    {
        (Api.ImageAliasesEntry alias, string etag) = await GetImageAliasAsync(name, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(imageType))
        {
            if (string.IsNullOrEmpty(alias.Type))
                alias.Type = "container";

            if (alias.Type != imageType)
                throw new IncusException("Alias doesn't exist for the specified type");
        }

        return (alias, etag);
    }

    /// <summary>Returns the alias targets grouped by architecture.</summary>
    public async Task<Dictionary<string, Api.ImageAliasesEntry>> GetImageAliasArchitecturesAsync(
        string imageType,
        string name,
        CancellationToken cancellationToken = default)
    {
        (Api.ImageAliasesEntry alias, _) = await GetImageAliasTypeAsync(imageType, name, cancellationToken).ConfigureAwait(false);
        (Api.Image image, _) = await GetImageAsync(alias.Target, cancellationToken).ConfigureAwait(false);

        return new Dictionary<string, Api.ImageAliasesEntry>
        {
            [image.Architecture] = alias,
        };
    }

    /// <summary>Requests that Incus creates, copies or imports a new image.</summary>
    public async Task<Operation> CreateImageAsync(
        Api.ImagesPost image,
        ImageCreateArgs? args = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(image.CompressionAlgorithm))
            CheckExtension("image_compression_algorithm");

        if (args is null)
        {
            (Operation operation, _) = await QueryOperationAsync(
                HttpMethod.Post, "/images", image, cancellationToken: cancellationToken).ConfigureAwait(false);

            return operation;
        }

        if (args.MetaFile is null)
            throw new IncusException("Metadata file is required");

        string url = BuildQueryUrl("/images");
        using HttpRequestMessage message = new(HttpMethod.Post, url);

        if (args.RootfsFile is null)
        {
            message.Content = new StreamContent(args.MetaFile);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }
        else
        {
            MultipartFormDataContent multipart = new();
            StreamContent metadata = new(args.MetaFile);
            multipart.Add(metadata, "metadata", args.MetaName);

            StreamContent rootfs = new(args.RootfsFile);
            multipart.Add(rootfs, args.Type == "virtual-machine" ? "rootfs.img" : "rootfs", args.RootfsName);
            message.Content = multipart;
        }

        if (image.Public)
            message.Headers.TryAddWithoutValidation("X-Incus-public", "true");

        if (!string.IsNullOrEmpty(image.Filename))
            message.Headers.TryAddWithoutValidation("X-Incus-filename", image.Filename);

        if (image.Properties is { Count: > 0 })
            message.Headers.TryAddWithoutValidation("X-Incus-properties", EncodeHeaderValues(image.Properties));

        if (image.Profiles is { Count: > 0 })
            message.Headers.TryAddWithoutValidation("X-Incus-profiles", EncodeHeaderList("profile", image.Profiles));

        if (image.Aliases is { Count: > 0 })
            message.Headers.TryAddWithoutValidation("X-Incus-aliases", EncodeHeaderList("alias", image.Aliases.Select(a => a.Name)));

        if (image.Source is { Fingerprint.Length: > 0, Secret.Length: > 0, Mode: "push" })
        {
            message.Headers.TryAddWithoutValidation("X-Incus-fingerprint", image.Source.Fingerprint);
            message.Headers.TryAddWithoutValidation("X-Incus-secret", image.Source.Secret);
        }

        await AddClientHeadersAsync(message, cancellationToken).ConfigureAwait(false);

        using HttpResponseMessage response = await _http
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        (Api.Response parsed, _) = await ParseResponseAsync(response, url, cancellationToken).ConfigureAwait(false);
        return WrapOperationResponse(parsed);
    }

    private static string EncodeHeaderValues(IReadOnlyDictionary<string, string> values)
        => string.Join("&", values.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private static string EncodeHeaderList(string name, IEnumerable<string> values)
        => string.Join("&", values.Select(value => $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}"));

    /// <summary>Copies an image from a remote server.</summary>
    public async Task<RemoteOperation> CopyImageAsync(
        IImageServer source,
        Api.Image image,
        ImageCopyArgs? args = null,
        CancellationToken cancellationToken = default)
    {
        if (ReferenceEquals(this, source))
            throw new IncusException("The source and target servers must be different");

        if (args is { Profiles.Count: > 0 })
            CheckExtension("image_copy_profile");
        else
            image.Profiles = null;

        IncusConnectionInfo sourceInfo = source.GetConnectionInfo();

        if (args?.Mode == "push")
        {
            IncusConnectionInfo info = GetConnectionInfo();

            Api.ImagesPost imagesPost = new()
            {
                Source = new Api.ImagesPostSource
                {
                    Fingerprint = image.Fingerprint,
                    Mode = "push",
                },
                ExpiresAt = image.ExpiresAt,
                Properties = image.Properties,
                Public = args.Public,
            };

            imagesPost.Aliases = args.Aliases;
            if (args.CopyAliases)
            {
                imagesPost.Aliases = image.Aliases is null ? new List<Api.ImageAlias>() : new List<Api.ImageAlias>(image.Aliases);
                imagesPost.Aliases.AddRange(args.Aliases);
            }

            Operation tokenOperation = await CreateImageAsync(imagesPost, null, cancellationToken).ConfigureAwait(false);
            Api.Operation tokenState = tokenOperation.GetOperation();

            if (tokenState.Metadata is null || !tokenState.Metadata.TryGetValue("secret", out object? secretValue))
                throw new IncusException("No token provided");

            string? secret = secretValue switch
            {
                string text => text,
                JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
                _ => null,
            };

            Api.ImageExportPost request = new()
            {
                Target = info.Url,
                Certificate = info.Certificate,
                Secret = secret ?? "",
                Project = info.Project,
                Profiles = image.Profiles,
            };

            Operation exportOperation;
            try
            {
                exportOperation = await source.ExportImageAsync(image.Fingerprint, request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await tokenOperation.CancelAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Task.Run(async () =>
            {
                try
                {
                    await exportOperation.WaitAsync(cancellationToken).ConfigureAwait(false);
                    await tokenOperation.CancelAsync(CancellationToken.None).ConfigureAwait(false);
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }, cancellationToken);

            return new RemoteOperation(completion.Task, exportOperation);
        }

        if (args?.Mode == "relay")
        {
            string directory = _args.TempPath ?? Path.GetTempPath();
            string metaPath = Path.Combine(directory, $"incus_image_{Guid.NewGuid():N}");
            string rootfsPath = Path.Combine(directory, $"incus_image_{Guid.NewGuid():N}");

            try
            {
                await using FileStream metaFile = new(metaPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024, true);
                await using FileStream rootfsFile = new(rootfsPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024, true);

                ImageFileResponse downloaded = await source.GetImageFileAsync(
                    image.Fingerprint,
                    new ImageFileRequest { MetaFile = metaFile, RootfsFile = rootfsFile },
                    cancellationToken).ConfigureAwait(false);

                metaFile.Position = 0;
                rootfsFile.Position = 0;

                Api.ImagesPost imagePost = new()
                {
                    Public = args.Public,
                    Profiles = image.Profiles,
                };

                imagePost.Aliases = args.Aliases;
                if (args.CopyAliases)
                {
                    imagePost.Aliases = image.Aliases is null ? new List<Api.ImageAlias>() : new List<Api.ImageAlias>(image.Aliases);
                    imagePost.Aliases.AddRange(args.Aliases);
                }

                ImageCreateArgs createArgs = new()
                {
                    MetaFile = metaFile,
                    MetaName = image.Filename,
                    Type = image.Type,
                };

                if (!string.IsNullOrEmpty(downloaded.RootfsName))
                {
                    createArgs.RootfsFile = rootfsFile;
                    createArgs.RootfsName = image.Filename;
                }

                TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                RemoteOperation result = new(completion.Task);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        Operation operation = await CreateImageAsync(imagePost, createArgs, cancellationToken).ConfigureAwait(false);
                        result.SetTarget(operation);
                        await operation.WaitAsync(cancellationToken).ConfigureAwait(false);

                        if (imagePost.Aliases is not null)
                        {
                            foreach (Api.ImageAlias entry in imagePost.Aliases)
                            {
                                Api.ImageAliasesPost alias = new()
                                {
                                    Name = entry.Name,
                                    Target = image.Fingerprint,
                                };

                                await CreateImageAliasAsync(alias, cancellationToken).ConfigureAwait(false);
                            }
                        }

                        completion.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(ex);
                    }
                }, cancellationToken);

                return result;
            }
            finally
            {
                TryDelete(metaPath);
                TryDelete(rootfsPath);
            }
        }

        Api.ImagesPost request1 = new()
        {
            Source = new Api.ImagesPostSource
            {
                Certificate = sourceInfo.Certificate,
                Protocol = sourceInfo.Protocol,
                Fingerprint = image.Fingerprint,
                Mode = "pull",
                Type = "image",
                Project = sourceInfo.Project,
            },
            Profiles = image.Profiles,
        };

        if (args is not null)
            request1.Source.ImageType = args.Type;

        if (!image.Public)
            request1.Source.Secret = await source.GetImageSecretAsync(image.Fingerprint, cancellationToken).ConfigureAwait(false);

        if (args is not null)
        {
            request1.Aliases = args.Aliases;
            request1.AutoUpdate = args.AutoUpdate;
            request1.Public = args.Public;

            if (args.CopyAliases)
            {
                request1.Aliases = image.Aliases is null ? new List<Api.ImageAlias>() : new List<Api.ImageAlias>(image.Aliases);
                request1.Aliases.AddRange(args.Aliases);
            }
        }

        return TryCopyImage(request1, sourceInfo.Addresses, cancellationToken);
    }

    private RemoteOperation TryCopyImage(Api.ImagesPost request, List<string> urls, CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
            throw new IncusException("The source server isn't listening on the network");

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RemoteOperation result = new(completion.Task);

        _ = Task.Run(async () =>
        {
            List<Exception> errors = new();

            try
            {
                foreach (string serverUrl in urls)
                {
                    try
                    {
                        request.Source ??= new Api.ImagesPostSource();
                        request.Source.Server = serverUrl;

                        Operation operation = await CreateImageAsync(request, null, cancellationToken).ConfigureAwait(false);
                        result.SetTarget(operation);
                        await operation.WaitAsync(cancellationToken).ConfigureAwait(false);

                        if (!HasExtension("image_create_aliases") && request.Aliases is not null)
                        {
                            Api.Operation state = operation.GetOperation();
                            if (state.Metadata is null || !state.Metadata.TryGetValue("fingerprint", out object? fingerprintValue))
                                throw new IncusException("Bad fingerprint");

                            string fingerprint = fingerprintValue switch
                            {
                                string text => text,
                                JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? "",
                                _ => "",
                            };

                            if (string.IsNullOrEmpty(fingerprint))
                                throw new IncusException("Bad fingerprint");

                            foreach (Api.ImageAlias entry in request.Aliases)
                            {
                                Api.ImageAliasesPost alias = new()
                                {
                                    Name = entry.Name,
                                    Target = fingerprint,
                                };

                                await CreateImageAliasAsync(alias, cancellationToken).ConfigureAwait(false);
                            }
                        }

                        completion.TrySetResult();
                        return;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errors.Add(ex);
                    }
                }

                IncusException failure = new("Failed remote image download", new AggregateException(errors));
                result.Fail(failure);
                completion.TrySetException(failure);
            }
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
        }, cancellationToken);

        return result;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort.
        }
    }

    /// <summary>Updates an image definition.</summary>
    public async Task UpdateImageAsync(
        string fingerprint,
        Api.ImagePut image,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        await QueryAsync(HttpMethod.Put, $"/images/{Uri.EscapeDataString(fingerprint)}", image, etag, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Requests that Incus removes an image.</summary>
    public async Task<Operation> DeleteImageAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Delete, $"/images/{Uri.EscapeDataString(fingerprint)}", cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Requests that Incus issues an image refresh.</summary>
    public async Task<Operation> RefreshImageAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        CheckExtension("image_force_refresh");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post, $"/images/{Uri.EscapeDataString(fingerprint)}/refresh", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return operation;
    }

    /// <summary>Requests a temporary image secret.</summary>
    public async Task<Operation> CreateImageSecretAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post, $"/images/{Uri.EscapeDataString(fingerprint)}/secret", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return operation;
    }

    /// <summary>Creates a new image alias.</summary>
    public async Task CreateImageAliasAsync(Api.ImageAliasesPost alias, CancellationToken cancellationToken = default)
    {
        await QueryAsync(HttpMethod.Post, "/images/aliases", alias, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates an image alias.</summary>
    public async Task UpdateImageAliasAsync(
        string name,
        Api.ImageAliasesEntryPut alias,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        await QueryAsync(HttpMethod.Put, $"/images/aliases/{Uri.EscapeDataString(name)}", alias, etag, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Renames an image alias.</summary>
    public async Task RenameImageAliasAsync(
        string name,
        Api.ImageAliasesEntryPost alias,
        CancellationToken cancellationToken = default)
    {
        await QueryAsync(HttpMethod.Post, $"/images/aliases/{Uri.EscapeDataString(name)}", alias, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Removes an image alias.</summary>
    public async Task DeleteImageAliasAsync(string name, CancellationToken cancellationToken = default)
    {
        await QueryAsync(HttpMethod.Delete, $"/images/aliases/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Exports (pushes) an image to a remote server.</summary>
    public async Task<Operation> ExportImageAsync(
        string fingerprint,
        Api.ImageExportPost image,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("images_push_relay");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post, $"/images/{Uri.EscapeDataString(fingerprint)}/export", image, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return operation;
    }
}
