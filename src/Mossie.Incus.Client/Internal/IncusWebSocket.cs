using System.Buffers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Mossie.Incus.Client.Internal;

/// <summary>
/// WebSocket connection wrapper. Because <see cref="System.Net.WebSockets.ClientWebSocket"/> cannot
/// dial Unix domain sockets, the HTTP Upgrade handshake is performed manually for every transport.
/// </summary>
public sealed class IncusWebSocket : IAsyncDisposable
{
    private static readonly byte[] Crlf = "\r\n"u8.ToArray();
    private readonly System.Net.WebSockets.WebSocket _socket;
    private readonly Stream _stream;

    private IncusWebSocket(System.Net.WebSockets.WebSocket socket, Stream stream)
    {
        _socket = socket;
        _stream = stream;
    }

    internal System.Net.WebSockets.WebSocket Socket => _socket;

    internal static async Task<IncusWebSocket> ConnectAsync(
        Uri baseUri,
        string? unixPath,
        string pathAndQuery,
        string? userAgent,
        string? bearerToken,
        bool requireAuthenticated,
        SslClientAuthenticationOptions? sslOptions,
        CancellationToken cancellationToken)
    {
        bool secure = baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        Stream stream = await OpenStreamAsync(baseUri, unixPath, secure, sslOptions, cancellationToken).ConfigureAwait(false);

        try
        {
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            string hostHeader = BuildHostHeader(baseUri);

            StringBuilder request = new();
            request.Append("GET ").Append(pathAndQuery).Append(" HTTP/1.1\r\n");
            request.Append("Host: ").Append(hostHeader).Append("\r\n");
            request.Append("Upgrade: websocket\r\n");
            request.Append("Connection: Upgrade\r\n");
            request.Append("Sec-WebSocket-Key: ").Append(key).Append("\r\n");
            request.Append("Sec-WebSocket-Version: 13\r\n");

            if (!string.IsNullOrEmpty(userAgent))
                request.Append("User-Agent: ").Append(userAgent).Append("\r\n");

            if (requireAuthenticated)
                request.Append("X-Incus-authenticated: true\r\n");

            if (!string.IsNullOrEmpty(bearerToken))
                request.Append("Authorization: Bearer ").Append(bearerToken).Append("\r\n");

            request.Append("\r\n");

            byte[] payload = Encoding.ASCII.GetBytes(request.ToString());
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            (int statusCode, string statusText, Dictionary<string, string> headers, byte[] remainder) =
                await ReadHeadersAsync(stream, cancellationToken).ConfigureAwait(false);

            if (statusCode != 101)
            {
                string body = await ReadRemainingAsync(stream, remainder, cancellationToken).ConfigureAwait(false);
                string message = TryExtractError(body) ?? $"Websocket handshake failed: {statusCode} {statusText}".TrimEnd();
                throw new IncusStatusException(statusCode, message);
            }

            if (!headers.TryGetValue("Sec-WebSocket-Accept", out string? accept)
                || !string.Equals(accept, ComputeAccept(key), StringComparison.Ordinal))
            {
                throw new IncusException("Websocket handshake failed: invalid Sec-WebSocket-Accept header.");
            }

            if (remainder.Length > 0)
            {
                // Data arrived together with the headers; hand the stream back with the
                // leftover bytes buffered so the websocket framing isn't corrupted.
                stream = new PrefixedStream(remainder, stream);
            }

            System.Net.WebSockets.WebSocket socket = System.Net.WebSockets.WebSocket.CreateFromStream(
                stream,
                new System.Net.WebSockets.WebSocketCreationOptions
                {
                    IsServer = false,
                    KeepAliveInterval = TimeSpan.FromSeconds(30),
                });

            return new IncusWebSocket(socket, stream);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<Stream> OpenStreamAsync(
        Uri baseUri,
        string? unixPath,
        bool secure,
        SslClientAuthenticationOptions? sslOptions,
        CancellationToken cancellationToken)
    {
        Stream stream;

        if (!string.IsNullOrEmpty(unixPath))
        {
            Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(unixPath), cancellationToken).ConfigureAwait(false);
                stream = new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        else
        {
            string host = baseUri.DnsSafeHost;
            int port = baseUri.Port > 0 ? baseUri.Port : (secure ? 443 : 80);

            TcpClient client = new();
            try
            {
                await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
                stream = client.GetStream();
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        if (!secure)
            return stream;

        SslStream ssl = new(stream, leaveInnerStreamOpen: false);
        try
        {
            SslClientAuthenticationOptions options = sslOptions is null
                ? new SslClientAuthenticationOptions()
                : new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = sslOptions.EnabledSslProtocols,
                    ClientCertificates = sslOptions.ClientCertificates,
                    RemoteCertificateValidationCallback = sslOptions.RemoteCertificateValidationCallback,
                    CertificateRevocationCheckMode = sslOptions.CertificateRevocationCheckMode,
                };

            options.TargetHost = baseUri.DnsSafeHost;

            await ssl.AuthenticateAsClientAsync(options, cancellationToken).ConfigureAwait(false);
            return ssl;
        }
        catch
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static string BuildHostHeader(Uri baseUri)
    {
        bool defaultPort = (baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && baseUri.Port == 443)
            || (baseUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && baseUri.Port == 80);

        return defaultPort || baseUri.IsDefaultPort
            ? baseUri.DnsSafeHost
            : $"{baseUri.DnsSafeHost}:{baseUri.Port}";
    }

    private static string ComputeAccept(string key)
    {
        byte[] hash = SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
        return Convert.ToBase64String(hash);
    }

    internal static async Task<(int StatusCode, string StatusText, Dictionary<string, string> Headers, byte[] Remainder)>
        ReadHeadersAsync(Stream stream, CancellationToken cancellationToken)
    {
        List<byte> buffer = new(512);
        byte[] single = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(single, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new IncusException("Websocket handshake failed: connection closed.");

            buffer.Add(single[0]);

            if (buffer.Count >= 4
                && buffer[^4] == (byte)'\r' && buffer[^3] == (byte)'\n'
                && buffer[^2] == (byte)'\r' && buffer[^1] == (byte)'\n')
            {
                break;
            }

            if (buffer.Count > 64 * 1024)
                throw new IncusException("Websocket handshake failed: response headers too large.");
        }

        string text = Encoding.ASCII.GetString(buffer.ToArray());
        string[] lines = text.Split("\r\n", StringSplitOptions.None);

        string[] statusParts = lines[0].Split(' ', 3);
        if (statusParts.Length < 2 || !int.TryParse(statusParts[1], out int statusCode))
            throw new IncusException($"Websocket handshake failed: invalid status line \"{lines[0]}\".");

        string statusText = statusParts.Length > 2 ? statusParts[2] : "";

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
                continue;

            int colon = lines[i].IndexOf(':');
            if (colon <= 0)
                continue;

            headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }

        return (statusCode, statusText, headers, Array.Empty<byte>());
    }

    private static async Task<string> ReadRemainingAsync(Stream stream, byte[] prefix, CancellationToken cancellationToken)
    {
        using MemoryStream memory = new();
        if (prefix.Length > 0)
            await memory.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Encoding.UTF8.GetString(memory.ToArray());
    }

    internal static string? TryExtractError(string body)
    {
        try
        {
            Api.Response? response = Serialization.IncusJson.Deserialize<Api.Response>(body);
            return string.IsNullOrEmpty(response?.Error) ? null : response.Error;
        }
        catch
        {
            return null;
        }
    }

    internal Task SendTextAsync(string text, CancellationToken cancellationToken)
        => _socket.SendAsync(Encoding.UTF8.GetBytes(text), System.Net.WebSockets.WebSocketMessageType.Text, true, cancellationToken);

    internal Task SendBinaryAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        => _socket.SendAsync(data, System.Net.WebSockets.WebSocketMessageType.Binary, true, cancellationToken).AsTask();

    internal async Task<(System.Net.WebSockets.WebSocketMessageType Type, byte[] Data)> ReceiveAsync(CancellationToken cancellationToken)
    {
        using MemoryStream memory = new();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);

        try
        {
            while (true)
            {
                System.Net.WebSockets.WebSocketReceiveResult result =
                    await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    return (result.MessageType, Array.Empty<byte>());

                await memory.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);

                if (result.EndOfMessage)
                    return (result.MessageType, memory.ToArray());
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == System.Net.WebSockets.WebSocketState.Open)
            {
                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, cts.Token)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // Best effort.
        }

        _socket.Dispose();
        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>A read-only stream that serves prefetched bytes before continuing with an inner stream.</summary>
internal sealed class PrefixedStream : Stream
{
    private readonly byte[] _prefix;
    private readonly Stream _inner;
    private int _position;

    internal PrefixedStream(byte[] prefix, Stream inner)
    {
        _prefix = prefix;
        _inner = inner;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position < _prefix.Length)
        {
            int available = Math.Min(count, _prefix.Length - _position);
            Array.Copy(_prefix, _position, buffer, offset, available);
            _position += available;
            return available;
        }

        return _inner.Read(buffer, offset, count);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < _prefix.Length)
        {
            int available = Math.Min(buffer.Length, _prefix.Length - _position);
            _prefix.AsMemory(_position, available).CopyTo(buffer);
            _position += available;
            return available;
        }

        return await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
