# Mossie.Incus.Client

[![CI](https://github.com/dmostert/Mossie.Incus.Client/actions/workflows/ci.yml/badge.svg)](https://github.com/dmostert/Mossie.Incus.Client/actions/workflows/ci.yml)

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

Open an interactive terminal or watch the instance console (see `Mossie.Incus.Client.VirtualTerminal`):

```csharp
using Mossie.Incus.Client.VirtualTerminal;

// Interactive shell:
using IncusTerminalSession session = new(client, "my-container");
await session.ConnectAsync(columns: 120, rows: 30);
Terminal.Session = session; // VirtualTerminal WPF/Avalonia TerminalControl

// Read-only console output:
using IncusConsoleSession console = new(client, "my-container");
await console.ConnectAsync();
```

## Terminal integration

The companion package `Mossie.Incus.Client.VirtualTerminal` provides two
[VirtualTerminal](https://github.com/Rikitav/VirtualTerminal) sessions that feed instance output
into a VT/ANSI screen buffer and plug directly into the `VirtualTerminal.WPF` and
`VirtualTerminal.Avalonia` `TerminalControl`:

* `IncusTerminalSession` — an interactive `exec` PTY (custom command and environment supported).
* `IncusConsoleSession` — the instance console device, read-only by default.

Both target `net10.0`. See its [README](src/Mossie.Incus.Client.VirtualTerminal/README.md).

## Design notes

* Models from Go `shared/api` live in `Mossie.Incus.Client.Api`. Embedded Go structs are flattened into
  C# properties; JSON names and `omitempty` semantics are preserved.
* `time.Time` maps to `DateTimeOffset` (RFC 3339), `json.RawMessage` to `JsonElement`, and free-form
  maps to `Dictionary<string, ...>`.
* Go `Writable()` helpers are available through `ApiModelExtensions.ToPut<T>()`.
* Methods returning an ETag use `Task<(T Value, string ETag)>`.
* Websocket endpoints return `IncusWebSocket`; NBD, SFTP and port-forward endpoints expose the upgraded
  connection as a `System.IO.Stream`.
* Console and exec control handlers (`InstanceConsoleArgs.Control`, `InstanceExecArgs.Control`) receive
  an `IncusWebSocket` so they can read and write control messages (window resize, signals, ...), matching
  the Go `func(conn *websocket.Conn)` callback.
* OIDC authentication is supplied through `IncusConnectionArgs.OidcTokenProvider`, which returns an
  `OidcTokens` set; the last set is available via `IncusClient.GetOIDCTokens()`.

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
| `ProtocolIncus` methods present | 343 / 346 |
| `ProtocolSimpleStreams` methods present | 19 / 20 |
| `ProtocolOCI` methods present | 19 / 20 |

The remaining Go methods are transport primitives (`DoHTTP`, `DoWebsocket`) and Go context plumbing
(`WithContext`). Their C# equivalents are `HttpClient` / `RawWebSocketAsync` and `CancellationToken`
parameters.

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

## Publishing

The [`Publish NuGet package`](.github/workflows/publish.yml) workflow publishes tagged releases:

```bash
git tag v7.6.0
git push origin v7.6.0
```

Publishing also runs when a GitHub Release is published. A release created from an existing tag is
safe to re-run because both push steps use `--skip-duplicate`.

Publishing uses [NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing):
the workflow requests a GitHub OIDC token and exchanges it for a short-lived nuget.org API key, so no
long-lived API key is stored in the repository.

One-time setup:

1. On nuget.org, open **Trusted Publishing** and add a policy:
   - **Repository Owner**: `dmostert`
   - **Repository**: `Mossie.Incus.Client`
   - **Workflow File**: `publish.yml` (file name only, not the full path)
   - **Environment**: leave empty, unless the workflow job is given an `environment:` — in that case
     the policy must use the same name.
2. Add the repository secret `NUGET_USER` with your nuget.org profile name (not your email address).
   This identifies the account that requests the temporary key; no API key secret is needed.
3. Keep `id-token: write` in the workflow permissions so GitHub can issue the OIDC token.

The workflow:

1. Derives the package version from the tag (`v7.6.1` → `7.6.1`) or from a manual dispatch input.
2. Restores, builds and runs the test suite.
3. Packs the `.nupkg` and `.snupkg` into `artifacts/packages`.
4. Exchanges the OIDC token for a temporary API key with `NuGet/login@v1` (valid for one hour) and
   pushes both packages with `dotnet nuget push --skip-duplicate`.

Manual runs of the workflow can specify a version and enable `dry_run` to build and pack without
publishing. Build artifacts are uploaded to the workflow run.

## License

Apache-2.0, matching the upstream Incus client. See [`LICENSE`](LICENSE).
