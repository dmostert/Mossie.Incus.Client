using Mossie.Incus.Client.Api;

namespace Mossie.Incus.Client;

/// <summary>
/// An operation that may span multiple servers, mirroring Go's <c>incus.RemoteOperation</c>.
/// The actual transfer runs in the background; <see cref="WaitAsync(CancellationToken)"/> waits for it to finish.
/// </summary>
public sealed class RemoteOperation : IAsyncDisposable
{
    private readonly object _lock = new();
    private readonly List<Func<Api.Operation, Task>> _handlers = new();
    private readonly List<EventTarget> _targets = new();
    private readonly Task _completion;

    private Operation? _target;
    private bool _finished;
    private Exception? _error;

    internal RemoteOperation(Task completion, Operation? target = null)
    {
        _completion = completion;
        if (target is not null)
            SetTarget(target);
    }

    internal void SetTarget(Operation target)
    {
        List<Func<Api.Operation, Task>> handlers;
        lock (_lock)
        {
            _target = target;
            handlers = new List<Func<Api.Operation, Task>>(_handlers);
        }

        foreach (Func<Api.Operation, Task> handler in handlers)
            Attach(target, handler);
    }

    internal void Fail(Exception error)
    {
        lock (_lock)
            _error = error;
    }

    /// <summary>The underlying target operation, if one has been created.</summary>
    public Operation? Target
    {
        get
        {
            lock (_lock)
                return _target;
        }
    }

    /// <summary>The error reported by the remote transfer, if any.</summary>
    public Exception? Error
    {
        get
        {
            lock (_lock)
                return _error;
        }
    }

    /// <summary>Adds a handler called whenever the target operation is updated.</summary>
    public Task<EventTarget?> AddHandlerAsync(Func<Api.Operation, Task> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        Operation? target;
        lock (_lock)
        {
            _handlers.Add(handler);
            target = _target;
        }

        if (target is null)
            return Task.FromResult<EventTarget?>(null);

        return AttachAsync(target, handler, cancellationToken);
    }

    /// <summary>Adds a synchronous handler called whenever the target operation is updated.</summary>
    public Task<EventTarget?> AddHandlerAsync(Action<Api.Operation> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AddHandlerAsync(op =>
        {
            handler(op);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    private async Task<EventTarget?> AttachAsync(Operation target, Func<Api.Operation, Task> handler, CancellationToken cancellationToken)
    {
        EventTarget? eventTarget = await target.AddHandlerAsync(handler, cancellationToken).ConfigureAwait(false);
        if (eventTarget is not null)
        {
            lock (_lock)
                _targets.Add(eventTarget);
        }

        return eventTarget;
    }

    private void Attach(Operation target, Func<Api.Operation, Task> handler)
    {
        // Fire-and-forget is acceptable here: the operation's own wait covers completion.
        _ = AttachAsync(target, handler, CancellationToken.None);
    }

    /// <summary>Attempts to cancel the target operation.</summary>
    public Task CancelTargetAsync(CancellationToken cancellationToken = default)
    {
        Operation? target;
        lock (_lock)
            target = _target;

        if (target is null)
            throw new IncusException("No associated target operation");

        return target.CancelAsync(cancellationToken);
    }

    /// <summary>Returns the current target operation.</summary>
    public Task<Api.Operation> GetTargetAsync()
    {
        Operation? target;
        lock (_lock)
            target = _target;

        if (target is null)
            throw new IncusException("No associated target operation");

        return Task.FromResult(target.GetOperation());
    }

    /// <summary>Waits until the whole remote operation reaches a final state.</summary>
    public Task WaitAsync(CancellationToken cancellationToken = default)
        => WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);

    /// <summary>Waits until the whole remote operation reaches a final state, with a timeout.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            await _completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using CancellationTokenSource timeoutSource = new(timeout);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            await _completion.WaitAsync(linked.Token).ConfigureAwait(false);
        }

        Operation? target;
        lock (_lock)
            target = _target;

        if (target is not null)
            await target.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            _finished = true;
            if (_error is not null)
                throw _error is IncusException incus ? incus : new IncusException(_error.Message, _error);
        }
    }

    /// <summary>True once the operation has been waited on.</summary>
    public bool IsFinished
    {
        get
        {
            lock (_lock)
                return _finished;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        List<EventTarget> targets;
        Operation? target;
        lock (_lock)
        {
            targets = new List<EventTarget>(_targets);
            _targets.Clear();
            target = _target;
        }

        if (target is not null)
        {
            foreach (EventTarget eventTarget in targets)
                await target.RemoveHandlerAsync(eventTarget).ConfigureAwait(false);

            await target.DisposeAsync().ConfigureAwait(false);
        }
    }
}
