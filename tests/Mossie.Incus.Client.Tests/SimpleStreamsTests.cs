using System.Security.Cryptography;
using System.Text;
using Mossie.Incus.Client.Api;

namespace Mossie.Incus.Client.Tests;

/// <summary>End-to-end parsing and download tests for the SimpleStreams client.</summary>
public sealed class SimpleStreamsTests
{
    [Fact]
    public async Task Client_ParsesMetadataAndDownloadsImageFiles()
    {
        string architecture = IncusArchitecture.LocalName();
        byte[] meta = Encoding.UTF8.GetBytes("incus-metadata-content");
        byte[] root = Encoding.UTF8.GetBytes("root filesystem content");

        string metaHash = Sha256(meta);
        string rootHash = Sha256(root);
        string fingerprint = Sha256([.. meta, .. root]);

        string index = $$"""
            {
              "index": {
                "images": {
                  "datatype": "image-downloads",
                  "path": "streams/v1/images.json",
                  "products": ["debian"],
                  "format": "products:1.0"
                }
              },
              "format": "index:1.0"
            }
            """;

        string products = $$"""
            {
              "content_id": "images",
              "datatype": "image-downloads",
              "format": "products:1.0",
              "products": {
                "debian": {
                  "aliases": "debian/12,debian/bookworm",
                  "arch": "{{architecture}}",
                  "os": "debian",
                  "release": "bookworm",
                  "release_title": "Debian Bookworm",
                  "supported": true,
                  "support_eol": "2028-06-30",
                  "variant": "default",
                  "requirements": { "secureboot": "false" },
                  "versions": {
                    "20240102_10:00": {
                      "label": "daily",
                      "items": {
                        "incus.tar.xz": {
                          "ftype": "incus.tar.xz",
                          "path": "images/meta.tar.xz",
                          "sha256": "{{metaHash}}",
                          "size": {{meta.Length}},
                          "combined_sha256": "{{fingerprint}}",
                          "combined_rootxz_sha256": "{{fingerprint}}"
                        },
                        "root.tar.xz": {
                          "ftype": "root.tar.xz",
                          "path": "images/root.tar.xz",
                          "sha256": "{{rootHash}}",
                          "size": {{root.Length}},
                          "combined_sha256": "{{fingerprint}}",
                          "combined_rootxz_sha256": "{{fingerprint}}"
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        Dictionary<string, byte[]> routes = new()
        {
            ["/streams/v1/index.json"] = Encoding.UTF8.GetBytes(index),
            ["/streams/v1/images.json"] = Encoding.UTF8.GetBytes(products),
            ["/images/meta.tar.xz"] = meta,
            ["/images/root.tar.xz"] = root,
        };

        await using TestHttpServer server = TestHttpServer.Start(routes);
        await using SimpleStreamsClient client = await SimpleStreamsClient.ConnectAsync(server.BaseUrl);

        List<Image> images = await client.GetImagesAsync();

        Image image = Assert.Single(images);
        Assert.Equal(fingerprint, image.Fingerprint);
        Assert.Equal(architecture, image.Architecture);
        Assert.Equal("container", image.Type);
        Assert.Equal(meta.Length + root.Length, image.Size);
        Assert.Equal("debian", image.Properties!["os"]);
        Assert.Equal("bookworm", image.Properties["release"]);
        Assert.Equal("root.tar.xz", image.Properties["type"]);
        Assert.Equal("false", image.Properties["requirements.secureboot"]);
        Assert.Equal(new DateTimeOffset(2028, 6, 30, 0, 0, 0, TimeSpan.Zero), image.ExpiresAt);
        Assert.Equal(4, image.Aliases!.Count);
        Assert.Contains(image.Aliases, alias => alias.Name == "debian/12");
        Assert.Contains(image.Aliases, alias => alias.Name == "debian/bookworm");
        Assert.Contains(image.Aliases, alias => alias.Name == $"debian/12/{architecture}");

        Assert.Equal(fingerprint, Assert.Single(await client.GetImageFingerprintsAsync()));

        (Image fetched, _) = await client.GetImageAsync(fingerprint[..16]);
        Assert.Equal(fingerprint, fetched.Fingerprint);

        (ImageAliasesEntry alias, _) = await client.GetImageAliasAsync("debian/12");
        Assert.Equal(fingerprint, alias.Target);
        Assert.Equal("container", alias.Type);

        Dictionary<string, ImageAliasesEntry> byArchitecture =
            await client.GetImageAliasArchitecturesAsync("container", "debian/12");
        Assert.Equal(fingerprint, byArchitecture[architecture].Target);

        Assert.Contains("debian/12", await client.GetImageAliasNamesAsync());

        using MemoryStream metaTarget = new();
        using MemoryStream rootTarget = new();
        ImageFileResponse response = await client.GetImageFileAsync(
            fingerprint,
            new ImageFileRequest { MetaFile = metaTarget, RootfsFile = rootTarget });

        Assert.Equal(meta.Length, response.MetaSize);
        Assert.Equal(root.Length, response.RootfsSize);
        Assert.Equal("meta.tar.xz", response.MetaName);
        Assert.Equal("root.tar.xz", response.RootfsName);
        Assert.Equal(meta, metaTarget.ToArray());
        Assert.Equal(root, rootTarget.ToArray());
    }

    [Fact]
    public async Task Client_RejectsUnsupportedFilters()
    {
        await using TestHttpServer server = TestHttpServer.Start(new Dictionary<string, byte[]>());
        await using SimpleStreamsClient client = await SimpleStreamsClient.ConnectAsync(server.BaseUrl);

        await Assert.ThrowsAsync<IncusException>(() => client.GetImagesWithFilterAsync(["os=debian"]));
        await Assert.ThrowsAsync<IncusException>(() => client.GetImagesAllProjectsWithFilterAsync(["os=debian"]));
    }

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
