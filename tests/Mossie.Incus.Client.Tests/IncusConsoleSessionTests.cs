using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.VirtualTerminal;

namespace Mossie.Incus.Client.Tests;

/// <summary>End-to-end tests for the VirtualTerminal-backed Incus console session.</summary>
public sealed class IncusConsoleSessionTests
{
    [Fact]
    public async Task ConnectAsync_MirrorsConsoleOutputIntoBuffer()
    {
        await using FakeIncusServer server = FakeIncusServer.Start();
        server.SuppressOperationEvent = true;

        await using IncusClient client = await IncusClient.ConnectUnixAsync(server.SocketPath);

        using IncusConsoleSession session = new(client, "caddy");

        TaskCompletionSource<bool> sawOutput = new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.BufferUpdated += (_, _) =>
        {
            lock (session.Buffer.SyncRoot)
            {
                if (session.Buffer.GetVisibleText().Contains("console ready", StringComparison.Ordinal))
                    sawOutput.TrySetResult(true);
            }
        };

        await session.ConnectAsync();

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await sawOutput.Task.WaitAsync(timeout.Token);

        lock (session.Buffer.SyncRoot)
        {
            Assert.Contains("console ready", session.Buffer.GetVisibleText());
        }

        Assert.True(session.IsConnected);
        Assert.False(session.AllowInput);
        Assert.NotNull(session.Operation);
    }

    [Fact]
    public async Task ConnectAsync_SendsConsoleRequestWithSize()
    {
        await using FakeIncusServer server = FakeIncusServer.Start();
        server.SuppressOperationEvent = true;

        await using IncusClient client = await IncusClient.ConnectUnixAsync(server.SocketPath);

        using IncusConsoleSession session = new(client, "caddy", new InstanceConsolePost
        {
            Type = "console",
        });

        await session.ConnectAsync(columns: 100, rows: 40);

        Assert.NotNull(server.LastConsoleBody);

        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(server.LastConsoleBody);
        System.Text.Json.JsonElement root = document.RootElement;

        Assert.Equal("console", root.GetProperty("type").GetString());
        Assert.Equal(100, root.GetProperty("width").GetInt32());
        Assert.Equal(40, root.GetProperty("height").GetInt32());
    }
}
