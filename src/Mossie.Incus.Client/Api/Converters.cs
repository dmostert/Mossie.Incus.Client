using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mossie.Incus.Client.Api;

/// <summary>
/// Converts <see cref="ConfigMap"/> values to and from JSON. Mirrors Go's
/// <c>ConfigMap.UnmarshalJSON</c>: numbers, booleans and <c>null</c> are converted to strings.
/// </summary>
public sealed class ConfigMapConverter : JsonConverter<ConfigMap>
{
    public override ConfigMap? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("JSON data not valid for ConfigMap: expected an object.");

        ConfigMap result = new();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return result;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("JSON data not valid for ConfigMap.");

            string key = reader.GetString()!;
            reader.Read();
            result[key] = ReadScalarAsString(ref reader, "ConfigMap");
        }

        throw new JsonException("JSON data not valid for ConfigMap: unexpected end of data.");
    }

    public override void Write(Utf8JsonWriter writer, ConfigMap value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (KeyValuePair<string, string> item in value)
        {
            writer.WriteString(item.Key, item.Value);
        }

        writer.WriteEndObject();
    }

    internal static string ReadScalarAsString(ref Utf8JsonReader reader, string typeName)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString()!;

            case JsonTokenType.True:
                return "true";

            case JsonTokenType.False:
                return "false";

            case JsonTokenType.Null:
                return "";

            case JsonTokenType.Number:
                if (reader.TryGetInt64(out long integer))
                    return integer.ToString(CultureInfo.InvariantCulture);

                double number = reader.GetDouble();
                if (number == Math.Truncate(number) && number >= long.MinValue && number <= long.MaxValue)
                    return ((long)number).ToString(CultureInfo.InvariantCulture);

                return number.ToString("R", CultureInfo.InvariantCulture);

            default:
                throw new JsonException($"Type {reader.TokenType} is not supported in {typeName}.");
        }
    }
}

/// <summary>
/// Converts <see cref="DevicesMap"/> values to and from JSON. Mirrors Go's
/// <c>DevicesMap.UnmarshalJSON</c>: numbers, booleans and <c>null</c> are converted to strings.
/// </summary>
public sealed class DevicesMapConverter : JsonConverter<DevicesMap>
{
    public override DevicesMap? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("JSON data not valid for DevicesMap: expected an object.");

        DevicesMap result = new();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return result;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("JSON data not valid for DevicesMap.");

            string key = reader.GetString()!;
            reader.Read();

            if (reader.TokenType == JsonTokenType.Null)
            {
                result[key] = new Dictionary<string, string>();
                continue;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Inner type for \"{key}\" is not supported in DevicesMap.");

            Dictionary<string, string> device = new();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException("JSON data not valid for DevicesMap.");

                string innerKey = reader.GetString()!;
                reader.Read();
                device[innerKey] = ConfigMapConverter.ReadScalarAsString(ref reader, "DevicesMap");
            }

            result[key] = device;
        }

        throw new JsonException("JSON data not valid for DevicesMap: unexpected end of data.");
    }

    public override void Write(Utf8JsonWriter writer, DevicesMap value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (KeyValuePair<string, Dictionary<string, string>> device in value)
        {
            writer.WritePropertyName(device.Key);
            writer.WriteStartObject();

            foreach (KeyValuePair<string, string> item in device.Value)
            {
                writer.WriteString(item.Key, item.Value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }
}
