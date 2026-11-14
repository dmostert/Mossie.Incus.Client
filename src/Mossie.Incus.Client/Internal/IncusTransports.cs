using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Mossie.Incus.Client.Internal;

/// <summary>TLS/HTTP transport construction mirroring the Go client's <c>tlsHTTPClient</c> and <c>unixHTTPClient</c>.</summary>
internal static class IncusTransports
{
    private const string InsecureTlsEnvVar = "INCUS_INSECURE_TLS";

    /// <summary>Creates the handler and validation state used for HTTPS connections.</summary>
    internal static (HttpMessageHandler Handler, RemoteCertificateValidationCallback? Validation, X509Certificate2? ExpectedCertificate)
        CreateHttpsHandler(IncusConnectionArgs args)
    {
        X509Certificate2? expected = ParseFirstCertificate(args.TlsServerCert);
        X509Certificate2Collection? customRoots = ParseCertificates(args.TlsCa);

        RemoteCertificateValidationCallback? validation = null;
        if (args.IdenticalCertificate || args.InsecureSkipVerify || expected is not null || customRoots is not null)
        {
            validation = (sender, certificate, chain, errors) =>
                ValidateCertificate(certificate, expected, customRoots, args.InsecureSkipVerify, args.IdenticalCertificate);
        }

        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            Expect100ContinueTimeout = TimeSpan.FromSeconds(30),
            ResponseDrainTimeout = TimeSpan.Zero,
            PooledConnectionLifetime = TimeSpan.Zero,
            PooledConnectionIdleTimeout = TimeSpan.Zero,
            SslOptions = CreateSslOptions(args, validation),
        };

        if (args.Proxy is not null)
            handler.Proxy = args.Proxy;

        return (handler, validation, expected);
    }

    /// <summary>Creates a handler that dials a Unix domain socket.</summary>
    internal static HttpMessageHandler CreateUnixHandler(IncusConnectionArgs args, string socketPath)
    {
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            Expect100ContinueTimeout = TimeSpan.FromSeconds(30),
            ResponseDrainTimeout = TimeSpan.Zero,
            PooledConnectionLifetime = TimeSpan.Zero,
            PooledConnectionIdleTimeout = TimeSpan.Zero,
            UseProxy = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

        return handler;
    }

    internal static SslClientAuthenticationOptions CreateSslOptions(IncusConnectionArgs args, RemoteCertificateValidationCallback? validation)
    {
        SslClientAuthenticationOptions options = new()
        {
            EnabledSslProtocols = IsInsecureTlsEnabled()
                ? SslProtocols.Tls12 | SslProtocols.Tls13
                : SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };

        X509Certificate2? clientCertificate = args.LoadClientCertificate();
        if (clientCertificate is not null)
            options.ClientCertificates = new X509CertificateCollection { clientCertificate };

        if (validation is not null)
            options.RemoteCertificateValidationCallback = validation;

        return options;
    }

    internal static bool ValidateCertificate(
        X509Certificate? certificate,
        X509Certificate2? expected,
        X509Certificate2Collection? customRoots,
        bool insecureSkipVerify,
        bool identicalCertificate)
    {
        if (certificate is null)
            return false;

        X509Certificate2 actual = certificate as X509Certificate2 ?? new X509Certificate2(certificate);

        if (identicalCertificate)
        {
            // Exact byte-for-byte match; ignore expiry, host name and chain issues.
            return expected is not null && actual.RawData.AsSpan().SequenceEqual(expected.RawData);
        }

        if (insecureSkipVerify)
            return true;

        using X509Chain chain = new();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

        if (customRoots is not null && customRoots.Count > 0)
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            foreach (X509Certificate2 root in customRoots)
                chain.ChainPolicy.CustomTrustStore.Add(root);

            if (expected is not null)
                chain.ChainPolicy.CustomTrustStore.Add(expected);
        }
        else if (expected is not null)
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(expected);
        }
        else
        {
            return true;
        }

        if (!chain.Build(actual))
            return false;

        // When an explicit certificate is supplied we trust it directly rather than
        // requiring the request host name to appear in its SANs, matching Go's behavior
        // of switching the TLS server name to the certificate's first DNS name.
        return true;
    }

    private static bool IsInsecureTlsEnabled()
    {
        string? value = Environment.GetEnvironmentVariable(InsecureTlsEnvVar);
        return !string.IsNullOrEmpty(value) && value is not "false" and not "0";
    }

    internal static X509Certificate2? ParseFirstCertificate(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
            return null;

        try
        {
            return X509Certificate2.CreateFromPem(pem);
        }
        catch (Exception ex)
        {
            throw new IncusException("Invalid remote certificate", ex);
        }
    }

    internal static X509Certificate2Collection? ParseCertificates(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
            return null;

        X509Certificate2Collection collection = new();
        try
        {
            collection.ImportFromPem(pem);
        }
        catch (Exception ex)
        {
            throw new IncusException("Invalid CA certificate", ex);
        }

        return collection;
    }

    /// <summary>Builds an <see cref="HttpClient"/> using the supplied handler and connection arguments.</summary>
    internal static HttpClient CreateClient(IncusConnectionArgs args, HttpMessageHandler handler, Uri baseAddress)
    {
        HttpMessageHandler finalHandler = args.TransportWrapper is null ? handler : args.TransportWrapper(handler);

        HttpClient client = new(finalHandler, disposeHandler: true)
        {
            BaseAddress = baseAddress,
            Timeout = Timeout.InfiniteTimeSpan,
        };

        if (!string.IsNullOrEmpty(args.UserAgent))
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", args.UserAgent);

        return client;
    }
}
