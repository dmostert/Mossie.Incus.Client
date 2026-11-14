using Mossie.Incus.Client.Api;

namespace Mossie.Incus.Client.Tests;

/// <summary>Tests for Go <c>Writable()</c> equivalents.</summary>
public sealed class ModelExtensionsTests
{
    [Fact]
    public void ToPut_CopiesOnlySharedWritableProperties()
    {
        Image image = new()
        {
            AutoUpdate = true,
            Public = true,
            Properties = new Dictionary<string, string> { ["os"] = "debian" },
            Profiles = ["default"],
            ExpiresAt = new DateTimeOffset(2028, 6, 30, 0, 0, 0, TimeSpan.Zero),
            Fingerprint = "should-not-copy",
            Architecture = "should-not-copy",
            Type = "container",
            Aliases = [new ImageAlias { Name = "debian/12" }],
        };

        ImagePut put = image.ToPut<ImagePut>();

        Assert.True(put.AutoUpdate);
        Assert.True(put.Public);
        Assert.Equal("debian", put.Properties!["os"]);
        Assert.Equal("default", Assert.Single(put.Profiles!));
        Assert.Equal(image.ExpiresAt, put.ExpiresAt);
    }

    [Fact]
    public void Writable_ReturnsDedicatedPutType()
    {
        Server server = new()
        {
            Config = new Dictionary<string, string> { ["core.https_address"] = ":8443" },
            Environment = new ServerEnvironment { ServerName = "node-1" },
        };

        ServerPut put = server.Writable();

        Assert.Equal(":8443", put.Config!["core.https_address"]);
    }

    [Fact]
    public void UnknownTargetProperties_AreIgnored()
    {
        Instance instance = new()
        {
            Name = "c1",
            Architecture = "x86_64",
        };

        InstancePut put = instance.Writable();

        Assert.Equal("x86_64", put.Architecture);
        Assert.Null(put.Config);
    }
}
