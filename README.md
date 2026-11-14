# Mossie.Incus.Client

A C# / .NET client for the [Incus](https://linuxcontainers.org/incus/) REST API, translated from the
official Go client (`github.com/lxc/incus/v7/client`) and API models (`github.com/lxc/incus/v7/shared/api`).

* Targets `net8.0` and `net10.0`
* Nullable enabled, `System.Text.Json`
* Async API surface mirroring the Go client
* Full API response model translation (see [Verification](#verification))
* HTTPS, TCP and Unix-socket transports, including websocket and raw stream endpoints

## Installation

```bash
dotnet add package Mossie.Incus.Client
```

## Quick start

```csharp
using Mossie.Incus.Client;
using Mossie.Incus.Client.Api;

await using IncusClient client = await IncusClient.ConnectAsync("https://incus.example.com:8443");

// List instances.
List<Instance> instances = await client.GetInstancesAsync(InstanceType.Any);

// Create a container from a remote image.
Operation operation = await client.CreateInstanceAsync(new InstancesPost
{
    Name = "demo",
    Type = InstanceType.Container.ToApiString(),
    Source = new InstanceSource
    {
        Type = "image",
        Protocol = "simplestreams",
        Server = "https://images.linuxcontainers.org",
        Alias = "debian/12",
    },
});

await operation.WaitAsync();
```

Connect over a Unix socket:

```csharp
await using IncusClient client = await IncusClient.ConnectUnixAsync("/var/lib/incus/unix.socket");
```

Connect to an image server:

```csharp
await using SimpleStreamsClient images =
    await SimpleStreamsClient.ConnectAsync("https://images.linuxcontainers.org");

List<Image> available = await images.GetImagesAsync();
```

## Design notes

* Models from Go `shared/api` live in `Mossie.Incus.Client.Api`. Embedded Go structs are flattened into
  C# properties; JSON names and `omitempty` semantics are preserved.
* `time.Time` maps to `DateTimeOffset` (RFC 3339), `json.RawMessage` to `JsonElement`, and free-form
  maps to `Dictionary<string, ...>`.
* Go `Writable()` helpers are available through `ApiModelExtensions.ToPut<T>()`.
* Methods returning an ETag use `Task<(T Value, string ETag)>`.
* Websocket endpoints return `IncusWebSocket`; NBD, SFTP and port-forward endpoints expose the upgraded
  connection as a `System.IO.Stream`.

## Verification

A verification script compares the Go `shared/api` structs and every exported method on
`ProtocolIncus`, `ProtocolSimpleStreams` and `ProtocolOCI` against the generated C# surface.

Latest result:

| Check | Result |
| --- | --- |
| API structs checked | 227 |
| Struct fields checked | 1,182 |
| Missing struct fields | 0 |
| Struct field type mismatches | 0 |
| `ProtocolIncus` methods present | 341 / 346 |
| `ProtocolSimpleStreams` methods present | 19 / 20 |
| `ProtocolOCI` methods present | 19 / 20 |

The remaining Go methods are transport primitives (`DoHTTP`, `DoWebsocket`), Go context plumbing
(`WithContext`), OIDC token access (`GetOIDCTokens`), and the Go-specific typed SFTP helper
(`GetStoragePoolVolumeFileSFTP`). Their C# equivalents are `HttpClient`, `CancellationToken`,
`IncusWebSocket` and the raw stream returned by `GetStoragePoolVolumeFileSFTPConnAsync`.

`ResponseType`, `StatusCode` and `StatusError` are represented by `IncusResponseType`,
`IncusStatusCode` and `IncusStatusException` respectively.

`docs/api-verification.md` describes the verification method and the intentional equivalents in
more detail.

## Tests

```bash
dotnet test Mossie.Incus.Client.slnx
```

The test project covers JSON serialization/deserialization of generated models, RFC 3339 date
handling, `Writable()`/`ToPut` helpers, architecture mapping, constants and error types, plus an
end-to-end SimpleStreams metadata parse and image download against an in-process HTTP server.

## License

Apache-2.0, matching the upstream Incus client. See [`LICENSE`](LICENSE).
