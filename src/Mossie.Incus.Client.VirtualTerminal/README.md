# Mossie.Incus.Client.VirtualTerminal

A [VirtualTerminal](https://github.com/Rikitav/VirtualTerminal) session for
[Mossie.Incus.Client](../Mossie.Incus.Client) that opens an **interactive terminal** inside an
Incus instance.

`IncusTerminalSession` derives from `VirtualTerminal.Session.TerminalSession`, runs an interactive
`exec` over the Incus exec PTY, and feeds the instance output into the VirtualTerminal screen
buffer. It plugs directly into the `VirtualTerminal.WPF` / `VirtualTerminal.Avalonia`
`TerminalControl`.

## Installation

```bash
dotnet add package Mossie.Incus.Client.VirtualTerminal
```

This pulls in `Mossie.Incus.Client` and `VirtualTerminal`. The package targets `net10.0` (the
target framework of the VirtualTerminal engine).

## Quick start

```csharp
using Mossie.Incus.Client;
using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.VirtualTerminal;

// Connect to the Incus server (HTTPS, TCP or Unix socket).
await using IncusClient client = await IncusClient.ConnectUnixAsync("/var/lib/incus/unix.socket");

// Create the terminal session. The default command is /bin/sh; pass an InstanceExecPost to change it.
using IncusTerminalSession session = new(client, "my-container");

// Start the interactive exec and wire it to the screen buffer.
await session.ConnectAsync(columns: 120, rows: 30);

// Attach to a VirtualTerminal control (WPF):
Terminal.Session = session;
// or (Avalonia):
// Terminal.CurrentSession = session;
```

For a specific shell or command, pass the command (and optional environment):

```csharp
// Single executable:
using IncusTerminalSession bash = new(client, "my-container", "bash");

// Command + arguments + environment variables:
using IncusTerminalSession login = new(
    client,
    "my-container",
    ["/bin/bash", "-l"],
    new Dictionary<string, string>
    {
        ["TERM"] = "xterm-256color",
        ["LANG"] = "C.UTF-8",
    });
```

Or configure the session after construction and before `ConnectAsync`:

```csharp
using IncusTerminalSession session = new(client, "my-container");
session.Exec.Command = ["/usr/bin/zsh", "-l"];
session.SetEnvironmentVariable("TERM", "xterm-256color");
session.SetEnvironmentVariable("EDITOR", "vim");
await session.ConnectAsync();
```

The default command is `/bin/sh`. Use `session.Exec` to set any other exec option (user, group,
working directory, ...).

## How it works

```
TerminalControl  ──input──▶  IncusTerminalSession  ──▶  Incus exec PTY  ──▶  instance
      ▲                             │                        │
      └────screen buffer◀── decoder ◀── instance output ◀─────┘
```

* `TerminalSession.Write` (keyboard input) is forwarded to the exec PTY.
* Instance output is decoded by the VirtualTerminal `TerminalDecoder` into `Buffer`.
* `Resize` sends a `window-resize` control message so the remote PTY reflows. A resize that
  arrives before the control socket is connected is remembered and applied once it is.
* Disposing the session stops the exec and closes the PTY.

## Notes

* The session does **not** own the `IncusClient`; dispose it separately.
* The terminal requires the instance to be running. For containers the shell must exist in the
  image (use `/bin/sh` as the portable default).
* The `InstanceExecPost.WaitForWS` flag is managed by the client; do not set it.

## License

Apache-2.0. See [LICENSE](../../LICENSE).
