using System.Net;
using System.Text.Json;
using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;
using Mossie.Incus.Client.Serialization;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Gets the events for the project defined on the client.</summary>
    public Task<EventListener> GetEventsAsync(CancellationToken cancellationToken = default)
        => GetEventsInternalAsync(allProjects: false, eventTypes: null, cancellationToken);

    /// <summary>Gets events filtered by type for the project defined on the client.</summary>
    public Task<EventListener> GetEventsByTypeAsync(IEnumerable<string> eventTypes, CancellationToken cancellationToken = default)
        => GetEventsInternalAsync(allProjects: false, eventTypes, cancellationToken);

    /// <summary>Gets events for all projects.</summary>
    public Task<EventListener> GetEventsAllProjectsAsync(CancellationToken cancellationToken = default)
        => GetEventsInternalAsync(allProjects: true, eventTypes: null, cancellationToken);

    /// <summary>Gets events filtered by type for all projects.</summary>
    public Task<EventListener> GetEventsAllProjectsByTypeAsync(IEnumerable<string> eventTypes, CancellationToken cancellationToken = default)
        => GetEventsInternalAsync(allProjects: true, eventTypes, cancellationToken);

    private async Task<EventListener> GetEventsInternalAsync(
        bool allProjects,
        IEnumerable<string>? eventTypes,
        CancellationToken cancellationToken)
    {
        SkipEvents = false;

        string project = GetConnectionInfo().Project;
        if (string.IsNullOrEmpty(project))
            throw new IncusException("Unexpected empty project in connection info");

        Dictionary<string, string> query = new(StringComparer.Ordinal);

        if (allProjects)
            query["all-projects"] = "true";
        else if (!string.IsNullOrEmpty(_project))
            query["project"] = _project;

        if (!string.IsNullOrEmpty(_clusterTarget))
            query["target"] = _clusterTarget;

        if (eventTypes is not null)
        {
            List<string> escaped = eventTypes.Select(IncusQuery.Escape).ToList();
            if (escaped.Count > 0)
                query["type"] = string.Join(",", escaped);
        }

        string path = "/events";
        string encoded = IncusQuery.Encode(query);
        if (encoded.Length > 0)
            path += "?" + encoded;

        string url = _baseUri.ToString().TrimEnd('/') + "/1.0" + path;

        IncusWebSocket connection;
        try
        {
            connection = await RawWebSocketAbsoluteAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (IncusStatusException ex) when (ex.StatusCode == (int)HttpStatusCode.Forbidden)
        {
            throw;
        }

        EventListener listener = CreateListenerRegistration(new EventListener(this, connection));
        return listener;
    }

    /// <summary>Sends an event to the server through an active event listener connection.</summary>
    public async Task SendEventAsync(Api.Event evt, CancellationToken cancellationToken = default)
    {
        foreach (EventListener listener in _activeListeners.Values)
        {
            if (!listener.IsActive)
                continue;

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            await listener.SendAsync(evt).WaitAsync(timeout.Token).ConfigureAwait(false);
            return;
        }

        throw new IncusException("No available event listener connection");
    }
}
