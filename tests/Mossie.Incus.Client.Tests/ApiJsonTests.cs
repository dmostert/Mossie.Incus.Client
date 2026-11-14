using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Serialization;

namespace Mossie.Incus.Client.Tests;

/// <summary>Serialization tests for generated API models.</summary>
public sealed class ApiJsonTests
{
    [Fact]
    public void Image_DeserializesAllFields()
    {
        const string json = """
            {
              "auto_update": true,
              "properties": { "os": "debian", "release": "bookworm" },
              "public": true,
              "expires_at": "2028-06-30T00:00:00Z",
              "profiles": ["default"],
              "aliases": [{ "name": "debian/12" }],
              "architecture": "x86_64",
              "cached": false,
              "filename": "rootfs.tar.xz",
              "fingerprint": "abc123",
              "size": "123",
              "created_at": "2024-01-02T03:04:05.5Z",
              "last_used_at": "2024-02-03T04:05:06Z",
              "uploaded_at": "2024-01-02T03:04:05Z",
              "type": "container"
            }
            """;

        Image image = IncusJson.Deserialize<Image>(json)!;

        Assert.True(image.AutoUpdate);
        Assert.True(image.Public);
        Assert.Equal("debian", image.Properties!["os"]);
        Assert.Equal("bookworm", image.Properties["release"]);
        Assert.Equal("default", Assert.Single(image.Profiles!));
        Assert.Equal("debian/12", Assert.Single(image.Aliases!).Name);
        Assert.Equal("x86_64", image.Architecture);
        Assert.Equal("rootfs.tar.xz", image.Filename);
        Assert.Equal("abc123", image.Fingerprint);
        Assert.Equal(123L, image.Size);
        Assert.Equal("container", image.Type);
        Assert.Equal(500, image.CreatedAt.Millisecond);
        Assert.Equal(new DateTimeOffset(2028, 6, 30, 0, 0, 0, TimeSpan.Zero), image.ExpiresAt);
    }

    [Fact]
    public void Image_RoundTripsJsonPropertyNames()
    {
        Image image = new()
        {
            Architecture = "aarch64",
            Fingerprint = "deadbeef",
            Properties = new Dictionary<string, string> { ["os"] = "alpine" },
            Size = 42,
        };

        string json = IncusJson.Serialize(image);

        Assert.Contains("\"architecture\":\"aarch64\"", json);
        Assert.Contains("\"fingerprint\":\"deadbeef\"", json);
        Assert.Contains("\"os\":\"alpine\"", json);
        Assert.Contains("\"size\":42", json);
    }

    [Fact]
    public void Instance_FlattensEmbeddedStructFields()
    {
        const string json = """
            {
              "name": "c1",
              "architecture": "x86_64",
              "status": "Running",
              "status_code": 103,
              "config": { "limits.cpu": "2" },
              "devices": { "root": { "path": "/", "type": "disk" } },
              "ephemeral": true,
              "profiles": ["default"],
              "stateful": false,
              "created_at": "2024-01-02T03:04:05Z",
              "expanded_devices": { "eth0": { "type": "nic" } }
            }
            """;

        Instance instance = IncusJson.Deserialize<Instance>(json)!;

        Assert.Equal("c1", instance.Name);
        Assert.Equal("x86_64", instance.Architecture);
        Assert.Equal("Running", instance.Status);
        Assert.Equal(103, instance.StatusCode);
        Assert.Equal("2", instance.Config!["limits.cpu"]);
        Assert.Equal("disk", instance.Devices!["root"]["type"]);
        Assert.True(instance.Ephemeral);
        Assert.Equal("default", Assert.Single(instance.Profiles!));
        Assert.Equal("nic", instance.ExpandedDevices!["eth0"]["type"]);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), instance.CreatedAt);
    }

    [Fact]
    public void Operation_DeserializesNestedResources()
    {
        const string json = """
            {
              "id": "op-1",
              "class": "task",
              "description": "Creating instance",
              "status": "Running",
              "status_code": 103,
              "resources": {
                "instances": ["/1.0/instances/c1"]
              },
              "metadata": { "kind": "instance" },
              "may_cancel": true,
              "created_at": "2024-01-02T03:04:05Z",
              "updated_at": "2024-01-02T03:04:06Z"
            }
            """;

        Api.Operation operation = IncusJson.Deserialize<Api.Operation>(json)!;

        Assert.Equal("op-1", operation.ID);
        Assert.Equal("Running", operation.Status);
        Assert.Equal(103, operation.StatusCode);
        Assert.Equal("Creating instance", operation.Description);
        Assert.True(operation.MayCancel);
        Assert.NotNull(operation.Resources);
        Assert.Equal("/1.0/instances/c1", Assert.Single(operation.Resources!["instances"]));
        Assert.Equal("instance", ((System.Text.Json.JsonElement)operation.Metadata!["kind"]).GetString());
    }
}
