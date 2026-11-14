using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the network names.</summary>
    public async Task<List<string>> GetNetworkNamesAsync(CancellationToken cancellationToken = default)
    {
        if (!HasExtension("network"))
            throw new IncusException("The server is missing the required \"network\" API extension");

        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, "/networks", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/networks", urls);
    }

    /// <summary>Returns all networks.</summary>
    public async Task<List<Api.Network>> GetNetworksAsync(CancellationToken cancellationToken = default)
    {
        if (!HasExtension("network"))
            throw new IncusException("The server is missing the required \"network\" API extension");

        (List<Api.Network> networks, _) = await QueryStructAsync<List<Api.Network>>(
            HttpMethod.Get, "/networks?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return networks;
    }

    /// <summary>Returns a filtered list of networks.</summary>
    public async Task<List<Api.Network>> GetNetworksWithFilterAsync(IEnumerable<string> filters, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("network"))
            throw new IncusException("The server is missing the required \"network\" API extension");

        string path = $"/networks?recursion=1&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}";

        (List<Api.Network> networks, _) = await QueryStructAsync<List<Api.Network>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return networks;
    }

    /// <summary>Returns all networks across all projects.</summary>
    public async Task<List<Api.Network>> GetNetworksAllProjectsAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("networks_all_projects");

        (List<Api.Network> networks, _) = await QueryStructAsync<List<Api.Network>>(
            HttpMethod.Get, "/networks?recursion=1&all-projects=true", cancellationToken: cancellationToken).ConfigureAwait(false);

        return networks;
    }

    /// <summary>Returns a filtered list of all networks across all projects.</summary>
    public async Task<List<Api.Network>> GetNetworksAllProjectsWithFilterAsync(IEnumerable<string> filters, CancellationToken cancellationToken = default)
    {
        CheckExtension("networks_all_projects");

        string path = $"/networks?recursion=1&all-projects=true&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}";

        (List<Api.Network> networks, _) = await QueryStructAsync<List<Api.Network>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return networks;
    }

    /// <summary>Returns a network entry.</summary>
    public async Task<(Api.Network Network, string ETag)> GetNetworkAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("network"))
            throw new IncusException("The server is missing the required \"network\" API extension");

        (Api.Network network, string etag) = await QueryStructAsync<Api.Network>(
            HttpMethod.Get, $"/networks/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (network, etag);
    }

    /// <summary>Returns the leases of a network.</summary>
    public async Task<List<Api.NetworkLease>> GetNetworkLeasesAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_leases");

        (List<Api.NetworkLease> leases, _) = await QueryStructAsync<List<Api.NetworkLease>>(
            HttpMethod.Get, $"/networks/{Uri.EscapeDataString(name)}/leases", cancellationToken: cancellationToken).ConfigureAwait(false);

        return leases;
    }

    /// <summary>Returns metrics and information about the running network.</summary>
    public async Task<Api.NetworkState> GetNetworkStateAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_state");

        (Api.NetworkState state, _) = await QueryStructAsync<Api.NetworkState>(
            HttpMethod.Get, $"/networks/{Uri.EscapeDataString(name)}/state", cancellationToken: cancellationToken).ConfigureAwait(false);

        return state;
    }

    /// <summary>Creates a new network.</summary>
    public async Task CreateNetworkAsync(Api.NetworksPost network, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("network"))
            throw new IncusException("The server is missing the required \"network\" API extension");

        await QueryAsync(HttpMethod.Post, "/networks", network, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates a network.</summary>
    public async Task UpdateNetworkAsync(string name, Api.NetworkPut network, string? etag = null, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("network"))
            throw new IncusException("The server is missing the required \"network\" API extension");

        await QueryAsync(HttpMethod.Put, $"/networks/{Uri.EscapeDataString(name)}", network, etag, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renames a network.</summary>
    public async Task RenameNetworkAsync(string name, Api.NetworkPost network, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("network"))
            throw new IncusException("The server is missing the required \"network\" API extension");

        await QueryAsync(HttpMethod.Post, $"/networks/{Uri.EscapeDataString(name)}", network, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a network.</summary>
    public async Task DeleteNetworkAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("network"))
            throw new IncusException("The server is missing the required \"network\" API extension");

        await QueryAsync(HttpMethod.Delete, $"/networks/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
