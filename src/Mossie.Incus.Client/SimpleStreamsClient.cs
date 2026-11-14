using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;
using Mossie.Incus.Client.SimpleStreams;

namespace Mossie.Incus.Client;

/// <summary>
/// Client for a SimpleStreams image server. Mirrors the Go <c>incus.ProtocolSimpleStreams</c> client.
/// </summary>
public sealed class SimpleStreamsClient : IImageServer, IAsyncDisposable, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _host;
    private readonly string _certificate;
    private readonly string? _userAgent;
    private readonly string? _tempPath;
    private readonly SimpleStreamsRepository _repository;

    private SimpleStreamsClient(
        HttpClient http,
        string host,
        string certificate,
        string? userAgent,
        string? tempPath,
        SimpleStreamsRepository repository)
    {
        _http = http;
        _host = host;
        _certificate = certificate;
        _userAgent = userAgent;
        _tempPath = tempPath;
        _repository = repository;
    }

    /// <summary>Connects to a remote SimpleStreams image server over HTTPS.</summary>
    public static Task<SimpleStreamsClient> ConnectAsync(
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

        string? cachePath = null;
        TimeSpan cacheExpiry = args.CacheExpiry == TimeSpan.Zero ? TimeSpan.FromHours(1) : args.CacheExpiry;

        if (!string.IsNullOrEmpty(args.CachePath))
        {
            if (!Directory.Exists(args.CachePath))
                throw new IncusException($"Cache directory \"{args.CachePath}\" doesn't exist");

            string hashedUrl = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(host))).ToLowerInvariant();
            cachePath = Path.Combine(args.CachePath, hashedUrl);

            if (!Directory.Exists(cachePath))
                Directory.CreateDirectory(cachePath);
        }

        SimpleStreamsRepository repository = new(host, http, args.UserAgent, cachePath, cacheExpiry);

        return Task.FromResult(new SimpleStreamsClient(http, host, args.TlsServerCert ?? "", args.UserAgent, args.TempPath, repository));
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

    /// <summary>Disconnects from the server (no-op).</summary>
    public void Disconnect()
    {
    }

    /// <inheritdoc />
    public IncusConnectionInfo GetConnectionInfo() => new()
    {
        Addresses = [_host],
        Certificate = _certificate,
        Protocol = "simplestreams",
        Url = _host,
    };

    /// <inheritdoc />
    public async Task<List<Image>> GetImagesAsync(CancellationToken cancellationToken = default)
        => await _repository.ListImagesAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<List<Image>> GetImagesAllProjectsAsync(CancellationToken cancellationToken = default)
        => GetImagesAsync(cancellationToken);

    /// <inheritdoc />
    public Task<List<Image>> GetImagesAllProjectsWithFilterAsync(
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
        => throw new IncusException("GetImagesWithFilter is not supported by the simplestreams protocol");

    /// <inheritdoc />
    public Task<List<Image>> GetImagesWithFilterAsync(
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
        => throw new IncusException("GetImagesWithFilter is not supported by the simplestreams protocol");

    /// <inheritdoc />
    public async Task<List<string>> GetImageFingerprintsAsync(CancellationToken cancellationToken = default)
    {
        List<Image> images = await _repository.ListImagesAsync(cancellationToken).ConfigureAwait(false);
        return images.Select(image => image.Fingerprint).ToList();
    }

    /// <inheritdoc />
    public async Task<(Image Image, string ETag)> GetImageAsync(
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Image image = await _repository.GetImageAsync(fingerprint, cancellationToken).ConfigureAwait(false);
            return (image, "");
        }
        catch (IncusException ex)
        {
            throw new IncusException($"Failed getting image: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public Task<(Image Image, string ETag)> GetPrivateImageAsync(
        string fingerprint,
        string secret,
        CancellationToken cancellationToken = default)
        => throw new IncusException("Private images aren't supported by the simplestreams protocol");

    /// <inheritdoc />
    public Task<ImageFileResponse> GetPrivateImageFileAsync(
        string fingerprint,
        string secret,
        ImageFileRequest request,
        CancellationToken cancellationToken = default)
        => throw new IncusException("Private images aren't supported by the simplestreams protocol");

    /// <inheritdoc />
    public Task<string> GetImageSecretAsync(string fingerprint, CancellationToken cancellationToken = default)
        => throw new IncusException("Private images aren't supported by the simplestreams protocol");

    /// <inheritdoc />
    public async Task<ImageFileResponse> GetImageFileAsync(
        string fingerprint,
        ImageFileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.MetaFile is null && request.RootfsFile is null)
            throw new IncusException("No file requested");

        Image image = await _repository.GetImageAsync(fingerprint, cancellationToken).ConfigureAwait(false);
        fingerprint = image.Fingerprint;

        Dictionary<string, SimpleStreamsDownloadableFile> files =
            await _repository.GetFilesAsync(fingerprint, cancellationToken).ConfigureAwait(false);

        ImageFileResponse response = new();

        Task<long> DownloadAsync(string path, string filename, string hash, Stream target)
            => DownloadWithFallbackAsync(path, target, hash, request.ProgressHandler, cancellationToken);

        if (files.TryGetValue("meta", out SimpleStreamsDownloadableFile? meta) && request.MetaFile is not null)
        {
            response.MetaSize = await DownloadAsync(meta.Path, "metadata", meta.Sha256, request.MetaFile).ConfigureAwait(false);
            response.MetaName = Path.GetFileName(meta.Path);
        }

        if (files.TryGetValue("root", out SimpleStreamsDownloadableFile? rootfs) && request.RootfsFile is not null)
        {
            bool downloaded = false;

            if (request.DeltaSourceRetriever is not null && ExecutableExists("xdelta3"))
            {
                foreach ((string filename, SimpleStreamsDownloadableFile file) in files)
                {
                    const string prefix = "root.delta-";
                    int index = filename.IndexOf(prefix, StringComparison.Ordinal);
                    if (index < 0)
                        continue;

                    string sourceFingerprint = filename[(index + prefix.Length)..];
                    string sourcePath = request.DeltaSourceRetriever(sourceFingerprint, "rootfs");
                    if (string.IsNullOrEmpty(sourcePath))
                        continue;

                    response.RootfsSize = await ApplyDeltaAsync(
                        file, sourcePath, request.RootfsFile, request.ProgressHandler, cancellationToken).ConfigureAwait(false);
                    response.RootfsName = Path.GetFileName(rootfs.Path);
                    downloaded = true;
                }
            }

            if (!downloaded)
            {
                response.RootfsSize = await DownloadAsync(rootfs.Path, "rootfs", rootfs.Sha256, request.RootfsFile)
                    .ConfigureAwait(false);
                response.RootfsName = Path.GetFileName(rootfs.Path);
            }
        }

        await ValidateFingerprintAsync(request, response, fingerprint, cancellationToken).ConfigureAwait(false);

        return response;
    }

    private async Task<long> DownloadWithFallbackAsync(
        string path,
        Stream target,
        string hash,
        Action<long, long>? progress,
        CancellationToken cancellationToken)
    {
        string httpHost = _host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? "http://" + _host["https://".Length..]
            : _host;

        string uri = SimpleStreamsRepository.JoinPath(httpHost, path);
        try
        {
            return await DownloadFileHashAsync(uri, target, hash, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            string httpsUri = SimpleStreamsRepository.JoinPath(_host, path);
            try
            {
                return await DownloadFileHashAsync(httpsUri, target, hash, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _repository.InvalidateCache();
                throw;
            }
        }
    }

    private static async Task ValidateFingerprintAsync(
        ImageFileRequest request,
        ImageFileResponse response,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        if (response.MetaSize > 0 && request.MetaFile is not null)
        {
            if (!request.MetaFile.CanSeek)
                return;

            request.MetaFile.Seek(0, SeekOrigin.Begin);
            await CopyToHashAsync(request.MetaFile, hash, cancellationToken).ConfigureAwait(false);
        }

        if (response.RootfsSize > 0 && request.RootfsFile is not null)
        {
            if (!request.RootfsFile.CanSeek)
                return;

            request.RootfsFile.Seek(0, SeekOrigin.Begin);
            await CopyToHashAsync(request.RootfsFile, hash, cancellationToken).ConfigureAwait(false);
        }

        string computed = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (computed != fingerprint)
            throw new IncusException($"Image fingerprint doesn't match. Got {computed} expected {fingerprint}");
    }

    private static async Task CopyToHashAsync(Stream source, IncrementalHash hash, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            hash.AppendData(buffer, 0, read);
        }
    }

    private async Task<long> DownloadFileHashAsync(
        string uri,
        Stream target,
        string expectedHash,
        Action<long, long>? progress,
        CancellationToken cancellationToken)
    {
        if (target.CanSeek)
        {
            target.SetLength(0);
            target.Position = 0;
        }

        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        if (!string.IsNullOrEmpty(_userAgent))
            request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);

        using HttpResponseMessage response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        long length = response.Content.Headers.ContentLength ?? -1;

        while (true)
        {
            int read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            hash.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;
            progress?.Invoke(total, length);
        }

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(expectedHash))
        {
            string computed = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (computed != expectedHash)
                throw new IncusException($"Hash mismatch for {uri}: got {computed} expected {expectedHash}");
        }

        return total;
    }

    private async Task<long> ApplyDeltaAsync(
        SimpleStreamsDownloadableFile file,
        string sourcePath,
        Stream target,
        Action<long, long>? progress,
        CancellationToken cancellationToken)
    {
        string deltaPath = Path.Combine(_tempPath ?? Path.GetTempPath(), $"incus_image_{Guid.NewGuid():N}");
        string patchedPath = Path.Combine(_tempPath ?? Path.GetTempPath(), $"incus_image_{Guid.NewGuid():N}");

        try
        {
            await using (FileStream deltaFile = new(deltaPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                await DownloadWithFallbackAsync(file.Path, deltaFile, file.Sha256, progress, cancellationToken)
                    .ConfigureAwait(false);
            }

            ProcessStartInfo startInfo = new("xdelta3")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add(sourcePath);
            startInfo.ArgumentList.Add(deltaPath);
            startInfo.ArgumentList.Add(patchedPath);

            using Process process = Process.Start(startInfo)
                ?? throw new IncusException("Failed to start xdelta3");

            string error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new IncusException($"xdelta3 failed: {error}");

            await using FileStream patched = new(patchedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (target.CanSeek)
            {
                target.SetLength(0);
                target.Position = 0;
            }

            long total = 0;
            byte[] buffer = new byte[64 * 1024];
            while (true)
            {
                int read = await patched.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
                progress?.Invoke(total, patched.Length);
            }

            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            return total;
        }
        finally
        {
            TryDelete(deltaPath);
            TryDelete(patchedPath);
        }
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

    /// <inheritdoc />
    public async Task<List<ImageAliasesEntry>> GetImageAliasesAsync(CancellationToken cancellationToken = default)
        => await _repository.ListAliasesAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<List<string>> GetImageAliasNamesAsync(CancellationToken cancellationToken = default)
    {
        List<ImageAliasesEntry> aliases = await _repository.ListAliasesAsync(cancellationToken).ConfigureAwait(false);
        return aliases.Select(alias => alias.Name).ToList();
    }

    /// <inheritdoc />
    public async Task<(ImageAliasesEntry Alias, string ETag)> GetImageAliasAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ImageAliasesEntry? alias;
        try
        {
            alias = await _repository.GetAliasAsync("container", name, cancellationToken).ConfigureAwait(false);
        }
        catch (IncusException)
        {
            alias = await _repository.GetAliasAsync("virtual-machine", name, cancellationToken).ConfigureAwait(false);
        }

        return (alias ?? throw new IncusException($"Alias '{name}' doesn't exist"), "");
    }

    /// <inheritdoc />
    public async Task<(ImageAliasesEntry Alias, string ETag)> GetImageAliasTypeAsync(
        string imageType,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(imageType))
            return await GetImageAliasAsync(name, cancellationToken).ConfigureAwait(false);

        ImageAliasesEntry? alias = await _repository.GetAliasAsync(imageType, name, cancellationToken).ConfigureAwait(false);
        return (alias ?? throw new IncusException($"Alias '{name}' doesn't exist"), "");
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, ImageAliasesEntry>> GetImageAliasArchitecturesAsync(
        string imageType,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(imageType))
        {
            try
            {
                return await _repository.GetAliasArchitecturesAsync("container", name, cancellationToken).ConfigureAwait(false);
            }
            catch (IncusException)
            {
                return await _repository.GetAliasArchitecturesAsync("virtual-machine", name, cancellationToken).ConfigureAwait(false);
            }
        }

        return await _repository.GetAliasArchitecturesAsync(imageType, name, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Operation> ExportImageAsync(
        string fingerprint,
        ImageExportPost image,
        CancellationToken cancellationToken = default)
        => throw new IncusException("Exporting images is not supported by the simplestreams protocol");
}
