using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the certificate fingerprints.</summary>
    public async Task<List<string>> GetCertificateFingerprintsAsync(CancellationToken cancellationToken = default)
    {
        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, "/certificates", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/certificates", urls);
    }

    /// <summary>Returns all certificates.</summary>
    public async Task<List<Api.Certificate>> GetCertificatesAsync(CancellationToken cancellationToken = default)
    {
        (List<Api.Certificate> certificates, _) = await QueryStructAsync<List<Api.Certificate>>(
            HttpMethod.Get, "/certificates?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return certificates;
    }

    /// <summary>Returns a filtered list of certificates.</summary>
    public async Task<List<Api.Certificate>> GetCertificatesWithFilterAsync(IEnumerable<string> filters, CancellationToken cancellationToken = default)
    {
        string path = $"/certificates?recursion=1&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}";

        (List<Api.Certificate> certificates, _) = await QueryStructAsync<List<Api.Certificate>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return certificates;
    }

    /// <summary>Returns a certificate entry.</summary>
    public async Task<(Api.Certificate Certificate, string ETag)> GetCertificateAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        (Api.Certificate certificate, string etag) = await QueryStructAsync<Api.Certificate>(
            HttpMethod.Get, $"/certificates/{Uri.EscapeDataString(fingerprint)}", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (certificate, etag);
    }

    /// <summary>Adds a certificate to the trust store.</summary>
    public async Task CreateCertificateAsync(Api.CertificatesPost certificate, CancellationToken cancellationToken = default)
    {
        await QueryAsync(HttpMethod.Post, "/certificates", certificate, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates a certificate definition.</summary>
    public async Task UpdateCertificateAsync(string fingerprint, Api.CertificatePut certificate, string? etag = null, CancellationToken cancellationToken = default)
    {
        CheckExtension("certificate_update");

        await QueryAsync(HttpMethod.Put, $"/certificates/{Uri.EscapeDataString(fingerprint)}", certificate, etag, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Removes a certificate from the trust store.</summary>
    public async Task DeleteCertificateAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        await QueryAsync(HttpMethod.Delete, $"/certificates/{Uri.EscapeDataString(fingerprint)}", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Requests a certificate add token.</summary>
    public async Task<Operation> CreateCertificateTokenAsync(Api.CertificatesPost certificate, CancellationToken cancellationToken = default)
    {
        CheckExtension("certificate_token");

        if (!certificate.Token)
            throw new IncusException("Token needs to be true if requesting a token");

        (Operation operation, _) = await QueryOperationAsync(HttpMethod.Post, "/certificates", certificate, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return operation;
    }
}
