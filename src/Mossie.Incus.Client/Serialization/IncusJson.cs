using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mossie.Incus.Client.Serialization;

/// <summary>
/// Central <see cref="JsonSerializerOptions"/> used for all Incus payloads.
/// </summary>
public static class IncusJson
{
    /// <summary>Default options used when serializing and deserializing API payloads.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Creates a fresh set of options, allowing callers to customize them.</summary>
    public static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        options.Converters.Add(new Rfc3339DateTimeOffsetConverter());
        options.Converters.Add(new NullableRfc3339DateTimeOffsetConverter());

        return options;
    }

    /// <summary>Serializes a value to a JSON string.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Deserializes a JSON string.</summary>
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
