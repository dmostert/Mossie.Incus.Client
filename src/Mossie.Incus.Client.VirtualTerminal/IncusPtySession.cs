using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Text.Json;
using Mossie.Incus.Client.Api;
using VirtualTerminal.Session;
using VirtualTerminal.Transport;

namespace Mossie.Incus.Client.VirtualTerminal;

/// <summary>
/// Base class for Incus PTY-backed terminal sessions (interactive exec or instance console).
/// </summary>
/// <remarks>
/// It bridges the Incus exec/console websocket to the VirtualTerminal screen buffer using a pair of
/// in-memory pipes, keeps the control channel alive, and forwards window resizes.
/// </remarks>
public abstract class IncusPtySession : TerminalSession
{
    // Session -> instance (user input) and instance -> session (terminal output).
    private readonly Pipe _toInstance = new();
    private readonly Pipe _fromInstance = new();

    private readonly object _resizeLock = new();
    private DuplexStream? _terminal;
    private IncusWebSocket? _control;
    private CancellationTokenSource? _cts;
    private ushort _pendingColumns;
    private ushort _pendingRows;
    private bool _hasPendingResize;
    private bool _connected;

    /// <summary>Initializes a new PTY session for an Incus instance.</summary>
    /// <param name="client">The connected Incus client.</param>
    /// <param name="instanceName">The instance the session targets.</param>
    protected IncusPtySession(IncusClient client, string instanceName)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        InstanceName = string.IsNullOrEmpty(instanceName)
            ? throw new ArgumentException("Instance name is required.", nameof(instanceName))
            : instanceName;
    }

    /// <summary>The Incus client used by this session.</summary>
    protected IncusClient Client { get; }

    /// <summary>The target instance name.</summary>
    protected string InstanceName { get; }

    /// <summary>The running operation, once connected.</summary>
    public Operation? Operation { get; protected set; }

    /// <summary>Whether the session is connected.</summary>
    public bool IsConnected => _connected;

    /// <summary>
    /// Creates the pipe bridge, attaches the transport, and returns the bidirectional terminal
    /// stream to pass to the client's exec/console request.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected async Task<Stream> OpenTerminalAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        _cts = new CancellationTokenSource();

        // The base class pumps instance output (transport.Input) into the decoder.
        StreamTerminalTransport transport = new(_fromInstance.Reader, _toInstance.Writer);
        await AttachTransportAsync(transport, cancellationToken).ConfigureAwait(false);

        // The client reads user input and writes instance output over this duplex stream.
        _terminal = new DuplexStream(_toInstance.Reader.AsStream(), _fromInstance.Writer.AsStream());
        return _terminal;
    }

    /// <summary>Marks the session as connected once the operation has started.</summary>
    protected void MarkConnected() => _connected = true;

    /// <summary>Control-channel handler passed to the client.</summary>
    protected Task HandleControlAsync(IncusWebSocket control) => RunControlAsync(control);

    /// <inheritdoc />
    public override void Resize(ushort columns, ushort rows, bool pushScrollback = true)
    {
        base.Resize(columns, rows, pushScrollback);

        // Remember the latest size so a resize that races the control connection is not lost.
        lock (_resizeLock)
        {
            _pendingColumns = columns;
            _pendingRows = rows;
            _hasPendingResize = true;
        }

        _ = SendResizeAsync(columns, rows);
    }

    private async Task SendResizeAsync(ushort columns, ushort rows)
    {
        IncusWebSocket? control = _control;
        CancellationToken token = _cts?.Token ?? CancellationToken.None;
        if (control is null || token.IsCancellationRequested)
            return;

        try
        {
            await SendResizeCoreAsync(control, columns, rows, token).ConfigureAwait(false);
        }
        catch
        {
            // The control channel may have closed; the next resize will retry.
        }
    }

    private static Task SendResizeCoreAsync(IncusWebSocket control, ushort columns, ushort rows, CancellationToken token)
    {
        InstanceExecControl message = new()
        {
            Command = "window-resize",
            Args = new Dictionary<string, string>
            {
                ["width"] = columns.ToString(),
                ["height"] = rows.ToString(),
            },
        };

        return control.SendTextAsync(JsonSerializer.Serialize(message), token);
    }

    private async Task RunControlAsync(IncusWebSocket control)
    {
        _control = control;
        CancellationToken token = _cts?.Token ?? CancellationToken.None;

        // Apply any resize that arrived before the control channel was connected.
        bool hasPending;
        ushort columns;
        ushort rows;
        lock (_resizeLock)
        {
            hasPending = _hasPendingResize;
            columns = _pendingColumns;
            rows = _pendingRows;
            _hasPendingResize = false;
        }

        if (hasPending)
        {
            try
            {
                await SendResizeCoreAsync(control, columns, rows, token).ConfigureAwait(false);
            }
            catch
            {
                // Best effort.
            }
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                (WebSocketMessageType type, byte[] _) = await control.ReceiveAsync(token).ConfigureAwait(false);
                if (type == WebSocketMessageType.Close)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Session disposed.
        }
        catch
        {
            // The control channel is best effort.
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        _connected = false;

        try
        {
            _cts?.Cancel();
        }
        catch
        {
        }

        // Stop both ends of the bridges so the client mirror unwinds.
        _toInstance.Writer.Complete();
        _fromInstance.Writer.Complete();
        _terminal?.Dispose();

        _cts?.Dispose();
        _cts = null;
    }
}
