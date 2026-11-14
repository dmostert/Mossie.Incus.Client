using Mossie.Incus.Client.Api;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the configuration metadata.</summary>
    public async Task<Api.MetadataConfiguration> GetMetadataConfigurationAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("metadata_configuration");

        (Api.MetadataConfiguration configuration, _) = await QueryStructAsync<Api.MetadataConfiguration>(
            HttpMethod.Get, "/metadata/configuration", cancellationToken: cancellationToken).ConfigureAwait(false);

        return configuration;
    }
}
