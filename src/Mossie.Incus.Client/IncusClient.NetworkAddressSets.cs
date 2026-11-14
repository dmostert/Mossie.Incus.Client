using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the network address set names.</summary>
    public async Task<List<string>> GetNetworkAddressSetNamesAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("network_address_set");

        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, "/network-address-sets", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/network-address-sets", urls);
    }

    /// <summary>Returns all network address sets.</summary>
    public async Task<List<Api.NetworkAddressSet>> GetNetworkAddressSetsAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("network_address_set");

        (List<Api.NetworkAddressSet> sets, _) = await QueryStructAsync<List<Api.NetworkAddressSet>>(
            HttpMethod.Get, "/network-address-sets?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return sets;
    }

    /// <summary>Returns all network address sets across all projects.</summary>
    public async Task<List<Api.NetworkAddressSet>> GetNetworkAddressSetsAllProjectsAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("network_address_set");

        (List<Api.NetworkAddressSet> sets, _) = await QueryStructAsync<List<Api.NetworkAddressSet>>(
            HttpMethod.Get, "/network-address-sets?recursion=1&all-projects=true", cancellationToken: cancellationToken).ConfigureAwait(false);

        return sets;
    }

    /// <summary>Returns a network address set entry.</summary>
    public async Task<(Api.NetworkAddressSet AddressSet, string ETag)> GetNetworkAddressSetAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_address_set");

        (Api.NetworkAddressSet set, string etag) = await QueryStructAsync<Api.NetworkAddressSet>(
            HttpMethod.Get, $"/network-address-sets/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (set, etag);
    }

    /// <summary>Creates a new network address set.</summary>
    public async Task CreateNetworkAddressSetAsync(Api.NetworkAddressSetsPost addressSet, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_address_set");

        await QueryAsync(HttpMethod.Post, "/network-address-sets", addressSet, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates a network address set.</summary>
    public async Task UpdateNetworkAddressSetAsync(string name, Api.NetworkAddressSetPut addressSet, string? etag = null, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_address_set");

        await QueryAsync(HttpMethod.Put, $"/network-address-sets/{Uri.EscapeDataString(name)}", addressSet, etag, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Renames a network address set.</summary>
    public async Task RenameNetworkAddressSetAsync(string name, Api.NetworkAddressSetPost addressSet, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_address_set");

        await QueryAsync(HttpMethod.Post, $"/network-address-sets/{Uri.EscapeDataString(name)}", addressSet, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Deletes a network address set.</summary>
    public async Task DeleteNetworkAddressSetAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_address_set");

        await QueryAsync(HttpMethod.Delete, $"/network-address-sets/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
