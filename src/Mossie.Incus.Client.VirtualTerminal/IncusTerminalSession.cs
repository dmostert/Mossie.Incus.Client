using Mossie.Incus.Client.Api;
using VirtualTerminal.Session;

namespace Mossie.Incus.Client.VirtualTerminal;

/// <summary>
/// An interactive terminal session backed by an Incus instance exec PTY.
/// </summary>
/// <remarks>
/// <para>
/// The session starts an interactive <c>exec</c> in the instance and bridges it to the
/// <see cref="VirtualTerminal"/> screen buffer. Instance output is decoded into
/// <see cref="TerminalSession.Buffer"/>; terminal input is forwarded to the instance; and
/// <see cref="TerminalSession.Resize"/> sends a <c>window-resize</c> control message so the remote
/// PTY reflows.
/// </para>
/// <para>
/// Assign the session to a <c>VirtualTerminal.WPF</c> or <c>VirtualTerminal.Avalonia</c>
/// <c>TerminalControl</c> and call <see cref="ConnectAsync"/> once the control is attached.
/// </para>
/// </remarks>
public sealed class IncusTerminalSession : IncusPtySession
{
    private readonly InstanceExecPost _exec;

    /// <summary>
    /// Initializes a new terminal session for an Incus instance using a fully specified exec request.
    /// </summary>
    /// <param name="client">The connected Incus client.</param>
    /// <param name="instanceName">The instance to open a terminal in.</param>
    /// <param name="exec">
    /// The exec request to run interactively. When <c>null</c>, <c>/bin/sh</c> is started.
    /// <see cref="InstanceExecPost.Interactive"/>, <see cref="InstanceExecPost.Width"/> and
    /// <see cref="InstanceExecPost.Height"/> are set by <see cref="ConnectAsync"/>.
    /// </param>
    public IncusTerminalSession(IncusClient client, string instanceName, InstanceExecPost? exec = null)
        : base(client, instanceName)
    {
        _exec = exec ?? new InstanceExecPost { Command = ["/bin/sh"] };
    }

    /// <summary>
    /// Initializes a new terminal session that runs the supplied command (for example
    /// <c>["bash", "-l"]</c>) with optional environment variables.
    /// </summary>
    /// <param name="client">The connected Incus client.</param>
    /// <param name="instanceName">The instance to open a terminal in.</param>
    /// <param name="command">Command and arguments to run.</param>
    /// <param name="environment">Environment variables to set for the command.</param>
    public IncusTerminalSession(
        IncusClient client,
        string instanceName,
        IEnumerable<string> command,
        IDictionary<string, string>? environment = null)
        : this(client, instanceName, CreateExec(command, environment))
    {
    }

    /// <summary>
    /// Initializes a new terminal session that runs a single executable (for example
    /// <c>"bash"</c>) with optional environment variables.
    /// </summary>
    /// <param name="client">The connected Incus client.</param>
    /// <param name="instanceName">The instance to open a terminal in.</param>
    /// <param name="command">Executable to run.</param>
    /// <param name="environment">Environment variables to set for the command.</param>
    public IncusTerminalSession(
        IncusClient client,
        string instanceName,
        string command,
        IDictionary<string, string>? environment = null)
        : this(client, instanceName, CreateExec(new[] { command }, environment))
    {
    }

    private static InstanceExecPost CreateExec(IEnumerable<string> command, IDictionary<string, string>? environment)
    {
        List<string> args = command?.ToList() ?? throw new ArgumentNullException(nameof(command));
        if (args.Count == 0)
            throw new ArgumentException("A command is required.", nameof(command));

        return new InstanceExecPost
        {
            Command = args,
            Environment = environment is null || environment.Count == 0
                ? null
                : new Dictionary<string, string>(environment),
        };
    }

    /// <summary>
    /// The exec request used to start the terminal. Mutate it before <see cref="ConnectAsync"/>
    /// to set the command, environment, user, working directory, and so on.
    /// </summary>
    public InstanceExecPost Exec => _exec;

    /// <summary>Sets an environment variable for the command (before <see cref="ConnectAsync"/>).</summary>
    /// <param name="name">Variable name.</param>
    /// <param name="value">Variable value.</param>
    public void SetEnvironmentVariable(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _exec.Environment ??= new Dictionary<string, string>();
        _exec.Environment[name] = value;
    }

    /// <summary>
    /// Starts the interactive exec and begins mirroring the instance PTY into the screen buffer.
    /// </summary>
    /// <param name="columns">Initial terminal width in columns.</param>
    /// <param name="rows">Initial terminal height in rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ConnectAsync(ushort columns = 80, ushort rows = 24, CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            throw new InvalidOperationException("The session is already connected.");

        Stream terminal = await OpenTerminalAsync(cancellationToken).ConfigureAwait(false);

        _exec.Interactive = true;
        _exec.Width = columns;
        _exec.Height = rows;

        Operation = await Client.ExecInstanceAsync(
            InstanceName,
            _exec,
            new InstanceExecArgs
            {
                Terminal = terminal,
                Control = HandleControlAsync,
            },
            cancellationToken).ConfigureAwait(false);

        MarkConnected();
    }
}
