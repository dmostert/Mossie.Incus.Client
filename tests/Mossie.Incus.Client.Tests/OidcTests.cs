using System.Net;
using System.Text;
using Mossie.Incus.Client.Api;

namespace Mossie.Incus.Client.Tests;

/// <summary>Tests for the OIDC token provider integration.</summary>
public sealed class OidcTests
{
    [Fact]
    public async Task OidcTokenProvider_IsInvokedAndCached()
    {
        CapturingHandler handler = new();
        OidcTokens tokens = new()
        {
            AccessToken = "opaque-access-token",
            IdToken = "raw-id-token",
            TokenType = "Bearer",
            RefreshToken = "refresh-token",
            Expiry = DateTimeOffset.UtcNow.AddHours(1),
        };

        IncusConnectionArgs args = new()
        {
            AuthType = IncusConstants.AuthenticationMethodOIDC,
            OidcTokenProvider = _ => Task.FromResult<OidcTokens?>(tokens),
            TransportWrapper = _ => handler,
        };

        await using IncusClient client = await IncusClient.ConnectAsync("https://incus.test", args);

        // The provider is invoked while the client fetches the server configuration.
        Assert.Equal("Bearer opaque-access-token", handler.Authorization);

        // An opaque access token is accompanied by the raw ID token header.
        Assert.Equal("raw-id-token", handler.OidcIdToken);

        OidcTokens? cached = client.GetOIDCTokens();
        Assert.NotNull(cached);
        Assert.Equal("opaque-access-token", cached.AccessToken);
        Assert.Equal("raw-id-token", cached.IdToken);
        Assert.Equal("refresh-token", cached.RefreshToken);
        Assert.Equal("Bearer", cached.TokenType);
    }

    [Fact]
    public async Task OidcTokenProvider_JwtToken_OmitsIdTokenHeader()
    {
        CapturingHandler handler = new();
        OidcTokens tokens = new()
        {
            // A JWT has three dot-separated segments, so no ID token header is sent.
            AccessToken = "header.payload.signature",
            IdToken = "raw-id-token",
        };

        IncusConnectionArgs args = new()
        {
            OidcTokenProvider = _ => Task.FromResult<OidcTokens?>(tokens),
            TransportWrapper = _ => handler,
        };

        await using IncusClient client = await IncusClient.ConnectAsync("https://incus.test", args);

        Assert.Equal("Bearer header.payload.signature", handler.Authorization);
        Assert.Null(handler.OidcIdToken);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        public string? OidcIdToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            OidcIdToken = request.Headers.TryGetValues("X-Incus-OIDC-idtoken", out IEnumerable<string>? values)
                ? values.FirstOrDefault()
                : null;

            const string body = """
                {
                  "type": "sync",
                  "status": "Success",
                  "status_code": 200,
                  "metadata": {
                    "api_extensions": [],
                    "api_status": "stable",
                    "api_version": "1.0",
                    "auth": "trusted",
                    "public": false,
                    "auth_methods": ["tls"],
                    "environment": {}
                  }
                }
                """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
