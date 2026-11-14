using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;
using Mossie.Incus.Client.Serialization;
using static Mossie.Incus.Client.Api.IncusConstants;

namespace Mossie.Incus.Client;

/// <summary>
/// Client for the Incus REST API. Mirrors the Go <c>incus.ProtocolIncus</c> client.
/// </summary>
public sealed partial class IncusClient : IImageServer, IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly IncusConnectionArgs _args;
    private readonly string? _unixPath;
    private readonly string _protocol;
    private readonly SslClientAuthenticationOptions _sslOptions;
    private readonly CancellationTokenSource _connectedCts = new();
    private readonly ConcurrentDictionary<long, EventListener> _activeListeners = new();

    private long _listenerSequence;
    private string? _serverCertificate;
    private string? _clusterTarget;
    private string? _project;
    private bool _requireAuthenticated;
    private bool _skipEvents;
    private Api.Server? _server;
    private string? _tempPath;

    private IncusClient(
        HttpClient http,
        Uri baseUri,
        IncusConnectionArgs args,
        string protocol,
        string? unixPath,
        SslClientAuthenticationOptions sslOptions,
        string? certificate,
        string? project,
        string? target,
        bool skipEvents,
        string? tempPath)
    {
        _http = http;
        _baseUri = baseUri;
        _args = args;
        _protocol = protocol;
        _unixPath = unixPath;
        _sslOptions = sslOptions;
        _serverCertificate = certificate;
        _project = project;
        _clusterTarget = target;
        _skipEvents = skipEvents;
        _tempPath = tempPath;
    }

    /// <summary>Protocol name, always <c>incus</c>.</summary>
    public string Protocol => _protocol;

    /// <summary>The base URL of the connection.</summary>
    public Uri BaseUri => _baseUri;

    /// <summary>The Unix socket path, when connected over a Unix socket.</summary>
    public string? SocketPath => _unixPath;

    /// <summary>Currently selected project (may be empty).</summary>
    public string? Project => _project;

    /// <summary>Currently selected cluster target (may be empty).</summary>
    public string? Target => _clusterTarget;

    /// <summary>The server certificate, when known.</summary>
    public string? Certificate => _serverCertificate;

    /// <summary>Temporary storage path used by import/export helpers.</summary>
    public string? TempPath => _tempPath;

    /// <summary>Connects to a remote Incus daemon over HTTPS.</summary>
    public static Task<IncusClient> ConnectAsync(string uri, IncusConnectionArgs? args = null, CancellationToken cancellationToken = default)
        => ConnectAsync(new Uri(uri.TrimEnd('/')), args, cancellationToken);

    /// <summary>Connects to a remote Incus daemon over HTTPS.</summary>
    public static async Task<IncusClient> ConnectAsync(Uri uri, IncusConnectionArgs? args = null, CancellationToken cancellationToken = default)
    {
        args ??= new IncusConnectionArgs();

        (HttpMessageHandler handler, _, X509Certificate2? expected) = IncusTransports.CreateHttpsHandler(args);
        Uri baseUri = EnsureTrailingSlash(uri);
        HttpClient http = IncusTransports.CreateClient(args, handler, baseUri);
        SslClientAuthenticationOptions sslOptions = CreateSslOptions(args, expected, handler);

        IncusClient client = new(
            http,
            baseUri,
            args,
            "incus",
            unixPath: null,
            sslOptions,
            args.TlsServerCert,
            project: null,
            target: null,
            skipEvents: args.SkipGetEvents,
            args.TempPath);

        if (!string.IsNullOrEmpty(args.AuthType) && args.AuthType == AuthenticationMethodOIDC)
            client._requireAuthenticated = true;

        if (!args.SkipGetServer)
            await client.GetServerAsync(cancellationToken).ConfigureAwait(false);

        return client;
    }

    /// <summary>Connects to a local Incus daemon over a Unix socket.</summary>
    public static async Task<IncusClient> ConnectUnixAsync(string? path = null, IncusConnectionArgs? args = null, CancellationToken cancellationToken = default)
    {
        args ??= new IncusConnectionArgs();

        (string socketPath, string project) = ResolveUnixSocket(path);
        Uri baseUri = new("http://unix.socket");

        HttpMessageHandler handler = IncusTransports.CreateUnixHandler(args, socketPath);
        HttpClient http = IncusTransports.CreateClient(args, handler, baseUri);
        SslClientAuthenticationOptions sslOptions = new();

        IncusClient client = new(
            http,
            baseUri,
            args,
            "incus",
            socketPath,
            sslOptions,
            certificate: null,
            project: string.IsNullOrEmpty(project) ? null : project,
            target: null,
            skipEvents: args.SkipGetEvents,
            args.TempPath);

        if (!args.SkipGetServer)
        {
            Api.Server server = (await client.GetServerAsync(cancellationToken).ConfigureAwait(false)).Server;
            client._serverCertificate = server.Environment.Certificate;
        }

        return client;
    }

    /// <summary>Connects to a VM agent over a preconfigured HTTP client.</summary>
    public static async Task<IncusClient> ConnectHttpAsync(IncusConnectionArgs? args, HttpClient client, CancellationToken cancellationToken = default)
    {
        args ??= new IncusConnectionArgs();
        Uri baseUri = new("https://custom.socket");

        IncusClient result = new(
            client,
            baseUri,
            args,
            "custom",
            unixPath: null,
            new SslClientAuthenticationOptions(),
            certificate: null,
            project: null,
            target: null,
            skipEvents: args.SkipGetEvents,
            args.TempPath);

        if (!args.SkipGetServer)
        {
            Api.Server server = (await result.GetServerAsync(cancellationToken).ConfigureAwait(false)).Server;
            result._serverCertificate = server.Environment.Certificate;
        }

        return result;
    }

    /// <summary>Connects to a public Incus daemon over HTTPS.</summary>
    public static Task<IncusClient> ConnectPublicAsync(string uri, IncusConnectionArgs? args = null, CancellationToken cancellationToken = default)
        => ConnectAsync(uri, args, cancellationToken);

    private static readonly string[] DefaultSocketDirs = ["/run/incus", "/var/lib/incus"];

    /// <summary>Resolves the Unix socket path and optional project name for a local connection.</summary>
    public static (string SocketPath, string Project) ResolveUnixSocket(string? path = null)
    {
        (string sock, string proj) = ResolveUnixSocketCore(path, ProbeActiveUnixSocket);
        if (sock.Length > 0)
            return (sock, proj);

        (sock, proj) = ResolveUnixSocketCore(path, File.Exists);
        if (sock.Length > 0)
            return (sock, proj);

        return SocketForDir(DefaultSocketDirs[^1]);
    }

    private static (string SocketPath, string Project) ResolveUnixSocketCore(string? path, Func<string, bool> probe)
    {
        if (!string.IsNullOrEmpty(path))
            return (path, "");

        string? envSocket = Environment.GetEnvironmentVariable("INCUS_SOCKET");
        if (!string.IsNullOrEmpty(envSocket))
            return (envSocket, "");

        string? envDir = Environment.GetEnvironmentVariable("INCUS_DIR");
        if (!string.IsNullOrEmpty(envDir))
            return SocketForDir(envDir);

        foreach (string dir in DefaultSocketDirs)
        {
            (string sock, string proj) = SocketForDir(dir);
            if (probe(sock))
                return (sock, proj);
        }

        return ("", "");
    }

    private static (string SocketPath, string Project) SocketForDir(string dir)
    {
        string path = Path.Combine(dir, "unix.socket");
        string userPath = Path.Combine(dir, "unix.socket.user");
        if (!PathIsWritable(path) && PathIsWritable(userPath))
            return (userPath, $"user-{GetEuid()}");

        return (path, "");
    }

    private static bool PathIsWritable(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();

    private static bool ProbeActiveUnixSocket(string socketPath)
    {
        if (!OperatingSystem.IsLinux())
            return false;

        try
        {
            using System.Net.Sockets.Socket socket = new(
                System.Net.Sockets.AddressFamily.Unix,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Unspecified);
            socket.Connect(new System.Net.Sockets.UnixDomainSocketEndPoint(socketPath));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns the HTTP client used by this connection.</summary>
    public HttpClient GetHttpClient() => _http;

    /// <summary>Returns basic connection information.</summary>
    public IncusConnectionInfo GetConnectionInfo()
    {
        string project = string.IsNullOrEmpty(_project) ? ProjectDefaultName : _project;
        string target = _clusterTarget ?? _server?.Environment.ServerName ?? "";

        List<string> urls = new();
        if (_protocol == "https")
            urls.Add(_baseUri.ToString().TrimEnd('/'));

        if (_server?.Environment.Addresses is not null)
        {
            foreach (string address in _server.Environment.Addresses)
            {
                if (address.StartsWith(':'))
                    continue;

                string url = $"https://{address}";
                if (!urls.Contains(url))
                    urls.Add(url);
            }
        }

        return new IncusConnectionInfo
        {
            Certificate = _serverCertificate ?? "",
            Protocol = "incus",
            Url = _baseUri.ToString().TrimEnd('/'),
            SocketPath = _unixPath ?? "",
            Project = project,
            Target = target,
            Addresses = urls,
        };
    }

    /// <summary>Returns true when the server advertises the supplied API extension.</summary>
    public bool HasExtension(string extension)
    {
        if (_server is null)
            return true;

        return _server.APIExtensions?.Contains(extension) == true;
    }

    /// <summary>Throws when the server is missing the supplied API extension.</summary>
    public void CheckExtension(string extension)
    {
        if (!HasExtension(extension))
            throw new IncusException($"The server is missing the required \"{extension}\" API extension");
    }

    /// <summary>Returns true when the server belongs to an Incus cluster.</summary>
    public bool IsClustered() => _server?.Environment.ServerClustered ?? false;

    /// <summary>Sets whether requests advertise that the client expects to be authenticated.</summary>
    public void RequireAuthenticated(bool authenticated) => _requireAuthenticated = authenticated;

    /// <summary>Returns a client bound to the supplied project.</summary>
    public IncusClient UseProject(string name) => Clone(project: name, target: _clusterTarget);

    /// <summary>Returns a client bound to the supplied cluster target.</summary>
    public IncusClient UseTarget(string name) => Clone(project: _project, target: name);

    private IncusClient Clone(string? project, string? target)
        => new(
            _http,
            _baseUri,
            _args,
            _protocol,
            _unixPath,
            _sslOptions,
            _serverCertificate,
            project,
            target,
            _skipEvents,
            _tempPath)
        {
            _server = _server,
            _requireAuthenticated = _requireAuthenticated,
        };

    /// <summary>Releases background event connections.</summary>
    public void Disconnect()
    {
        if (_connectedCts.IsCancellationRequested)
            return;

        _connectedCts.Cancel();

        foreach (EventListener listener in _activeListeners.Values)
            listener.Abort();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Disconnect();
        _http.Dispose();
        _connectedCts.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Performs a raw API request against the server.</summary>
    public Task<(Api.Response Response, string ETag)> RawQueryAsync(
        HttpMethod method,
        string path,
        object? data = null,
        string? etag = null,
        CancellationToken cancellationToken = default)
        => RawQueryWithHeadersAsync(method, path, data, etag, null, cancellationToken);

    /// <summary>Performs a raw API request with additional headers.</summary>
    public async Task<(Api.Response Response, string ETag)> RawQueryWithHeadersAsync(
        HttpMethod method,
        string path,
        object? data,
        string? etag,
        IReadOnlyDictionary<string, string[]>? headers,
        CancellationToken cancellationToken = default)
    {
        string url = _baseUri.ToString().TrimEnd('/') + path;
        return await RawQueryAbsoluteAsync(method, url, data, etag, headers, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Performs a request against an endpoint returning a background operation.</summary>
    public Task<(Operation Operation, string ETag)> RawOperationAsync(
        HttpMethod method,
        string path,
        object? data = null,
        string? etag = null,
        CancellationToken cancellationToken = default)
        => QueryOperationAsync(method, path, data, etag, cancellationToken);

    internal string BuildQueryUrl(string path)
        => IncusQuery.AddClientAttributes(_baseUri.ToString().TrimEnd('/') + "/1.0" + path, _project, _clusterTarget);

    internal async Task<(Api.Response Response, string ETag)> QueryAsync(
        HttpMethod method,
        string path,
        object? data = null,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        string url = BuildQueryUrl(path);
        return await RawQueryAbsoluteAsync(method, url, data, etag, null, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<(T Value, string ETag)> QueryStructAsync<T>(
        HttpMethod method,
        string path,
        object? data = null,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        (Api.Response response, string responseEtag) = await QueryAsync(method, path, data, etag, cancellationToken).ConfigureAwait(false);

        T? value;
        if (response.Metadata.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            value = default;
        else
            value = response.Metadata.Deserialize<T>(IncusJson.Options);

        if (value is null)
            throw new IncusException("The server returned an empty response.");

        return (value, responseEtag);
    }

    internal async Task<(Operation Operation, string ETag)> QueryOperationAsync(
        HttpMethod method,
        string path,
        object? data = null,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        EventListener? listener = null;
        bool skipListener = _skipEvents;

        if (!skipListener)
        {
            try
            {
                listener = await GetEventsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IncusStatusException ex) when (ex.StatusCode == (int)HttpStatusCode.Forbidden)
            {
                skipListener = true;
            }
            catch (Exception) when (skipListener)
            {
                listener = null;
            }
        }

        try
        {
            (Api.Response response, string responseEtag) = await QueryAsync(method, path, data, etag, cancellationToken).ConfigureAwait(false);
            Api.Operation operation = DeserializeOperation(response);

            return (new Operation(this, operation, listener, skipListener), responseEtag);
        }
        catch
        {
            if (listener is not null)
                await listener.DisconnectAsync().ConfigureAwait(false);

            throw;
        }
    }

    internal static Api.Operation DeserializeOperation(Api.Response response)
    {
        if (response.Metadata.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new IncusException("The server did not return an operation.");

        Api.Operation? operation = response.Metadata.Deserialize<Api.Operation>(IncusJson.Options);
        if (operation is null)
            throw new IncusException("The server returned an invalid operation.");

        return operation;
    }

    private async Task<(Api.Response Response, string ETag)> RawQueryAbsoluteAsync(
        HttpMethod method,
        string url,
        object? data,
        string? etag,
        IReadOnlyDictionary<string, string[]>? headers,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, url);

        if (data is not null)
        {
            if (data is Stream stream)
            {
                request.Content = new StreamContent(stream);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            }
            else if (data is byte[] bytes)
            {
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            }
            else
            {
                string json = JsonSerializer.Serialize(data, data.GetType(), IncusJson.Options);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
        }

        if (!string.IsNullOrEmpty(etag))
            request.Headers.TryAddWithoutValidation("If-Match", etag);

        await AddClientHeadersAsync(request, cancellationToken).ConfigureAwait(false);

        if (headers is not null)
        {
            foreach (KeyValuePair<string, string[]> header in headers)
            {
                request.Headers.Remove(header.Key);
                if (header.Value.Length > 0)
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                else if (request.Content is not null)
                    request.Content.Headers.Remove(header.Key);
            }
        }

        using HttpResponseMessage response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        return await ParseResponseAsync(response, url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends a request and returns the response body as a stream.</summary>
    internal async Task<(Stream Stream, string ETag, HttpResponseMessage Response)> QueryStreamAsync(
        HttpMethod method,
        string path,
        object? data = null,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        string url = BuildQueryUrl(path);
        return await RawQueryStreamAbsoluteAsync(method, url, data, etag, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<(Stream Stream, string ETag, HttpResponseMessage Response)> RawQueryStreamAbsoluteAsync(
        HttpMethod method,
        string url,
        object? data,
        string? etag,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, url);

        if (data is not null)
        {
            if (data is Stream stream)
            {
                request.Content = new StreamContent(stream);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            }
            else if (data is byte[] bytes)
            {
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            }
            else
            {
                string json = JsonSerializer.Serialize(data, data.GetType(), IncusJson.Options);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
        }

        if (!string.IsNullOrEmpty(etag))
            request.Headers.TryAddWithoutValidation("If-Match", etag);

        await AddClientHeadersAsync(request, cancellationToken).ConfigureAwait(false);

        HttpResponseMessage response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        int status = (int)response.StatusCode;
        if (status < 200 || status >= 300)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string? error = TryReadError(body);
            response.Dispose();
            throw new IncusStatusException(status, error ?? $"Failed to fetch {url}: {status} {response.ReasonPhrase}");
        }

        string responseEtag = GetHeader(response, "ETag");
        Stream content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return (content, responseEtag, response);
    }

    private async Task AddClientHeadersAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_args.UserAgent))
            request.Headers.TryAddWithoutValidation("User-Agent", _args.UserAgent);

        if (_requireAuthenticated)
            request.Headers.TryAddWithoutValidation("X-Incus-authenticated", "true");

        if (_args.OidcTokenProvider is not null)
        {
            string? token = await _args.OidcTokenProvider(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static async Task<(Api.Response Response, string ETag)> ParseResponseAsync(
        HttpResponseMessage response,
        string url,
        CancellationToken cancellationToken)
    {
        string etag = GetHeader(response, "ETag");
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        Api.Response? parsed;
        try
        {
            parsed = IncusJson.Deserialize<Api.Response>(body);
        }
        catch (JsonException) when ((int)response.StatusCode != (int)HttpStatusCode.OK)
        {
            throw new IncusException($"Failed to fetch {url}: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        if (parsed is null)
            throw new IncusException($"Failed to fetch {url}: the server returned an empty response.");

        if (parsed.Type == IncusResponseType.ErrorResponse)
            throw new IncusStatusException((int)response.StatusCode, parsed.Error);

        return (parsed, etag);
    }

    private static string? TryReadError(string body)
    {
        try
        {
            Api.Response? response = IncusJson.Deserialize<Api.Response>(body);
            return string.IsNullOrEmpty(response?.Error) ? null : response.Error;
        }
        catch
        {
            return null;
        }
    }

    private static string GetHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out IEnumerable<string>? values))
            return values.FirstOrDefault() ?? "";

        if (response.Content.Headers.TryGetValues(name, out values))
            return values.FirstOrDefault() ?? "";

        return "";
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        string text = uri.ToString();
        return text.EndsWith('/') ? uri : new Uri(text + "/");
    }

    private static SslClientAuthenticationOptions CreateSslOptions(
        IncusConnectionArgs args,
        X509Certificate2? expected,
        HttpMessageHandler handler)
    {
        RemoteCertificateValidationCallback? validation = null;

        if (args.IdenticalCertificate || args.InsecureSkipVerify || expected is not null || !string.IsNullOrEmpty(args.TlsCa))
        {
            X509Certificate2Collection? roots = IncusTransports.ParseCertificates(args.TlsCa);
            validation = (_, certificate, _, _) =>
                IncusTransports.ValidateCertificate(certificate, expected, roots, args.InsecureSkipVerify, args.IdenticalCertificate);
        }

        return IncusTransports.CreateSslOptions(args, validation);
    }

    internal EventListener CreateListenerRegistration(EventListener listener)
    {
        _activeListeners[Interlocked.Increment(ref _listenerSequence)] = listener;
        return listener;
    }

    internal void RemoveListenerRegistration(EventListener listener)
    {
        foreach (KeyValuePair<long, EventListener> entry in _activeListeners)
        {
            if (ReferenceEquals(entry.Value, listener))
            {
                _activeListeners.TryRemove(entry.Key, out _);
                return;
            }
        }
    }

    /// <summary>Establishes a raw websocket connection to the supplied API path.</summary>
    public async Task<IncusWebSocket> RawWebSocketAsync(string path, CancellationToken cancellationToken = default)
    {
        string url = _baseUri.ToString().TrimEnd('/') + "/1.0" + path;
        return await RawWebSocketAbsoluteAsync(url, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<IncusWebSocket> RawWebSocketAbsoluteAsync(string url, CancellationToken cancellationToken)
    {
        string? token = null;
        if (_args.OidcTokenProvider is not null)
            token = await _args.OidcTokenProvider(cancellationToken).ConfigureAwait(false);

        Uri uri = new(url);
        string pathAndQuery = uri.PathAndQuery;

        return await IncusWebSocket.ConnectAsync(
            _baseUri,
            _unixPath,
            pathAndQuery,
            _args.UserAgent,
            token,
            _requireAuthenticated,
            _sslOptions,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Establishes a raw HTTP upgrade connection (NBD, SFTP or port forwarding).</summary>
    internal async Task<Stream> RawConnectionAsync(
        HttpMethod method,
        string path,
        string protocol,
        object? data,
        CancellationToken cancellationToken)
    {
        string? token = null;
        if (_args.OidcTokenProvider is not null)
            token = await _args.OidcTokenProvider(cancellationToken).ConfigureAwait(false);

        string url = BuildQueryUrl(path);
        Uri uri = new(url);

        byte[]? body = null;
        if (data is not null)
            body = JsonSerializer.SerializeToUtf8Bytes(data, data.GetType(), IncusJson.Options);

        return await IncusRawConnection.ConnectAsync(
            _baseUri,
            _unixPath,
            uri.PathAndQuery,
            protocol,
            method,
            body,
            body is null ? null : "application/json",
            _args.UserAgent,
            token,
            _requireAuthenticated,
            _sslOptions,
            cancellationToken).ConfigureAwait(false);
    }

    internal string? GetProjectForConnectionInfo() => _project;

    internal void SetServer(Api.Server server) => _server = server;

    internal Api.Server? GetCachedServer() => _server;

    internal string? GetServerCertificate() => _serverCertificate;

    internal void SetServerCertificate(string? certificate) => _serverCertificate = certificate;

    internal bool SkipEvents
    {
        get => _skipEvents;
        set => _skipEvents = value;
    }

    internal CancellationToken ConnectedToken => _connectedCts.Token;

    internal IncusConnectionArgs ConnectionArgs => _args;
}

/// <summary>Basic information about an Incus connection.</summary>
public sealed class IncusConnectionInfo
{
    /// <summary>Server certificate (PEM), if known.</summary>
    public string Certificate { get; set; } = "";

    /// <summary>Protocol name.</summary>
    public string Protocol { get; set; } = "";

    /// <summary>Server URL.</summary>
    public string Url { get; set; } = "";

    /// <summary>Unix socket path.</summary>
    public string SocketPath { get; set; } = "";

    /// <summary>Project name.</summary>
    public string Project { get; set; } = "";

    /// <summary>Cluster target name.</summary>
    public string Target { get; set; } = "";

    /// <summary>Known server addresses.</summary>
    public List<string> Addresses { get; set; } = new();
}
