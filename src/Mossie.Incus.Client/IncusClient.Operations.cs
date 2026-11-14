using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Returns the list of operation UUIDs.</summary>
    public async Task<List<string>> GetOperationUUIDsAsync(CancellationToken cancellationToken = default)
    {
        (List<string> urls, _) = await QueryStructAsync<List<string>>(HttpMethod.Get, "/operations", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return IncusQuery.UrlsToResourceNames("/operations", urls);
    }

    /// <summary>Returns the list of operations.</summary>
    public async Task<List<Api.Operation>> GetOperationsAsync(CancellationToken cancellationToken = default)
    {
        (Dictionary<string, List<Api.Operation>> grouped, _) = await QueryStructAsync<Dictionary<string, List<Api.Operation>>>(
            HttpMethod.Get, "/operations?recursion=1", cancellationToken: cancellationToken).ConfigureAwait(false);

        return grouped.Values.SelectMany(x => x).ToList();
    }

    /// <summary>Returns the list of operations across all projects.</summary>
    public async Task<List<Api.Operation>> GetOperationsAllProjectsAsync(CancellationToken cancellationToken = default)
    {
        CheckExtension("operations_get_query_all_projects");

        (Dictionary<string, List<Api.Operation>> grouped, _) = await QueryStructAsync<Dictionary<string, List<Api.Operation>>>(
            HttpMethod.Get, "/operations?recursion=1&all-projects=true", cancellationToken: cancellationToken).ConfigureAwait(false);

        return grouped.Values.SelectMany(x => x).ToList();
    }

    /// <summary>Returns an operation entry.</summary>
    public async Task<(Api.Operation Operation, string ETag)> GetOperationAsync(string uuid, CancellationToken cancellationToken = default)
    {
        (Api.Operation operation, string etag) = await QueryStructAsync<Api.Operation>(
            HttpMethod.Get, $"/operations/{Uri.EscapeDataString(uuid)}", cancellationToken: cancellationToken).ConfigureAwait(false);

        return (operation, etag);
    }

    /// <summary>Waits for an operation to complete or the timeout to be reached.</summary>
    public async Task<(Api.Operation Operation, string ETag)> GetOperationWaitAsync(
        string uuid,
        int timeout,
        CancellationToken cancellationToken = default)
    {
        (Api.Operation operation, string etag) = await QueryStructAsync<Api.Operation>(
            HttpMethod.Get,
            $"/operations/{Uri.EscapeDataString(uuid)}/wait?timeout={timeout}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (operation, etag);
    }

    /// <summary>Waits for an operation to complete using its secret.</summary>
    public async Task<(Api.Operation Operation, string ETag)> GetOperationWaitSecretAsync(
        string uuid,
        string secret,
        int timeout,
        CancellationToken cancellationToken = default)
    {
        string path = $"/operations/{Uri.EscapeDataString(uuid)}/wait?secret={IncusQuery.Escape(secret)}&timeout={timeout}";

        (Api.Operation operation, string etag) = await QueryStructAsync<Api.Operation>(
            HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);

        return (operation, etag);
    }

    /// <summary>Returns a websocket connection for an operation.</summary>
    public Task<IncusWebSocket> GetOperationWebSocketAsync(string uuid, string secret, CancellationToken cancellationToken = default)
    {
        string path = $"/operations/{Uri.EscapeDataString(uuid)}/websocket";
        if (!string.IsNullOrEmpty(secret))
            path += $"?secret={IncusQuery.Escape(secret)}";

        string url = _baseUri.ToString().TrimEnd('/') + "/1.0" + path;
        return RawWebSocketAbsoluteAsync(url, cancellationToken);
    }

    /// <summary>Cancels a running operation.</summary>
    public async Task DeleteOperationAsync(string uuid, CancellationToken cancellationToken = default)
    {
        await QueryAsync(HttpMethod.Delete, $"/operations/{Uri.EscapeDataString(uuid)}", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
