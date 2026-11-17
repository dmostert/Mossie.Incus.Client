using Mossie.Incus.Client.Api;

namespace Mossie.Incus.Client.VirtualTerminal;

/// <summary>
/// A read-only (by default) terminal session that displays an Incus instance console.
/// </summary>
/// <remarks>
/// <para>
/// The session attaches to the instance console device (<c>/1.0/instances/{name}/console</c>) and
/// decodes its output into the <see cref="VirtualTerminal"/> screen buffer. It is useful for
/// watching boot logs, serial console output, or an interactive console.
/// </para>
/// <para>
/// The console is read-only unless <see cref="AllowInput"/> is enabled (containers expose a
/// writable serial console; VM VGA consoles are output-only).
/// </para>
/// </remarks>
public sealed class IncusConsoleSession : IncusPtySession
{
    private readonly InstanceConsolePost _console;

    /// <summary>
    /// Initializes a new console session for an Incus instance.
    /// </summary>
    /// <param name="client">The connected Incus client.</param>
    /// <param name="instanceName">The instance whose console to display.</param>
    /// <param name="console">
    /// The console request. When <c>null</c>, the default text console is used.
    /// <see cref="InstanceConsolePost.Width"/>, <see cref="InstanceConsolePost.Height"/> and
    /// <see cref="InstanceConsolePost.Type"/> are set by <see cref="ConnectAsync"/> when unset.
    /// </param>
    public IncusConsoleSession(IncusClient client, string instanceName, InstanceConsolePost? console = null)
        : base(client, instanceName)
    {
        _console = console ?? new InstanceConsolePost();
    }

    /// <summary>
    /// Whether terminal input is forwarded to the console. Defaults to <c>false</c> (read-only).
    /// </summary>
    public bool AllowInput { get; set; }

    /// <summary>The console request used to attach.</summary>
    public InstanceConsolePost Console => _console;

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> data)
    {
        if (!AllowInput)
            return;

        base.Write(data);
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!AllowInput)
            return ValueTask.CompletedTask;

        return base.WriteAsync(data, cancellationToken);
    }

    /// <summary>
    /// Attaches to the instance console and begins mirroring it into the screen buffer.
    /// </summary>
    /// <param name="columns">Initial console width in columns (used when the console request has no size).</param>
    /// <param name="rows">Initial console height in rows (used when the console request has no size).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ConnectAsync(ushort columns = 80, ushort rows = 24, CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            throw new InvalidOperationException("The session is already connected.");

        Stream terminal = await OpenTerminalAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(_console.Type))
            _console.Type = "console";

        if (_console.Width <= 0)
            _console.Width = columns;

        if (_console.Height <= 0)
            _console.Height = rows;

        Operation = await Client.ConsoleInstanceAsync(
            InstanceName,
            _console,
            new InstanceConsoleArgs
            {
                Terminal = terminal,
                Control = HandleControlAsync,
            },
            cancellationToken).ConfigureAwait(false);

        MarkConnected();
    }
}
