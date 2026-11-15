using System.Text;
using Mossie.Incus.Client.Api;

namespace Mossie.Incus.Client.Tests;

/// <summary>Regression tests for ExecInstanceAsync (RecordOutput flow).</summary>
public sealed class ExecInstanceTests
{
    [Fact]
    public async Task ExecInstanceAsync_RecordOutput_CompletesWhenEventDelivered()
    {
        await using FakeIncusServer server = FakeIncusServer.Start();
        await using IncusClient client = await IncusClient.ConnectUnixAsync(server.SocketPath);

        using MemoryStream stdin = new();
        using MemoryStream stdout = new();
        using MemoryStream stderr = new();

        Operation op = await client.ExecInstanceAsync(
            "caddy",
            new Api.InstanceExecPost
            {
                Command = ["caddy", "reload"],
                Interactive = false,
                RecordOutput = true,
            },
            new InstanceExecArgs { Stdin = stdin, Stdout = stdout, Stderr = stderr });

        Assert.NotNull(op);
        Assert.Equal("caddy output", Encoding.UTF8.GetString(stdout.ToArray()));
        Assert.Equal("caddy output", Encoding.UTF8.GetString(stderr.ToArray()));
    }

    [Fact]
    public async Task ExecInstanceAsync_RecordOutput_PollingFallbackCompletesWhenEventIsMissed()
    {
        // No event is delivered, and the operation only reports Success after the first poll.
        // The polling fallback in Operation.WaitAsync must detect completion.
        await using FakeIncusServer server = FakeIncusServer.Start();
        server.SuppressOperationEvent = true;
        server.RunningRepliesBeforeSuccess = 1;

        await using IncusClient client = await IncusClient.ConnectUnixAsync(server.SocketPath);

        using MemoryStream stdin = new();
        using MemoryStream stdout = new();
        using MemoryStream stderr = new();

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

        await client.ExecInstanceAsync(
            "caddy",
            new Api.InstanceExecPost
            {
                Command = ["caddy", "reload"],
                Interactive = false,
                RecordOutput = true,
            },
            new InstanceExecArgs { Stdin = stdin, Stdout = stdout, Stderr = stderr },
            timeout.Token);
    }

    [Fact]
    public async Task ExecInstanceAsync_RecordOutput_EventAloneCompletes()
    {
        // Event is delivered, but GET /operations keeps reporting Running: only the event can
        // complete the wait. This isolates the event-listener path.
        await using FakeIncusServer server = FakeIncusServer.Start();
        server.SuppressOperationEvent = false;
        server.OperationStaysRunning = true;

        await using IncusClient client = await IncusClient.ConnectUnixAsync(server.SocketPath);

        using MemoryStream stdin = new();
        using MemoryStream stdout = new();
        using MemoryStream stderr = new();

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

        await client.ExecInstanceAsync(
            "caddy",
            new Api.InstanceExecPost
            {
                Command = ["caddy", "reload"],
                Interactive = false,
                RecordOutput = true,
            },
            new InstanceExecArgs { Stdin = stdin, Stdout = stdout, Stderr = stderr },
            timeout.Token);
    }

    [Fact]
    public async Task ExecInstanceAsync_RecordOutput_RefreshAloneCompletes()
    {
        // No event, but GET /operations returns Success: only the refresh can complete the wait.
        await using FakeIncusServer server = FakeIncusServer.Start();
        server.SuppressOperationEvent = true;
        server.OperationStaysRunning = false;

        await using IncusClient client = await IncusClient.ConnectUnixAsync(server.SocketPath);

        using MemoryStream stdin = new();
        using MemoryStream stdout = new();
        using MemoryStream stderr = new();

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

        await client.ExecInstanceAsync(
            "caddy",
            new Api.InstanceExecPost
            {
                Command = ["caddy", "reload"],
                Interactive = false,
                RecordOutput = true,
            },
            new InstanceExecArgs { Stdin = stdin, Stdout = stdout, Stderr = stderr },
            timeout.Token);
    }
}
