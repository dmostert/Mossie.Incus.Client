using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the names of all projects.</summary>
    public async Task<List<string>> GetProjectNamesAsync(CancellationToken cancellationToken = default)
    {
        if (!HasExtension("projects"))
            throw new IncusException("The server is missing the required \"projects\" API extension");

        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, "/projects", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/projects", urls);
    }

    /// <summary>Returns all projects.</summary>
    public async Task<List<Api.Project>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        if (!HasExtension("projects"))
            throw new IncusException("The server is missing the required \"projects\" API extension");

        (List<Api.Project> projects, _) = await QueryStructAsync<List<Api.Project>>(
            HttpMethod.Get, "/projects?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return projects;
    }

    /// <summary>Returns a filtered list of projects.</summary>
    public async Task<List<Api.Project>> GetProjectsWithFilterAsync(IEnumerable<string> filters, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("projects"))
            throw new IncusException("The server is missing the required \"projects\" API extension");

        string path = $"/projects?recursion=1&filter={IncusQuery.Escape(IncusQuery.ParseFilters(filters))}";

        (List<Api.Project> projects, _) = await QueryStructAsync<List<Api.Project>>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return projects;
    }

    /// <summary>Returns a project entry.</summary>
    public async Task<(Api.Project Project, string ETag)> GetProjectAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("projects"))
            throw new IncusException("The server is missing the required \"projects\" API extension");

        (Api.Project project, string etag) = await QueryStructAsync<Api.Project>(
            HttpMethod.Get, $"/projects/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (project, etag);
    }

    /// <summary>Returns the usage state of a project.</summary>
    public async Task<Api.ProjectState> GetProjectStateAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("project_usage");

        (Api.ProjectState state, _) = await QueryStructAsync<Api.ProjectState>(
            HttpMethod.Get, $"/projects/{Uri.EscapeDataString(name)}/state", cancellationToken: cancellationToken).ConfigureAwait(false);

        return state;
    }

    /// <summary>Returns the access entry of a project.</summary>
    public async Task<Api.Access> GetProjectAccessAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("project_access"))
            throw new IncusException("The server is missing the required \"project_access\" API extension");

        (Api.Access access, _) = await QueryStructAsync<Api.Access>(
            HttpMethod.Get, $"/projects/{Uri.EscapeDataString(name)}/access", cancellationToken: cancellationToken).ConfigureAwait(false);

        return access;
    }

    /// <summary>Creates a new project.</summary>
    public async Task CreateProjectAsync(Api.ProjectsPost project, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("projects"))
            throw new IncusException("The server is missing the required \"projects\" API extension");

        await QueryAsync(HttpMethod.Post, "/projects", project, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates a project.</summary>
    public async Task UpdateProjectAsync(string name, Api.ProjectPut project, string? etag = null, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("projects"))
            throw new IncusException("The server is missing the required \"projects\" API extension");

        await QueryAsync(HttpMethod.Put, $"/projects/{Uri.EscapeDataString(name)}", project, etag, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renames a project.</summary>
    public async Task<Operation> RenameProjectAsync(string name, Api.ProjectPost project, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("projects"))
            throw new IncusException("The server is missing the required \"projects\" API extension");

        (Operation operation, _) = await QueryOperationAsync(
            HttpMethod.Post, $"/projects/{Uri.EscapeDataString(name)}", project, cancellationToken: cancellationToken).ConfigureAwait(false);

        return operation;
    }

    /// <summary>Deletes a project.</summary>
    public async Task DeleteProjectAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!HasExtension("projects"))
            throw new IncusException("The server is missing the required \"projects\" API extension");

        await QueryAsync(HttpMethod.Delete, $"/projects/{Uri.EscapeDataString(name)}", cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a project and everything inside of it.</summary>
    public async Task DeleteProjectForceAsync(string name, CancellationToken cancellationToken = default)
    {
        CheckExtension("projects_force_delete");

        await QueryAsync(HttpMethod.Delete, $"/projects/{Uri.EscapeDataString(name)}?force=1", cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
