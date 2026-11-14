using System.Globalization;
using System.Net;
using System.Text.Json;
using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;
using static Mossie.Incus.Client.Api.IncusConstants;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the names of all storage volumes in a pool.</summary>
    public async Task<List<string>> GetStoragePoolVolumeNamesAsync(string pool, CancellationToken cancellationToken = default)
    {
        CheckExtension("storage");

        string basePath = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes";
        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, basePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames(basePath, urls);
    }

    /// <summary>Returns the names of all storage volumes in a pool, grouped by project.</summary>
    public async Task<Dictionary<string, List<string>>> GetStoragePoolVolumeNamesAllProjectsAsync(string pool, CancellationToken cancellationToken = default)
    {
        CheckExtension("storage");
        CheckExtension("storage_volumes_all_projects");

        string basePath = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes?all-projects=true";
        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, basePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, List<string>> names = new();
        string marker = $"/1.0/storage-pools/{Uri.EscapeDataString(pool)}/volumes/";

        foreach (string url in urls)
        {
            Uri parsed = new(url);
            string project = "";
            foreach (string pair in parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int index = pair.IndexOf('=');
                if (index < 0)
                    continue;

                if (Uri.UnescapeDataString(pair[..index]) == "project")
                    project = Uri.UnescapeDataString(pair[(index + 1)..]);
            }

            if (project.Length == 0)
                project = ProjectDefaultName;

            int position = parsed.AbsolutePath.IndexOf(marker, StringComparison.Ordinal);
            if (position < 0)
                throw new IncusException($"Unexpected URL path \"{url}\"");

            string name = parsed.AbsolutePath[(position + marker.Length)..];
            if (!names.TryGetValue(project, out List<string>? list))
            {
                list = new List<string>();
                names[project] = list;
            }

            list.Add(name);
        }

        return names;
    }

    /// <summary>Returns all storage volumes in a pool.</summary>
    public async Task<List<Api.StorageVolume>> GetStoragePoolVolumesAsync(string pool, CancellationToken cancellationToken = default)
    {
        CheckExtension("storage");

        (List<Api.StorageVolume> volumes, _) = await QueryStructAsync<List<Api.StorageVolume>>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes?recursion=1",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return volumes;
    }

    /// <summary>Returns all storage volumes in a pool across all projects.</summary>
    public async Task<List<Api.StorageVolume>> GetStoragePoolVolumesAllProjectsAsync(string pool, CancellationToken cancellationToken = default)
    {
        CheckExtension("storage");
        CheckExtension("storage_volumes_all_projects");

        (List<Api.StorageVolume> volumes, _) = await QueryStructAsync<List<Api.StorageVolume>>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes?recursion=1&all-projects=true",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return volumes;
    }

    /// <summary>Returns a filtered list of storage volumes in a pool.</summary>
    public async Task<List<Api.StorageVolume>> GetStoragePoolVolumesWithFilterAsync(
        string pool,
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage");

        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes?recursion=1&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}";
        (List<Api.StorageVolume> volumes, _) = await QueryStructAsync<List<Api.StorageVolume>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return volumes;
    }

    /// <summary>Returns a filtered list of storage volumes in a pool across all projects.</summary>
    public async Task<List<Api.StorageVolume>> GetStoragePoolVolumesWithFilterAllProjectsAsync(
        string pool,
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage");
        CheckExtension("storage_volumes_all_projects");

        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes?recursion=1&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}&all-projects=true";
        (List<Api.StorageVolume> volumes, _) = await QueryStructAsync<List<Api.StorageVolume>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return volumes;
    }

    /// <summary>Returns all storage volumes in a pool using the full representation.</summary>
    public async Task<List<Api.StorageVolumeFull>> GetStoragePoolVolumesFullAsync(string pool, CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_volume_full");

        (List<Api.StorageVolumeFull> volumes, _) = await QueryStructAsync<List<Api.StorageVolumeFull>>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes?recursion=2",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return volumes;
    }

    /// <summary>Returns all storage volumes in a pool across all projects using the full representation.</summary>
    public async Task<List<Api.StorageVolumeFull>> GetStoragePoolVolumesFullAllProjectsAsync(string pool, CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_volume_full");

        (List<Api.StorageVolumeFull> volumes, _) = await QueryStructAsync<List<Api.StorageVolumeFull>>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes?recursion=2&all-projects=true",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return volumes;
    }

    /// <summary>Returns a filtered list of storage volumes in a pool using the full representation.</summary>
    public async Task<List<Api.StorageVolumeFull>> GetStoragePoolVolumesFullWithFilterAsync(
        string pool,
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_volume_full");

        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes?recursion=2&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}";
        (List<Api.StorageVolumeFull> volumes, _) = await QueryStructAsync<List<Api.StorageVolumeFull>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return volumes;
    }

    /// <summary>Returns a filtered list of storage volumes in a pool across all projects using the full representation.</summary>
    public async Task<List<Api.StorageVolumeFull>> GetStoragePoolVolumesFullWithFilterAllProjectsAsync(
        string pool,
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_volume_full");

        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes?recursion=2&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}&all-projects=true";
        (List<Api.StorageVolumeFull> volumes, _) = await QueryStructAsync<List<Api.StorageVolumeFull>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return volumes;
    }

    /// <summary>Returns a storage volume.</summary>
    public async Task<(Api.StorageVolume Volume, string ETag)> GetStoragePoolVolumeAsync(
        string pool,
        string volumeType,
        string name,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage");

        (Api.StorageVolume volume, string etag) = await QueryStructAsync<Api.StorageVolume>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(name)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (volume, etag);
    }

    /// <summary>Returns a storage volume using the full representation.</summary>
    public async Task<(Api.StorageVolumeFull Volume, string ETag)> GetStoragePoolVolumeFullAsync(
        string pool,
        string volumeType,
        string name,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_volume_full");

        (Api.StorageVolumeFull volume, string etag) = await QueryStructAsync<Api.StorageVolumeFull>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(name)}?recursion=1",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (volume, etag);
    }

    /// <summary>Returns the state of a storage volume.</summary>
    public async Task<Api.StorageVolumeState> GetStoragePoolVolumeStateAsync(
        string pool,
        string volumeType,
        string name,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_volume_state");

        (Api.StorageVolumeState state, _) = await QueryStructAsync<Api.StorageVolumeState>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(name)}/state",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return state;
    }

    /// <summary>Creates a storage volume.</summary>
    public async Task CreateStoragePoolVolumeAsync(
        string pool,
        Api.StorageVolumesPost volume,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage");

        await QueryAsync(
            HttpMethod.Post,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volume.Type)}",
            volume,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a storage volume snapshot.</summary>
    public async Task<Operation> CreateStoragePoolVolumeSnapshotAsync(
        string pool,
        string volumeType,
        string volumeName,
        Api.StorageVolumeSnapshotsPost snapshot,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_api_volume_snapshots");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/snapshots",
            snapshot,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Returns the names of all snapshots of a storage volume.</summary>
    public async Task<List<string>> GetStoragePoolVolumeSnapshotNamesAsync(
        string pool,
        string volumeType,
        string volumeName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_api_volume_snapshots");

        string basePath = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/snapshots";
        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, basePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames(basePath, urls);
    }

    /// <summary>Returns all snapshots of a storage volume.</summary>
    public async Task<List<Api.StorageVolumeSnapshot>> GetStoragePoolVolumeSnapshotsAsync(
        string pool,
        string volumeType,
        string volumeName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_api_volume_snapshots");

        (List<Api.StorageVolumeSnapshot> snapshots, _) = await QueryStructAsync<List<Api.StorageVolumeSnapshot>>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/snapshots?recursion=1",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return snapshots;
    }

    /// <summary>Returns a storage volume snapshot.</summary>
    public async Task<(Api.StorageVolumeSnapshot Snapshot, string ETag)> GetStoragePoolVolumeSnapshotAsync(
        string pool,
        string volumeType,
        string volumeName,
        string snapshotName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_api_volume_snapshots");

        (Api.StorageVolumeSnapshot snapshot, string etag) = await QueryStructAsync<Api.StorageVolumeSnapshot>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/snapshots/{Uri.EscapeDataString(snapshotName)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (snapshot, etag);
    }

    /// <summary>Renames a storage volume snapshot.</summary>
    public async Task<Operation> RenameStoragePoolVolumeSnapshotAsync(
        string pool,
        string volumeType,
        string volumeName,
        string snapshotName,
        Api.StorageVolumeSnapshotPost snapshot,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_api_volume_snapshots");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/snapshots/{Uri.EscapeDataString(snapshotName)}",
            snapshot,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Deletes a storage volume snapshot.</summary>
    public async Task<Operation> DeleteStoragePoolVolumeSnapshotAsync(
        string pool,
        string volumeType,
        string volumeName,
        string snapshotName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_api_volume_snapshots");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Delete,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/snapshots/{Uri.EscapeDataString(snapshotName)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Updates a storage volume snapshot.</summary>
    public async Task UpdateStoragePoolVolumeSnapshotAsync(
        string pool,
        string volumeType,
        string volumeName,
        string snapshotName,
        Api.StorageVolumeSnapshotPut volume,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_api_volume_snapshots");

        await QueryOperationAsync(
            HttpMethod.Put,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/snapshots/{Uri.EscapeDataString(snapshotName)}",
            volume,
            etag,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requests that Incus prepares for a storage volume migration.</summary>
    public async Task<Operation> MigrateStoragePoolVolumeAsync(
        string pool,
        Api.StorageVolumePost volume,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_api_remote_volume_handling");

        if (!volume.Migration)
            throw new IncusException("Can't ask for a rename through MigrateStoragePoolVolume");

        string name = volume.Name;
        int slash = name.IndexOf('/');
        bool isSnapshot = slash >= 0;

        object request;
        string path;
        if (isSnapshot)
        {
            CheckExtension("storage_api_remote_volume_snapshot_copy");

            string parent = name[..slash];
            string snapshot = name[(slash + 1)..];
            request = new Api.StorageVolumeSnapshotPost
            {
                Name = snapshot,
                Migration = volume.Migration,
                Target = volume.Target,
            };
            path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/custom/{Uri.EscapeDataString(parent)}/snapshots/{Uri.EscapeDataString(snapshot)}";
        }
        else
        {
            request = volume;
            path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/custom/{Uri.EscapeDataString(name)}";
        }

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post, path, request, cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Copies an existing storage volume.</summary>
    public async Task<RemoteOperation> CopyStoragePoolVolumeAsync(
        string pool,
        IncusClient source,
        string sourcePool,
        Api.StorageVolume volume,
        StoragePoolVolumeCopyArgs? args = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_api_local_volume_handling");

        if (args is not null && args.VolumeOnly)
            CheckExtension("storage_api_volume_snapshots");

        if (args is not null && args.Refresh)
            CheckExtension("custom_volume_refresh");

        if (args is not null && args.RefreshExcludeOlder)
            CheckExtension("custom_volume_refresh_exclude_older_snapshots");

        Api.StorageVolumesPost request = new()
        {
            Name = args?.Name ?? "",
            Type = volume.Type,
            Config = volume.Config,
            Description = volume.Description,
            ContentType = volume.ContentType,
            Source = new Api.StorageVolumeSource
            {
                Name = volume.Name,
                Type = "copy",
                Pool = sourcePool,
                VolumeOnly = args?.VolumeOnly ?? false,
                Refresh = args?.Refresh ?? false,
                RefreshExcludeOlder = args?.RefreshExcludeOlder ?? false,
            },
        };

        IncusConnectionInfo sourceInfo = source.GetConnectionInfo();
        IncusConnectionInfo destInfo = GetConnectionInfo();
        bool clusterInternal = HasExtension("cluster_internal_custom_volume_copy");

        if (destInfo.Url == sourceInfo.Url && destInfo.SocketPath == sourceInfo.SocketPath &&
            (volume.Location == (_clusterTarget ?? "") ||
             (volume.Location == "none" && string.IsNullOrEmpty(_clusterTarget)) || clusterInternal))
        {
            if (destInfo.Project != sourceInfo.Project)
            {
                CheckExtension("storage_api_project");
                request.Source.Project = sourceInfo.Project;
            }

            if (clusterInternal)
                request.Source.Location = sourceInfo.Target;

            (Operation operation, _) = await QueryOperationAsync(
                HttpMethod.Post,
                $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volume.Type)}",
                request,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return WrapOperation(operation);
        }

        CheckExtension("storage_api_remote_volume_handling");

        Api.StorageVolumePost sourceRequest = new()
        {
            Migration = true,
            Name = volume.Name,
            Pool = sourcePool,
            VolumeOnly = args?.VolumeOnly ?? false,
        };

        if (args is not null && args.Mode == "push")
        {
            IncusConnectionInfo info = GetConnectionInfo();

            request.Source.Type = "migration";
            request.Source.Mode = "push";

            (Operation operation, _) = await QueryOperationAsync(
                HttpMethod.Post,
                $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volume.Type)}",
                request,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            Api.Operation operationInfo = operation.GetOperation();
            Dictionary<string, string> targetSecrets = ExtractSecrets(operationInfo.Metadata);

            sourceRequest.Target = new Api.StorageVolumePostTarget
            {
                Operation = operationInfo.ID,
                Websockets = targetSecrets,
                Certificate = info.Certificate,
            };

            return TryMigrateStoragePoolVolume(source, sourcePool, sourceRequest, info.Addresses, cancellationToken);
        }

        IncusConnectionInfo sourceConnection = source.GetConnectionInfo();

        Operation sourceOperation = await source.MigrateStoragePoolVolumeAsync(sourcePool, sourceRequest, cancellationToken).ConfigureAwait(false);
        Api.Operation sourceOperationInfo = sourceOperation.GetOperation();
        Dictionary<string, string> sourceSecrets = ExtractSecrets(sourceOperationInfo.Metadata);

        if (args is not null && args.Mode == "relay")
        {
            request.Source.Type = "migration";
            request.Source.Mode = "push";

            (Operation targetOperation, _) = await QueryOperationAsync(
                HttpMethod.Post,
                $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volume.Type)}",
                request,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            Api.Operation targetOperationInfo = targetOperation.GetOperation();
            Dictionary<string, string> targetSecrets = ExtractSecrets(targetOperationInfo.Metadata);

            await ProxyMigrationAsync(targetOperation, targetSecrets, source, sourceOperation, sourceSecrets, cancellationToken)
                .ConfigureAwait(false);

            return WrapOperation(targetOperation);
        }

        request.Source.Type = "migration";
        request.Source.Mode = "pull";
        request.Source.Operation = sourceOperationInfo.ID;
        request.Source.Websockets = sourceSecrets;
        request.Source.Certificate = sourceConnection.Certificate;

        return TryCreateStoragePoolVolume(pool, request, sourceConnection.Addresses, cancellationToken);
    }

    /// <summary>Renames or moves an existing storage volume.</summary>
    public async Task<RemoteOperation> MoveStoragePoolVolumeAsync(
        string pool,
        IncusClient source,
        string sourcePool,
        Api.StorageVolume volume,
        StoragePoolVolumeMoveArgs? args = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_api_local_volume_handling");

        if (!ReferenceEquals(this, source))
            throw new IncusException("Moving storage volumes between remotes is not implemented");

        Api.StorageVolumePost request = new()
        {
            Name = args?.Name ?? "",
            Pool = pool,
        };

        if (!string.IsNullOrEmpty(args?.Project))
        {
            CheckExtension("storage_volume_project_move");
            request.Project = args.Project;
        }

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            $"/storage-pools/{Uri.EscapeDataString(sourcePool)}/volumes/{Uri.EscapeDataString(volume.Type)}/{Uri.EscapeDataString(volume.Name)}",
            request,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return WrapOperation(operation);
    }

    /// <summary>Updates a storage volume.</summary>
    public async Task UpdateStoragePoolVolumeAsync(
        string pool,
        string volumeType,
        string name,
        Api.StorageVolumePut volume,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage");

        if (!string.IsNullOrEmpty(volume.Restore))
            CheckExtension("storage_api_volume_snapshots");

        await QueryAsync(
            HttpMethod.Put,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(name)}",
            volume,
            etag,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a storage volume.</summary>
    public async Task DeleteStoragePoolVolumeAsync(
        string pool,
        string volumeType,
        string name,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage");

        await QueryAsync(
            HttpMethod.Delete,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(name)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rebuilds an existing custom storage volume as empty.</summary>
    public async Task<Operation> RebuildStoragePoolVolumeAsync(
        string pool,
        string volumeType,
        string name,
        Api.StorageVolumeRebuildPost volume,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_volumes_rebuild");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(name)}/rebuild",
            volume,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Renames a storage volume.</summary>
    public async Task RenameStoragePoolVolumeAsync(
        string pool,
        string volumeType,
        string name,
        Api.StorageVolumePost volume,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_api_volume_rename");

        await QueryAsync(
            HttpMethod.Post,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(name)}",
            volume,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the names of all backups of a custom storage volume.</summary>
    public async Task<List<string>> GetStorageVolumeBackupNamesAsync(
        string pool,
        string volumeName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("custom_volume_backup");

        string basePath = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/custom/{Uri.EscapeDataString(volumeName)}/backups";
        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, basePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames(basePath, urls);
    }

    /// <summary>Returns all backups of a custom storage volume.</summary>
    public async Task<List<Api.StorageVolumeBackup>> GetStorageVolumeBackupsAsync(
        string pool,
        string volumeName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("custom_volume_backup");

        (List<Api.StorageVolumeBackup> backups, _) = await QueryStructAsync<List<Api.StorageVolumeBackup>>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/custom/{Uri.EscapeDataString(volumeName)}/backups?recursion=1",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return backups;
    }

    /// <summary>Returns a custom storage volume backup.</summary>
    public async Task<(Api.StorageVolumeBackup Backup, string ETag)> GetStorageVolumeBackupAsync(
        string pool,
        string volumeName,
        string name,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("custom_volume_backup");

        (Api.StorageVolumeBackup backup, string etag) = await QueryStructAsync<Api.StorageVolumeBackup>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/custom/{Uri.EscapeDataString(volumeName)}/backups/{Uri.EscapeDataString(name)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (backup, etag);
    }

    /// <summary>Creates a new custom storage volume backup.</summary>
    public async Task<Operation> CreateStorageVolumeBackupAsync(
        string pool,
        string volumeName,
        Api.StorageVolumeBackupsPost backup,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("custom_volume_backup");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/custom/{Uri.EscapeDataString(volumeName)}/backups",
            backup,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Renames a custom storage volume backup.</summary>
    public async Task<Operation> RenameStorageVolumeBackupAsync(
        string pool,
        string volumeName,
        string name,
        Api.StorageVolumeBackupPost backup,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("custom_volume_backup");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/custom/{Uri.EscapeDataString(volumeName)}/backups/{Uri.EscapeDataString(name)}",
            backup,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Deletes a custom storage volume backup.</summary>
    public async Task<Operation> DeleteStorageVolumeBackupAsync(
        string pool,
        string volumeName,
        string name,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("custom_volume_backup");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Delete,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/custom/{Uri.EscapeDataString(volumeName)}/backups/{Uri.EscapeDataString(name)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Downloads a custom storage volume backup.</summary>
    public async Task<BackupFileResponse> GetStorageVolumeBackupFileAsync(
        string pool,
        string volumeName,
        string name,
        BackupFileRequest request,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("custom_volume_backup");

        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/custom/{Uri.EscapeDataString(volumeName)}/backups/{Uri.EscapeDataString(name)}/export";
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

    /// <summary>Creates a direct streaming backup of a custom storage volume.</summary>
    public async Task CreateStorageVolumeBackupStreamAsync(
        string pool,
        string volumeName,
        Api.StorageVolumeBackupsPost backup,
        BackupFileRequest request,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("direct_backup");

        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/custom/{Uri.EscapeDataString(volumeName)}/backups";
        using HttpRequestMessage message = new(HttpMethod.Post, BuildQueryUrl(path));
        message.Headers.Accept.ParseAdd("application/octet-stream");

        string json = JsonSerializer.Serialize(backup, IncusJsonOptions);
        message.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        await AddClientHeadersAsync(message, cancellationToken).ConfigureAwait(false);

        using HttpResponseMessage response = await _http
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
            await ParseResponseAsync(response, BuildQueryUrl(path), cancellationToken).ConfigureAwait(false);

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await CopyWithProgressAsync(stream, request.BackupFile, request.ProgressHandler, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a storage volume from a migration stream.</summary>
    public async Task<Operation> CreateStoragePoolVolumeFromMigrationAsync(
        string pool,
        Api.StorageVolumesPost volume,
        CancellationToken cancellationToken = default)
    {
        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volume.Type)}",
            volume,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Creates a custom storage volume from an ISO file.</summary>
    public async Task<Operation> CreateStoragePoolVolumeFromISOAsync(
        string pool,
        StorageVolumeBackupArgs args,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("custom_volume_iso");

        if (string.IsNullOrEmpty(args.Name))
            throw new IncusException("Missing volume name");

        Dictionary<string, string[]> headers = new()
        {
            ["X-Incus-name"] = new[] { args.Name },
            ["X-Incus-type"] = new[] { "iso" },
        };

        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/custom";
        (Api.Response response, _) = await RawQueryWithHeadersAsync(
            HttpMethod.Post, BuildQueryUrl(path), args.BackupFile, null, headers, cancellationToken).ConfigureAwait(false);

        return WrapOperationResponse(response);
    }

    /// <summary>Creates a custom storage volume from a backup file.</summary>
    public async Task<Operation> CreateStoragePoolVolumeFromBackupAsync(
        string pool,
        StorageVolumeBackupArgs args,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("custom_volume_backup");

        if (!string.IsNullOrEmpty(args.Name))
            CheckExtension("backup_override_name");

        Dictionary<string, string[]> headers = new();
        if (!string.IsNullOrEmpty(args.Name))
            headers["X-Incus-name"] = new[] { args.Name };

        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/custom";
        (Api.Response response, _) = await RawQueryWithHeadersAsync(
            HttpMethod.Post, BuildQueryUrl(path), args.BackupFile, null, headers, cancellationToken).ConfigureAwait(false);

        return WrapOperationResponse(response);
    }

    /// <summary>Returns a raw connection to a storage volume's NBD endpoint.</summary>
    public async Task<Stream> GetStoragePoolVolumeBlockNBDConnAsync(
        string pool,
        string volumeType,
        string volumeName,
        StorageVolumeNBDPost args,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_volume_nbd");

        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/nbd";
        if (args.Writable)
            path = IncusQuery.SetParameter(path, "writable", "1");

        return await RawConnectionAsync(HttpMethod.Get, path, "nbd", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns a raw connection to a storage volume's SFTP endpoint.</summary>
    public async Task<Stream> GetStoragePoolVolumeFileSFTPConnAsync(
        string pool,
        string volumeType,
        string volumeName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("custom_volume_sftp");

        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/sftp";
        return await RawConnectionAsync(HttpMethod.Get, path, "sftp", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns an SFTP stream for a storage volume.</summary>
    /// <remarks>
    /// Unlike the Go client this returns the raw connection rather than a full
    /// <c>*sftp.Client</c>; the caller supplies its own SFTP framing over the stream.
    /// </remarks>
    public Task<Stream> GetStoragePoolVolumeFileSFTPAsync(
        string pool,
        string volumeType,
        string volumeName,
        CancellationToken cancellationToken = default)
        => GetStoragePoolVolumeFileSFTPConnAsync(pool, volumeType, volumeName, cancellationToken);

    /// <summary>Retrieves the provided path from the storage volume.</summary>
    public async Task<(Stream? Content, InstanceFileResponse Response)> GetStorageVolumeFileAsync(
        string pool,
        string volumeType,
        string volumeName,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/files";
        return await GetFileAsync(path, filePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a file in the storage volume.</summary>
    public async Task CreateStorageVolumeFileAsync(
        string pool,
        string volumeType,
        string volumeName,
        string filePath,
        InstanceFileArgs args,
        CancellationToken cancellationToken = default)
    {
        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/files";
        await CreateFileAsync(path, filePath, args, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a file in the storage volume.</summary>
    public async Task DeleteStorageVolumeFileAsync(
        string pool,
        string volumeType,
        string volumeName,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        string path = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/files?path={Uri.EscapeDataString(filePath)}";
        await QueryAsync(HttpMethod.Delete, path, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns a list of volume bitmap names.</summary>
    public async Task<List<string>> GetStorageVolumeBitmapNamesAsync(
        string pool,
        string volumeType,
        string volumeName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_volume_nbd");

        string basePath = $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/bitmaps";
        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, basePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames(basePath, urls);
    }

    /// <summary>Returns the bitmaps of a storage volume.</summary>
    public async Task<List<Api.StorageVolumeBitmap>> GetStorageVolumeBitmapsAsync(
        string pool,
        string volumeType,
        string volumeName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_volume_nbd");

        (List<Api.StorageVolumeBitmap> bitmaps, _) = await QueryStructAsync<List<Api.StorageVolumeBitmap>>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/bitmaps?recursion=1",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return bitmaps;
    }

    /// <summary>Returns a storage volume bitmap.</summary>
    public async Task<Api.StorageVolumeBitmap> GetStorageVolumeBitmapAsync(
        string pool,
        string volumeType,
        string volumeName,
        string bitmapName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_volume_nbd");

        (Api.StorageVolumeBitmap bitmap, _) = await QueryStructAsync<Api.StorageVolumeBitmap>(
            HttpMethod.Get,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/bitmaps/{Uri.EscapeDataString(bitmapName)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return bitmap;
    }

    /// <summary>Creates a storage volume bitmap.</summary>
    public async Task CreateStorageVolumeBitmapAsync(
        string pool,
        string volumeType,
        string volumeName,
        Api.StorageVolumeBitmapsPost bitmap,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_volume_nbd");

        await QueryAsync(
            HttpMethod.Post,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/bitmaps",
            bitmap,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a storage volume bitmap.</summary>
    public async Task DeleteStorageVolumeBitmapAsync(
        string pool,
        string volumeType,
        string volumeName,
        string bitmapName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_volume_nbd");

        await QueryAsync(
            HttpMethod.Delete,
            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(volumeType)}/{Uri.EscapeDataString(volumeName)}/bitmaps/{Uri.EscapeDataString(bitmapName)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static Dictionary<string, string> ExtractSecrets(Dictionary<string, object>? metadata)
    {
        Dictionary<string, string> secrets = new();
        if (metadata is null)
            return secrets;

        foreach (KeyValuePair<string, object> item in metadata)
        {
            switch (item.Value)
            {
                case string text:
                    secrets[item.Key] = text;
                    break;
                case JsonElement element when element.ValueKind == JsonValueKind.String:
                    secrets[item.Key] = element.GetString() ?? "";
                    break;
            }
        }

        return secrets;
    }

    private RemoteOperation TryMigrateStoragePoolVolume(
        IncusClient source,
        string pool,
        Api.StorageVolumePost request,
        List<string> urls,
        CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
            throw new IncusException("The source server isn't listening on the network");

        string operation = request.Target?.Operation ?? "";
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RemoteOperation result = new(completion.Task);

        _ = Task.Run(async () =>
        {
            List<Exception> errors = new();
            try
            {
                foreach (string serverUrl in urls)
                {
                    try
                    {
                        request.Target ??= new Api.StorageVolumePostTarget();
                        request.Target.Operation = $"{serverUrl}/1.0/operations/{Uri.EscapeDataString(operation)}";

                        Operation target = await source.MigrateStoragePoolVolumeAsync(pool, request, cancellationToken).ConfigureAwait(false);
                        result.SetTarget(target);
                        await target.WaitAsync(cancellationToken).ConfigureAwait(false);
                        completion.TrySetResult();
                        return;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errors.Add(ex);
                    }
                }

                IncusException failure = new("Failed storage volume creation", new AggregateException(errors));
                result.Fail(failure);
                completion.TrySetException(failure);
            }
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
        }, cancellationToken);

        return result;
    }

    private RemoteOperation TryCreateStoragePoolVolume(
        string pool,
        Api.StorageVolumesPost request,
        List<string> urls,
        CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
            throw new IncusException("The source server isn't listening on the network");

        string operation = request.Source.Operation;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RemoteOperation result = new(completion.Task);

        _ = Task.Run(async () =>
        {
            List<Exception> errors = new();
            try
            {
                foreach (string serverUrl in urls)
                {
                    try
                    {
                        request.Source.Operation = $"{serverUrl}/1.0/operations/{Uri.EscapeDataString(operation)}";

                        (Operation target, _) = await QueryOperationAsync(
                            HttpMethod.Post,
                            $"/storage-pools/{Uri.EscapeDataString(pool)}/volumes/{Uri.EscapeDataString(request.Type)}",
                            request,
                            cancellationToken: cancellationToken).ConfigureAwait(false);

                        result.SetTarget(target);
                        await target.WaitAsync(cancellationToken).ConfigureAwait(false);
                        completion.TrySetResult();
                        return;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errors.Add(ex);
                    }
                }

                IncusException failure = new("Failed storage volume creation", new AggregateException(errors));
                result.Fail(failure);
                completion.TrySetException(failure);
            }
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
        }, cancellationToken);

        return result;
    }

    private RemoteOperation WrapOperation(Operation operation)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            try
            {
                await operation.WaitAsync().ConfigureAwait(false);
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return new RemoteOperation(completion.Task, operation);
    }

    private Operation WrapOperationResponse(Api.Response response)
    {
        Api.Operation operation = DeserializeOperation(response);
        return new Operation(this, operation, listener: null, skipListener: true);
    }

    private static async Task<long> CopyWithProgressAsync(
        Stream source,
        Stream destination,
        Action<long, long>? progress,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        long reported = 0;

        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;

            if (progress is not null && total - reported >= 1024 * 1024)
            {
                progress(total, source.CanSeek ? source.Length : -1);
                reported = total;
            }
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        progress?.Invoke(total, total);
        return total;
    }

    private static readonly JsonSerializerOptions IncusJsonOptions = Serialization.IncusJson.Options;
}
