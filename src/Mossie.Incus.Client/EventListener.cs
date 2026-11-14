using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;
using Mossie.Incus.Client.Serialization;

namespace Mossie.Incus.Client;

/// <summary>Identifies a handler registered on an <see cref="EventListener"/>.</summary>
public sealed class EventTarget
{
    internal EventTarget(IEnumerable<string>? types, Func<Api.Event, Task> handler)
    {
        Types = types is null ? null : new HashSet<string>(types, StringComparer.Ordinal);
        Handler = handler;
    }

    internal HashSet<string>? Types { get; }

    internal Func<Api.Event, Task> Handler { get; }

    internal bool Matches(string type) => Types is null || Types.Contains(type);
}

/// <summary>A buffered channel of matching events.</summary>
public sealed class EventChannel
{
    internal EventChannel(IEnumerable<string>? types, int capacity)
    {
        Types = types is null ? null : new HashSet<string>(types, StringComparer.Ordinal);
        Channel = System.Threading.Channels.Channel.CreateBounded<Api.Event>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = true,
            });
    }

    internal HashSet<string>? Types { get; }

    internal Channel<Api.Event> Channel { get; }

    /// <summary>Reader for the events delivered to this channel.</summary>
    public ChannelReader<Api.Event> Reader => Channel.Reader;

    internal bool Matches(string type) => Types is null || Types.Contains(type);
}

/// <summary>
/// A live Incus event stream. Mirrors Go's <c>incus.EventListener</c>.
/// </summary>
public sealed class EventListener : IAsyncDisposable
{
    private const int DefaultChannelSize = 1000;

    private readonly IncusClient _client;
    private readonly IncusWebSocket _connection;
    private readonly CancellationTokenSource _cts;
    private readonly Task _readerTask;
    private readonly List<EventTarget> _targets = new();
    private readonly List<EventChannel> _channels = new();
    private readonly object _lock = new();

    private volatile string? _error;
    private int _disconnectRequested;
    private int _completed;

    internal EventListener(IncusClient client, IncusWebSocket connection)
    {
        _client = client;
        _connection = connection;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(client.ConnectedToken);
        _readerTask = Task.Run(ReadLoopAsync);
    }

    /// <summary>True while the connection is active.</summary>
    public bool IsActive => Volatile.Read(ref _completed) == 0;

    /// <summary>The error that terminated the listener, if any.</summary>
    public string? Error => _error;

    /// <summary>Adds an event handler, optionally filtered by event type.</summary>
    public EventTarget AddHandler(IEnumerable<string>? types, Func<Api.Event, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        EventTarget target = new(types, handler);

        lock (_lock)
        {
            if (!IsActive)
                return target;

            _targets.Add(target);
        }

        return target;
    }

    /// <summary>Adds an event handler, optionally filtered by event type.</summary>
    public EventTarget AddHandler(IEnumerable<string>? types, Action<Api.Event> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return AddHandler(types, evt =>
        {
            handler(evt);
            return Task.CompletedTask;
        });
    }

    /// <summary>Removes a previously registered handler.</summary>
    public bool RemoveHandler(EventTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (_lock)
        {
            return _targets.Remove(target);
        }
    }

    /// <summary>Adds a bounded channel receiving matching events.</summary>
    public EventChannel AddChannel(IEnumerable<string>? types, int size = 0)
    {
        EventChannel channel = new(types, size <= 0 ? DefaultChannelSize : size);

        lock (_lock)
        {
            if (!IsActive)
            {
                channel.Channel.Writer.TryComplete();
                return channel;
            }

            _channels.Add(channel);
        }

        return channel;
    }

    /// <summary>Removes and completes a channel.</summary>
    public bool RemoveChannel(EventChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        lock (_lock)
        {
            if (!_channels.Remove(channel))
                return false;

            channel.Channel.Writer.TryComplete();
            return true;
        }
    }

    /// <summary>Waits until the server disconnects or the listener is disposed.</summary>
    public async Task WaitAsync()
    {
        try
        {
            await _readerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Deliberate disconnect.
        }

        if (_error is not null)
            throw new IncusException(_error);
    }

    /// <summary>Stops listening for events.</summary>
    public async Task DisconnectAsync()
    {
        Interlocked.Exchange(ref _disconnectRequested, 1);
        await StopAsync().ConfigureAwait(false);
    }

    internal void Abort() => _ = StopAsync();

    private async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 1)
            return;

        try
        {
            _cts.Cancel();
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best effort.
        }

        CompleteChannels();
        _client.RemoveListenerRegistration(this);
    }

    private void CompleteChannels()
    {
        lock (_lock)
        {
            foreach (EventChannel channel in _channels)
                channel.Channel.Writer.TryComplete();

            _channels.Clear();
            _targets.Clear();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                (System.Net.WebSockets.WebSocketMessageType type, byte[] data) =
                    await _connection.ReceiveAsync(_cts.Token).ConfigureAwait(false);

                if (type == System.Net.WebSockets.WebSocketMessageType.Close)
                    break;

                if (type != System.Net.WebSockets.WebSocketMessageType.Text)
                    continue;

                Api.Event? evt;
                try
                {
                    evt = JsonSerializer.Deserialize<Api.Event>(data, IncusJson.Options);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (evt is null || string.IsNullOrEmpty(evt.Type))
                    continue;

                Dispatch(evt);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on disconnect.
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref _disconnectRequested) == 0)
                _error = ex.Message;
        }
        finally
        {
            await StopAsync().ConfigureAwait(false);
        }
    }

    private void Dispatch(Api.Event evt)
    {
        List<EventTarget> targets;
        List<EventChannel> channels;

        lock (_lock)
        {
            targets = new List<EventTarget>(_targets);
            channels = new List<EventChannel>(_channels);
        }

        foreach (EventTarget target in targets)
        {
            if (!target.Matches(evt.Type))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await target.Handler(evt).ConfigureAwait(false);
                }
                catch
                {
                    // Event handlers must not tear down the listener.
                }
            });
        }

        foreach (EventChannel channel in channels)
        {
            if (!channel.Matches(evt.Type))
                continue;

            if (!channel.Channel.Writer.TryWrite(evt))
            {
                _error = "Event channel is too far behind";
                _ = StopAsync();
                return;
            }
        }
    }

    internal async Task SendAsync(Api.Event evt)
    {
        string json = JsonSerializer.Serialize(evt, IncusJson.Options);
        await _connection.SendTextAsync(json, _cts.Token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
