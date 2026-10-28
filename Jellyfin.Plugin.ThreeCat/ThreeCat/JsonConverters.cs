using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Reads a JSON member that 3Cat serializes as a bare object when it holds a single value and as an
/// array when it holds several (<c>programes_tv</c>, <c>bbands</c>, <c>subtitols</c>, <c>imatges</c>...),
/// so the models do not have to care which of the two shapes arrived.
/// </summary>
/// <typeparam name="T">Type of the items.</typeparam>
public sealed class SingleOrArrayConverter<T> : JsonConverter<List<T>>
{
    /// <inheritdoc />
    public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.StartArray:
                var list = new List<T>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var item = JsonSerializer.Deserialize<T>(ref reader, options);
                    if (item is not null)
                    {
                        list.Add(item);
                    }
                }

                return list;

            case JsonTokenType.StartObject:
                var single = JsonSerializer.Deserialize<T>(ref reader, options);
                return single is null ? null : [single];

            default:
                reader.Skip();
                return null;
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, List<T>? value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options);
}

/// <summary>
/// Reads an integer that 3Cat sometimes serializes as a number, as a string or as an empty value.
/// </summary>
public sealed class FlexibleInt32Converter : JsonConverter<int?>
{
    /// <inheritdoc />
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = ReadInt64(ref reader);
        return value.HasValue ? (int)value.Value : null;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    internal static long? ReadInt64(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null or JsonTokenType.False:
                return null;

            case JsonTokenType.Number:
                return reader.TryGetInt64(out var number) ? number : (long)reader.GetDouble();

            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }

                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)
                    ? (long)real
                    : null;

            default:
                reader.Skip();
                return null;
        }
    }
}

/// <summary>
/// Reads a long that 3Cat sometimes serializes as a number, as a string or as an empty value.
/// </summary>
public sealed class FlexibleInt64Converter : JsonConverter<long?>
{
    /// <inheritdoc />
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => FlexibleInt32Converter.ReadInt64(ref reader);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>
/// Reads a text field that 3Cat sometimes serializes as a number (the <c>id</c> of a program or of a
/// video arrives as a number in some feeds and as a string in others) and treats empty strings as
/// <c>null</c>.
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    /// <inheritdoc />
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null or JsonTokenType.False:
                return null;

            case JsonTokenType.True:
                return "true";

            case JsonTokenType.String:
                var text = reader.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;

            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var integer))
                {
                    return integer.ToString(CultureInfo.InvariantCulture);
                }

                return reader.TryGetDecimal(out var real)
                    ? real.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture);

            default:
                reader.Skip();
                return null;
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}

/// <summary>
/// Reads the <c>targets</c> member of a video. The catalog of <c>api.3cat.cat/videos</c> publishes it as a
/// bare array of labelled values (<c>[ { "id": "PUT_INF", "desc": "Infantil" } ]</c>), and some feeds wrap
/// it in an object with a single key (<c>{ "target": [ ... ] }</c>), so both shapes fill the same model.
/// </summary>
public sealed class ThreeCatTargetsConverter : JsonConverter<ThreeCatTargets>
{
    /// <inheritdoc />
    public override ThreeCatTargets? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var targets = LabelledListReader.Read(ref reader, "target", options);

        return targets is null ? null : new ThreeCatTargets { Targets = targets };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ThreeCatTargets value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value.Targets, options);
}

/// <summary>
/// Reads the <c>canals</c> member of a video, published as a bare array of labelled values
/// (<c>[ { "id": "PUC_S3", "desc": "Super3" } ]</c>) and, in some feeds, as
/// <c>{ "canal": [ ... ] }</c>.
/// </summary>
public sealed class ThreeCatVideoChannelsConverter : JsonConverter<ThreeCatVideoChannels>
{
    /// <inheritdoc />
    public override ThreeCatVideoChannels? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var channels = LabelledListReader.Read(ref reader, "canal", options);

        return channels is null ? null : new ThreeCatVideoChannels { Channels = channels };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ThreeCatVideoChannels value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value.Channels, options);
}

/// <summary>
/// Reads a list of labelled values that 3Cat publishes as a bare array, as a single object or as an
/// object wrapped around one key (<c>{ "target": [ ... ] }</c>).
/// </summary>
internal static class LabelledListReader
{
    /// <summary>
    /// Reads the list from the current token of the reader.
    /// </summary>
    /// <param name="reader">Reader placed at the value.</param>
    /// <param name="key">Key of the wrapper object, if the value arrives wrapped.</param>
    /// <param name="options">Serializer options.</param>
    /// <returns>The list, or <c>null</c> when the value holds none.</returns>
    public static List<ThreeCatLabel>? Read(ref Utf8JsonReader reader, string key, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.StartArray:
                var list = new List<ThreeCatLabel>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var label = JsonSerializer.Deserialize<ThreeCatLabel>(ref reader, options);
                    if (label is not null)
                    {
                        list.Add(label);
                    }
                }

                return list;

            case JsonTokenType.StartObject:
                using (var document = JsonDocument.ParseValue(ref reader))
                {
                    var root = document.RootElement;
                    return Read(root.TryGetProperty(key, out var wrapped) ? wrapped : root, options);
                }

            default:
                reader.Skip();
                return null;
        }
    }

    private static List<ThreeCatLabel>? Read(JsonElement element, JsonSerializerOptions options)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                var list = new List<ThreeCatLabel>();
                foreach (var item in element.EnumerateArray())
                {
                    var label = item.Deserialize<ThreeCatLabel>(options);
                    if (label is not null)
                    {
                        list.Add(label);
                    }
                }

                return list;

            case JsonValueKind.Object:
                var single = element.Deserialize<ThreeCatLabel>(options);
                return single is null ? null : [single];

            default:
                return null;
        }
    }
}

