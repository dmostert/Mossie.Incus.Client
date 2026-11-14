using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mossie.Incus.Client.Serialization;

/// <summary>
/// Serializes <see cref="DateTimeOffset"/> exactly like Go's <c>time.Time</c> (RFC 3339 nano),
/// truncating to the 100ns tick resolution available on .NET.
/// </summary>
public sealed class Rfc3339DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        string? value = reader.GetString();
        if (string.IsNullOrEmpty(value))
            return default;

        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        DateTimeOffset utc = value.ToUniversalTime();
        string text = utc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

        long fraction = utc.Ticks % TimeSpan.TicksPerSecond;
        if (fraction != 0)
            text += "." + fraction.ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0');

        writer.WriteStringValue(text + "Z");
    }
}

/// <summary>Converts nullable <see cref="DateTimeOffset"/> values as RFC 3339.</summary>
public sealed class NullableRfc3339DateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    private static readonly Rfc3339DateTimeOffsetConverter Inner = new();

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        return Inner.Read(ref reader, typeof(DateTimeOffset), options);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            Inner.Write(writer, value.Value, options);
    }
}
