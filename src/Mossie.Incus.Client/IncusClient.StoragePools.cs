using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the names of all storage pools.</summary>
    public async Task<List<string>> GetStoragePoolNamesAsync(CancellationToken cancellationToken = default)
    {
        if (!HasExtension("storage"))
            throw new IncusException("The server is missing the required \"storage\" API extension");

        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, "/storage-pools", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/storage-pools", urls);
    }

    /// <summary>Returns all storage pools.</summary>
    public async Task<List<Api.StoragePool>> GetStoragePoolsAsync(CancellationToken cancellationToken = default)
    {
        if (!HasExtension("storage"))
            throw new IncusException("The server is missing the required \"storage\" API extension");

        (List<Api.StoragePool> pools, _) = await QueryStructAsync<List<Api.StoragePool>>(
            HttpMethod.Get, "/storage-pools?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return pools;
    }

    /// <summary>Returns a filtered list of storage pools.</summary>
    public async Task<List<Api.StoragePool>> GetStoragePoolsWithFilterAsync(IEnumerable<string> filters, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("storage"))
            throw new IncusException("The server is missing the required \"storage\" API extension");

        string path = $"/storage-pools?recursion=1&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}";

        (List<Api.StoragePool> pools, _) = await QueryStructAsync<List<Api.StoragePool>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return pools;
    }

    /// <summary>Returns a storage pool.</summary>
    public async Task<(Api.StoragePool Pool, string ETag)> GetStoragePoolAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("storage"))
            throw new IncusException("The server is missing the required \"storage\" API extension");

        (Api.StoragePool pool, string etag) = await QueryStructAsync<Api.StoragePool>(
            HttpMethod.Get, $"/storage-pools/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (pool, etag);
    }

    /// <summary>Creates a storage pool.</summary>
    public async Task CreateStoragePoolAsync(Api.StoragePoolsPost pool, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("storage"))
            throw new IncusException("The server is missing the required \"storage\" API extension");

        await QueryAsync(HttpMethod.Post, "/storage-pools", pool, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates a storage pool.</summary>
    public async Task UpdateStoragePoolAsync(string name, Api.StoragePoolPut pool, string? etag = null, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("storage"))
            throw new IncusException("The server is missing the required \"storage\" API extension");

        await QueryAsync(HttpMethod.Put, $"/storage-pools/{Uri.EscapeDataString(name)}", pool, etag, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a storage pool.</summary>
    public async Task DeleteStoragePoolAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("storage"))
            throw new IncusException("The server is missing the required \"storage\" API extension");

        await QueryAsync(HttpMethod.Delete, $"/storage-pools/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the resources available to a storage pool.</summary>
    public async Task<Api.ResourcesStoragePool> GetStoragePoolResourcesAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("resources");

        (Api.ResourcesStoragePool resources, _) = await QueryStructAsync<Api.ResourcesStoragePool>(
            HttpMethod.Get, $"/storage-pools/{Uri.EscapeDataString(name)}/resources", cancellationToken: cancellationToken).ConfigureAwait(false);

        return resources;
    }
}
