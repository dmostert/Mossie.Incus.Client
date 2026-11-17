using System.Text;
using System.Threading.Channels;
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

    [Fact]
    public async Task ExecInstanceAsync_Interactive_UsesTerminalStream()
    {
        await using FakeIncusServer server = FakeIncusServer.Start();
        server.SuppressOperationEvent = true;

        await using IncusClient client = await IncusClient.ConnectUnixAsync(server.SocketPath);

        using DuplexStream terminal = new();
        terminal.SendInput("hello\n");

        await client.ExecInstanceAsync(
            "caddy",
            new Api.InstanceExecPost
            {
                Command = ["sh"],
                Interactive = true,
            },
            new InstanceExecArgs { Terminal = terminal });

        // The fake server echoes terminal input back over the PTY websocket; the client must
        // mirror it into the same Terminal stream.
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        byte[] echo = await terminal.ReadOutputAsync(timeout.Token);
        Assert.Equal("hello\n", Encoding.UTF8.GetString(echo));
    }

    /// <summary>A bidirectional in-memory stream used to exercise interactive exec.</summary>
    private sealed class DuplexStream : Stream
    {
        private readonly Channel<byte[]> _input = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<byte[]> _output = Channel.CreateUnbounded<byte[]>();
        private byte[] _current = Array.Empty<byte>();
        private int _offset;

        public void SendInput(string text) => _input.Writer.TryWrite(Encoding.UTF8.GetBytes(text));

        public async Task<byte[]> ReadOutputAsync(CancellationToken cancellationToken)
            => await _output.Reader.ReadAsync(cancellationToken);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Write(byte[] buffer, int offset, int count)
            => _output.Writer.TryWrite(buffer.AsSpan(offset, count).ToArray());

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_offset >= _current.Length)
            {
                _current = await _input.Reader.ReadAsync(cancellationToken);
                _offset = 0;
            }

            int count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _output.Writer.TryWrite(buffer.ToArray());
            return ValueTask.CompletedTask;
        }
    }
}
