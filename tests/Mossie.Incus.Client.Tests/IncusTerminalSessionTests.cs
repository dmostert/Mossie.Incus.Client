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
}
