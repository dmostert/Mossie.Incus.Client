namespace Mossie.Incus.Client.VirtualTerminal;

/// <summary>
/// A <see cref="Stream"/> that reads from one stream and writes to another, used to present the
/// Incus PTY as a single bidirectional stream.
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
