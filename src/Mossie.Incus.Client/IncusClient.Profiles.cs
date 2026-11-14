using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the available profile names.</summary>
    public async Task<List<string>> GetProfileNamesAsync(CancellationToken cancellationToken = default)
    {
        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, "/profiles", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/profiles", urls);
    }

    /// <summary>Returns all profiles.</summary>
    public async Task<List<Api.Profile>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        (List<Api.Profile> profiles, _) = await QueryStructAsync<List<Api.Profile>>(
            HttpMethod.Get, "/profiles?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return profiles;
    }

    /// <summary>Returns a filtered list of profiles.</summary>
    public async Task<List<Api.Profile>> GetProfilesWithFilterAsync(IEnumerable<string> filters, CancellationToken cancellationToken = default)
    {
        string path = $"/profiles?recursion=1&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}";

        (List<Api.Profile> profiles, _) = await QueryStructAsync<List<Api.Profile>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return profiles;
    }

    /// <summary>Returns all profiles across all projects.</summary>
    public async Task<List<Api.Profile>> GetProfilesAllProjectsAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("profiles_all_projects");

        (List<Api.Profile> profiles, _) = await QueryStructAsync<List<Api.Profile>>(
            HttpMethod.Get, "/profiles?recursion=1&all-projects=true", cancellationToken: cancellationToken).ConfigureAwait(false);

        return profiles;
    }

    /// <summary>Returns a filtered list of profiles across all projects.</summary>
    public async Task<List<Api.Profile>> GetProfilesAllProjectsWithFilterAsync(IEnumerable<string> filters, CancellationToken cancellationToken = default)
    {
        CheckExtension("profiles_all_projects");

        string path = $"/profiles?recursion=1&all-projects=true&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}";

        (List<Api.Profile> profiles, _) = await QueryStructAsync<List<Api.Profile>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return profiles;
    }

    /// <summary>Returns a profile entry.</summary>
    public async Task<(Api.Profile Profile, string ETag)> GetProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        (Api.Profile profile, string etag) = await QueryStructAsync<Api.Profile>(
            HttpMethod.Get, $"/profiles/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (profile, etag);
    }

    /// <summary>Creates a new profile.</summary>
    public async Task CreateProfileAsync(Api.ProfilesPost profile, CancellationToken cancellationToken = default)
    {
        await QueryAsync(HttpMethod.Post, "/profiles", profile, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates a profile.</summary>
    public async Task UpdateProfileAsync(string name, Api.ProfilePut profile, string? etag = null, CancellationToken cancellationToken = default)
    {
        await QueryAsync(HttpMethod.Put, $"/profiles/{Uri.EscapeDataString(name)}", profile, etag, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renames a profile.</summary>
    public async Task RenameProfileAsync(string name, Api.ProfilePost profile, CancellationToken cancellationToken = default)
    {
        await QueryAsync(HttpMethod.Post, $"/profiles/{Uri.EscapeDataString(name)}", profile, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a profile.</summary>
    public async Task DeleteProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        await QueryAsync(HttpMethod.Delete, $"/profiles/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
