using Mossie.Incus.Client.Api;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the network allocations for the current project.</summary>
    public async Task<List<Api.NetworkAllocations>> GetNetworkAllocationsAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("network_allocations");

        (List<Api.NetworkAllocations> allocations, _) = await QueryStructAsync<List<Api.NetworkAllocations>>(
            HttpMethod.Get, "/network-allocations", cancellationToken: cancellationToken).ConfigureAwait(false);

        return allocations;
    }

    /// <summary>Returns the network allocations across all projects.</summary>
    public async Task<List<Api.NetworkAllocations>> GetNetworkAllocationsAllProjectsAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("network_allocations");

        (List<Api.NetworkAllocations> allocations, _) = await QueryStructAsync<List<Api.NetworkAllocations>>(
            HttpMethod.Get, "/network-allocations?all-projects=true", cancellationToken: cancellationToken).ConfigureAwait(false);

        return allocations;
    }
}
