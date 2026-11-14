using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;

namespace Mossie.Incus.Client;

/// <summary>
/// Common connection properties, mirroring the Go <c>incus.ConnectionArgs</c> type.
/// </summary>
public sealed class IncusConnectionArgs
{
    /// <summary>TLS certificate of the remote server (PEM). If not specified, the system CA is used.</summary>
    public string? TlsServerCert { get; set; }

    /// <summary>TLS certificate to use for client authentication (PEM).</summary>
    public string? TlsClientCert { get; set; }

    /// <summary>TLS key to use for client authentication (PEM).</summary>
    public string? TlsClientKey { get; set; }

    /// <summary>TLS CA to validate against when in PKI mode (PEM).</summary>
    public string? TlsCa { get; set; }

    /// <summary>User agent string sent with every request.</summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Authentication type. Use <c>tls</c> (default) or <c>oidc</c>.
    /// When set to <c>oidc</c>, <see cref="OidcTokenProvider"/> should be supplied.
    /// </summary>
    public string? AuthType { get; set; }

    /// <summary>Custom proxy applied to the HTTP transport.</summary>
    public IWebProxy? Proxy { get; set; }

    /// <summary>
    /// Custom HTTP client used as the base for the connection. When set, the client's
    /// transport is used as-is (Unix socket connections still use an internally built
    /// transport because the socket path is required).
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>Wraps the handler created by the library (equivalent of Go's HTTPTransporter).</summary>
    public Func<HttpMessageHandler, HttpMessageHandler>? TransportWrapper { get; set; }

    /// <summary>Skip verification of the server certificate chain and host name.</summary>
    public bool InsecureSkipVerify { get; set; }

    /// <summary>Perform an exact certificate match, ignoring expiry and any other validation issue.</summary>
    public bool IdenticalCertificate { get; set; }

    /// <summary>Cookie container used for the connection.</summary>
    public CookieContainer? CookieJar { get; set; }

    /// <summary>
    /// Supplies the current OIDC token set. The callback is invoked for each request and may
    /// refresh the tokens. This replaces Go's interactive Zitadel device/browser flow; the last
    /// returned token set is cached and exposed via <see cref="IncusClient.GetOIDCTokens"/>.
    /// </summary>
    public Func<CancellationToken, Task<OidcTokens?>>? OidcTokenProvider { get; set; }

    /// <summary>Do not block for OIDC authentication (kept for API parity).</summary>
    public bool OidcNonInteractive { get; set; }

    /// <summary>Skip automatically connecting to the events endpoint.</summary>
    public bool SkipGetEvents { get; set; }

    /// <summary>Skip the automatic GetServer request upon connection.</summary>
    public bool SkipGetServer { get; set; }

    /// <summary>Caching support for image servers.</summary>
    public string? CachePath { get; set; }

    /// <summary>Cache expiry for image servers. Defaults to one hour.</summary>
    public TimeSpan CacheExpiry { get; set; }

    /// <summary>Temporary storage path.</summary>
    public string? TempPath { get; set; }

    /// <summary>Client certificates loaded from <see cref="TlsClientCert"/> and <see cref="TlsClientKey"/>.</summary>
    internal X509Certificate2? LoadClientCertificate()
    {
        if (string.IsNullOrEmpty(TlsClientCert) || string.IsNullOrEmpty(TlsClientKey))
            return null;

        X509Certificate2 cert = X509Certificate2.CreateFromPem(TlsClientCert, TlsClientKey);
        // Re-import so that the private key is usable by SslStream on all platforms.
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pkcs12), null);
#else
        return new X509Certificate2(cert.Export(X509ContentType.Pkcs12));
#endif
    }
}

/// <summary>
/// A set of OpenID Connect tokens, mirroring the Go <c>oidc.Tokens[*oidc.IDTokenClaims]</c> type.
/// </summary>
public sealed class OidcTokens
{
    /// <summary>Bearer access token sent in the <c>Authorization</c> header.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Token type (for example <c>Bearer</c>).</summary>
    public string? TokenType { get; set; }

    /// <summary>Refresh token, when one was issued by the provider.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>Expiry of the access token.</summary>
    public DateTimeOffset? Expiry { get; set; }

    /// <summary>Raw ID token (JWT), sent alongside the access token when it is opaque.</summary>
    public string? IdToken { get; set; }
}
