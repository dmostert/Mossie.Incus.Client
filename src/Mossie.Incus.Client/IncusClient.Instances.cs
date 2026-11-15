using System.Net;
using System.Text;
using System.Text.Json;
using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;
using Mossie.Incus.Client.Serialization;
using static Mossie.Incus.Client.Api.IncusConstants;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    private const string InstancesPath = "/instances";

    private static string InstanceTypePath(InstanceType type)
    {
        string path = InstancesPath;
        if (type != InstanceType.Any)
            path = IncusQuery.SetParameter(path, "instance-type", type.ToApiString());

        return path;
    }

    private static string InstanceTypePath(string? type)
    {
        string path = InstancesPath;
        if (!string.IsNullOrEmpty(type))
            path = IncusQuery.SetParameter(path, "instance-type", type);

        return path;
    }

    private static string InstancePath(string name)
        => $"{InstancesPath}/{Uri.EscapeDataString(name)}";

    private static string InstanceSnapshotPath(string instanceName, string name)
        => $"{InstancePath(instanceName)}/snapshots/{Uri.EscapeDataString(name)}";

    private static string InstanceBackupPath(string instanceName, string name)
        => $"{InstancePath(instanceName)}/backups/{Uri.EscapeDataString(name)}";

    /// <summary>Returns the names of all instances.</summary>
    public async Task<List<string>> GetInstanceNamesAsync(
        InstanceType instanceType,
        CancellationToken cancellationToken = default)
    {
        string basePath = InstanceTypePath(instanceType);
        (List<string> urls, _) = await QueryStructAsync<List<string>>(
            HttpMethod.Get, basePath, cancellationToken: cancellationToken).ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames(InstancesPath, urls);
    }

    /// <summary>Returns the names of all instances across all projects.</summary>
    public async Task<Dictionary<string, List<string>>> GetInstanceNamesAllProjectsAsync(
        InstanceType instanceType,
        CancellationToken cancellationToken = default)
    {
        string path = IncusQuery.SetParameter(InstanceTypePath(instanceType), "all-projects", "true");
        path = IncusQuery.SetParameter(path, "recursion", "1");

        (List<Api.Instance> instances, _) = await QueryStructAsync<List<Api.Instance>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        Dictionary<string, List<string>> names = new();
        foreach (Api.Instance instance in instances)
        {
            if (!names.TryGetValue(instance.Project, out List<string>? projectNames))
            {
                projectNames = new List<string>();
                names[instance.Project] = projectNames;
            }

            projectNames.Add(instance.Name);
        }

        return names;
    }

    /// <summary>Returns all instances.</summary>
    public async Task<List<Api.Instance>> GetInstancesAsync(
        InstanceType instanceType,
        CancellationToken cancellationToken = default)
    {
        string path = IncusQuery.SetParameter(InstanceTypePath(instanceType), "recursion", "1");
        (List<Api.Instance> instances, _) = await QueryStructAsync<List<Api.Instance>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return instances;
    }

    /// <summary>Returns a filtered list of instances.</summary>
    public async Task<List<Api.Instance>> GetInstancesWithFilterAsync(
        InstanceType instanceType,
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("api_filtering");

        string path = IncusQuery.SetParameter(InstanceTypePath(instanceType), "recursion", "1");
        path = IncusQuery.SetParameter(path, "filter", IncusQuery.ParseFilters(filters));

        (List<Api.Instance> instances, _) = await QueryStructAsync<List<Api.Instance>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return instances;
    }

    /// <summary>Returns all instances across all projects.</summary>
    public async Task<List<Api.Instance>> GetInstancesAllProjectsAsync(
        InstanceType instanceType,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instance_all_projects");

        string path = IncusQuery.SetParameter(InstanceTypePath(instanceType), "recursion", "1");
        path = IncusQuery.SetParameter(path, "all-projects", "true");

        (List<Api.Instance> instances, _) = await QueryStructAsync<List<Api.Instance>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return instances;
    }

    /// <summary>Returns a filtered list of instances across all projects.</summary>
    public async Task<List<Api.Instance>> GetInstancesAllProjectsWithFilterAsync(
        InstanceType instanceType,
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("api_filtering");
        CheckExtension("instance_all_projects");

        string path = IncusQuery.SetParameter(InstanceTypePath(instanceType), "recursion", "1");
        path = IncusQuery.SetParameter(path, "all-projects", "true");
        path = IncusQuery.SetParameter(path, "filter", IncusQuery.ParseFilters(filters));

        (List<Api.Instance> instances, _) = await QueryStructAsync<List<Api.Instance>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return instances;
    }

    /// <summary>Updates all instances to match the requested state.</summary>
    public async Task<Operation> UpdateInstancesAsync(
        Api.InstancesPut state,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Put, InstancesPath, state, etag, cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Rebuilds an instance.</summary>
    public async Task<Operation> RebuildInstanceAsync(
        string instanceName,
        Api.InstanceRebuildPost instance,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instances_rebuild");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            $"{InstancePath(instanceName)}/rebuild",
            instance,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Rebuilds an instance from an image on another server.</summary>
    public async Task<RemoteOperation> RebuildInstanceFromImageAsync(
        IImageServer source,
        Api.Image image,
        string instanceName,
        Api.InstanceRebuildPost request,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instances_rebuild");

        IncusConnectionInfo? info = await GetSourceImageConnectionInfoAsync(source, image, request.Source, cancellationToken)
            .ConfigureAwait(false);

        if (info is null)
        {
            Operation operation = await RebuildInstanceAsync(instanceName, request, cancellationToken).ConfigureAwait(false);
            return WrapOperation(operation);
        }

        return TryRebuildInstance(instanceName, request, info.Addresses, null, cancellationToken);
    }

    /// <summary>Returns all instances including snapshots, backups and state.</summary>
    public async Task<List<Api.InstanceFull>> GetInstancesFullAsync(
        InstanceType instanceType,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_full");

        string path = IncusQuery.SetParameter(InstanceTypePath(instanceType), "recursion", "2");
        (List<Api.InstanceFull> instances, _) = await QueryStructAsync<List<Api.InstanceFull>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return instances;
    }

    /// <summary>Returns a filtered list of full instances.</summary>
    public async Task<List<Api.InstanceFull>> GetInstancesFullWithFilterAsync(
        InstanceType instanceType,
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("api_filtering");
        CheckExtension("container_full");

        string path = IncusQuery.SetParameter(InstanceTypePath(instanceType), "recursion", "2");
        path = IncusQuery.SetParameter(path, "filter", IncusQuery.ParseFilters(filters));

        (List<Api.InstanceFull> instances, _) = await QueryStructAsync<List<Api.InstanceFull>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return instances;
    }

    /// <summary>Returns all full instances across all projects.</summary>
    public async Task<List<Api.InstanceFull>> GetInstancesFullAllProjectsAsync(
        InstanceType instanceType,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_full");
        CheckExtension("instance_all_projects");

        string path = IncusQuery.SetParameter(InstanceTypePath(instanceType), "recursion", "2");
        path = IncusQuery.SetParameter(path, "all-projects", "true");

        (List<Api.InstanceFull> instances, _) = await QueryStructAsync<List<Api.InstanceFull>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return instances;
    }

    /// <summary>Returns a filtered list of full instances across all projects.</summary>
    public async Task<List<Api.InstanceFull>> GetInstancesFullAllProjectsWithFilterAsync(
        InstanceType instanceType,
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("api_filtering");
        CheckExtension("container_full");
        CheckExtension("instance_all_projects");

        string path = IncusQuery.SetParameter(InstanceTypePath(instanceType), "recursion", "2");
        path = IncusQuery.SetParameter(path, "all-projects", "true");
        path = IncusQuery.SetParameter(path, "filter", IncusQuery.ParseFilters(filters));

        (List<Api.InstanceFull> instances, _) = await QueryStructAsync<List<Api.InstanceFull>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return instances;
    }

    /// <summary>Returns an instance.</summary>
    public async Task<(Api.Instance Instance, string ETag)> GetInstanceAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        (Api.Instance instance, string etag) = await QueryStructAsync<Api.Instance>(
            HttpMethod.Get, InstancePath(name), cancellationToken: cancellationToken).ConfigureAwait(false);

        return (instance, etag);
    }

    /// <summary>Returns an instance along with its snapshots, backups and state.</summary>
    public async Task<(Api.InstanceFull Instance, string ETag)> GetInstanceFullAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        if (!HasExtension("instance_get_full"))
        {
            Api.InstanceFull instance = new();

            (Api.Instance basic, _) = await GetInstanceAsync(name, cancellationToken).ConfigureAwait(false);
            (Api.InstanceState state, _) = await GetInstanceStateAsync(name, cancellationToken).ConfigureAwait(false);
            List<Api.InstanceSnapshot> snapshots = await GetInstanceSnapshotsAsync(name, cancellationToken).ConfigureAwait(false);
            List<Api.InstanceBackup> backups = await GetInstanceBackupsAsync(name, cancellationToken).ConfigureAwait(false);

            instance.Architecture = basic.Architecture;
            instance.Config = basic.Config;
            instance.Devices = basic.Devices;
            instance.Ephemeral = basic.Ephemeral;
            instance.Profiles = basic.Profiles;
            instance.Restore = basic.Restore;
            instance.DiskOnly = basic.DiskOnly;
            instance.Stateful = basic.Stateful;
            instance.Description = basic.Description;
            instance.CreatedAt = basic.CreatedAt;
            instance.ExpandedConfig = basic.ExpandedConfig;
            instance.ExpandedDevices = basic.ExpandedDevices;
            instance.Name = basic.Name;
            instance.Status = basic.Status;
            instance.StatusCode = basic.StatusCode;
            instance.LastUsedAt = basic.LastUsedAt;
            instance.Location = basic.Location;
            instance.Type = basic.Type;
            instance.Project = basic.Project;
            instance.State = state;
            instance.Snapshots = snapshots;
            instance.Backups = backups;

            return (instance, "");
        }

        string path = IncusQuery.SetParameter(InstancePath(name), "recursion", "1");
        (Api.InstanceFull full, string etag) = await QueryStructAsync<Api.InstanceFull>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return (full, etag);
    }

    /// <summary>Creates an instance from a backup file.</summary>
    public async Task<Operation> CreateInstanceFromBackupAsync(
        InstanceBackupArgs args,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_backup");

        bool hasOverrides = !string.IsNullOrEmpty(args.PoolName)
            || !string.IsNullOrEmpty(args.Name)
            || args.Config.Count > 0
            || args.Devices.Count > 0;

        if (!hasOverrides)
        {
            (Operation operation, _) = await QueryOperationAsync(
                HttpMethod.Post, InstancesPath, args.BackupFile, cancellationToken: cancellationToken).ConfigureAwait(false);

            return operation;
        }

        if (!string.IsNullOrEmpty(args.PoolName))
            CheckExtension("container_backup_override_pool");

        if (!string.IsNullOrEmpty(args.Name))
            CheckExtension("backup_override_name");

        if (args.Config.Count > 0 || args.Devices.Count > 0)
            CheckExtension("backup_override_config");

        Dictionary<string, string[]> headers = new();
        if (!string.IsNullOrEmpty(args.PoolName))
            headers["X-Incus-pool"] = new[] { args.PoolName };

        if (!string.IsNullOrEmpty(args.Name))
            headers["X-Incus-name"] = new[] { args.Name };

        if (args.Config.Count > 0)
            headers["X-Incus-config"] = new[] { string.Join(" ", args.Config) };

        if (args.Devices.Count > 0)
            headers["X-Incus-devices"] = new[] { string.Join(" ", args.Devices) };

        (Api.Response response, _) = await RawQueryWithHeadersAsync(
            HttpMethod.Post, BuildQueryUrl(InstancesPath), args.BackupFile, null, headers, cancellationToken)
            .ConfigureAwait(false);

        return WrapOperationResponse(response);
    }

    /// <summary>Requests that Incus creates a new instance.</summary>
    public async Task<Operation> CreateInstanceAsync(
        Api.InstancesPost instance,
        CancellationToken cancellationToken = default)
    {
        string path = InstanceTypePath(instance.Type);

        if (instance.Source.InstanceOnly)
            CheckExtension("container_only_migration");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post, path, instance, cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Creates an instance from an image on another server.</summary>
    public async Task<RemoteOperation> CreateInstanceFromImageAsync(
        IImageServer source,
        Api.Image image,
        Api.InstancesPost request,
        CancellationToken cancellationToken = default)
    {
        IncusConnectionInfo? info = await GetSourceImageConnectionInfoAsync(source, image, request.Source, cancellationToken)
            .ConfigureAwait(false);

        if (info is null)
        {
            Operation operation = await CreateInstanceAsync(request, cancellationToken).ConfigureAwait(false);
            return WrapOperation(operation);
        }

        return TryCreateInstance(request, info.Addresses, null, cancellationToken);
    }

    /// <summary>Copies an instance from another server.</summary>
    public async Task<RemoteOperation> CopyInstanceAsync(
        IncusClient source,
        Api.Instance instance,
        InstanceCopyArgs? args = null,
        CancellationToken cancellationToken = default)
    {
        Api.InstancesPost request = new()
        {
            Name = instance.Name,
            Architecture = instance.Architecture,
            Config = instance.Config,
            Devices = instance.Devices,
            Ephemeral = instance.Ephemeral,
            Profiles = instance.Profiles,
            Restore = instance.Restore,
            DiskOnly = instance.DiskOnly,
            Stateful = instance.Stateful,
            Description = instance.Description,
            Type = instance.Type,
        };

        if (instance.Config is not null && instance.Config.TryGetValue("volatile.base_image", out string? baseImage))
            request.Source.BaseImage = baseImage;

        if (args is not null)
        {
            if (args.InstanceOnly)
            {
                CheckExtension("container_only_migration");

                if (!source.HasExtension("container_only_migration"))
                    throw new IncusException("The source server is missing the required \"container_only_migration\" API extension");
            }

            if (args.Mode is "push" or "relay")
            {
                CheckExtension("container_push");

                if (!source.HasExtension("container_push"))
                    throw new IncusException("The source server is missing the required \"container_push\" API extension");
            }

            if (args.Mode == "push" && !source.HasExtension("container_push_target"))
                throw new IncusException("The source server is missing the required \"container_push_target\" API extension");

            if (args.Refresh)
            {
                CheckExtension("container_incremental_copy");

                if (!source.HasExtension("container_incremental_copy"))
                    throw new IncusException("The source server is missing the required \"container_incremental_copy\" API extension");
            }

            if (args.RefreshExcludeOlder && !source.HasExtension("custom_volume_refresh_exclude_older_snapshots"))
                throw new IncusException("The source server is missing the required \"custom_volume_refresh_exclude_older_snapshots\" API extension");

            if (args.AllowInconsistent && !HasExtension("instance_allow_inconsistent_copy"))
                throw new IncusException("The source server is missing the required \"instance_allow_inconsistent_copy\" API extension");

            if (!string.IsNullOrEmpty(args.Name))
                request.Name = args.Name;

            request.Source.Live = args.Live;
            request.Source.InstanceOnly = args.InstanceOnly;
            request.Source.Refresh = args.Refresh;
            request.Source.RefreshExcludeOlder = args.RefreshExcludeOlder;
            request.Source.AllowInconsistent = args.AllowInconsistent;
        }

        if (request.Source.Live)
            request.Source.Live = instance.StatusCode == IncusStatusCode.Running;

        IncusConnectionInfo sourceInfo = source.GetConnectionInfo();
        IncusConnectionInfo destInfo = GetConnectionInfo();

        bool local = destInfo.Url == sourceInfo.Url
            && destInfo.SocketPath == sourceInfo.SocketPath
            && (!IsClustered() || instance.Location == (_clusterTarget ?? "") || HasExtension("cluster_internal_copy"));

        if (local)
        {
            if (destInfo.Project != sourceInfo.Project)
            {
                CheckExtension("container_copy_project");
                request.Source.Project = sourceInfo.Project;
            }

            request.Source.Type = "copy";
            request.Source.Source = instance.Name;

            Operation operation = await CreateInstanceAsync(request, cancellationToken).ConfigureAwait(false);
            return WrapOperation(operation);
        }

        Api.InstancePost sourceRequest = new()
        {
            Migration = true,
            Live = request.Source.Live,
            InstanceOnly = request.Source.InstanceOnly,
            AllowInconsistent = request.Source.AllowInconsistent,
        };

        if (source.HasExtension("dependent"))
            sourceRequest.Devices = request.Devices;

        if (args is not null && args.Mode == "push")
        {
            IncusConnectionInfo info = GetConnectionInfo();

            request.Source.Type = "migration";
            request.Source.Mode = "push";
            request.Source.Refresh = args.Refresh;
            request.Source.RefreshExcludeOlder = args.RefreshExcludeOlder;

            Operation operation = await CreateInstanceAsync(request, cancellationToken).ConfigureAwait(false);
            Api.Operation operationInfo = operation.GetOperation();
            Dictionary<string, string> targetSecrets = ExtractSecrets(operationInfo.Metadata);

            sourceRequest.Target = new Api.InstancePostTarget
            {
                Operation = operationInfo.ID,
                Websockets = targetSecrets,
                Certificate = info.Certificate,
            };

            return TryMigrateInstance(source, instance.Name, sourceRequest, info.Addresses, operation, cancellationToken);
        }

        IncusConnectionInfo sourceConnection = source.GetConnectionInfo();
        Operation sourceOperation = await source.MigrateInstanceAsync(instance.Name, sourceRequest, cancellationToken)
            .ConfigureAwait(false);
        Api.Operation sourceOperationInfo = sourceOperation.GetOperation();
        Dictionary<string, string> sourceSecrets = ExtractSecrets(sourceOperationInfo.Metadata);

        if (args is not null && args.Mode == "relay")
        {
            request.Source.Type = "migration";
            request.Source.Mode = "push";

            Operation targetOperation = await CreateInstanceAsync(request, cancellationToken).ConfigureAwait(false);
            Api.Operation targetOperationInfo = targetOperation.GetOperation();
            Dictionary<string, string> targetSecrets = ExtractSecrets(targetOperationInfo.Metadata);

            await ProxyMigrationAsync(
                targetOperation, targetSecrets, source, sourceOperation, sourceSecrets, cancellationToken)
                .ConfigureAwait(false);

            return WrapOperation(targetOperation);
        }

        request.Source.Type = "migration";
        request.Source.Mode = "pull";
        request.Source.Operation = sourceOperationInfo.ID;
        request.Source.Websockets = sourceSecrets;
        request.Source.Certificate = sourceConnection.Certificate;

        return TryCreateInstance(request, sourceConnection.Addresses, sourceOperation, cancellationToken);
    }

    /// <summary>Updates an instance definition.</summary>
    public async Task<Operation> UpdateInstanceAsync(
        string name,
        Api.InstancePut instance,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        if (instance.DiskOnly)
            CheckExtension("instance_snapshot_disk_only_restore");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Put, InstancePath(name), instance, etag, cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Renames an instance.</summary>
    public async Task<Operation> RenameInstanceAsync(
        string name,
        Api.InstancePost instance,
        CancellationToken cancellationToken = default)
    {
        if (instance.Migration)
            throw new IncusException("Can't ask for a migration through RenameInstance");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post, InstancePath(name), instance, cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Requests that Incus prepares for an instance migration.</summary>
    public async Task<Operation> MigrateInstanceAsync(
        string name,
        Api.InstancePost instance,
        CancellationToken cancellationToken = default)
    {
        if (instance.InstanceOnly)
            CheckExtension("container_only_migration");

        if (!string.IsNullOrEmpty(instance.Pool))
            CheckExtension("instance_pool_move");

        if (!string.IsNullOrEmpty(instance.Project))
            CheckExtension("instance_project_move");

        if (instance.AllowInconsistent)
            CheckExtension("cluster_migration_inconsistent_copy");

        if (!instance.Migration)
            throw new IncusException("Can't ask for a rename through MigrateInstance");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post, InstancePath(name), instance, cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Deletes an instance.</summary>
    public async Task<Operation> DeleteInstanceAsync(string name, CancellationToken cancellationToken = default)
    {
        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Delete, InstancePath(name), cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Spawns a command inside an instance.</summary>
    public async Task<Operation> ExecInstanceAsync(
        string instanceName,
        Api.InstanceExecPost exec,
        InstanceExecArgs? args = null,
        CancellationToken cancellationToken = default)
    {
        args ??= new InstanceExecArgs();

        if (exec.RecordOutput)
            CheckExtension("container_exec_recording");

        if (exec.User > 0 || exec.Group > 0 || !string.IsNullOrEmpty(exec.Cwd))
            CheckExtension("container_exec_user_group_cwd");

        string path = IsAgent() ? "/exec" : $"{InstancePath(instanceName)}/exec";

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post, path, exec, cancellationToken: cancellationToken).ConfigureAwait(false);

        Api.Operation operationInfo = operation.GetOperation();
        Dictionary<string, string> fds = ExtractOperationMap(operationInfo, "fds");

        if (exec.RecordOutput && (args.Stdout is not null || args.Stderr is not null))
        {
            await operation.WaitAsync(cancellationToken).ConfigureAwait(false);

            operationInfo = operation.GetOperation();
            Dictionary<string, string> outputFiles = ExtractOperationMap(operationInfo, "output");

            if (outputFiles.TryGetValue("1", out string? stdoutFile) && !string.IsNullOrEmpty(stdoutFile))
            {
                await using Stream reader = await GetInstanceExecOutputLogFileAsync(
                    instanceName, Path.GetFileName(stdoutFile), cancellationToken).ConfigureAwait(false);

                if (args.Stdout is not null)
                    await reader.CopyToAsync(args.Stdout, cancellationToken).ConfigureAwait(false);

                await DeleteInstanceExecOutputLogFileAsync(
                    instanceName, Path.GetFileName(stdoutFile), cancellationToken).ConfigureAwait(false);
            }

            if (outputFiles.TryGetValue("2", out string? stderrFile) && !string.IsNullOrEmpty(stderrFile))
            {
                await using Stream reader = await GetInstanceExecOutputLogFileAsync(
                    instanceName, Path.GetFileName(stderrFile), cancellationToken).ConfigureAwait(false);

                if (args.Stderr is not null)
                    await reader.CopyToAsync(args.Stderr, cancellationToken).ConfigureAwait(false);

                await DeleteInstanceExecOutputLogFileAsync(
                    instanceName, Path.GetFileName(stderrFile), cancellationToken).ConfigureAwait(false);
            }
        }

        List<string> websocketNames = new();
        if (fds.ContainsKey(SecretNameControl))
            websocketNames.Add(SecretNameControl);

        if (exec.Interactive)
        {
            if (fds.TryGetValue("0", out string? interactiveFd)
                && !string.IsNullOrEmpty(interactiveFd)
                && args.Stdin is not null
                && args.Stdout is not null)
            {
                websocketNames.Add("0");
            }
        }
        else
        {
            foreach (string name in new[] { "0", "1", "2" })
            {
                if (fds.TryGetValue(name, out string? fd) && !string.IsNullOrEmpty(fd))
                    websocketNames.Add(name);
            }
        }

        Dictionary<string, IncusWebSocket> connections = await GetExecWebsocketsAsync(
            operationInfo.ID, fds, websocketNames, cancellationToken).ConfigureAwait(false);

        CancellationToken dataToken = args.CancellationToken == default
            ? ConnectedToken
            : args.CancellationToken;

        if (connections.TryGetValue(SecretNameControl, out IncusWebSocket? control))
            StartControlHandler(control, args.Control);

        if (exec.Interactive)
        {
            if (connections.TryGetValue("0", out IncusWebSocket? terminal))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Task write = MirrorToWebSocketAsync(terminal, args.Stdin, dataToken);
                        Task read = MirrorFromWebSocketAsync(terminal, args.Stdout, dataToken);

                        await read.ConfigureAwait(false);
                        await terminal.DisposeAsync().ConfigureAwait(false);
                        await write.ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best effort mirror.
                    }
                    finally
                    {
                        if (control is not null)
                            await control.DisposeAsync().ConfigureAwait(false);

                        args.DataDone?.TrySetResult();
                    }
                }, CancellationToken.None);
            }
            else
            {
                if (control is not null)
                    await control.DisposeAsync().ConfigureAwait(false);

                args.DataDone?.TrySetResult();
            }
        }
        else
        {
            Task? stdin = null;
            if (connections.TryGetValue("0", out IncusWebSocket? stdinSocket))
                stdin = MirrorToWebSocketAsync(stdinSocket, args.Stdin, dataToken);

            List<Task> output = new();
            if (connections.TryGetValue("1", out IncusWebSocket? stdoutSocket))
                output.Add(MirrorFromWebSocketAsync(stdoutSocket, args.Stdout, dataToken));

            if (connections.TryGetValue("2", out IncusWebSocket? stderrSocket))
                output.Add(MirrorFromWebSocketAsync(stderrSocket, args.Stderr, dataToken));

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.WhenAll(output).ConfigureAwait(false);

                    if (stdinSocket is not null)
                    {
                        await stdinSocket.DisposeAsync().ConfigureAwait(false);
                        if (stdin is not null)
                            await stdin.ConfigureAwait(false);
                    }

                    foreach (KeyValuePair<string, IncusWebSocket> connection in connections)
                    {
                        if (connection.Key == "0")
                            continue;

                        await connection.Value.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Best effort mirror.
                }
                finally
                {
                    args.DataDone?.TrySetResult();
                }
            }, CancellationToken.None);
        }

        return operation;
    }

    private async Task<Dictionary<string, IncusWebSocket>> GetExecWebsocketsAsync(
        string operationId,
        Dictionary<string, string> fds,
        List<string> names,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Task<IncusWebSocket>> pending = new();
        foreach (string name in names)
            pending[name] = GetOperationWebSocketAsync(operationId, fds[name], cancellationToken);

        try
        {
            await Task.WhenAll(pending.Values).ConfigureAwait(false);
        }
        catch
        {
            // Errors are inspected below.
        }

        Dictionary<string, Exception> errors = new();
        Dictionary<string, IncusWebSocket> connections = new();

        foreach (KeyValuePair<string, Task<IncusWebSocket>> item in pending)
        {
            if (item.Value.IsCompletedSuccessfully)
                connections[item.Key] = item.Value.Result;
            else if (item.Value.IsFaulted)
                errors[item.Key] = item.Value.Exception?.GetBaseException() ?? new IncusException("Failed to connect");
        }

        if (errors.Count == 1 && errors.ContainsKey(SecretNameControl))
        {
            try
            {
                (Api.Operation operation, _) = await GetOperationAsync(operationId, cancellationToken).ConfigureAwait(false);
                if (IncusStatusCode.IsFinal(operation.StatusCode))
                    errors.Remove(SecretNameControl);
            }
            catch
            {
                // Keep the original error.
            }
        }

        if (errors.Count > 0)
        {
            foreach (IncusWebSocket connection in connections.Values)
                await connection.DisposeAsync().ConfigureAwait(false);

            throw errors.Values.First();
        }

        return connections;
    }

    private static void StartControlHandler(IncusWebSocket socket, Func<IncusWebSocket, Task>? handler)
    {
        if (handler is null)
            return;

        // The control handler owns the connection and runs its own read/write loop, matching
        // the Go client's `go args.Control(controlConn)`.
        _ = Task.Run(async () =>
        {
            try
            {
                await handler(socket).ConfigureAwait(false);
            }
            catch
            {
                // The control channel is best effort.
            }
        }, CancellationToken.None);
    }

    private static async Task MirrorToWebSocketAsync(
        IncusWebSocket socket,
        Stream? source,
        CancellationToken cancellationToken)
    {
        if (source is null)
            return;

        byte[] buffer = new byte[32 * 1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                await socket.SendBinaryAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best effort mirror.
        }
    }

    private static async Task MirrorFromWebSocketAsync(
        IncusWebSocket socket,
        Stream? destination,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                (System.Net.WebSockets.WebSocketMessageType type, byte[] data) =
                    await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);

                if (type == System.Net.WebSockets.WebSocketMessageType.Close)
                    break;

                if (destination is not null)
                    await destination.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best effort mirror.
        }
    }

    private static Dictionary<string, string> ExtractOperationMap(Api.Operation operation, string key)
    {
        Dictionary<string, string> values = new();
        if (operation.Metadata is null || !operation.Metadata.TryGetValue(key, out object? value))
            return values;

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                    values[property.Name] = property.Value.GetString() ?? "";
            }
        }
        else if (value is Dictionary<string, object> dictionary)
        {
            foreach (KeyValuePair<string, object> item in dictionary)
            {
                if (item.Value is string text)
                    values[item.Key] = text;
                else if (item.Value is JsonElement itemElement && itemElement.ValueKind == JsonValueKind.String)
                    values[item.Key] = itemElement.GetString() ?? "";
            }
        }
        else if (value is Dictionary<string, string> stringDictionary)
        {
            foreach (KeyValuePair<string, string> item in stringDictionary)
                values[item.Key] = item.Value;
        }

        return values;
    }

    /// <summary>Retrieves a file or directory listing from an instance.</summary>
    public async Task<(Stream? Content, InstanceFileResponse Response)> GetInstanceFileAsync(
        string instanceName,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        string path = IsAgent() ? "/files" : $"{InstancePath(instanceName)}/files";
        return await GetFileAsync(path, filePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a file in an instance.</summary>
    public async Task CreateInstanceFileAsync(
        string instanceName,
        string filePath,
        InstanceFileArgs args,
        CancellationToken cancellationToken = default)
    {
        if (args.Type == "directory")
            CheckExtension("directory_manipulation");

        if (args.Type == "symlink")
            CheckExtension("file_symlinks");

        if (args.WriteMode == "append")
            CheckExtension("file_append");

        string path = IsAgent() ? "/files" : $"{InstancePath(instanceName)}/files";
        await CreateFileAsync(path, filePath, args, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a file in an instance.</summary>
    public async Task DeleteInstanceFileAsync(
        string instanceName,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("file_delete");

        string path = IsAgent() ? "/files" : $"{InstancePath(instanceName)}/files";
        path = IncusQuery.SetParameter(path, "path", filePath);

        await QueryAsync(HttpMethod.Delete, path, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns a raw connection to an instance's NBD endpoint.</summary>
    public async Task<Stream> GetInstanceNBDConnAsync(
        string instanceName,
        InstanceNBDArgs args,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instance_nbd");

        string path = $"{InstancePath(instanceName)}/nbd";
        if (args.Reuse)
            path = IncusQuery.SetParameter(path, "reuse", "1");

        return await RawConnectionAsync(HttpMethod.Get, path, "nbd", null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns a raw connection to an address and TCP port inside an instance.</summary>
    public Task<Stream> GetInstancePortForwardConnAsync(
        string instanceName,
        Api.InstancePortForwardPost forward,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instance_port_forward");

        return RawConnectionAsync(
            HttpMethod.Post, $"{InstancePath(instanceName)}/port-forward", "tcp", forward, cancellationToken);
    }

    /// <summary>Returns a raw connection to an instance's SFTP endpoint.</summary>
    public Task<Stream> GetInstanceFileSFTPConnAsync(
        string instanceName,
        CancellationToken cancellationToken = default)
        => RawConnectionAsync(HttpMethod.Get, $"{InstancePath(instanceName)}/sftp", "sftp", null, cancellationToken);

    /// <summary>Returns an SFTP stream for an instance.</summary>
    public Task<Stream> GetInstanceFileSFTPAsync(
        string instanceName,
        CancellationToken cancellationToken = default)
        => GetInstanceFileSFTPConnAsync(instanceName, cancellationToken);

    /// <summary>Returns the snapshot names of an instance.</summary>
    public async Task<List<string>> GetInstanceSnapshotNamesAsync(
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        string basePath = $"{InstancePath(instanceName)}/snapshots";
        (List<string> urls, _) = await QueryStructAsync<List<string>>(
            HttpMethod.Get, basePath, cancellationToken: cancellationToken).ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames(basePath, urls);
    }

    /// <summary>Returns the snapshots of an instance.</summary>
    public async Task<List<Api.InstanceSnapshot>> GetInstanceSnapshotsAsync(
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        string path = IncusQuery.SetParameter($"{InstancePath(instanceName)}/snapshots", "recursion", "1");
        (List<Api.InstanceSnapshot> snapshots, _) = await QueryStructAsync<List<Api.InstanceSnapshot>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return snapshots;
    }

    /// <summary>Returns an instance snapshot.</summary>
    public async Task<(Api.InstanceSnapshot Snapshot, string ETag)> GetInstanceSnapshotAsync(
        string instanceName,
        string name,
        CancellationToken cancellationToken = default)
    {
        (Api.InstanceSnapshot snapshot, string etag) = await QueryStructAsync<Api.InstanceSnapshot>(
            HttpMethod.Get, InstanceSnapshotPath(instanceName, name), cancellationToken: cancellationToken).ConfigureAwait(false);

        return (snapshot, etag);
    }

    /// <summary>Creates a snapshot of an instance.</summary>
    public async Task<Operation> CreateInstanceSnapshotAsync(
        string instanceName,
        Api.InstanceSnapshotsPost snapshot,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.ExpiresAt is not null)
            CheckExtension("snapshot_expiry_creation");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            $"{InstancePath(instanceName)}/snapshots",
            snapshot,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Copies an instance snapshot from another server.</summary>
    public async Task<RemoteOperation> CopyInstanceSnapshotAsync(
        IncusClient source,
        string instanceName,
        Api.InstanceSnapshot snapshot,
        InstanceSnapshotCopyArgs? args = null,
        CancellationToken cancellationToken = default)
    {
        string[] fields = snapshot.Name.Split('/');
        string containerName = instanceName;
        string snapshotName = fields[^1];

        Api.InstancesPost request = new()
        {
            Name = containerName,
            Architecture = snapshot.Architecture,
            Config = snapshot.Config,
            Devices = snapshot.Devices,
            Ephemeral = snapshot.Ephemeral,
            Profiles = snapshot.Profiles,
        };

        if (snapshot.Stateful && args is not null && args.Live)
        {
            CheckExtension("container_snapshot_stateful_migration");

            request.Stateful = snapshot.Stateful;
            request.Source.Live = false;
        }

        if (snapshot.Config is not null && snapshot.Config.TryGetValue("volatile.base_image", out string? baseImage))
            request.Source.BaseImage = baseImage;

        if (args is not null)
        {
            if (args.Mode is "push" or "relay")
            {
                CheckExtension("container_push");

                if (!source.HasExtension("container_push"))
                    throw new IncusException("The source server is missing the required \"container_push\" API extension");
            }

            if (args.Mode == "push" && !source.HasExtension("container_push_target"))
                throw new IncusException("The source server is missing the required \"container_push_target\" API extension");

            if (!string.IsNullOrEmpty(args.Name))
                request.Name = args.Name;
        }

        IncusConnectionInfo sourceInfo = source.GetConnectionInfo();
        IncusConnectionInfo destInfo = GetConnectionInfo();

        (Api.Instance instance, _) = await source.GetInstanceAsync(containerName, cancellationToken).ConfigureAwait(false);

        bool local = destInfo.Url == sourceInfo.Url
            && destInfo.SocketPath == sourceInfo.SocketPath
            && (!IsClustered() || instance.Location == (_clusterTarget ?? "") || HasExtension("cluster_internal_copy"));

        if (local)
        {
            if (destInfo.Project != sourceInfo.Project)
            {
                CheckExtension("container_copy_project");
                request.Source.Project = sourceInfo.Project;
            }

            request.Source.Type = "copy";
            request.Source.Source = $"{containerName}/{snapshotName}";

            Operation operation = await CreateInstanceAsync(request, cancellationToken).ConfigureAwait(false);
            return WrapOperation(operation);
        }

        if (source.HasExtension("virtual-machines"))
        {
            (Api.Instance fullInstance, _) = await source.GetInstanceAsync(instanceName, cancellationToken).ConfigureAwait(false);
            request.Type = fullInstance.Type;
        }

        Api.InstanceSnapshotPost sourceRequest = new()
        {
            Migration = true,
            Name = args?.Name ?? "",
        };

        if (snapshot.Stateful && args is not null && args.Live)
            sourceRequest.Live = args.Live;

        if (args is not null && args.Mode == "push")
        {
            IncusConnectionInfo info = GetConnectionInfo();

            request.Source.Type = "migration";
            request.Source.Mode = "push";

            Operation operation = await CreateInstanceAsync(request, cancellationToken).ConfigureAwait(false);
            Api.Operation operationInfo = operation.GetOperation();
            Dictionary<string, string> targetSecrets = ExtractSecrets(operationInfo.Metadata);

            sourceRequest.Target = new Api.InstancePostTarget
            {
                Operation = operationInfo.ID,
                Websockets = targetSecrets,
                Certificate = info.Certificate,
            };

            return TryMigrateInstanceSnapshot(
                source, containerName, snapshotName, sourceRequest, info.Addresses, cancellationToken);
        }

        IncusConnectionInfo sourceConnection = source.GetConnectionInfo();
        Operation sourceOperation = await source.MigrateInstanceSnapshotAsync(
            containerName, snapshotName, sourceRequest, cancellationToken).ConfigureAwait(false);
        Api.Operation sourceOperationInfo = sourceOperation.GetOperation();
        Dictionary<string, string> sourceSecrets = ExtractSecrets(sourceOperationInfo.Metadata);

        if (args is not null && args.Mode == "relay")
        {
            request.Source.Type = "migration";
            request.Source.Mode = "push";

            Operation targetOperation = await CreateInstanceAsync(request, cancellationToken).ConfigureAwait(false);
            Api.Operation targetOperationInfo = targetOperation.GetOperation();
            Dictionary<string, string> targetSecrets = ExtractSecrets(targetOperationInfo.Metadata);

            await ProxyMigrationAsync(
                targetOperation, targetSecrets, source, sourceOperation, sourceSecrets, cancellationToken)
                .ConfigureAwait(false);

            return WrapOperation(targetOperation);
        }

        request.Source.Type = "migration";
        request.Source.Mode = "pull";
        request.Source.Operation = sourceOperationInfo.ID;
        request.Source.Websockets = sourceSecrets;
        request.Source.Certificate = sourceConnection.Certificate;

        return TryCreateInstance(request, sourceConnection.Addresses, sourceOperation, cancellationToken);
    }

    /// <summary>Renames an instance snapshot.</summary>
    public async Task<Operation> RenameInstanceSnapshotAsync(
        string instanceName,
        string name,
        Api.InstanceSnapshotPost instance,
        CancellationToken cancellationToken = default)
    {
        if (instance.Migration)
            throw new IncusException("Can't ask for a migration through RenameInstanceSnapshot");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            InstanceSnapshotPath(instanceName, name),
            instance,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Requests that Incus prepares for an instance snapshot migration.</summary>
    public async Task<Operation> MigrateInstanceSnapshotAsync(
        string instanceName,
        string name,
        Api.InstanceSnapshotPost instance,
        CancellationToken cancellationToken = default)
    {
        if (!instance.Migration)
            throw new IncusException("Can't ask for a rename through MigrateInstanceSnapshot");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            InstanceSnapshotPath(instanceName, name),
            instance,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Deletes an instance snapshot.</summary>
    public async Task<Operation> DeleteInstanceSnapshotAsync(
        string instanceName,
        string name,
        CancellationToken cancellationToken = default)
    {
        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Delete, InstanceSnapshotPath(instanceName, name), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return operation;
    }

    /// <summary>Updates an instance snapshot.</summary>
    public async Task<Operation> UpdateInstanceSnapshotAsync(
        string instanceName,
        string name,
        Api.InstanceSnapshotPut instance,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("snapshot_expiry");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Put,
            InstanceSnapshotPath(instanceName, name),
            instance,
            etag,
            cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Returns the runtime state of an instance.</summary>
    public async Task<(Api.InstanceState State, string ETag)> GetInstanceStateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        string path = IsAgent() ? "/state" : $"{InstancePath(name)}/state";

        (Api.InstanceState state, string etag) = await QueryStructAsync<Api.InstanceState>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return (state, etag);
    }

    /// <summary>Updates the runtime state of an instance.</summary>
    public async Task<Operation> UpdateInstanceStateAsync(
        string name,
        Api.InstanceStatePut state,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Put, $"{InstancePath(name)}/state", state, etag, cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Returns an access entry for an instance.</summary>
    public async Task<Api.Access> GetInstanceAccessAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instance_access");

        (Api.Access access, _) = await QueryStructAsync<Api.Access>(
            HttpMethod.Get, $"{InstancePath(name)}/access", cancellationToken: cancellationToken).ConfigureAwait(false);

        return access;
    }

    /// <summary>Returns the log file names of an instance.</summary>
    public async Task<List<string>> GetInstanceLogfilesAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        string basePath = $"{InstancePath(name)}/logs";
        (List<string> urls, _) = await QueryStructAsync<List<string>>(
            HttpMethod.Get, basePath, cancellationToken: cancellationToken).ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames(basePath, urls);
    }

    /// <summary>Returns the content of an instance log file.</summary>
    public async Task<Stream> GetInstanceLogfileAsync(
        string name,
        string filename,
        CancellationToken cancellationToken = default)
    {
        (Stream stream, _, HttpResponseMessage response) = await QueryStreamAsync(
            HttpMethod.Get, $"{InstancePath(name)}/logs/{Uri.EscapeDataString(filename)}", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new ResponseStream(stream, response);
    }

    /// <summary>Deletes an instance log file.</summary>
    public async Task DeleteInstanceLogfileAsync(
        string name,
        string filename,
        CancellationToken cancellationToken = default)
    {
        await QueryAsync(
            HttpMethod.Delete,
            $"{InstancePath(name)}/logs/{Uri.EscapeDataString(filename)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private Task<Stream> GetInstanceExecOutputLogFileAsync(
        string name,
        string filename,
        CancellationToken cancellationToken)
    {
        CheckExtension("container_exec_recording");

        return GetInstanceStreamAsync(
            $"{InstancePath(name)}/logs/exec-output/{Uri.EscapeDataString(filename)}", cancellationToken);
    }

    private async Task DeleteInstanceExecOutputLogFileAsync(
        string instanceName,
        string filename,
        CancellationToken cancellationToken)
    {
        CheckExtension("container_exec_recording");

        await QueryAsync(
            HttpMethod.Delete,
            $"{InstancePath(instanceName)}/logs/exec-output/{Uri.EscapeDataString(filename)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns instance metadata.</summary>
    public async Task<(Api.ImageMetadata Metadata, string ETag)> GetInstanceMetadataAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_edit_metadata");

        (Api.ImageMetadata metadata, string etag) = await QueryStructAsync<Api.ImageMetadata>(
            HttpMethod.Get, $"{InstancePath(name)}/metadata", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (metadata, etag);
    }

    /// <summary>Updates instance metadata.</summary>
    public async Task UpdateInstanceMetadataAsync(
        string name,
        Api.ImageMetadata metadata,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_edit_metadata");

        await QueryAsync(
            HttpMethod.Put, $"{InstancePath(name)}/metadata", metadata, etag, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the names of an instance's template files.</summary>
    public async Task<List<string>> GetInstanceTemplateFilesAsync(
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_edit_metadata");

        (List<string> templates, _) = await QueryStructAsync<List<string>>(
            HttpMethod.Get, $"{InstancePath(instanceName)}/metadata/templates", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return templates;
    }

    /// <summary>Returns the content of an instance template file.</summary>
    public async Task<Stream> GetInstanceTemplateFileAsync(
        string instanceName,
        string templateName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_edit_metadata");

        string path = IncusQuery.SetParameter(
            $"{InstancePath(instanceName)}/metadata/templates", "path", templateName);

        return await GetInstanceStreamAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates an instance template file.</summary>
    public async Task CreateInstanceTemplateFileAsync(
        string instanceName,
        string templateName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_edit_metadata");

        string path = IncusQuery.SetParameter(
            $"{InstancePath(instanceName)}/metadata/templates", "path", templateName);

        await QueryAsync(HttpMethod.Post, path, content, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes an instance template file.</summary>
    public async Task DeleteInstanceTemplateFileAsync(
        string name,
        string templateName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_edit_metadata");

        string path = IncusQuery.SetParameter(
            $"{InstancePath(name)}/metadata/templates", "path", templateName);

        await QueryAsync(HttpMethod.Delete, path, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Attaches to the console device of an instance.</summary>
    public async Task<Operation> ConsoleInstanceAsync(
        string instanceName,
        Api.InstanceConsolePost console,
        InstanceConsoleArgs args,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("console");

        if (string.IsNullOrEmpty(console.Type))
            console.Type = "console";

        if (console.Type == "vga")
            CheckExtension("console_vga_type");

        if (console.Force)
            CheckExtension("console_force");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            $"{InstancePath(instanceName)}/console",
            console,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        Api.Operation operationInfo = operation.GetOperation();
        Dictionary<string, string> fds = ExtractOperationMap(operationInfo, "fds");

        if (args.Terminal is null)
            throw new IncusException("A terminal must be set");

        if (args.Control is null)
            throw new IncusException("A control channel must be set");

        if (!fds.TryGetValue(SecretNameControl, out string? controlSecret) || string.IsNullOrEmpty(controlSecret))
            throw new IncusException("Did not receive a file descriptor for the control channel");

        IncusWebSocket control = await GetOperationWebSocketAsync(operationInfo.ID, controlSecret, cancellationToken)
            .ConfigureAwait(false);

        StartControlHandler(control, args.Control);

        IncusWebSocket connection = await GetOperationWebSocketAsync(
            operationInfo.ID, fds["0"], cancellationToken).ConfigureAwait(false);

        CancellationToken dataToken = args.ConsoleDisconnect == default
            ? ConnectedToken
            : args.ConsoleDisconnect;

        _ = Task.Run(async () =>
        {
            try
            {
                Task read = MirrorFromWebSocketAsync(connection, args.Terminal, dataToken);
                Task write = MirrorToWebSocketAsync(connection, args.Terminal, dataToken);

                await read.ConfigureAwait(false);
                await connection.DisposeAsync().ConfigureAwait(false);
                await write.ConfigureAwait(false);
            }
            catch
            {
                // Best effort console mirror.
            }
            finally
            {
                await control.DisposeAsync().ConfigureAwait(false);
            }
        }, CancellationToken.None);

        return operation;
    }

    /// <summary>Attaches to an instance console and returns a callback that opens further connections.</summary>
    public async Task<(Operation Operation, Func<Stream, Task> Connect)> ConsoleInstanceDynamicAsync(
        string instanceName,
        Api.InstanceConsolePost console,
        InstanceConsoleArgs args,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("console");

        if (string.IsNullOrEmpty(console.Type))
            console.Type = "console";

        if (console.Type == "vga")
            CheckExtension("console_vga_type");

        if (console.Force)
            CheckExtension("console_force");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            $"{InstancePath(instanceName)}/console",
            console,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        Api.Operation operationInfo = operation.GetOperation();
        Dictionary<string, string> fds = ExtractOperationMap(operationInfo, "fds");

        if (args.Control is null)
            throw new IncusException("A control channel must be set");

        if (!fds.TryGetValue(SecretNameControl, out string? controlSecret) || string.IsNullOrEmpty(controlSecret))
            throw new IncusException("Did not receive a file descriptor for the control channel");

        IncusWebSocket control = await GetOperationWebSocketAsync(operationInfo.ID, controlSecret, cancellationToken)
            .ConfigureAwait(false);

        StartControlHandler(control, args.Control);

        CancellationToken dataToken = args.ConsoleDisconnect == default
            ? ConnectedToken
            : args.ConsoleDisconnect;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, dataToken).ConfigureAwait(false);
            }
            catch
            {
                // Disconnect requested.
            }
            finally
            {
                await control.DisposeAsync().ConfigureAwait(false);
            }
        }, CancellationToken.None);

        async Task Connect(Stream terminal)
        {
            IncusWebSocket connection = await GetOperationWebSocketAsync(
                operationInfo.ID, fds["0"], dataToken).ConfigureAwait(false);

            try
            {
                Task read = MirrorFromWebSocketAsync(connection, terminal, dataToken);
                Task write = MirrorToWebSocketAsync(connection, terminal, dataToken);

                await read.ConfigureAwait(false);
                await write.ConfigureAwait(false);
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        return (operation, Connect);
    }

    /// <summary>Returns the console log of an instance.</summary>
    public async Task<Stream> GetInstanceConsoleLogAsync(
        string instanceName,
        InstanceConsoleLogArgs? args = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("console");

        return await GetInstanceStreamAsync($"{InstancePath(instanceName)}/console", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Deletes the console log of an instance.</summary>
    public async Task DeleteInstanceConsoleLogAsync(
        string instanceName,
        InstanceConsoleLogArgs? args = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("console");

        await QueryAsync(
            HttpMethod.Delete, $"{InstancePath(instanceName)}/console", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Returns the backup names of an instance.</summary>
    public async Task<List<string>> GetInstanceBackupNamesAsync(
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_backup");

        string basePath = $"{InstancePath(instanceName)}/backups";
        (List<string> urls, _) = await QueryStructAsync<List<string>>(
            HttpMethod.Get, basePath, cancellationToken: cancellationToken).ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames(basePath, urls);
    }

    /// <summary>Returns the backups of an instance.</summary>
    public async Task<List<Api.InstanceBackup>> GetInstanceBackupsAsync(
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_backup");

        string path = IncusQuery.SetParameter($"{InstancePath(instanceName)}/backups", "recursion", "1");
        (List<Api.InstanceBackup> backups, _) = await QueryStructAsync<List<Api.InstanceBackup>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return backups;
    }

    /// <summary>Returns an instance backup.</summary>
    public async Task<(Api.InstanceBackup Backup, string ETag)> GetInstanceBackupAsync(
        string instanceName,
        string name,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_backup");

        (Api.InstanceBackup backup, string etag) = await QueryStructAsync<Api.InstanceBackup>(
            HttpMethod.Get, InstanceBackupPath(instanceName, name), cancellationToken: cancellationToken).ConfigureAwait(false);

        return (backup, etag);
    }

    /// <summary>Creates a backup of an instance.</summary>
    public async Task<Operation> CreateInstanceBackupAsync(
        string instanceName,
        Api.InstanceBackupsPost backup,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_backup");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            $"{InstancePath(instanceName)}/backups",
            backup,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Renames an instance backup.</summary>
    public async Task<Operation> RenameInstanceBackupAsync(
        string instanceName,
        string name,
        Api.InstanceBackupPost backup,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_backup");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post,
            InstanceBackupPath(instanceName, name),
            backup,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Deletes an instance backup.</summary>
    public async Task<Operation> DeleteInstanceBackupAsync(
        string instanceName,
        string name,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_backup");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Delete, InstanceBackupPath(instanceName, name), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return operation;
    }

    /// <summary>Downloads an instance backup.</summary>
    public async Task<BackupFileResponse> GetInstanceBackupFileAsync(
        string instanceName,
        string name,
        BackupFileRequest request,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("container_backup");

        string path = $"{InstanceBackupPath(instanceName, name)}/export";
        (Stream stream, _, HttpResponseMessage response) = await QueryStreamAsync(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        using (response)
        using (stream)
        {
            long size = await CopyWithProgressAsync(
                stream, request.BackupFile, request.ProgressHandler, cancellationToken).ConfigureAwait(false);

            return new BackupFileResponse { Size = size };
        }
    }

    /// <summary>Creates a direct streaming backup of an instance.</summary>
    public async Task CreateInstanceBackupStreamAsync(
        string instanceName,
        Api.InstanceBackupsPost backup,
        BackupFileRequest request,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("direct_backup");

        string url = BuildQueryUrl($"{InstancePath(instanceName)}/backups");
        using HttpRequestMessage message = new(HttpMethod.Post, url);
        message.Headers.Accept.ParseAdd("application/octet-stream");

        string json = JsonSerializer.Serialize(backup, IncusJsonOptions);
        message.Content = new StringContent(json, Encoding.UTF8, "application/json");

        await AddClientHeadersAsync(message, cancellationToken).ConfigureAwait(false);

        using HttpResponseMessage response = await _http
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
            await ParseResponseAsync(response, url, cancellationToken).ConfigureAwait(false);

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await CopyWithProgressAsync(stream, request.BackupFile, request.ProgressHandler, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Returns memory debug information for an instance.</summary>
    public async Task<Stream> GetInstanceDebugMemoryAsync(
        string name,
        string format,
        CancellationToken cancellationToken = default)
    {
        string path = InstanceTypePath(InstanceType.VirtualMachine);
        path = IncusQuery.SetParameter(path, "format", format);
        path = InstanceQueryPath(path, name, "debug/memory");

        return await GetInstanceStreamAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a bitmap for an instance.</summary>
    public async Task CreateInstanceBitmapAsync(
        string name,
        Api.StorageVolumeBitmapsPost bitmap,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("storage_volume_nbd");

        await QueryAsync(
            HttpMethod.Post, $"{InstancePath(name)}/bitmaps", bitmap, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Runs a low-level repair action on an instance.</summary>
    public async Task RepairInstanceAsync(
        string name,
        Api.InstanceDebugRepairPost repair,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instances_debug_repair");

        await QueryAsync(
            HttpMethod.Post, $"{InstancePath(name)}/debug/repair", repair, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Returns all NVRAM variables of an instance, grouped by GUID.</summary>
    public async Task<Dictionary<string, Dictionary<string, Api.InstanceNVRAMVariable>>> GetInstanceNVRAMAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instance_nvram");

        string path = InstanceTypePath(InstanceType.VirtualMachine);
        path = IncusQuery.SetParameter(InstanceQueryPath(path, name, "nvram"), "recursion", "2");

        (Dictionary<string, Dictionary<string, Api.InstanceNVRAMVariable>> variables, _) =
            await QueryStructAsync<Dictionary<string, Dictionary<string, Api.InstanceNVRAMVariable>>>(
                HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return variables;
    }

    /// <summary>Returns the NVRAM variables of an instance for a specific GUID.</summary>
    public async Task<Dictionary<string, Api.InstanceNVRAMVariable>> GetInstanceNVRAMGUIDAsync(
        string name,
        string guid,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instance_nvram");

        string path = InstanceTypePath(InstanceType.VirtualMachine);
        path = InstanceQueryPath(path, name, $"nvram/{Uri.EscapeDataString(guid)}");
        path = IncusQuery.SetParameter(path, "recursion", "1");

        (Dictionary<string, Api.InstanceNVRAMVariable> variables, _) =
            await QueryStructAsync<Dictionary<string, Api.InstanceNVRAMVariable>>(
                HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return variables;
    }

    /// <summary>Returns a raw NVRAM variable and its attributes.</summary>
    public async Task<(byte[] Data, uint Attributes)> GetRawInstanceNVRAMGUIDVarAsync(
        string name,
        string guid,
        string varName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instance_nvram");

        string path = InstanceTypePath(InstanceType.VirtualMachine);
        path = InstanceQueryPath(
            path, name, $"nvram/{Uri.EscapeDataString(guid)}/{Uri.EscapeDataString(varName)}");

        string url = BuildQueryUrl(path);
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/octet-stream");
        await AddClientHeadersAsync(request, cancellationToken).ConfigureAwait(false);

        using HttpResponseMessage response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
            await ParseResponseAsync(response, url, cancellationToken).ConfigureAwait(false);

        string attributeText = GetHeader(response, "X-Incus-attributes");
        if (!uint.TryParse(attributeText, out uint attributes))
            throw new IncusException("Invalid or missing X-Incus-attributes header");

        byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return (data, attributes);
    }

    /// <summary>Returns an interpreted NVRAM variable.</summary>
    public async Task<(Api.InstanceNVRAMVariable Variable, string ETag)> GetInstanceNVRAMGUIDVarAsync(
        string name,
        string guid,
        string varName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instance_nvram");

        string path = InstanceTypePath(InstanceType.VirtualMachine);
        path = InstanceQueryPath(
            path, name, $"nvram/{Uri.EscapeDataString(guid)}/{Uri.EscapeDataString(varName)}");
        path = IncusQuery.SetParameter(path, "recursion", "1");

        (Api.InstanceNVRAMVariable variable, string etag) = await QueryStructAsync<Api.InstanceNVRAMVariable>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return (variable, etag);
    }

    /// <summary>Deletes an interpreted NVRAM variable.</summary>
    public async Task DeleteInstanceNVRAMGUIDVarAsync(
        string name,
        string guid,
        string varName,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instance_nvram");

        string path = InstanceTypePath(InstanceType.VirtualMachine);
        path = InstanceQueryPath(
            path, name, $"nvram/{Uri.EscapeDataString(guid)}/{Uri.EscapeDataString(varName)}");

        await QueryAsync(HttpMethod.Delete, path, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets or deletes interpreted NVRAM variables in bulk.</summary>
    public async Task UpdateInstanceNVRAMAsync(
        string name,
        Dictionary<string, Dictionary<string, Api.InstanceNVRAMVariablePut>> variables,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instance_nvram_bulk_update");

        string path = InstanceTypePath(InstanceType.VirtualMachine);
        path = InstanceQueryPath(path, name, "nvram");

        await QueryAsync(HttpMethod.Patch, path, variables, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets a raw NVRAM variable.</summary>
    public async Task UpdateRawInstanceNVRAMGUIDVarAsync(
        string name,
        string guid,
        string varName,
        byte[] data,
        uint attributes,
        long timestamp = 0,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instance_nvram");

        string path = InstanceTypePath(InstanceType.VirtualMachine);
        path = InstanceQueryPath(
            path, name, $"nvram/{Uri.EscapeDataString(guid)}/{Uri.EscapeDataString(varName)}");

        Dictionary<string, string[]> headers = new()
        {
            ["X-Incus-attributes"] = new[] { attributes.ToString() },
        };

        if (timestamp != 0)
            headers["X-Incus-timestamp"] = new[] { timestamp.ToString() };

        await RawQueryWithHeadersAsync(
            HttpMethod.Put, BuildQueryUrl(path), data, null, headers, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets an interpreted NVRAM variable.</summary>
    public async Task UpdateInstanceNVRAMGUIDVarAsync(
        string name,
        string guid,
        string varName,
        Api.InstanceNVRAMVariablePut data,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("instance_nvram");

        string path = InstanceTypePath(InstanceType.VirtualMachine);
        path = InstanceQueryPath(
            path, name, $"nvram/{Uri.EscapeDataString(guid)}/{Uri.EscapeDataString(varName)}");

        await QueryAsync(HttpMethod.Put, path, data, etag, cancellationToken).ConfigureAwait(false);
    }

    private static string InstanceQueryPath(string basePath, string instanceName, string suffix)
        => $"{basePath}/{Uri.EscapeDataString(instanceName)}/{suffix}";

    private async Task<Stream> GetInstanceStreamAsync(string path, CancellationToken cancellationToken)
    {
        (Stream stream, _, HttpResponseMessage response) = await QueryStreamAsync(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ResponseStream(stream, response);
    }

    private async Task<IncusConnectionInfo?> GetSourceImageConnectionInfoAsync(
        IImageServer source,
        Api.Image image,
        Api.InstanceSource instanceSource,
        CancellationToken cancellationToken)
    {
        instanceSource.Type = "image";

        if (IsSameServer(source))
        {
            instanceSource.Fingerprint = image.Fingerprint;
            instanceSource.Alias = "";
            return null;
        }

        instanceSource.Mode = "pull";

        if (!string.IsNullOrEmpty(instanceSource.Alias) && image.Public)
        {
            instanceSource.Fingerprint = "";
        }
        else
        {
            instanceSource.Fingerprint = image.Fingerprint;
            instanceSource.Alias = "";
        }

        IncusConnectionInfo info = source.GetConnectionInfo();
        instanceSource.Protocol = info.Protocol;
        instanceSource.Certificate = info.Certificate;

        if (!image.Public)
            instanceSource.Secret = await source.GetImageSecretAsync(image.Fingerprint, cancellationToken).ConfigureAwait(false);

        return info;
    }

    private bool IsSameServer(IImageServer source)
    {
        if (ReferenceEquals(this, source))
            return true;

        if (GetCachedServer() is null)
            return false;

        IncusConnectionInfo sourceInfo = source.GetConnectionInfo();
        IncusConnectionInfo destinationInfo = GetConnectionInfo();

        return sourceInfo.Protocol == destinationInfo.Protocol
            && sourceInfo.Certificate == destinationInfo.Certificate
            && sourceInfo.Project == destinationInfo.Project
            && sourceInfo.Target == destinationInfo.Target;
    }

    private RemoteOperation TryRebuildInstance(
        string instanceName,
        Api.InstanceRebuildPost request,
        List<string> urls,
        Operation? waitOperation,
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
                        if (string.IsNullOrEmpty(operation))
                            request.Source.Server = serverUrl;
                        else
                            request.Source.Operation = $"{serverUrl}/1.0/operations/{Uri.EscapeDataString(operation)}";

                        Operation target = await RebuildInstanceAsync(instanceName, request, cancellationToken)
                            .ConfigureAwait(false);

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

                IncusException failure = new("Failed instance rebuild", new AggregateException(errors));
                result.Fail(failure);
                completion.TrySetException(failure);
            }
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
        }, CancellationToken.None);

        return result;
    }

    private RemoteOperation TryCreateInstance(
        Api.InstancesPost request,
        List<string> urls,
        Operation? waitOperation,
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
                        if (string.IsNullOrEmpty(operation))
                            request.Source.Server = serverUrl;
                        else
                            request.Source.Operation = $"{serverUrl}/1.0/operations/{Uri.EscapeDataString(operation)}";

                        Operation target = await CreateInstanceAsync(request, cancellationToken).ConfigureAwait(false);
                        result.SetTarget(target);

                        Task wait = target.WaitAsync(cancellationToken);
                        if (waitOperation is not null)
                        {
                            Task sourceWait = waitOperation.WaitAsync(cancellationToken);
                            Task first = await Task.WhenAny(wait, sourceWait).ConfigureAwait(false);

                            if (first == sourceWait)
                            {
                                await sourceWait.ConfigureAwait(false);
                                await wait.ConfigureAwait(false);
                            }
                            else
                            {
                                await wait.ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            await wait.ConfigureAwait(false);
                        }

                        completion.TrySetResult();
                        return;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errors.Add(ex);
                    }
                }

                IncusException failure = new("Failed instance creation", new AggregateException(errors));
                result.Fail(failure);
                completion.TrySetException(failure);
            }
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
        }, CancellationToken.None);

        return result;
    }

    private RemoteOperation TryMigrateInstance(
        IncusClient source,
        string name,
        Api.InstancePost request,
        List<string> urls,
        Operation? waitOperation,
        CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
            throw new IncusException("The target server isn't listening on the network");

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
                        request.Target ??= new Api.InstancePostTarget();
                        request.Target.Operation = $"{serverUrl}/1.0/operations/{Uri.EscapeDataString(operation)}";

                        Operation target = await source.MigrateInstanceAsync(name, request, cancellationToken)
                            .ConfigureAwait(false);

                        result.SetTarget(target);

                        Task wait = target.WaitAsync(cancellationToken);
                        if (waitOperation is not null)
                        {
                            Task targetWait = waitOperation.WaitAsync(cancellationToken);
                            Task first = await Task.WhenAny(wait, targetWait).ConfigureAwait(false);

                            if (first == targetWait)
                            {
                                await targetWait.ConfigureAwait(false);
                                await wait.ConfigureAwait(false);
                            }
                            else
                            {
                                await wait.ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            await wait.ConfigureAwait(false);
                        }

                        completion.TrySetResult();
                        return;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errors.Add(ex);
                    }
                }

                IncusException failure = new("Failed instance migration", new AggregateException(errors));
                result.Fail(failure);
                completion.TrySetException(failure);
            }
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
        }, CancellationToken.None);

        return result;
    }

    private RemoteOperation TryMigrateInstanceSnapshot(
        IncusClient source,
        string instanceName,
        string name,
        Api.InstanceSnapshotPost request,
        List<string> urls,
        CancellationToken cancellationToken)
    {
        if (urls.Count == 0)
            throw new IncusException("The target server isn't listening on the network");

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
                        request.Target ??= new Api.InstancePostTarget();
                        request.Target.Operation = $"{serverUrl}/1.0/operations/{Uri.EscapeDataString(operation)}";

                        Operation target = await source.MigrateInstanceSnapshotAsync(
                            instanceName, name, request, cancellationToken).ConfigureAwait(false);

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

                IncusException failure = new("Failed instance migration", new AggregateException(errors));
                result.Fail(failure);
                completion.TrySetException(failure);
            }
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
        }, CancellationToken.None);

        return result;
    }
}
