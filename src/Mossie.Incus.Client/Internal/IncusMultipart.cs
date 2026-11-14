using System.Text;

namespace Mossie.Incus.Client.Internal;

/// <summary>
/// Minimal streaming MIME multipart reader used for image downloads.
/// </summary>
internal sealed class IncusMultipartReader
{
    private readonly Stream _inner;
    private readonly byte[] _delimiter;
    private readonly byte[] _buffer = new byte[64 * 1024];

    private int _position;
    private int _length;
    private bool _finished;

    internal IncusMultipartReader(string boundary, Stream inner)
    {
        _inner = inner;
        _delimiter = Encoding.ASCII.GetBytes("\r\n--" + boundary);
    }

    /// <summary>Consumes the initial boundary and returns false when the multipart body is empty.</summary>
    internal async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        string? line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
            return false;

        if (line.EndsWith("--", StringComparison.Ordinal))
            return false;

        return true;
    }

    /// <summary>Reads a section's headers. Returns null at the end of the multipart body.</summary>
    internal async Task<Dictionary<string, string>?> ReadHeadersAsync(CancellationToken cancellationToken)
    {
        if (_finished)
            return null;

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            string? line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                _finished = true;
                return headers.Count == 0 ? null : headers;
            }

            if (line.Length == 0)
                return headers;

            int colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
    }

    /// <summary>Returns a stream covering the current section body.</summary>
    internal Stream OpenBody() => new BodyStream(this);

    private async Task<int> ReadBodyAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        if (_finished)
            return 0;

        while (true)
        {
            if (_length == 0)
            {
                if (!await RefillAsync(cancellationToken).ConfigureAwait(false))
                {
                    _finished = true;
                    return 0;
                }
            }

            int index = IndexOf(_buffer, _position, _length, _delimiter);
            if (index >= 0)
            {
                int available = index - _position;
                int copy = Math.Min(destination.Length, available);
                if (copy > 0)
                {
                    _buffer.AsSpan(_position, copy).CopyTo(destination.Span);
                    _position += copy;
                    return copy;
                }

                _position = index + _delimiter.Length;
                await AfterDelimiterAsync(cancellationToken).ConfigureAwait(false);
                return 0;
            }

            int safe = _length - _position - (_delimiter.Length - 1);
            if (safe > 0)
            {
                int copy = Math.Min(destination.Length, safe);
                _buffer.AsSpan(_position, copy).CopyTo(destination.Span);
                _position += copy;
                return copy;
            }

            if (!await RefillAsync(cancellationToken).ConfigureAwait(false))
            {
                _finished = true;
                return 0;
            }
        }
    }

    private async Task AfterDelimiterAsync(CancellationToken cancellationToken)
    {
        // After "--boundary" comes either "\r\n" (another part) or "--" (end of body).
        byte[] suffix = new byte[2];
        for (int i = 0; i < suffix.Length; i++)
        {
            int value = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            if (value < 0)
            {
                _finished = true;
                return;
            }

            suffix[i] = (byte)value;
        }

        if (suffix[0] == '-' && suffix[1] == '-')
        {
            _finished = true;
            await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (suffix[0] == '\r' && suffix[1] == '\n')
            return;

        throw new IncusException("Invalid multipart boundary");
    }

    private async Task<int> ReadByteAsync(CancellationToken cancellationToken)
    {
        if (_length == 0 && !await RefillAsync(cancellationToken).ConfigureAwait(false))
            return -1;

        return _buffer[_position++];
    }

    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        StringBuilder builder = new();

        while (true)
        {
            if (_length == 0 && !await RefillAsync(cancellationToken).ConfigureAwait(false))
                return builder.Length == 0 ? null : builder.ToString();

            int newline = Array.IndexOf(_buffer, (byte)'\n', _position, _length);
            if (newline >= 0)
            {
                int count = newline - _position;
                if (count > 0 && _buffer[newline - 1] == (byte)'\r')
                    count--;

                if (count > 0)
                    builder.Append(Encoding.ASCII.GetString(_buffer, _position, count));

                _position = newline + 1;
                return builder.ToString();
            }

            if (_length > 0)
            {
                builder.Append(Encoding.ASCII.GetString(_buffer, _position, _length));
                _position += _length;
            }

            if (!await RefillAsync(cancellationToken).ConfigureAwait(false))
                return builder.Length == 0 ? null : builder.ToString();
        }
    }

    private async Task<bool> RefillAsync(CancellationToken cancellationToken)
    {
        if (_position > 0)
        {
            int remaining = _length;
            if (remaining > 0)
                Array.Copy(_buffer, _position, _buffer, 0, remaining);

            _position = 0;
            _length = remaining;
        }

        int read = await _inner.ReadAsync(_buffer.AsMemory(_length), cancellationToken).ConfigureAwait(false);
        if (read <= 0)
            return false;

        _length += read;
        return true;
    }

    private static int IndexOf(byte[] buffer, int start, int length, byte[] pattern)
    {
        int end = start + length - pattern.Length;
        for (int i = start; i <= end; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (buffer[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return i;
        }

        return -1;
    }

    private sealed class BodyStream : Stream
    {
        private readonly IncusMultipartReader _reader;
        private bool _done;

        internal BodyStream(IncusMultipartReader reader) => _reader = reader;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_done)
                return 0;

            int read = await _reader.ReadBodyAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                _done = true;

            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
