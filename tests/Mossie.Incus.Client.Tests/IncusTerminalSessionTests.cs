using System.Text;
using Mossie.Incus.Client.VirtualTerminal;

namespace Mossie.Incus.Client.Tests;

/// <summary>End-to-end tests for the VirtualTerminal-backed Incus terminal session.</summary>
public sealed class IncusTerminalSessionTests
{
    [Fact]
    public async Task ConnectAsync_MirrorsPtyOutputIntoBuffer()
    {
        await using FakeIncusServer server = FakeIncusServer.Start();
        server.SuppressOperationEvent = true;

        await using IncusClient client = await IncusClient.ConnectUnixAsync(server.SocketPath);

        using IncusTerminalSession session = new(client, "caddy");

        TaskCompletionSource<bool> sawHello = new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.BufferUpdated += (_, _) =>
        {
            lock (session.Buffer.SyncRoot)
            {
                if (session.Buffer.GetVisibleText().Contains("hello", StringComparison.Ordinal))
                    sawHello.TrySetResult(true);
            }
        };

        await session.ConnectAsync(columns: 80, rows: 24);

        session.Write(Encoding.UTF8.GetBytes("hello\n"));

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await sawHello.Task.WaitAsync(timeout.Token);

        lock (session.Buffer.SyncRoot)
        {
            Assert.Contains("hello", session.Buffer.GetVisibleText());
        }

        Assert.True(session.IsConnected);
        Assert.NotNull(session.Operation);
    }

    [Fact]
    public async Task Constructor_CustomCommandAndEnvironment_AreSent()
    {
        await using FakeIncusServer server = FakeIncusServer.Start();
        server.SuppressOperationEvent = true;

        await using IncusClient client = await IncusClient.ConnectUnixAsync(server.SocketPath);

        using IncusTerminalSession session = new(
            client,
            "caddy",
            ["/bin/bash", "-l"],
            new Dictionary<string, string> { ["TERM"] = "xterm-256color" });

        await session.ConnectAsync();

        Assert.NotNull(server.LastExecBody);

        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(server.LastExecBody);
        System.Text.Json.JsonElement root = document.RootElement;

        Assert.Equal("/bin/bash", root.GetProperty("command")[0].GetString());
        Assert.Equal("-l", root.GetProperty("command")[1].GetString());
        Assert.Equal("xterm-256color", root.GetProperty("environment").GetProperty("TERM").GetString());
        Assert.True(root.GetProperty("interactive").GetBoolean());
        Assert.True(root.GetProperty("wait-for-websocket").GetBoolean());
    }

    [Fact]
    public async Task SetEnvironmentVariable_AfterConstruction_IsSent()
    {
        await using FakeIncusServer server = FakeIncusServer.Start();
        server.SuppressOperationEvent = true;

        await using IncusClient client = await IncusClient.ConnectUnixAsync(server.SocketPath);

        using IncusTerminalSession session = new(client, "caddy", "bash");
        session.SetEnvironmentVariable("LANG", "C.UTF-8");
        session.Exec.Command = ["bash", "-i"];

        await session.ConnectAsync();

        Assert.NotNull(server.LastExecBody);

        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(server.LastExecBody);
        System.Text.Json.JsonElement root = document.RootElement;

        Assert.Equal("bash", root.GetProperty("command")[0].GetString());
        Assert.Equal("-i", root.GetProperty("command")[1].GetString());
        Assert.Equal("C.UTF-8", root.GetProperty("environment").GetProperty("LANG").GetString());
    }
}
