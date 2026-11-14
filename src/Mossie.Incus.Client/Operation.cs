using System.Text.Json;
using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Serialization;

namespace Mossie.Incus.Client;

/// <summary>
/// A running Incus operation (asynchronous processing). Mirrors Go's <c>incus.Operation</c>.
/// </summary>
public sealed class Operation : IAsyncDisposable
{
    private static readonly string[] ConnectSecretKeys = { "control", "filesystem", "fds" };

    private readonly IncusClient _client;
    private readonly bool _skipListener;
    private readonly List<EventTarget> _handlers = new();
    private readonly object _lock = new();

    private Api.Operation _operation;
    private EventListener? _listener;

    internal Operation(IncusClient client, Api.Operation operation, EventListener? listener, bool skipListener)
    {
        _client = client;
        _operation = operation;
        _listener = listener;
        _skipListener = skipListener;
    }

    /// <summary>Returns the current API operation.</summary>
    public Api.Operation GetOperation()
    {
        lock (_lock)
            return _operation;
    }

    /// <summary>The operation identifier.</summary>
    public string Id => GetOperation().ID;

    /// <summary>Requests cancellation of the operation.</summary>
    public Task CancelAsync(CancellationToken cancellationToken = default)
        => _client.DeleteOperationAsync(Id, cancellationToken);

    /// <summary>Pulls the current version of the operation.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        (Api.Operation operation, _) = await _client.GetOperationAsync(Id, cancellationToken).ConfigureAwait(false);

        lock (_lock)
            ApplyUpdate(operation);
    }

    /// <summary>Adds a handler called whenever this operation is updated.</summary>
    public async Task<EventTarget?> AddHandlerAsync(Func<Api.Operation, Task> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (_skipListener)
            throw new IncusException("Cannot add handler, client operation does not support event listeners");

        EventListener listener = await EnsureListenerAsync(cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            if (IncusStatusCode.IsFinal(_operation.StatusCode))
                return null;
        }

        EventTarget target = listener.AddHandler(new[] { "operation" }, async evt =>
        {
            Api.Operation? updated = ParseEventOperation(evt);
            if (updated is null || updated.ID != Id)
                return;

            lock (_lock)
                ApplyUpdate(updated);

            await handler(updated).ConfigureAwait(false);
        });

        lock (_lock)
            _handlers.Add(target);

        return target;
    }

    /// <summary>Adds a synchronous handler called whenever this operation is updated.</summary>
    public Task<EventTarget?> AddHandlerAsync(Action<Api.Operation> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AddHandlerAsync(op =>
        {
            handler(op);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    /// <summary>Removes a previously added handler.</summary>
    public async Task RemoveHandlerAsync(EventTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_skipListener)
            throw new IncusException("Cannot remove handler, client operation does not support event listeners");

        lock (_lock)
            _handlers.Remove(target);

        EventListener? listener;
        lock (_lock)
            listener = _listener;

        if (listener is not null)
            listener.RemoveHandler(target);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Returns a raw websocket connection for the operation.</summary>
    public Task<IncusWebSocket> GetWebSocketAsync(string secret, CancellationToken cancellationToken = default)
        => _client.GetOperationWebSocketAsync(Id, secret, cancellationToken);

    /// <summary>Waits until the operation reaches a final state.</summary>
    public Task WaitAsync(CancellationToken cancellationToken = default) => WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);

    /// <summary>Waits until the operation reaches a final state, with a timeout.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_skipListener)
        {
            await WaitByPollingAsync(timeout, cancellationToken).ConfigureAwait(false);
            return;
        }

        lock (_lock)
        {
            if (IncusStatusCode.IsFinal(_operation.StatusCode))
            {
                ThrowIfFailed(_operation);
                return;
            }
        }

        EventListener listener = await EnsureListenerAsync(cancellationToken).ConfigureAwait(false);
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        EventTarget target = listener.AddHandler(new[] { "operation" }, evt =>
        {
            Api.Operation? updated = ParseEventOperation(evt);
            if (updated is null || updated.ID != Id)
                return;

            bool done;
            lock (_lock)
            {
                ApplyUpdate(updated);
                done = IncusStatusCode.IsFinal(_operation.StatusCode);
            }

            if (done)
                completion.TrySetResult(true);
        });

        try
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);

            lock (_lock)
            {
                if (IncusStatusCode.IsFinal(_operation.StatusCode))
                    completion.TrySetResult(true);
            }

            using CancellationTokenSource timeoutSource = new();
            if (timeout != Timeout.InfiniteTimeSpan)
                timeoutSource.CancelAfter(timeout);

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            using CancellationTokenRegistration registration = linked.Token.Register(() =>
            {
                completion.TrySetCanceled(linked.Token);
            });

            try
            {
                await completion.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await listener.DisconnectAsync().ConfigureAwait(false);

                try
                {
                    await CancelAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore cancellation failures.
                }

                throw;
            }
        }
        finally
        {
            listener.RemoveHandler(target);
        }

        lock (_lock)
            ThrowIfFailed(_operation);
    }

    private async Task WaitByPollingAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        int seconds = -1;
        if (timeout != Timeout.InfiniteTimeSpan)
            seconds = (int)Math.Max(0, timeout.TotalSeconds);

        (Api.Operation operation, _) = await _client
            .GetOperationWaitAsync(Id, seconds, cancellationToken)
            .ConfigureAwait(false);

        lock (_lock)
            ApplyUpdate(operation);

        ThrowIfFailed(operation);
    }

    private async Task<EventListener> EnsureListenerAsync(CancellationToken cancellationToken)
    {
        if (_skipListener)
            throw new IncusException("Cannot set up event listener, client operation does not support event listeners");

        EventListener? existing;
        lock (_lock)
            existing = _listener;

        if (existing is not null)
            return existing;

        EventListener created = await _client.GetEventsAsync(cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            if (_listener is not null)
            {
                created.Abort();
                return _listener;
            }

            _listener = created;
            return created;
        }
    }

    private void ApplyUpdate(Api.Operation updated)
    {
        foreach (string key in ConnectSecretKeys)
        {
            if (updated.Metadata is not null && updated.Metadata.ContainsKey(key))
                continue;

            if (_operation.Metadata is not null && _operation.Metadata.TryGetValue(key, out object? value))
            {
                updated.Metadata ??= new Dictionary<string, object>();
                updated.Metadata[key] = value;
            }
        }

        _operation = updated;
    }

    internal void ApplyUpdateFromRefresh(Api.Operation updated)
    {
        lock (_lock)
            ApplyUpdate(updated);
    }

    private static Api.Operation? ParseEventOperation(Api.Event evt)
    {
        if (evt.Type != "operation" || evt.Metadata.ValueKind != JsonValueKind.Object)
            return null;

        return evt.Metadata.Deserialize<Api.Operation>(IncusJson.Options);
    }

    private static void ThrowIfFailed(Api.Operation operation)
    {
        if (!string.IsNullOrEmpty(operation.Err))
            throw new IncusException(operation.Err);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        EventListener? listener;
        lock (_lock)
        {
            listener = _listener;
            _listener = null;
        }

        if (listener is not null)
            await listener.DisconnectAsync().ConfigureAwait(false);
    }
}
