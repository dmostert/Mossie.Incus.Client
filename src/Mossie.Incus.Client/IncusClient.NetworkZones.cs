using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the names of all network zones.</summary>
    public async Task<List<string>> GetNetworkZoneNamesAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("network_dns");

        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, "/network-zones", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/network-zones", urls);
    }

    /// <summary>Returns all network zones.</summary>
    public async Task<List<Api.NetworkZone>> GetNetworkZonesAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("network_dns");

        (List<Api.NetworkZone> zones, _) = await QueryStructAsync<List<Api.NetworkZone>>(
            HttpMethod.Get, "/network-zones?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return zones;
    }

    /// <summary>Returns all network zones across all projects.</summary>
    public async Task<List<Api.NetworkZone>> GetNetworkZonesAllProjectsAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("network_dns");
        CheckExtension("network_zones_all_projects");

        (List<Api.NetworkZone> zones, _) = await QueryStructAsync<List<Api.NetworkZone>>(
            HttpMethod.Get, "/network-zones?recursion=1&all-projects=true", cancellationToken: cancellationToken).ConfigureAwait(false);

        return zones;
    }

    /// <summary>Returns a network zone.</summary>
    public async Task<(Api.NetworkZone Zone, string ETag)> GetNetworkZoneAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_dns");

        (Api.NetworkZone zone, string etag) = await QueryStructAsync<Api.NetworkZone>(
            HttpMethod.Get, $"/network-zones/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (zone, etag);
    }

    /// <summary>Creates a network zone.</summary>
    public async Task CreateNetworkZoneAsync(Api.NetworkZonesPost zone, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_dns");
        await QueryAsync(HttpMethod.Post, "/network-zones", zone, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates a network zone.</summary>
    public async Task UpdateNetworkZoneAsync(string name, Api.NetworkZonePut zone, string? etag = null, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_dns");
        await QueryAsync(HttpMethod.Put, $"/network-zones/{Uri.EscapeDataString(name)}", zone, etag, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a network zone.</summary>
    public async Task DeleteNetworkZoneAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_dns");
        await QueryAsync(HttpMethod.Delete, $"/network-zones/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the names of all records in a network zone.</summary>
    public async Task<List<string>> GetNetworkZoneRecordNamesAsync(string zone, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_dns_records");

        (List<string> urls, _) = await QueryStructAsync<List<string>>(
            HttpMethod.Get, $"/network-zones/{Uri.EscapeDataString(zone)}/records", cancellationToken: cancellationToken).ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames($"/network-zones/{Uri.EscapeDataString(zone)}/records", urls);
    }

    /// <summary>Returns all records in a network zone.</summary>
    public async Task<List<Api.NetworkZoneRecord>> GetNetworkZoneRecordsAsync(string zone, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_dns_records");

        (List<Api.NetworkZoneRecord> records, _) = await QueryStructAsync<List<Api.NetworkZoneRecord>>(
            HttpMethod.Get, $"/network-zones/{Uri.EscapeDataString(zone)}/records?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return records;
    }

    /// <summary>Returns a network zone record.</summary>
    public async Task<(Api.NetworkZoneRecord Record, string ETag)> GetNetworkZoneRecordAsync(
        string zone,
        string name,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("network_dns_records");

        (Api.NetworkZoneRecord record, string etag) = await QueryStructAsync<Api.NetworkZoneRecord>(
            HttpMethod.Get,
            $"/network-zones/{Uri.EscapeDataString(zone)}/records/{Uri.EscapeDataString(name)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (record, etag);
    }

    /// <summary>Creates a network zone record.</summary>
    public async Task CreateNetworkZoneRecordAsync(
        string zone,
        Api.NetworkZoneRecordsPost record,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("network_dns_records");
        await QueryAsync(HttpMethod.Post, $"/network-zones/{Uri.EscapeDataString(zone)}/records", record, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Updates a network zone record.</summary>
    public async Task UpdateNetworkZoneRecordAsync(
        string zone,
        string name,
        Api.NetworkZoneRecordPut record,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        CheckExtension("network_dns_records");
        await QueryAsync(
            HttpMethod.Put,
            $"/network-zones/{Uri.EscapeDataString(zone)}/records/{Uri.EscapeDataString(name)}",
            record,
            etag,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a network zone record.</summary>
    public async Task DeleteNetworkZoneRecordAsync(string zone, string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("network_dns_records");
        await QueryAsync(
            HttpMethod.Delete,
            $"/network-zones/{Uri.EscapeDataString(zone)}/records/{Uri.EscapeDataString(name)}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
