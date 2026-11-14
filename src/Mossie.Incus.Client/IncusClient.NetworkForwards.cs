using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the network forward listen addresses.</summary>
    public async Task<List<string>> GetNetworkForwardAddressesAsync(string networkName, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_forward");

        string basePath = $"/networks/{Uri.EscapeDataString(networkName)}/forwards";
        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, basePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames(basePath, urls);
    }

    /// <summary>Returns the network forwards.</summary>
    public async Task<List<Api.NetworkForward>> GetNetworkForwardsAsync(string networkName, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_forward");

        (List<Api.NetworkForward> forwards, _) = await QueryStructAsync<List<Api.NetworkForward>>(
            HttpMethod.Get, $"/networks/{Uri.EscapeDataString(networkName)}/forwards?recursion=1",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return forwards;
    }

    /// <summary>Returns a network forward entry.</summary>
    public async Task<(Api.NetworkForward Forward, string ETag)> GetNetworkForwardAsync(
        string networkName,
        string listenAddress,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("network_forward");

        (Api.NetworkForward forward, string etag) = await QueryStructAsync<Api.NetworkForward>(
            HttpMethod.Get,
            $"/networks/{Uri.EscapeDataString(networkName)}/forwards/{Uri.EscapeDataString(listenAddress)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (forward, etag);
    }

    /// <summary>Creates a new network forward.</summary>
    public async Task CreateNetworkForwardAsync(string networkName, Api.NetworkForwardsPost forward, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_forward");

        await QueryAsync(HttpMethod.Post, $"/networks/{Uri.EscapeDataString(networkName)}/forwards", forward, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Updates a network forward.</summary>
    public async Task UpdateNetworkForwardAsync(
        string networkName,
        string listenAddress,
        Api.NetworkForwardPut forward,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("network_forward");

        await QueryAsync(
            HttpMethod.Put,
            $"/networks/{Uri.EscapeDataString(networkName)}/forwards/{Uri.EscapeDataString(listenAddress)}",
            forward,
            etag,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a network forward.</summary>
    public async Task DeleteNetworkForwardAsync(string networkName, string listenAddress, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_forward");

        await QueryAsync(
            HttpMethod.Delete,
            $"/networks/{Uri.EscapeDataString(networkName)}/forwards/{Uri.EscapeDataString(listenAddress)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
