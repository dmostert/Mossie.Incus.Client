using System.Reflection;

namespace Mossie.Incus.Client.Api;

/// <summary>
/// Helpers that emulate Go's <c>Writable()</c> methods by copying the writable subset of a model
/// onto its corresponding <c>*Put</c> type.
/// </summary>
public static class ApiModelExtensions
{
    /// <summary>Copies all properties that also exist on <typeparamref name="TTarget"/>.</summary>
    public static TTarget ToPut<TTarget>(this object source)
        where TTarget : new()
    {
        TTarget target = new();
        Type targetType = typeof(TTarget);

        foreach (PropertyInfo sourceProperty in source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!sourceProperty.CanRead || sourceProperty.GetIndexParameters().Length > 0)
                continue;

            PropertyInfo? targetProperty = targetType.GetProperty(sourceProperty.Name, BindingFlags.Public | BindingFlags.Instance);
            if (targetProperty is null || !targetProperty.CanWrite)
                continue;

            if (!targetProperty.PropertyType.IsAssignableFrom(sourceProperty.PropertyType))
                continue;

            targetProperty.SetValue(target, sourceProperty.GetValue(source));
        }

        return target;
    }

    /// <summary>Returns the embedded <see cref="NetworksPost"/> portion of an initialization entry.</summary>
    public static NetworksPost ToPost(this InitNetworksProjectPost value) => new()
    {
        Config = value.Config,
        Description = value.Description,
        Name = value.Name,
        Type = value.Type,
    };

    /// <summary>Returns the embedded <see cref="StorageVolumesPost"/> portion of an initialization entry.</summary>
    public static StorageVolumesPost ToPost(this InitStorageVolumesProjectPost value) => new()
    {
        Config = value.Config,
        Description = value.Description,
        Restore = value.Restore,
        Name = value.Name,
        Type = value.Type,
        Source = value.Source,
        ContentType = value.ContentType,
    };

    /// <summary>Returns the embedded <see cref="ProfilesPost"/> portion of an initialization entry.</summary>
    public static ProfilesPost ToPost(this InitProfileProjectPost value) => new()
    {
        Config = value.Config,
        Description = value.Description,
        Devices = value.Devices is null
            ? null
            : value.Devices.ToDictionary(pair => pair.Key, pair => new Dictionary<string, string>(pair.Value)),
        Name = value.Name,
    };

    public static StorageVolumeSnapshotPut Writable(this StorageVolumeSnapshot value) => value.ToPut<StorageVolumeSnapshotPut>();

    public static StorageVolumePut Writable(this StorageVolume value) => value.ToPut<StorageVolumePut>();

    public static StorageBucketPut Writable(this StorageBucket value) => value.ToPut<StorageBucketPut>();

    public static StorageBucketKeyPut Writable(this StorageBucketKey value) => value.ToPut<StorageBucketKeyPut>();

    public static StoragePoolPut Writable(this StoragePool value) => value.ToPut<StoragePoolPut>();

    public static ServerPut Writable(this Server value) => value.ToPut<ServerPut>();

    public static ProjectPut Writable(this Project value) => value.ToPut<ProjectPut>();

    public static ProfilePut Writable(this Profile value) => value.ToPut<ProfilePut>();

    public static NetworkZonePut Writable(this NetworkZone value) => value.ToPut<NetworkZonePut>();

    public static NetworkZoneRecordPut Writable(this NetworkZoneRecord value) => value.ToPut<NetworkZoneRecordPut>();

    public static NetworkPeerPut Writable(this NetworkPeer value) => value.ToPut<NetworkPeerPut>();

    public static NetworkLoadBalancerPut Writable(this NetworkLoadBalancer value) => value.ToPut<NetworkLoadBalancerPut>();

    public static NetworkIntegrationPut Writable(this NetworkIntegration value) => value.ToPut<NetworkIntegrationPut>();

    public static NetworkForwardPut Writable(this NetworkForward value) => value.ToPut<NetworkForwardPut>();

    public static NetworkAddressSetPut Writable(this NetworkAddressSet value) => value.ToPut<NetworkAddressSetPut>();

    public static NetworkACLPut Writable(this NetworkACL value) => value.ToPut<NetworkACLPut>();

    public static NetworkPut Writable(this Network value) => value.ToPut<NetworkPut>();

    public static InstanceSnapshotPut Writable(this InstanceSnapshot value) => value.ToPut<InstanceSnapshotPut>();

    public static InstancePut Writable(this Instance value) => value.ToPut<InstancePut>();

    public static ImagePut Writable(this Image value) => value.ToPut<ImagePut>();

    public static ClusterMemberPut Writable(this ClusterMember value) => value.ToPut<ClusterMemberPut>();

    public static ClusterGroupPut Writable(this ClusterGroup value) => value.ToPut<ClusterGroupPut>();

    public static CertificatePut Writable(this Certificate value) => value.ToPut<CertificatePut>();
}
