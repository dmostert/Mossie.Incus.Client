using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Text.Json;
using Mossie.Incus.Client.Api;
using VirtualTerminal.Session;
using VirtualTerminal.Transport;

namespace Mossie.Incus.Client.VirtualTerminal;

/// <summary>
/// An interactive terminal session backed by an Incus instance exec PTY.
/// </summary>
/// <remarks>
/// <para>
/// The session starts an interactive <c>exec</c> in the instance and bridges it to the
/// <see cref="VirtualTerminal"/> screen buffer. Instance output is decoded into
/// <see cref="TerminalSession.Buffer"/>; terminal input is forwarded to the instance; and
/// <see cref="Resize"/> sends a <c>window-resize</c> control message so the remote PTY reflows.
/// </para>
/// <para>
/// Assign the session to a <c>VirtualTerminal.WPF</c> or <c>VirtualTerminal.Avalonia</c>
/// <c>TerminalControl</c> and call <see cref="ConnectAsync"/> once the control is attached.
/// </para>
/// </remarks>
public sealed class IncusTerminalSession : TerminalSession
{
    private readonly IncusClient _client;
    private readonly string _instanceName;
    private readonly InstanceExecPost _exec;

    // Session -> instance (user input) and instance -> session (terminal output).
    private readonly Pipe _toInstance = new();
    private readonly Pipe _fromInstance = new();

    private CancellationTokenSource? _cts;
    private DuplexStream? _terminal;
    private IncusWebSocket? _control;
    private bool _connected;

    private readonly object _resizeLock = new();
    private ushort _pendingColumns;
    private ushort _pendingRows;
    private bool _hasPendingResize;

    /// <summary>
    /// Initializes a new terminal session for an Incus instance.
    /// </summary>
    /// <param name="client">The connected Incus client.</param>
    /// <param name="instanceName">The instance to open a terminal in.</param>
    /// <param name="exec">
    /// The exec request to run interactively. When <c>null</c>, <c>/bin/sh</c> is started.
    /// <see cref="InstanceExecPost.Interactive"/>, <see cref="InstanceExecPost.Width"/> and
    /// <see cref="InstanceExecPost.Height"/> are set by <see cref="ConnectAsync"/>.
    /// </param>
    public IncusTerminalSession(IncusClient client, string instanceName, InstanceExecPost? exec = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _instanceName = string.IsNullOrEmpty(instanceName)
            ? throw new ArgumentException("Instance name is required.", nameof(instanceName))
            : instanceName;

        _exec = exec ?? new InstanceExecPost { Command = ["/bin/sh"] };
    }

    /// <summary>The running exec operation, once connected.</summary>
    public Operation? Operation { get; private set; }

    /// <summary>Whether <see cref="ConnectAsync"/> has completed.</summary>
    public bool IsConnected => _connected;

    /// <summary>
    /// Starts the interactive exec and begins mirroring the instance PTY into the screen buffer.
    /// </summary>
    /// <param name="columns">Initial terminal width in columns.</param>
    /// <param name="rows">Initial terminal height in rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ConnectAsync(ushort columns = 80, ushort rows = 24, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_connected)
            throw new InvalidOperationException("The session is already connected.");

        _cts = new CancellationTokenSource();

        // The base class pumps instance output (transport.Input) into the decoder.
        StreamTerminalTransport transport = new(_fromInstance.Reader, _toInstance.Writer);
        await AttachTransportAsync(transport, cancellationToken).ConfigureAwait(false);

        // The client reads user input and writes instance output over this duplex stream.
        _terminal = new DuplexStream(_toInstance.Reader.AsStream(), _fromInstance.Writer.AsStream());

        _exec.Interactive = true;
        _exec.Width = columns;
        _exec.Height = rows;

        Operation = await _client.ExecInstanceAsync(
            _instanceName,
            _exec,
            new InstanceExecArgs
            {
                Terminal = _terminal,
                Control = HandleControlAsync,
            },
            cancellationToken).ConfigureAwait(false);

        _connected = true;
    }

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

    private async Task HandleControlAsync(IncusWebSocket control)
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

        // Stop both ends of the bridges so the exec mirror unwinds.
        _toInstance.Writer.Complete();
        _fromInstance.Writer.Complete();
        _terminal?.Dispose();

        _cts?.Dispose();
        _cts = null;
    }
}

/// <summary>
/// A <see cref="Stream"/> that reads from one stream and writes to another, used to present the
/// Incus exec PTY as a single bidirectional stream.
/// </summary>
internal sealed class DuplexStream : Stream
{
    private readonly Stream _read;
    private readonly Stream _write;

    internal DuplexStream(Stream read, Stream write)
    {
        _read = read;
        _write = write;
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _write.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _write.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) => _read.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _read.Read(buffer);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _read.ReadAsync(buffer, cancellationToken);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _read.ReadAsync(buffer, offset, count, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => _write.Write(buffer, offset, count);
    public override void Write(ReadOnlySpan<byte> buffer) => _write.Write(buffer);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => _write.WriteAsync(buffer, cancellationToken);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _write.WriteAsync(buffer, offset, count, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _read.Dispose();
            _write.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _read.DisposeAsync().ConfigureAwait(false);
        await _write.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
