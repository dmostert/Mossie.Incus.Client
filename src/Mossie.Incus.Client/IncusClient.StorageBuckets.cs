using System.Net;
using System.Text.Json;
using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;
using Mossie.Incus.Client.Serialization;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the names of all storage buckets in a pool.</summary>
    public async Task<List<string>> GetStoragePoolBucketNamesAsync(string poolName, CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets");

        string basePath = $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets";
        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, basePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames(basePath, urls);
    }

    /// <summary>Returns all storage buckets in a pool.</summary>
    public async Task<List<Api.StorageBucket>> GetStoragePoolBucketsAsync(string poolName, CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets");

        (List<Api.StorageBucket> buckets, _) = await QueryStructAsync<List<Api.StorageBucket>>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets?recursion=1",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return buckets;
    }

    /// <summary>Returns a filtered list of storage buckets in a pool.</summary>
    public async Task<List<Api.StorageBucket>> GetStoragePoolBucketsWithFilterAsync(
        string poolName,
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets");

        string path = $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets?recursion=1&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}";
        (List<Api.StorageBucket> buckets, _) = await QueryStructAsync<List<Api.StorageBucket>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return buckets;
    }

    /// <summary>Returns all storage buckets in a pool across all projects.</summary>
    public async Task<List<Api.StorageBucket>> GetStoragePoolBucketsAllProjectsAsync(string poolName, CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets_all_projects");

        (List<Api.StorageBucket> buckets, _) = await QueryStructAsync<List<Api.StorageBucket>>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets?recursion=1&all-projects=true",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return buckets;
    }

    /// <summary>Returns a filtered list of storage buckets in a pool across all projects.</summary>
    public async Task<List<Api.StorageBucket>> GetStoragePoolBucketsWithFilterAllProjectsAsync(
        string poolName,
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets");
        CheckExtension("storage_buckets_all_projects");

        string path = $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets?recursion=1&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}&all-projects=true";
        (List<Api.StorageBucket> buckets, _) = await QueryStructAsync<List<Api.StorageBucket>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return buckets;
    }

    /// <summary>Returns all storage buckets in a pool using the full representation.</summary>
    public async Task<List<Api.StorageBucketFull>> GetStoragePoolBucketsFullAsync(string poolName, CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_bucket_full");

        (List<Api.StorageBucketFull> buckets, _) = await QueryStructAsync<List<Api.StorageBucketFull>>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets?recursion=2",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return buckets;
    }

    /// <summary>Returns a filtered list of storage buckets in a pool using the full representation.</summary>
    public async Task<List<Api.StorageBucketFull>> GetStoragePoolBucketsFullWithFilterAsync(
        string poolName,
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_bucket_full");

        string path = $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets?recursion=2&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}";
        (List<Api.StorageBucketFull> buckets, _) = await QueryStructAsync<List<Api.StorageBucketFull>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return buckets;
    }

    /// <summary>Returns all storage buckets in a pool across all projects using the full representation.</summary>
    public async Task<List<Api.StorageBucketFull>> GetStoragePoolBucketsFullAllProjectsAsync(string poolName, CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_bucket_full");

        (List<Api.StorageBucketFull> buckets, _) = await QueryStructAsync<List<Api.StorageBucketFull>>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets?recursion=2&all-projects=true",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return buckets;
    }

    /// <summary>Returns a filtered list of storage buckets in a pool across all projects using the full representation.</summary>
    public async Task<List<Api.StorageBucketFull>> GetStoragePoolBucketsFullWithFilterAllProjectsAsync(
        string poolName,
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_bucket_full");

        string path = $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets?recursion=2&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}&all-projects=true";
        (List<Api.StorageBucketFull> buckets, _) = await QueryStructAsync<List<Api.StorageBucketFull>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return buckets;
    }

    /// <summary>Returns a storage bucket.</summary>
    public async Task<(Api.StorageBucket Bucket, string ETag)> GetStoragePoolBucketAsync(
        string poolName,
        string bucketName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets");

        (Api.StorageBucket bucket, string etag) = await QueryStructAsync<Api.StorageBucket>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets/{Uri.EscapeDataString(bucketName)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (bucket, etag);
    }

    /// <summary>Returns a storage bucket using the full representation.</summary>
    public async Task<(Api.StorageBucketFull Bucket, string ETag)> GetStoragePoolBucketFullAsync(
        string poolName,
        string bucketName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_bucket_full");

        (Api.StorageBucketFull bucket, string etag) = await QueryStructAsync<Api.StorageBucketFull>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets/{Uri.EscapeDataString(bucketName)}?recursion=1",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (bucket, etag);
    }

    /// <summary>
    /// Creates a storage bucket. When the server supports initial credentials, the generated key is returned.
    /// </summary>
    public async Task<Api.StorageBucketKey?> CreateStoragePoolBucketAsync(
        string poolName,
        Api.StorageBucketsPost bucket,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets");

        string path = $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets";

        if (HasExtension("storage_buckets_create_credentials"))
        {
            (Api.StorageBucketKey key, _) = await QueryStructAsync<Api.StorageBucketKey>(
                HttpMethod.Post, path, bucket, cancellationToken: cancellationToken).ConfigureAwait(false);
            return key;
        }

        await QueryAsync(HttpMethod.Post, path, bucket, cancellationToken: cancellationToken).ConfigureAwait(false);
        return null;
    }

    /// <summary>Updates a storage bucket.</summary>
    public async Task UpdateStoragePoolBucketAsync(
        string poolName,
        string bucketName,
        Api.StorageBucketPut bucket,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets");

        await QueryAsync(
            HttpMethod.Put,
            $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets/{Uri.EscapeDataString(bucketName)}",
            bucket,
            etag,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a storage bucket.</summary>
    public async Task DeleteStoragePoolBucketAsync(string poolName, string bucketName, CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets");

        await QueryAsync(
            HttpMethod.Delete,
            $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets/{Uri.EscapeDataString(bucketName)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the names of all keys of a storage bucket.</summary>
    public async Task<List<string>> GetStoragePoolBucketKeyNamesAsync(
        string poolName,
        string bucketName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets");

        string basePath = $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets/{Uri.EscapeDataString(bucketName)}/keys";
        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, basePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames(basePath, urls);
    }

    /// <summary>Returns all keys of a storage bucket.</summary>
    public async Task<List<Api.StorageBucketKey>> GetStoragePoolBucketKeysAsync(
        string poolName,
        string bucketName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets");

        (List<Api.StorageBucketKey> keys, _) = await QueryStructAsync<List<Api.StorageBucketKey>>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets/{Uri.EscapeDataString(bucketName)}/keys?recursion=1",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return keys;
    }

    /// <summary>Returns a key of a storage bucket.</summary>
    public async Task<(Api.StorageBucketKey Key, string ETag)> GetStoragePoolBucketKeyAsync(
        string poolName,
        string bucketName,
        string keyName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets");

        (Api.StorageBucketKey key, string etag) = await QueryStructAsync<Api.StorageBucketKey>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets/{Uri.EscapeDataString(bucketName)}/keys/{Uri.EscapeDataString(keyName)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (key, etag);
    }

    /// <summary>Adds a key to a storage bucket.</summary>
    public async Task<Api.StorageBucketKey> CreateStoragePoolBucketKeyAsync(
        string poolName,
        string bucketName,
        Api.StorageBucketKeysPost key,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets");

        (Api.StorageBucketKey created, _) = await QueryStructAsync<Api.StorageBucketKey>(
            HttpMethod.Post,
            $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets/{Uri.EscapeDataString(bucketName)}/keys",
            key,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return created;
    }

    /// <summary>Updates an existing storage bucket key.</summary>
    public async Task UpdateStoragePoolBucketKeyAsync(
        string poolName,
        string bucketName,
        string keyName,
        Api.StorageBucketKeyPut key,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets");

        await QueryAsync(
            HttpMethod.Put,
            $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets/{Uri.EscapeDataString(bucketName)}/keys/{Uri.EscapeDataString(keyName)}",
            key,
            etag,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes a key from a storage bucket.</summary>
    public async Task DeleteStoragePoolBucketKeyAsync(
        string poolName,
        string bucketName,
        string keyName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_buckets");

        await QueryAsync(
            HttpMethod.Delete,
            $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets/{Uri.EscapeDataString(bucketName)}/keys/{Uri.EscapeDataString(keyName)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a new storage bucket backup.</summary>
    public async Task<Operation> CreateStoragePoolBucketBackupAsync(
        string poolName,
        string bucketName,
        Api.StorageBucketBackupsPost backup,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_bucket_backup");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets/{Uri.EscapeDataString(bucketName)}/backups",
            backup,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Deletes an existing storage bucket backup.</summary>
    public async Task<Operation> DeleteStoragePoolBucketBackupAsync(
        string pool,
        string bucketName,
        string name,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_bucket_backup");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Delete,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/buckets/{Uri.EscapeDataString(bucketName)}/backups/{Uri.EscapeDataString(name)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Downloads a storage bucket backup.</summary>
    public async Task<BackupFileResponse> GetStoragePoolBucketBackupFileAsync(
        string pool,
        string bucketName,
        string name,
        BackupFileRequest request,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_bucket_backup");

        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/buckets/{Uri.EscapeDataString(bucketName)}/backups/{Uri.EscapeDataString(name)}/export";
        (Stream stream, _, HttpResponseMessage response) = await QueryStreamAsync(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        using (response)
        using (stream)
        {
            long size = await CopyWithProgressAsync(stream, request.BackupFile, request.ProgressHandler, cancellationToken)
                .ConfigureAwait(false);

            return new BackupFileResponse { Size = size };
        }
    }

    /// <summary>Creates a direct streaming backup of a storage bucket.</summary>
    public async Task CreateStoragePoolBucketBackupStreamAsync(
        string poolName,
        string bucketName,
        Api.StorageBucketBackupsPost backup,
        BackupFileRequest request,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("direct_backup");

        string path = $"/storage-pools/{Uri.EscapeDataString(poolName)}/buckets/{Uri.EscapeDataString(bucketName)}/backups";
        using HttpRequestMessage message = new(HttpMethod.Post, BuildQueryUrl(path));
        message.Headers.Accept.ParseAdd("application/octet-stream");

        string json = JsonSerializer.Serialize(backup, IncusJson.Options);
        message.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        await AddClientHeadersAsync(message, cancellationToken).ConfigureAwait(false);

        using HttpResponseMessage response = await _http
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
            await ParseResponseAsync(response, BuildQueryUrl(path), cancellationToken).ConfigureAwait(false);

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await CopyWithProgressAsync(stream, request.BackupFile, request.ProgressHandler, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a new storage bucket from a backup.</summary>
    public async Task<Operation> CreateStoragePoolBucketFromBackupAsync(
        string pool,
        StoragePoolBucketBackupArgs args,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_bucket_backup");

        Dictionary<string, string[]> headers = new();
        if (!string.IsNullOrEmpty(args.Name))
            headers["X-Incus-name"] = new[] { args.Name };

        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/buckets";
        (Api.Response response, _) = await RawQueryWithHeadersAsync(
            HttpMethod.Post, BuildQueryUrl(path), args.BackupFile, null, headers, cancellationToken).ConfigureAwait(false);

        return WrapOperationResponse(response);
    }
}
