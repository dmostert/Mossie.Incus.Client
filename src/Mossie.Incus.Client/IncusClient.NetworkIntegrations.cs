using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the network integration names.</summary>
    public async Task<List<string>> GetNetworkIntegrationNamesAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("network_integrations");

        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, "/network-integrations", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/network-integrations", urls);
    }

    /// <summary>Returns the network integrations.</summary>
    public async Task<List<Api.NetworkIntegration>> GetNetworkIntegrationsAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("network_integrations");

        (List<Api.NetworkIntegration> integrations, _) = await QueryStructAsync<List<Api.NetworkIntegration>>(
            HttpMethod.Get, "/network-integrations?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return integrations;
    }

    /// <summary>Returns a network integration entry.</summary>
    public async Task<(Api.NetworkIntegration Integration, string ETag)> GetNetworkIntegrationAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_integrations");

        (Api.NetworkIntegration integration, string etag) = await QueryStructAsync<Api.NetworkIntegration>(
            HttpMethod.Get, $"/network-integrations/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (integration, etag);
    }

    /// <summary>Creates a new network integration.</summary>
    public async Task CreateNetworkIntegrationAsync(Api.NetworkIntegrationsPost integration, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_integrations");

        await QueryAsync(HttpMethod.Post, "/network-integrations", integration, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates a network integration.</summary>
    public async Task UpdateNetworkIntegrationAsync(string name, Api.NetworkIntegrationPut integration, string? etag = null, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_integrations");

        await QueryAsync(HttpMethod.Put, $"/network-integrations/{Uri.EscapeDataString(name)}", integration, etag, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Renames a network integration.</summary>
    public async Task RenameNetworkIntegrationAsync(string name, Api.NetworkIntegrationPost integration, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_integrations");

        await QueryAsync(HttpMethod.Post, $"/network-integrations/{Uri.EscapeDataString(name)}", integration, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Deletes a network integration.</summary>
    public async Task DeleteNetworkIntegrationAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_integrations");

        await QueryAsync(HttpMethod.Delete, $"/network-integrations/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
