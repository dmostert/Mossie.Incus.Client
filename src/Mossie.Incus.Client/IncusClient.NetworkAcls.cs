using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the network ACL names.</summary>
    public async Task<List<string>> GetNetworkACLNamesAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("network_acl");

        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, "/network-acls", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/network-acls", urls);
    }

    /// <summary>Returns all network ACLs.</summary>
    public async Task<List<Api.NetworkACL>> GetNetworkACLsAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("network_acl");

        (List<Api.NetworkACL> acls, _) = await QueryStructAsync<List<Api.NetworkACL>>(
            HttpMethod.Get, "/network-acls?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return acls;
    }

    /// <summary>Returns all network ACLs across all projects.</summary>
    public async Task<List<Api.NetworkACL>> GetNetworkACLsAllProjectsAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("network_acls_all_projects");

        (List<Api.NetworkACL> acls, _) = await QueryStructAsync<List<Api.NetworkACL>>(
            HttpMethod.Get, "/network-acls?recursion=1&all-projects=true", cancellationToken: cancellationToken).ConfigureAwait(false);

        return acls;
    }

    /// <summary>Returns a network ACL entry.</summary>
    public async Task<(Api.NetworkACL Acl, string ETag)> GetNetworkACLAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_acl");

        (Api.NetworkACL acl, string etag) = await QueryStructAsync<Api.NetworkACL>(
            HttpMethod.Get, $"/network-acls/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (acl, etag);
    }

    /// <summary>Returns the ACL log file.</summary>
    public async Task<Stream> GetNetworkACLLogfileAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_acl_log");

        (Stream stream, _, HttpResponseMessage response) = await QueryStreamAsync(
            HttpMethod.Get, $"/network-acls/{Uri.EscapeDataString(name)}/log", cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ResponseStream(stream, response);
    }

    /// <summary>Creates a new network ACL.</summary>
    public async Task CreateNetworkACLAsync(Api.NetworkACLsPost acl, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_acl");

        await QueryAsync(HttpMethod.Post, "/network-acls", acl, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates a network ACL.</summary>
    public async Task UpdateNetworkACLAsync(string name, Api.NetworkACLPut acl, string? etag = null, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_acl");

        await QueryAsync(HttpMethod.Put, $"/network-acls/{Uri.EscapeDataString(name)}", acl, etag, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renames a network ACL.</summary>
    public async Task RenameNetworkACLAsync(string name, Api.NetworkACLPost acl, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_acl");

        await QueryAsync(HttpMethod.Post, $"/network-acls/{Uri.EscapeDataString(name)}", acl, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a network ACL.</summary>
    public async Task DeleteNetworkACLAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_acl");

        await QueryAsync(HttpMethod.Delete, $"/network-acls/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
