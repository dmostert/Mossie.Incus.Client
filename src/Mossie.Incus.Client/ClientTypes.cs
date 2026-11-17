using Mossie.Incus.Client.Api;

namespace Mossie.Incus.Client;

/// <summary>Identifies the kind of instance to operate on.</summary>
public enum InstanceType
{
    /// <summary>All instance types.</summary>
    Any,

    /// <summary>Containers only.</summary>
    Container,

    /// <summary>Virtual machines only.</summary>
    VirtualMachine,
}

/// <summary>Converts <see cref="InstanceType"/> values to the API representation.</summary>
public static class InstanceTypeExtensions
{
    /// <summary>Returns the API string for the supplied instance type.</summary>
    public static string ToApiString(this InstanceType type) => type switch
    {
        InstanceType.Container => "container",
        InstanceType.VirtualMachine => "virtual-machine",
        _ => "",
    };
}

/// <summary>Options passed when downloading a backup file.</summary>
public sealed class BackupFileRequest
{
    /// <summary>Destination for the backup file.</summary>
    public Stream BackupFile { get; set; } = Stream.Null;

    /// <summary>Receives progress updates as (current, total) byte counts.</summary>
    public Action<long, long>? ProgressHandler { get; set; }

    /// <summary>Optional cancellation source for the download.</summary>
    public CancellationToken CancellationToken { get; set; }
}

/// <summary>Result of a backup file download.</summary>
public sealed class BackupFileResponse
{
    /// <summary>Size of the backup file in bytes.</summary>
    public long Size { get; set; }
}

/// <summary>Options passed when uploading an image directly.</summary>
public sealed class ImageCreateArgs
{
    /// <summary>Stream containing the image metadata tarball.</summary>
    public Stream? MetaFile { get; set; }

    /// <summary>Filename for the metadata tarball.</summary>
    public string MetaName { get; set; } = "";

    /// <summary>Stream containing the root filesystem tarball.</summary>
    public Stream? RootfsFile { get; set; }

    /// <summary>Filename for the root filesystem tarball.</summary>
    public string RootfsName { get; set; } = "";

    /// <summary>Receives progress updates as (current, total) byte counts.</summary>
    public Action<long, long>? ProgressHandler { get; set; }

    /// <summary>Type of the image (container or virtual-machine).</summary>
    public string Type { get; set; } = "";
}

/// <summary>Options passed when downloading an image file.</summary>
public sealed class ImageFileRequest
{
    /// <summary>Destination for the metadata file.</summary>
    public Stream? MetaFile { get; set; }

    /// <summary>Destination for the root filesystem file.</summary>
    public Stream? RootfsFile { get; set; }

    /// <summary>Receives progress updates as (current, total) byte counts.</summary>
    public Action<long, long>? ProgressHandler { get; set; }

    /// <summary>Optional cancellation source for the download.</summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>Returns the path of a local copy of an image file, or an empty string when unavailable.</summary>
    public Func<string, string, string>? DeltaSourceRetriever { get; set; }
}

/// <summary>Result of an image file download.</summary>
public sealed class ImageFileResponse
{
    /// <summary>Filename of the metadata file.</summary>
    public string MetaName { get; set; } = "";

    /// <summary>Size of the metadata file in bytes.</summary>
    public long MetaSize { get; set; }

    /// <summary>Filename of the root filesystem file.</summary>
    public string RootfsName { get; set; } = "";

    /// <summary>Size of the root filesystem file in bytes.</summary>
    public long RootfsSize { get; set; }
}

/// <summary>Options passed when copying an image.</summary>
public sealed class ImageCopyArgs
{
    /// <summary>Aliases to add to the copied image.</summary>
    public List<ImageAlias> Aliases { get; set; } = new();

    /// <summary>Whether Incus should keep this image up to date.</summary>
    public bool AutoUpdate { get; set; }

    /// <summary>Whether to copy the source image aliases to the target.</summary>
    public bool CopyAliases { get; set; }

    /// <summary>Whether this image is available to unauthenticated users.</summary>
    public bool Public { get; set; }

    /// <summary>The image type to use for resolution.</summary>
    public string Type { get; set; } = "";

    /// <summary>The transfer mode: <c>pull</c> (default), <c>push</c> or <c>relay</c>.</summary>
    public string Mode { get; set; } = "";

    /// <summary>Profiles to apply on the target.</summary>
    public List<string> Profiles { get; set; } = new();
}

/// <summary>Options passed when copying a storage volume.</summary>
public class StoragePoolVolumeCopyArgs
{
    /// <summary>New name for the target.</summary>
    public string Name { get; set; } = "";

    /// <summary>The transfer mode: <c>pull</c> (default), <c>push</c> or <c>relay</c>.</summary>
    public string Mode { get; set; } = "";

    /// <summary>Copy only the volume, without its snapshots.</summary>
    public bool VolumeOnly { get; set; }

    /// <summary>Refresh an existing volume instead of failing when it exists.</summary>
    public bool Refresh { get; set; }

    /// <summary>Skip snapshots that are older than the target's existing ones during refresh.</summary>
    public bool RefreshExcludeOlder { get; set; }
}

/// <summary>Options passed when moving a storage volume.</summary>
public sealed class StoragePoolVolumeMoveArgs : StoragePoolVolumeCopyArgs
{
    /// <summary>Project to move the volume to.</summary>
    public string Project { get; set; } = "";
}

/// <summary>Options passed when creating a storage volume from a backup.</summary>
public sealed class StorageVolumeBackupArgs
{
    /// <summary>The backup file.</summary>
    public Stream BackupFile { get; set; } = Stream.Null;

    /// <summary>Name to import the backup as.</summary>
    public string Name { get; set; } = "";
}

/// <summary>Options passed when creating an instance from a backup.</summary>
public sealed class InstanceBackupArgs
{
    /// <summary>The backup file.</summary>
    public Stream BackupFile { get; set; } = Stream.Null;

    /// <summary>Storage pool to use.</summary>
    public string PoolName { get; set; } = "";

    /// <summary>Name to import the backup as.</summary>
    public string Name { get; set; } = "";

    /// <summary>Configuration overrides.</summary>
    public List<string> Config { get; set; } = new();

    /// <summary>Device overrides.</summary>
    public List<string> Devices { get; set; } = new();
}

/// <summary>Options passed when copying an instance.</summary>
public sealed class InstanceCopyArgs
{
    /// <summary>If set, the instance will be renamed on copy.</summary>
    public string Name { get; set; } = "";

    /// <summary>Transfer the running state (live migration).</summary>
    public bool Live { get; set; }

    /// <summary>Copy only the instance, without its snapshots.</summary>
    public bool InstanceOnly { get; set; }

    /// <summary>The transfer mode: <c>pull</c> (default), <c>push</c> or <c>relay</c>.</summary>
    public string Mode { get; set; } = "";

    /// <summary>Perform an incremental copy.</summary>
    public bool Refresh { get; set; }

    /// <summary>Skip snapshots older than the target's existing ones during refresh.</summary>
    public bool RefreshExcludeOlder { get; set; }

    /// <summary>Allow copying an instance whose filesystem is inconsistent.</summary>
    public bool AllowInconsistent { get; set; }
}

/// <summary>Options passed when copying an instance snapshot.</summary>
public sealed class InstanceSnapshotCopyArgs
{
    /// <summary>If set, the instance will be renamed on copy.</summary>
    public string Name { get; set; } = "";

    /// <summary>The transfer mode: <c>pull</c> (default), <c>push</c> or <c>relay</c>.</summary>
    public string Mode { get; set; } = "";

    /// <summary>Transfer the running state (live migration).</summary>
    public bool Live { get; set; }
}

/// <summary>Options passed when attaching to an instance console.</summary>
public sealed class InstanceConsoleArgs
{
    /// <summary>Bidirectional stream connected to the console.</summary>
    public Stream? Terminal { get; set; }

    /// <summary>
    /// Control-message handler (for example window resize). The handler owns the supplied
    /// <see cref="IncusWebSocket"/> and runs its own read/write loop, mirroring the Go client's
    /// <c>func(conn *websocket.Conn)</c> control callback.
    /// </summary>
    public Func<IncusWebSocket, Task>? Control { get; set; }

    /// <summary>Cancel to disconnect from the console.</summary>
    public CancellationToken ConsoleDisconnect { get; set; }
}

/// <summary>Options passed when retrieving an instance console log.</summary>
public sealed class InstanceConsoleLogArgs
{
}

/// <summary>Options passed when executing a command in an instance.</summary>
public sealed class InstanceExecArgs
{
    /// <summary>
    /// Bidirectional terminal used for interactive exec (a single PTY over one websocket).
    /// When set it takes precedence over <see cref="Stdin"/> and <see cref="Stdout"/>; the same
    /// stream is read for input and written for output. Ignored for non-interactive exec.
    /// </summary>
    public Stream? Terminal { get; set; }

    /// <summary>Standard input (non-interactive exec, or interactive when <see cref="Terminal"/> is not set).</summary>
    public Stream? Stdin { get; set; }

    /// <summary>Standard output (non-interactive exec, or interactive when <see cref="Terminal"/> is not set).</summary>
    public Stream? Stdout { get; set; }

    /// <summary>Standard error (non-interactive exec only; interactive PTYs merge stderr into stdout).</summary>
    public Stream? Stderr { get; set; }

    /// <summary>
    /// Control-message handler (window resize, signals, ...). The handler owns the supplied
    /// <see cref="IncusWebSocket"/> and runs its own read/write loop, mirroring the Go client's
    /// <c>func(conn *websocket.Conn)</c> control callback.
    /// </summary>
    public Func<IncusWebSocket, Task>? Control { get; set; }

    /// <summary>Signalled when all data operations are done.</summary>
    public TaskCompletionSource? DataDone { get; set; }

    /// <summary>Optional cancellation source for the data channels.</summary>
    public CancellationToken CancellationToken { get; set; }
}

/// <summary>Options passed when uploading a file to an instance or storage volume.</summary>
public sealed class InstanceFileArgs
{
    /// <summary>File content.</summary>
    public Stream Content { get; set; } = Stream.Null;

    /// <summary>User id that owns the file.</summary>
    public long UID { get; set; } = -1;

    /// <summary>Group id that owns the file.</summary>
    public long GID { get; set; } = -1;

    /// <summary>File permissions (octal).</summary>
    public int Mode { get; set; } = -1;

    /// <summary>File type (<c>file</c> or <c>directory</c>).</summary>
    public string Type { get; set; } = "";

    /// <summary>File write mode (<c>overwrite</c> or <c>append</c>).</summary>
    public string WriteMode { get; set; } = "";
}

/// <summary>Options passed when connecting to an instance's disks over NBD.</summary>
public sealed class InstanceNBDArgs
{
    /// <summary>Connect to an already running NBD session.</summary>
    public bool Reuse { get; set; }
}

/// <summary>Result of an instance or storage volume file download.</summary>
public sealed class InstanceFileResponse
{
    /// <summary>User id that owns the file.</summary>
    public long UID { get; set; }

    /// <summary>Group id that owns the file.</summary>
    public long GID { get; set; }

    /// <summary>File permissions.</summary>
    public int Mode { get; set; }

    /// <summary>File type.</summary>
    public string Type { get; set; } = "";

    /// <summary>If a directory, the list of entries inside it.</summary>
    public List<string> Entries { get; set; } = new();
}

/// <summary>Options passed when creating a storage bucket from a backup.</summary>
public sealed class StoragePoolBucketBackupArgs
{
    /// <summary>The backup file.</summary>
    public Stream BackupFile { get; set; } = Stream.Null;

    /// <summary>Name to import the backup as.</summary>
    public string Name { get; set; } = "";
}

/// <summary>Options passed when connecting to a storage volume over NBD.</summary>
public sealed class StorageVolumeNBDPost
{
    /// <summary>Request a writable connection.</summary>
    public bool Writable { get; set; }
}
