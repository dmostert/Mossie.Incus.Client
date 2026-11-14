using System.Net.Security;
using System.Text;

namespace Mossie.Incus.Client.Internal;

/// <summary>
/// Performs the HTTP Upgrade handshake used by the raw Incus endpoints (NBD, SFTP and
/// port forwarding). Unlike <see cref="IncusWebSocket"/> these protocols do not use
/// WebSocket framing: the upgraded connection is returned as a plain stream.
/// </summary>
internal static class IncusRawConnection
{
    internal static async Task<Stream> ConnectAsync(
        Uri baseUri,
        string? unixPath,
        string pathAndQuery,
        string upgradeProtocol,
        HttpMethod method,
        byte[]? body,
        string? contentType,
        string? userAgent,
        string? bearerToken,
        bool requireAuthenticated,
        SslClientAuthenticationOptions? sslOptions,
        CancellationToken cancellationToken)
    {
        bool secure = baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        Stream stream = await IncusWebSocket.OpenStreamAsync(baseUri, unixPath, secure, sslOptions, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            StringBuilder request = new();
            request.Append(method.Method).Append(' ').Append(pathAndQuery).Append(" HTTP/1.1\r\n");
            request.Append("Host: ").Append(IncusWebSocket.BuildHostHeader(baseUri)).Append("\r\n");
            request.Append("Upgrade: ").Append(upgradeProtocol).Append("\r\n");
            request.Append("Connection: Upgrade\r\n");

            if (!string.IsNullOrEmpty(contentType))
                request.Append("Content-Type: ").Append(contentType).Append("\r\n");

            if (body is not null)
                request.Append("Content-Length: ").Append(body.Length).Append("\r\n");

            if (!string.IsNullOrEmpty(userAgent))
                request.Append("User-Agent: ").Append(userAgent).Append("\r\n");

            if (requireAuthenticated)
                request.Append("X-Incus-authenticated: true\r\n");

            if (!string.IsNullOrEmpty(bearerToken))
                request.Append("Authorization: Bearer ").Append(bearerToken).Append("\r\n");

            request.Append("\r\n");

            byte[] header = Encoding.ASCII.GetBytes(request.ToString());
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            if (body is not null)
                await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            (int statusCode, string statusText, Dictionary<string, string> headers, _) =
                await IncusWebSocket.ReadHeadersAsync(stream, cancellationToken).ConfigureAwait(false);

            if (statusCode != 101)
            {
                string responseBody = await ReadRemainingAsync(stream, cancellationToken).ConfigureAwait(false);
                string message = IncusWebSocket.TryExtractError(responseBody)
                    ?? $"Failed to upgrade connection: {statusCode} {statusText}".TrimEnd();
                throw new IncusStatusException(statusCode, message);
            }

            if (!headers.TryGetValue("Upgrade", out string? upgrade)
                || !string.Equals(upgrade, upgradeProtocol, StringComparison.OrdinalIgnoreCase))
            {
                throw new IncusException("Missing or unexpected Upgrade header in response");
            }

            return stream;
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<string> ReadRemainingAsync(Stream stream, CancellationToken cancellationToken)
    {
        using MemoryStream memory = new();
        byte[] buffer = new byte[8192];

        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(memory.ToArray());
    }
}
