using System.Net;
using System.Net.Sockets;

namespace Mossie.Incus.Client.Tests;

/// <summary>A tiny in-process HTTP server used to serve SimpleStreams metadata in tests.</summary>
internal sealed class TestHttpServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly IReadOnlyDictionary<string, byte[]> _responses;
    private Task? _loop;

    private TestHttpServer(IReadOnlyDictionary<string, byte[]> responses, int port)
    {
        _responses = responses;
        BaseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(BaseUrl + "/");
    }

    internal string BaseUrl { get; }

    internal static TestHttpServer Start(IReadOnlyDictionary<string, byte[]> responses)
    {
        TestHttpServer server = new(responses, GetFreePort());
        server._listener.Start();
        server._loop = Task.Run(server.AcceptLoopAsync);
        return server;
    }

    private static int GetFreePort()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => RespondAsync(context));
        }
    }

    private async Task RespondAsync(HttpListenerContext context)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";
            if (_responses.TryGetValue(path, out byte[]? body))
            {
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentLength64 = body.Length;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            }
        }
        catch (Exception)
        {
            // The test client may close the connection early; ignore.
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (Exception)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (Exception)
        {
        }

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        _cts.Dispose();
    }
}
