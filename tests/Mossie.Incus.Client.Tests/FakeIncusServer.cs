using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mossie.Incus.Client.Tests;

/// <summary>A minimal fake Incus daemon over a Unix socket: serves HTTP and the events websocket.</summary>
internal sealed class FakeIncusServer : IAsyncDisposable
{
    private readonly string _socketPath;
    private readonly Socket _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly List<string> _requests = new();
    private readonly object _lock = new();
    private readonly string _operationId = Guid.NewGuid().ToString("N");

    /// <summary>When true, the events websocket never emits the operation completion event.</summary>
    public bool SuppressOperationEvent { get; set; }

    /// <summary>When true, GET /operations/{id} keeps reporting Running (simulating a slow command).</summary>
    public bool OperationStaysRunning { get; set; }

    /// <summary>Number of GET /operations/{id} requests to answer with "Running" before "Success".</summary>
    public int RunningRepliesBeforeSuccess { get; set; }

    /// <summary>The JSON body of the most recent exec request.</summary>
    public string? LastExecBody { get; private set; }

    /// <summary>The JSON body of the most recent console request.</summary>
    public string? LastConsoleBody { get; private set; }

    private int _operationGets;

    private FakeIncusServer(string socketPath, Socket listener)
    {
        _socketPath = socketPath;
        _listener = listener;
        _loop = Task.Run(AcceptLoopAsync);
    }

    public string SocketPath => _socketPath;

    public IReadOnlyList<string> Requests
    {
        get { lock (_lock) return _requests.ToList(); }
    }

    public static FakeIncusServer Start()
    {
        string path = Path.Combine(Path.GetTempPath(), $"incus-test-{Guid.NewGuid():N}.sock");
        Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(16);
        return new FakeIncusServer(path, listener);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync(_cts.Token).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private void Log(string request)
    {
        lock (_lock)
            _requests.Add(request);
    }

    private async Task HandleClientAsync(Socket client)
    {
        try
        {
            using NetworkStream stream = new(client, ownsSocket: true);

            (string requestLine, Dictionary<string, string> headers, string body) =
                await ReadRequestAsync(stream).ConfigureAwait(false);
            Log(requestLine);

            if (headers.TryGetValue("upgrade", out string? upgrade) &&
                upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
            {
                await HandleWebsocketAsync(stream, requestLine, headers).ConfigureAwait(false);
                return;
            }

            await HandleHttpAsync(stream, requestLine, body).ConfigureAwait(false);
        }
        catch
        {
            // Client closed the connection.
        }
    }

    private static async Task<(string RequestLine, Dictionary<string, string> Headers, string Body)> ReadRequestAsync(Stream stream)
    {
        List<byte> buffer = new();
        byte[] single = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(single).ConfigureAwait(false);
            if (read == 0)
                break;

            buffer.Add(single[0]);

            if (buffer.Count >= 4 && buffer[^4] == (byte)'\r' && buffer[^3] == (byte)'\n' &&
                buffer[^2] == (byte)'\r' && buffer[^1] == (byte)'\n')
            {
                break;
            }
        }

        string text = Encoding.ASCII.GetString(buffer.ToArray());
        string[] lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon > 0)
                headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }

        string body = "";
        if (headers.TryGetValue("content-length", out string? lengthText) && int.TryParse(lengthText, out int length) && length > 0)
        {
            byte[] bodyBuffer = new byte[length];
            int total = 0;
            while (total < length)
            {
                int read = await stream.ReadAsync(bodyBuffer.AsMemory(total, length - total)).ConfigureAwait(false);
                if (read == 0)
                    break;

                total += read;
            }

            body = Encoding.UTF8.GetString(bodyBuffer, 0, total);
        }

        return (lines[0], headers, body);
    }

    private async Task HandleWebsocketAsync(Stream stream, string requestLine, Dictionary<string, string> headers)
    {
        string key = headers["sec-websocket-key"];
        string accept = Convert.ToBase64String(
            SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

        string response = "HTTP/1.1 101 Switching Protocols\r\n" +
                          "Upgrade: websocket\r\n" +
                          "Connection: Upgrade\r\n" +
                          $"Sec-WebSocket-Accept: {accept}\r\n\r\n";

        byte[] responseBytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(responseBytes).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);

        bool isEvents = requestLine.Contains("/events");

        if (isEvents && !SuppressOperationEvent)
        {
            // Give the client time to POST the operation and register its handler, so the event
            // arrives deterministically after the handler is installed.
            await Task.Delay(500).ConfigureAwait(false);

            string evt = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["type"] = "operation",
                ["timestamp"] = "2026-01-01T00:00:00Z",
                ["metadata"] = new Dictionary<string, object?>
                {
                    ["id"] = _operationId,
                    ["class"] = "task",
                    ["status"] = "Success",
                    ["status_code"] = 200,
                    ["metadata"] = new Dictionary<string, object?> { ["return"] = 0 },
                },
            });

            await SendWebsocketTextAsync(stream, evt).ConfigureAwait(false);
        }

        // Exec data websockets echo the client's frames back (simulating a PTY), so the terminal
        // mirror can be exercised end to end.
        if (!isEvents && requestLine.Contains("secret=term"))
        {
            while (true)
            {
                (byte[]? payload, bool isText) = await ReadWebsocketFrameAsync(stream).ConfigureAwait(false);
                if (payload is null)
                    break;

                await SendWebsocketFrameAsync(stream, payload, isText).ConfigureAwait(false);
            }

            return;
        }

        // Console websocket: emit some console output so the read-only session can be exercised.
        if (!isEvents && requestLine.Contains("secret=console"))
        {
            await SendWebsocketTextAsync(stream, "console ready\n").ConfigureAwait(false);

            byte[] consoleBuffer = new byte[1024];
            while (await stream.ReadAsync(consoleBuffer).ConfigureAwait(false) > 0)
            {
            }

            return;
        }

        // Keep the socket open until the client disconnects.
        byte[] buffer = new byte[1024];
        while (await stream.ReadAsync(buffer).ConfigureAwait(false) > 0)
        {
        }
    }

    private static async Task<(byte[]? Payload, bool IsText)> ReadWebsocketFrameAsync(Stream stream)
    {
        byte[] header = new byte[2];
        if (await stream.ReadAsync(header.AsMemory(0, 2)).ConfigureAwait(false) < 2)
            return (null, false);

        bool fin = (header[0] & 0x80) != 0;
        int opcode = header[0] & 0x0F;
        bool masked = (header[1] & 0x80) != 0;
        long length = header[1] & 0x7F;

        if (opcode == 0x8) // close
            return (null, false);

        if (length == 126)
        {
            byte[] extended = new byte[2];
            await stream.ReadExactlyAsync(extended).ConfigureAwait(false);
            length = (extended[0] << 8) | extended[1];
        }
        else if (length == 127)
        {
            byte[] extended = new byte[8];
            await stream.ReadExactlyAsync(extended).ConfigureAwait(false);
            length = 0;
            for (int i = 0; i < 8; i++)
                length = (length << 8) | extended[i];
        }

        byte[] mask = new byte[4];
        if (masked)
            await stream.ReadExactlyAsync(mask).ConfigureAwait(false);

        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload).ConfigureAwait(false);

        if (masked)
        {
            for (int i = 0; i < payload.Length; i++)
                payload[i] ^= mask[i % 4];
        }

        return (payload, opcode == 0x1);
    }

    private static async Task SendWebsocketFrameAsync(Stream stream, byte[] payload, bool isText)
    {
        using MemoryStream frame = new();
        frame.WriteByte((byte)(0x80 | (isText ? 0x1 : 0x2)));

        if (payload.Length < 126)
        {
            frame.WriteByte((byte)payload.Length);
        }
        else if (payload.Length <= 0xFFFF)
        {
            frame.WriteByte(126);
            frame.WriteByte((byte)(payload.Length >> 8));
            frame.WriteByte((byte)payload.Length);
        }
        else
        {
            frame.WriteByte(127);
            for (int i = 7; i >= 0; i--)
                frame.WriteByte((byte)((long)payload.Length >> (8 * i)));
        }

        frame.Write(payload);
        await stream.WriteAsync(frame.ToArray()).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private async Task HandleHttpAsync(Stream stream, string requestLine, string requestBody)
    {
        string[] parts = requestLine.Split(' ');
        string method = parts[0];
        string path = parts[1];

        string body;
        int status;

        if (method == "POST" && path.Contains("/console"))
        {
            LastConsoleBody = requestBody;

            // The console exposes a terminal (fd "0") and a control socket.
            body = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["type"] = "async",
                ["status"] = "Operation created",
                ["status_code"] = 100,
                ["metadata"] = new Dictionary<string, object?>
                {
                    ["id"] = _operationId,
                    ["class"] = "websocket",
                    ["status"] = "Running",
                    ["status_code"] = 103,
                    ["metadata"] = new Dictionary<string, object?>
                    {
                        ["fds"] = new Dictionary<string, object?>
                        {
                            ["0"] = "console0",
                            ["control"] = "ctrl0",
                        },
                    },
                },
            });
            status = 202;
        }
        else if (method == "POST" && path.Contains("/exec"))
        {
            LastExecBody = requestBody;

            bool interactive = requestBody.Contains("\"interactive\":true", StringComparison.Ordinal);
            if (interactive)
            {
                // Interactive exec exposes a terminal (fd "0") and a control socket.
                body = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["type"] = "async",
                    ["status"] = "Operation created",
                    ["status_code"] = 100,
                    ["metadata"] = new Dictionary<string, object?>
                    {
                        ["id"] = _operationId,
                        ["class"] = "websocket",
                        ["status"] = "Running",
                        ["status_code"] = 103,
                        ["metadata"] = new Dictionary<string, object?>
                        {
                            ["fds"] = new Dictionary<string, object?>
                            {
                                ["0"] = "term0",
                                ["control"] = "ctrl0",
                            },
                        },
                    },
                });
                status = 202;
            }
            else
            {
                body = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["type"] = "async",
                    ["status"] = "Operation created",
                    ["status_code"] = 100,
                    ["metadata"] = new Dictionary<string, object?>
                    {
                        ["id"] = _operationId,
                        ["class"] = "task",
                        ["status"] = "Running",
                        ["status_code"] = 103,
                        ["metadata"] = new Dictionary<string, object?>
                        {
                            ["output"] = new Dictionary<string, object?>
                            {
                                ["1"] = "/1.0/instances/caddy/logs/exec-output/exec_stdout.log",
                                ["2"] = "/1.0/instances/caddy/logs/exec-output/exec_stderr.log",
                            },
                        },
                    },
                });
                status = 202;
            }
        }
        else if (method == "GET" && path.Contains("/operations/"))
        {
            int gets = Interlocked.Increment(ref _operationGets);
            bool running = OperationStaysRunning || gets <= RunningRepliesBeforeSuccess;
            string statusName = running ? "Running" : "Success";
            int statusCode = running ? 103 : 200;
            body = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["type"] = "sync",
                ["status"] = statusName,
                ["status_code"] = statusCode,
                ["metadata"] = new Dictionary<string, object?>
                {
                    ["id"] = _operationId,
                    ["class"] = "task",
                    ["status"] = statusName,
                    ["status_code"] = statusCode,
                    ["metadata"] = new Dictionary<string, object?>
                    {
                        ["return"] = 0,
                        ["output"] = new Dictionary<string, object?>
                        {
                            ["1"] = "/1.0/instances/caddy/logs/exec-output/exec_stdout.log",
                            ["2"] = "/1.0/instances/caddy/logs/exec-output/exec_stderr.log",
                        },
                    },
                },
            });
            status = 200;
        }
        else if (method == "GET" && path.Contains("/logs/exec-output/"))
        {
            body = "caddy output";
            status = 200;
        }
        else if (method == "DELETE" && path.Contains("/logs/exec-output/"))
        {
            body = """{"type":"sync","status":"Success","status_code":200,"metadata":null}""";
            status = 200;
        }
        else if (method == "GET" && path.EndsWith("/1.0"))
        {
            body = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["type"] = "sync",
                ["status"] = "Success",
                ["status_code"] = 200,
                ["metadata"] = new Dictionary<string, object?>
                {
                    ["api_extensions"] = new[] { "container_exec_recording", "console", "console_vga_type", "console_force" },
                    ["api_status"] = "stable",
                    ["api_version"] = "1.0",
                    ["auth"] = "trusted",
                    ["public"] = false,
                    ["environment"] = new Dictionary<string, object?>(),
                },
            });
            status = 200;
        }
        else
        {
            body = """{"type":"error","error":"not found","error_code":404}""";
            status = 404;
        }

        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string headers = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Status")}\r\n" +
                         "Content-Type: application/json\r\n" +
                         $"Content-Length: {bodyBytes.Length}\r\n" +
                         "Connection: close\r\n\r\n";

        byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
        await stream.WriteAsync(headerBytes).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static async Task SendWebsocketTextAsync(Stream stream, string text)
    {
        byte[] payload = Encoding.UTF8.GetBytes(text);
        using MemoryStream frame = new();
        frame.WriteByte(0x81); // FIN + text

        if (payload.Length < 126)
        {
            frame.WriteByte((byte)payload.Length);
        }
        else
        {
            frame.WriteByte(126);
            frame.WriteByte((byte)(payload.Length >> 8));
            frame.WriteByte((byte)payload.Length);
        }

        frame.Write(payload);
        await stream.WriteAsync(frame.ToArray()).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            _listener.Close();
        }
        catch
        {
        }

        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            File.Delete(_socketPath);
        }
        catch
        {
        }

        _cts.Dispose();
    }
}
