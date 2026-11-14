using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns information about the cluster.</summary>
    public async Task<(Api.Cluster Cluster, string ETag)> GetClusterAsync(CancellationToken cancellationToken = default)
    {
        if (!HasExtension("clustering"))
            throw new IncusException("The server is missing the required \"clustering\" API extension");

        (Api.Cluster cluster, string etag) = await QueryStructAsync<Api.Cluster>(
            HttpMethod.Get, "/cluster", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (cluster, etag);
    }

    /// <summary>Bootstraps a new cluster or joins an existing one.</summary>
    public async Task<Operation> UpdateClusterAsync(Api.ClusterPut cluster, string? etag = null, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("clustering"))
            throw new IncusException("The server is missing the required \"clustering\" API extension");

        if (!string.IsNullOrEmpty(cluster.ServerAddress)
            || !string.IsNullOrEmpty(cluster.ClusterToken)
            || (cluster.MemberConfig is { Count: > 0 }))
        {
            CheckExtension("clustering_join");
        }

        (Operation operation, _) = await QueryOperationAsync(HttpMethod.Put, "/cluster", cluster, etag, cancellationToken)
            .ConfigureAwait(false);

        return operation;
    }

    /// <summary>Removes the given member from the cluster.</summary>
    public async Task DeleteClusterMemberAsync(string name, bool force = false, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("clustering"))
            throw new IncusException("The server is missing the required \"clustering\" API extension");

        string path = $"/cluster/members/{name}" + (force ? "?force=1" : "");
        await QueryAsync(HttpMethod.Delete, path, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes a pending member from the cluster.</summary>
    public async Task DeletePendingClusterMemberAsync(string name, bool force = false, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("clustering"))
            throw new IncusException("The server is missing the required \"clustering\" API extension");

        string path = $"/cluster/members/{name}?pending=1" + (force ? "&force=1" : "");
        await QueryAsync(HttpMethod.Delete, path, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the URLs of the current cluster members.</summary>
    public async Task<List<string>> GetClusterMemberNamesAsync(CancellationToken cancellationToken = default)
    {
        if (!HasExtension("clustering"))
            throw new IncusException("The server is missing the required \"clustering\" API extension");

        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, "/cluster/members", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/cluster/members", urls);
    }

    /// <summary>Returns a filtered list of cluster members.</summary>
    public async Task<List<Api.ClusterMember>> GetClusterMembersWithFilterAsync(IEnumerable<string> filters, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("clustering"))
            throw new IncusException("The server is missing the required \"clustering\" API extension");

        string path = $"/cluster/members?recursion=1&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}";

        (List<Api.ClusterMember> members, _) = await QueryStructAsync<List<Api.ClusterMember>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return members;
    }

    /// <summary>Returns the current cluster members.</summary>
    public async Task<List<Api.ClusterMember>> GetClusterMembersAsync(CancellationToken cancellationToken = default)
    {
        if (!HasExtension("clustering"))
            throw new IncusException("The server is missing the required \"clustering\" API extension");

        (List<Api.ClusterMember> members, _) = await QueryStructAsync<List<Api.ClusterMember>>(
            HttpMethod.Get, "/cluster/members?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return members;
    }

    /// <summary>Returns information about a cluster member.</summary>
    public async Task<(Api.ClusterMember Member, string ETag)> GetClusterMemberAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("clustering"))
            throw new IncusException("The server is missing the required \"clustering\" API extension");

        (Api.ClusterMember member, string etag) = await QueryStructAsync<Api.ClusterMember>(
            HttpMethod.Get, $"/cluster/members/{name}", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (member, etag);
    }

    /// <summary>Updates information about a cluster member.</summary>
    public async Task UpdateClusterMemberAsync(string name, Api.ClusterMemberPut member, string? etag = null, CancellationToken cancellationToken = default)
    {
        CheckExtension("clustering_edit_roles");

        if (!string.IsNullOrEmpty(member.FailureDomain))
            CheckExtension("clustering_failure_domains");

        await QueryAsync(HttpMethod.Put, $"/cluster/members/{name}", member, etag, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renames an existing cluster member.</summary>
    public async Task RenameClusterMemberAsync(string name, Api.ClusterMemberPost member, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("clustering"))
            throw new IncusException("The server is missing the required \"clustering\" API extension");

        await QueryAsync(HttpMethod.Post, $"/cluster/members/{name}", member, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Generates a join token to add a cluster member.</summary>
    public async Task<Operation> CreateClusterMemberAsync(Api.ClusterMembersPost member, CancellationToken cancellationToken = default)
    {
        CheckExtension("clustering_join_token");

        (Operation operation, _) = await QueryOperationAsync(HttpMethod.Post, "/cluster/members", member, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return operation;
    }

    /// <summary>Updates the cluster certificate for every node.</summary>
    public async Task UpdateClusterCertificateAsync(Api.ClusterCertificatePut certificates, string? etag = null, CancellationToken cancellationToken = default)
    {
        CheckExtension("clustering_update_cert");

        await QueryAsync(HttpMethod.Put, "/cluster/certificate", certificates, etag, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns state information about a cluster member.</summary>
    public async Task<(Api.ClusterMemberState State, string ETag)> GetClusterMemberStateAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("cluster_member_state");

        (Api.ClusterMemberState state, string etag) = await QueryStructAsync<Api.ClusterMemberState>(
            HttpMethod.Get, $"/cluster/members/{name}/state", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (state, etag);
    }

    /// <summary>Evacuates or restores a cluster member.</summary>
    public async Task<Operation> UpdateClusterMemberStateAsync(string name, Api.ClusterMemberStatePost state, CancellationToken cancellationToken = default)
    {
        CheckExtension("clustering_evacuation");

        (Operation operation, _) = await QueryOperationAsync(HttpMethod.Post, $"/cluster/members/{name}/state", state, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return operation;
    }

    /// <summary>Returns the cluster groups.</summary>
    public async Task<List<Api.ClusterGroup>> GetClusterGroupsAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("clustering_groups");

        (List<Api.ClusterGroup> groups, _) = await QueryStructAsync<List<Api.ClusterGroup>>(
            HttpMethod.Get, "/cluster/groups?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return groups;
    }

    /// <summary>Returns the cluster group names.</summary>
    public async Task<List<string>> GetClusterGroupNamesAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("clustering_groups");

        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, "/cluster/groups", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/cluster/groups", urls);
    }

    /// <summary>Renames an existing cluster group.</summary>
    public async Task RenameClusterGroupAsync(string name, Api.ClusterGroupPost group, CancellationToken cancellationToken = default)
    {
        CheckExtension("clustering_groups");

        await QueryAsync(HttpMethod.Post, $"/cluster/groups/{name}", group, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a new cluster group.</summary>
    public async Task CreateClusterGroupAsync(Api.ClusterGroupsPost group, CancellationToken cancellationToken = default)
    {
        CheckExtension("clustering_groups");

        await QueryAsync(HttpMethod.Post, "/cluster/groups", group, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes an existing cluster group.</summary>
    public async Task DeleteClusterGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("clustering_groups");

        await QueryAsync(HttpMethod.Delete, $"/cluster/groups/{name}", cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates information about a cluster group.</summary>
    public async Task UpdateClusterGroupAsync(string name, Api.ClusterGroupPut group, string? etag = null, CancellationToken cancellationToken = default)
    {
        CheckExtension("clustering_groups");

        await QueryAsync(HttpMethod.Put, $"/cluster/groups/{name}", group, etag, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns information about a cluster group.</summary>
    public async Task<(Api.ClusterGroup Group, string ETag)> GetClusterGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("clustering_groups");

        (Api.ClusterGroup group, string etag) = await QueryStructAsync<Api.ClusterGroup>(
            HttpMethod.Get, $"/cluster/groups/{name}", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (group, etag);
    }
}
