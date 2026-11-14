using System.Text.Json;
using Mossie.Incus.Client.Serialization;

namespace Mossie.Incus.Client.Tests;

/// <summary>Tests for RFC 3339 date handling.</summary>
public sealed class SerializationTests
{
    [Fact]
    public void Serialize_WritesUtcRfc3339WithoutFractionWhenWholeSeconds()
    {
        DateTimeOffset value = new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

        string json = JsonSerializer.Serialize(value, IncusJson.Options);

        Assert.Equal("\"2024-01-02T03:04:05Z\"", json);
    }

    [Fact]
    public void Serialize_ConvertsOffsetsToUtc()
    {
        DateTimeOffset value = new(2024, 1, 2, 5, 4, 5, TimeSpan.FromHours(2));

        string json = JsonSerializer.Serialize(value, IncusJson.Options);

        Assert.Equal("\"2024-01-02T03:04:05Z\"", json);
    }

    [Fact]
    public void Serialize_TrimsTrailingZerosFromFraction()
    {
        DateTimeOffset value = new(2024, 1, 2, 3, 4, 5, 500, TimeSpan.Zero);

        string json = JsonSerializer.Serialize(value, IncusJson.Options);

        Assert.Equal("\"2024-01-02T03:04:05.5Z\"", json);
    }

    [Fact]
    public void Deserialize_TruncatesNanosecondsToTicks()
    {
        DateTimeOffset parsed = IncusJson.Deserialize<DateTimeOffset>("\"2024-01-02T03:04:05.123456789Z\"");

        DateTimeOffset whole = new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        Assert.Equal(whole.UtcTicks + 1_234_568, parsed.UtcTicks);
    }

    [Fact]
    public void Deserialize_NullableHandlesNull()
    {
        Assert.Null(IncusJson.Deserialize<DateTimeOffset?>("null"));

        string json = JsonSerializer.Serialize<DateTimeOffset?>(null, IncusJson.Options);
        Assert.Equal("null", json);
    }
}
