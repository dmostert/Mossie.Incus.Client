using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the warning UUIDs.</summary>
    public async Task<List<string>> GetWarningUUIDsAsync(CancellationToken cancellationToken = default)
    {
        if (!HasExtension("warnings"))
            throw new IncusException("The server is missing the required \"warnings\" API extension");

        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, "/warnings", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/warnings", urls);
    }

    /// <summary>Returns all warnings.</summary>
    public async Task<List<Api.Warning>> GetWarningsAsync(CancellationToken cancellationToken = default)
    {
        if (!HasExtension("warnings"))
            throw new IncusException("The server is missing the required \"warnings\" API extension");

        (List<Api.Warning> warnings, _) = await QueryStructAsync<List<Api.Warning>>(
            HttpMethod.Get, "/warnings?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return warnings;
    }

    /// <summary>Returns a warning by UUID.</summary>
    public async Task<(Api.Warning Warning, string ETag)> GetWarningAsync(string uuid, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("warnings"))
            throw new IncusException("The server is missing the required \"warnings\" API extension");

        (Api.Warning warning, string etag) = await QueryStructAsync<Api.Warning>(
            HttpMethod.Get, $"/warnings/{Uri.EscapeDataString(uuid)}", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (warning, etag);
    }

    /// <summary>Updates a warning.</summary>
    public async Task UpdateWarningAsync(string uuid, Api.WarningPut warning, string? etag = null, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("warnings"))
            throw new IncusException("The server is missing the required \"warnings\" API extension");

        await QueryAsync(HttpMethod.Put, $"/warnings/{Uri.EscapeDataString(uuid)}", warning, etag, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a warning.</summary>
    public async Task DeleteWarningAsync(string uuid, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("warnings"))
            throw new IncusException("The server is missing the required \"warnings\" API extension");

        await QueryAsync(HttpMethod.Delete, $"/warnings/{Uri.EscapeDataString(uuid)}", cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
