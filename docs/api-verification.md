# API translation verification

The verification compares the Go Incus client at tag `v7.6.0` with this C# package.

## What is checked

1. **API models** — every non-interface type declared in `shared/api` (238 types).
   * Structs must have a C# class with the same name.
   * Every JSON field (including fields promoted from embedded structs) must have a matching
     `[JsonPropertyName]` property.
   * Extra C# properties are reported to catch accidental additions.
2. **Client methods** — every exported method on `ProtocolIncus`, `ProtocolSimpleStreams` and
   `ProtocolOCI` must have a corresponding public C# method. Go `...WithContext` variants map to the
   same C# method because cancellation is passed through `CancellationToken`.

## Result

| Check | Result |
| --- | --- |
| API structs checked | 227 |
| Struct fields checked | 1,182 |
| Missing struct fields | **0** |
| Struct field type mismatches | **0** |
| Extra struct fields | **0** |
| `ProtocolIncus` methods | 343 / 346 |
| `ProtocolSimpleStreams` methods | 19 / 20 |
| `ProtocolOCI` methods | 19 / 20 |

The full machine-readable output is in [`verification-report.json`](verification-report.json).

## Remaining Go types and methods

These are intentional equivalents rather than missing API response data.

| Go | C# equivalent | Why |
| --- | --- | --- |
| `api.ResponseType` (defined string) | `Api.IncusResponseType` constants | Constant string values; no payload model. |
| `api.StatusCode` (defined int) | `Api.IncusStatusCode` constants | Constant integer values; no payload model. |
| `api.StatusError` | `IncusStatusException` / `IncusException` | Go error type, not an API response payload. |
| `api.URL` | `System.Uri` / query helpers | Build-time helper for REST URLs, not an API payload. |
| `MetadataConfigEntityName`, `MetadataConfigGroupName` | `string` dictionary keys | Defined string aliases used only as map keys. |
| `ProtocolIncus.DoHTTP` | `IncusClient.GetHttpClient()` | .NET callers send requests through `HttpClient`. |
| `ProtocolIncus.DoWebsocket` | `IncusClient.RawWebSocketAsync` / `IncusWebSocket` | .NET websocket abstraction. |
| `ProtocolIncus.WithContext` | `CancellationToken` parameters | Idiomatic .NET cancellation. |
| `ProtocolOCI.DoHTTP`, `ProtocolSimpleStreams.DoHTTP` | `GetHttpClient()` | Same as above. |

## Running the verification

```bash
# Clone the matching Go source.
git clone --depth 1 --branch v7.6.0 https://github.com/lxc/incus.git /tmp/incus

# Build the Go manifest and run the comparison.
go run scripts/verify/manifest.go \
  /tmp/incus/shared/api \
  /tmp/incus/client > /tmp/incus-manifest.json

python3 scripts/verify/verify.py \
  /tmp/incus-manifest.json \
  src/Mossie.Incus.Client > docs/verification-report.json
```
