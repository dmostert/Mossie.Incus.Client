using System.Net.WebSockets;
using System.Text;
using Mossie.Incus.Client.Internal;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>
    /// Relays the migration websockets between a source and a target operation (relay transfer mode).
    /// </summary>
    internal async Task ProxyMigrationAsync(
        Operation targetOperation,
        Dictionary<string, string> targetSecrets,
        IncusClient source,
        Operation sourceOperation,
        Dictionary<string, string> sourceSecrets,
        CancellationToken cancellationToken = default)
    {
        foreach (string name in targetSecrets.Keys)
        {
            if (!sourceSecrets.ContainsKey(name))
                throw new IncusException($"Migration target expects the \"{name}\" socket but source isn't providing it");
        }

        if (!targetSecrets.TryGetValue("control", out string? targetControlSecret) || string.IsNullOrEmpty(targetControlSecret))
            throw new IncusException("Migration target didn't setup the required \"control\" socket");

        Dictionary<string, (IncusWebSocket Source, IncusWebSocket Target, Task Pump)> proxies = new();

        IncusWebSocket sourceControl = await source
            .GetOperationWebSocketAsync(sourceOperation.Id, sourceSecrets["control"], cancellationToken).ConfigureAwait(false);

        IncusWebSocket targetControl = await targetOperation
            .GetWebSocketAsync(targetControlSecret, cancellationToken).ConfigureAwait(false);

        proxies["control"] = (sourceControl, targetControl, PumpPairAsync(sourceControl, targetControl, cancellationToken));

        foreach (KeyValuePair<string, string> secret in sourceSecrets)
        {
            if (secret.Key == "control")
                continue;

            if (!targetSecrets.TryGetValue(secret.Key, out string? targetSecret) || string.IsNullOrEmpty(targetSecret))
            {
                // The target may not require every source socket.
                continue;
            }

            try
            {
                IncusWebSocket sourceSocket = await source
                    .GetOperationWebSocketAsync(sourceOperation.Id, secret.Value, cancellationToken).ConfigureAwait(false);

                IncusWebSocket targetSocket = await targetOperation
                    .GetWebSocketAsync(targetSecret, cancellationToken).ConfigureAwait(false);

                proxies[secret.Key] = (sourceSocket, targetSocket, PumpPairAsync(sourceSocket, targetSocket, cancellationToken));
            }
            catch (Exception)
            {
                // Match Go: stop opening additional sockets when one cannot be established.
                break;
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await proxies["control"].Pump.ConfigureAwait(false);

                List<Task> remaining = new();
                foreach (KeyValuePair<string, (IncusWebSocket Source, IncusWebSocket Target, Task Pump)> proxy in proxies)
                {
                    if (proxy.Key == "control")
                        continue;

                    remaining.Add(proxy.Value.Pump);
                }

                await Task.WhenAll(remaining).ConfigureAwait(false);
            }
            catch
            {
                // Best effort cleanup.
            }
            finally
            {
                foreach (KeyValuePair<string, (IncusWebSocket Source, IncusWebSocket Target, Task Pump)> proxy in proxies)
                {
                    await proxy.Value.Source.DisposeAsync().ConfigureAwait(false);
                    await proxy.Value.Target.DisposeAsync().ConfigureAwait(false);
                }
            }
        }, CancellationToken.None);
    }

    private static async Task PumpPairAsync(IncusWebSocket first, IncusWebSocket second, CancellationToken cancellationToken)
    {
        Task forward = PumpSocketAsync(first, second, cancellationToken);
        Task backward = PumpSocketAsync(second, first, cancellationToken);

        await Task.WhenAny(forward, backward).ConfigureAwait(false);
    }

    private static async Task PumpSocketAsync(IncusWebSocket from, IncusWebSocket to, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                (WebSocketMessageType type, byte[] data) = await from.ReceiveAsync(cancellationToken).ConfigureAwait(false);

                if (type == WebSocketMessageType.Close)
                    return;

                if (type == WebSocketMessageType.Text)
                    await to.SendTextAsync(Encoding.UTF8.GetString(data), cancellationToken).ConfigureAwait(false);
                else
                    await to.SendBinaryAsync(data, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // A closed peer ends the relay.
        }
    }
}
