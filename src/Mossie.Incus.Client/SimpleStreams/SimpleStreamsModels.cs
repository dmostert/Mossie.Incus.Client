using System.Text.Json.Serialization;

namespace Mossie.Incus.Client.SimpleStreams;

/// <summary>Base structure of a simplestreams <c>index.json</c> document.</summary>
public sealed class SimpleStreamsIndex
{
    /// <summary>Indices keyed by name.</summary>
    [JsonPropertyName("index")]
    public Dictionary<string, SimpleStreamsIndexEntry> Index { get; set; } = new();

    /// <summary>Last update time.</summary>
    [JsonPropertyName("updated")]
    public string? Updated { get; set; }

    /// <summary>Document format.</summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = "";
}

/// <summary>An entry inside <c>index.json</c>.</summary>
public sealed class SimpleStreamsIndexEntry
{
    /// <summary>Data type (only <c>image-downloads</c> is relevant).</summary>
    [JsonPropertyName("datatype")]
    public string DataType { get; set; } = "";

    /// <summary>Path to the products document.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>Last update time.</summary>
    [JsonPropertyName("updated")]
    public string? Updated { get; set; }

    /// <summary>Product names.</summary>
    [JsonPropertyName("products")]
    public List<string> Products { get; set; } = new();

    /// <summary>Document format.</summary>
    [JsonPropertyName("format")]
    public string? Format { get; set; }
}

/// <summary>Base of a simplestreams <c>download.json</c> document.</summary>
public sealed class SimpleStreamsProducts
{
    /// <summary>Content identifier.</summary>
    [JsonPropertyName("content_id")]
    public string ContentId { get; set; } = "";

    /// <summary>Data type.</summary>
    [JsonPropertyName("datatype")]
    public string DataType { get; set; } = "";

    /// <summary>Document format.</summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = "";

    /// <summary>License text.</summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>Products keyed by product name.</summary>
    [JsonPropertyName("products")]
    public Dictionary<string, SimpleStreamsProduct> Products { get; set; } = new();

    /// <summary>Last update time.</summary>
    [JsonPropertyName("updated")]
    public string? Updated { get; set; }
}

/// <summary>A single product inside <c>download.json</c>.</summary>
public sealed class SimpleStreamsProduct
{
    /// <summary>Comma-separated aliases.</summary>
    [JsonPropertyName("aliases")]
    public string Aliases { get; set; } = "";

    /// <summary>Architecture name.</summary>
    [JsonPropertyName("arch")]
    public string Architecture { get; set; } = "";

    /// <summary>Operating system name.</summary>
    [JsonPropertyName("os")]
    public string OperatingSystem { get; set; } = "";

    /// <summary>Product requirements.</summary>
    [JsonPropertyName("requirements")]
    public Dictionary<string, string>? Requirements { get; set; }

    /// <summary>Release name.</summary>
    [JsonPropertyName("release")]
    public string Release { get; set; } = "";

    /// <summary>Release codename.</summary>
    [JsonPropertyName("release_codename")]
    public string? ReleaseCodename { get; set; }

    /// <summary>Release title.</summary>
    [JsonPropertyName("release_title")]
    public string ReleaseTitle { get; set; } = "";

    /// <summary>Whether the product is supported.</summary>
    [JsonPropertyName("supported")]
    public bool Supported { get; set; }

    /// <summary>End-of-life date.</summary>
    [JsonPropertyName("support_eol")]
    public string? SupportedEol { get; set; }

    /// <summary>Product version string.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>Product versions keyed by serial.</summary>
    [JsonPropertyName("versions")]
    public Dictionary<string, SimpleStreamsProductVersion> Versions { get; set; } = new();

    /// <summary>Product variant.</summary>
    [JsonPropertyName("variant")]
    public string? Variant { get; set; }
}

/// <summary>A particular version of a product.</summary>
public sealed class SimpleStreamsProductVersion
{
    /// <summary>Files keyed by item name.</summary>
    [JsonPropertyName("items")]
    public Dictionary<string, SimpleStreamsProductVersionItem> Items { get; set; } = new();

    /// <summary>Version label.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>Public name.</summary>
    [JsonPropertyName("pubname")]
    public string? PublicName { get; set; }
}

/// <summary>A file inside a product version.</summary>
public sealed class SimpleStreamsProductVersionItem
{
    /// <summary>Combined disk1 image hash.</summary>
    [JsonPropertyName("combined_disk1-img_sha256")]
    public string CombinedSha256DiskImg { get; set; } = "";

    /// <summary>Combined disk-kvm image hash.</summary>
    [JsonPropertyName("combined_disk-kvm-img_sha256")]
    public string CombinedSha256DiskKvmImg { get; set; } = "";

    /// <summary>Combined UEFI image hash.</summary>
    [JsonPropertyName("combined_uefi1-img_sha256")]
    public string CombinedSha256DiskUefiImg { get; set; } = "";

    /// <summary>Combined rootfs tar.xz hash.</summary>
    [JsonPropertyName("combined_rootxz_sha256")]
    public string CombinedSha256RootXz { get; set; } = "";

    /// <summary>Combined hash.</summary>
    [JsonPropertyName("combined_sha256")]
    public string CombinedSha256 { get; set; } = "";

    /// <summary>Combined squashfs hash.</summary>
    [JsonPropertyName("combined_squashfs_sha256")]
    public string CombinedSha256SquashFs { get; set; } = "";

    /// <summary>Combined image type.</summary>
    [JsonPropertyName("combined_type")]
    public string CombinedType { get; set; } = "";

    /// <summary>File type.</summary>
    [JsonPropertyName("ftype")]
    public string FileType { get; set; } = "";

    /// <summary>MD5 hash.</summary>
    [JsonPropertyName("md5")]
    public string? HashMd5 { get; set; }

    /// <summary>Path to the file, relative to the image server.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>SHA-256 hash.</summary>
    [JsonPropertyName("sha256")]
    public string? HashSha256 { get; set; }

    /// <summary>File size in bytes.</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>Base image for a delta.</summary>
    [JsonPropertyName("delta_base")]
    public string? DeltaBase { get; set; }
}

/// <summary>A downloadable file with its URL, hash and size.</summary>
public sealed class SimpleStreamsDownloadableFile
{
    /// <summary>Path to the file.</summary>
    public string Path { get; set; } = "";

    /// <summary>SHA-256 hash.</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>File size in bytes.</summary>
    public long Size { get; set; }
}
