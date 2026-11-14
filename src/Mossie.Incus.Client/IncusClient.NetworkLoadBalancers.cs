using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the network load balancer listen addresses.</summary>
    public async Task<List<string>> GetNetworkLoadBalancerAddressesAsync(string networkName, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_load_balancer");

        string basePath = $"/networks/{Uri.EscapeDataString(networkName)}/load-balancers";
        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, basePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames(basePath, urls);
    }

    /// <summary>Returns the network load balancers.</summary>
    public async Task<List<Api.NetworkLoadBalancer>> GetNetworkLoadBalancersAsync(string networkName, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_load_balancer");

        (List<Api.NetworkLoadBalancer> loadBalancers, _) = await QueryStructAsync<List<Api.NetworkLoadBalancer>>(
            HttpMethod.Get, $"/networks/{Uri.EscapeDataString(networkName)}/load-balancers?recursion=1",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return loadBalancers;
    }

    /// <summary>Returns a network load balancer entry.</summary>
    public async Task<(Api.NetworkLoadBalancer LoadBalancer, string ETag)> GetNetworkLoadBalancerAsync(
        string networkName,
        string listenAddress,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("network_load_balancer");

        (Api.NetworkLoadBalancer loadBalancer, string etag) = await QueryStructAsync<Api.NetworkLoadBalancer>(
            HttpMethod.Get,
            $"/networks/{Uri.EscapeDataString(networkName)}/load-balancers/{Uri.EscapeDataString(listenAddress)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (loadBalancer, etag);
    }

    /// <summary>Creates a new network load balancer.</summary>
    public async Task CreateNetworkLoadBalancerAsync(string networkName, Api.NetworkLoadBalancersPost loadBalancer, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_load_balancer");

        await QueryAsync(
            HttpMethod.Post,
            $"/networks/{Uri.EscapeDataString(networkName)}/load-balancers",
            loadBalancer,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates a network load balancer.</summary>
    public async Task UpdateNetworkLoadBalancerAsync(
        string networkName,
        string listenAddress,
        Api.NetworkLoadBalancerPut loadBalancer,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("network_load_balancer");

        await QueryAsync(
            HttpMethod.Put,
            $"/networks/{Uri.EscapeDataString(networkName)}/load-balancers/{Uri.EscapeDataString(listenAddress)}",
            loadBalancer,
            etag,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a network load balancer.</summary>
    public async Task DeleteNetworkLoadBalancerAsync(string networkName, string listenAddress, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_load_balancer");

        await QueryAsync(
            HttpMethod.Delete,
            $"/networks/{Uri.EscapeDataString(networkName)}/load-balancers/{Uri.EscapeDataString(listenAddress)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the state of a network load balancer.</summary>
    public async Task<Api.NetworkLoadBalancerState> GetNetworkLoadBalancerStateAsync(
        string networkName,
        string listenAddress,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("network_load_balancer_state");

        (Api.NetworkLoadBalancerState state, _) = await QueryStructAsync<Api.NetworkLoadBalancerState>(
            HttpMethod.Get,
            $"/networks/{Uri.EscapeDataString(networkName)}/load-balancers/{Uri.EscapeDataString(listenAddress)}/state",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return state;
    }
}
