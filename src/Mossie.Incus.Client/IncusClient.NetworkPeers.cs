using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the network peer names.</summary>
    public async Task<List<string>> GetNetworkPeerNamesAsync(string networkName, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_peer");

        string basePath = $"/networks/{Uri.EscapeDataString(networkName)}/peers";
        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, basePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames(basePath, urls);
    }

    /// <summary>Returns the network peers.</summary>
    public async Task<List<Api.NetworkPeer>> GetNetworkPeersAsync(string networkName, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_peer");

        (List<Api.NetworkPeer> peers, _) = await QueryStructAsync<List<Api.NetworkPeer>>(
            HttpMethod.Get, $"/networks/{Uri.EscapeDataString(networkName)}/peers?recursion=1",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return peers;
    }

    /// <summary>Returns a network peer entry.</summary>
    public async Task<(Api.NetworkPeer Peer, string ETag)> GetNetworkPeerAsync(
        string networkName,
        string peerName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("network_peer");

        (Api.NetworkPeer peer, string etag) = await QueryStructAsync<Api.NetworkPeer>(
            HttpMethod.Get,
            $"/networks/{Uri.EscapeDataString(networkName)}/peers/{Uri.EscapeDataString(peerName)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (peer, etag);
    }

    /// <summary>Creates a new network peer.</summary>
    public async Task CreateNetworkPeerAsync(string networkName, Api.NetworkPeersPost peer, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_peer");

        if (!string.IsNullOrEmpty(peer.Type) && peer.Type != "local")
            CheckExtension("network_integrations");

        await QueryAsync(HttpMethod.Post, $"/networks/{Uri.EscapeDataString(networkName)}/peers", peer, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Updates a network peer.</summary>
    public async Task UpdateNetworkPeerAsync(
        string networkName,
        string peerName,
        Api.NetworkPeerPut peer,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("network_peer");

        await QueryAsync(
            HttpMethod.Put,
            $"/networks/{Uri.EscapeDataString(networkName)}/peers/{Uri.EscapeDataString(peerName)}",
            peer,
            etag,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a network peer.</summary>
    public async Task DeleteNetworkPeerAsync(string networkName, string peerName, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_peer");

        await QueryAsync(
            HttpMethod.Delete,
            $"/networks/{Uri.EscapeDataString(networkName)}/peers/{Uri.EscapeDataString(peerName)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
