using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Serialization;
using static Mossie.Incus.Client.Api.IncusConstants;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the server status.</summary>
    public async Task<(Api.Server Server, string ETag)> GetServerAsync(CancellationToken cancellationToken = default)
    {
        (Api.Server server, string etag) = await QueryStructAsync<Api.Server>(HttpMethod.Get, "", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(server.Environment.CertificateFingerprint) && !string.IsNullOrEmpty(server.Environment.Certificate))
            server.Environment.CertificateFingerprint = CertificateFingerprint(server.Environment.Certificate);

        if (!server.Public && (server.AuthMethods is null || server.AuthMethods.Count == 0))
            server.AuthMethods = new List<string> { AuthenticationMethodTLS };

        SetServer(server);
        if (string.IsNullOrEmpty(_serverCertificate))
            SetServerCertificate(server.Environment.Certificate);

        return (server, etag);
    }

    /// <summary>Updates the server status.</summary>
    public async Task UpdateServerAsync(Api.ServerPut server, string? etag = null, CancellationToken cancellationToken = default)
    {
        await QueryAsync(HttpMethod.Put, "", server, etag, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns true when the server is an Incus agent.</summary>
    public bool IsAgent() => GetCachedServer()?.Environment.Server == "incus-agent";

    /// <summary>Returns the resources available to the server.</summary>
    public async Task<Api.Resources> GetServerResourcesAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("resources");
        (Api.Resources resources, _) = await QueryStructAsync<Api.Resources>(HttpMethod.Get, "/resources", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return resources;
    }

    /// <summary>Returns the OpenMetrics text produced by the server.</summary>
    public async Task<string> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (!HasExtension("metrics"))
            throw new IncusException("The server is missing the required \"metrics\" API extension");

        using HttpResponseMessage response = await SendRawAsync(HttpMethod.Get, "/metrics", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new IncusException($"Bad HTTP status: {(int)response.StatusCode}");

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies a server preseed configuration.</summary>
    public async Task ApplyServerPreseedAsync(Api.InitPreseed config, CancellationToken cancellationToken = default)
    {
        if (config.Config is { Count: > 0 })
        {
            (Api.Server server, string etag) = await GetServerAsync(cancellationToken).ConfigureAwait(false);

            server.Config ??= new ConfigMap();
            foreach (KeyValuePair<string, string> item in config.Config)
                server.Config[item.Key] = item.Value;

            await UpdateServerAsync(server.Writable(), etag, cancellationToken).ConfigureAwait(false);
        }

        if (config.StoragePools is { Count: > 0 })
        {
            List<string> poolNames = await GetStoragePoolNamesAsync(cancellationToken).ConfigureAwait(false);

            foreach (Api.StoragePoolsPost pool in config.StoragePools)
            {
                if (!poolNames.Contains(pool.Name))
                {
                    await CreateStoragePoolAsync(pool, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                (Api.StoragePool current, string etag) = await GetStoragePoolAsync(pool.Name, cancellationToken).ConfigureAwait(false);
                if (current.Driver != pool.Driver)
                    throw new IncusException($"Storage pool \"{pool.Name}\" is of type \"{pool.Driver}\" instead of \"{pool.Driver}\".");

                if (!string.IsNullOrEmpty(pool.Description))
                    current.Description = pool.Description;

                current.Config ??= new ConfigMap();
                if (pool.Config is not null)
                {
                    foreach (KeyValuePair<string, string> item in pool.Config)
                        current.Config[item.Key] = item.Value;
                }

                await UpdateStoragePoolAsync(pool.Name, current.Writable(), etag, cancellationToken).ConfigureAwait(false);
            }
        }

        if (config.Projects is { Count: > 0 })
        {
            List<string> projectNames = await GetProjectNamesAsync(cancellationToken).ConfigureAwait(false);

            foreach (Api.ProjectsPost project in config.Projects)
            {
                if (!projectNames.Contains(project.Name))
                {
                    await CreateProjectAsync(project, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                (Api.Project current, string etag) = await GetProjectAsync(project.Name, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(project.Description))
                    current.Description = project.Description;

                current.Config ??= new ConfigMap();
                if (project.Config is not null)
                {
                    foreach (KeyValuePair<string, string> item in project.Config)
                        current.Config[item.Key] = item.Value;
                }

                await UpdateProjectAsync(project.Name, current.Writable(), etag, cancellationToken).ConfigureAwait(false);
            }
        }

        if (config.Networks is { Count: > 0 })
        {
            // Networks in the default project first, so that other projects may reference them.
            foreach (Api.InitNetworksProjectPost network in config.Networks.Where(n => string.IsNullOrEmpty(n.Project) || n.Project == ProjectDefaultName))
                await ApplyPreseedNetworkAsync(network, cancellationToken).ConfigureAwait(false);

            foreach (Api.InitNetworksProjectPost network in config.Networks.Where(n => !string.IsNullOrEmpty(n.Project) && n.Project != ProjectDefaultName))
                await ApplyPreseedNetworkAsync(network, cancellationToken).ConfigureAwait(false);
        }

        if (config.StorageVolumes is { Count: > 0 })
        {
            foreach (Api.InitStorageVolumesProjectPost volume in config.StorageVolumes)
            {
                IncusClient projectClient = UseProject(volume.Project);
                try
                {
                    (Api.StorageVolume current, string etag) = await projectClient
                        .GetStoragePoolVolumeAsync(volume.Pool, volume.Type, volume.Name, cancellationToken)
                        .ConfigureAwait(false);

                    if (current.Type != volume.Type)
                        throw new IncusException($"Storage volume \"{volume.Name}\" in project \"{volume.Project}\" is of type \"{current.Type}\" instead of \"{volume.Type}\".");

                    if (!string.IsNullOrEmpty(volume.Description))
                        current.Description = volume.Description;

                    current.Config ??= new ConfigMap();
                    if (volume.Config is not null)
                    {
                        foreach (KeyValuePair<string, string> item in volume.Config)
                            current.Config[item.Key] = item.Value;
                    }

                    await projectClient.UpdateStoragePoolVolumeAsync(volume.Pool, volume.Type, volume.Name, current.Writable(), etag, cancellationToken).ConfigureAwait(false);
                }
                catch (IncusStatusException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
                {
                    await projectClient.CreateStoragePoolVolumeAsync(volume.Pool, volume.ToPost(), cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task ApplyPreseedNetworkAsync(Api.InitNetworksProjectPost target, CancellationToken cancellationToken)
    {
        string project = string.IsNullOrEmpty(target.Project) ? ProjectDefaultName : target.Project;
        IncusClient projectClient = UseProject(project);

        Api.Network? network = null;
        string etag = "";
        try
        {
            (network, etag) = await projectClient.GetNetworkAsync(target.Name, cancellationToken).ConfigureAwait(false);
        }
        catch (IncusStatusException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            network = null;
        }

        if (network is null || !network.Managed)
        {
            await projectClient.CreateNetworkAsync(target.ToPost(), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrEmpty(target.Description))
            network.Description = target.Description;

        network.Config ??= new ConfigMap();
        if (target.Config is not null)
        {
            foreach (KeyValuePair<string, string> item in target.Config)
                network.Config[item.Key] = item.Value;
        }

        await projectClient.UpdateNetworkAsync(target.Name, network.Writable(), etag, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Computes the SHA-256 fingerprint (lowercase hex) of a PEM encoded certificate.</summary>
    public static string CertificateFingerprint(string pem)
    {
        using X509Certificate2 certificate = X509Certificate2.CreateFromPem(pem);
        return Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();
    }

    /// <summary>Sends a request without parsing the JSON response envelope.</summary>
    internal async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(method, BuildQueryUrl(path));
        await AddClientHeadersAsync(request, cancellationToken).ConfigureAwait(false);

        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            request.Dispose();
        }
    }
}
