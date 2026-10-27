using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Reads the dates of the 3Cat catalog, which arrive in two shapes: the catalog feed publishes a
/// plain string («15/06/2026 08:00:00») and the media endpoint publishes an object with the same text
/// plus the ISO value with the offset («2027-06-07T23:59:59+02:00»).
/// </summary>
public sealed class ThreeCatDateConverter : JsonConverter<DateTimeOffset?>
{
    private static readonly string[] Formats =
    [
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy HH:mm",
        "dd/MM/yyyy",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd"
    ];

    /// <inheritdoc />
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null or JsonTokenType.False:
                return null;

            case JsonTokenType.String:
                return Parse(reader.GetString());

            case JsonTokenType.StartObject:
                using (var document = JsonDocument.ParseValue(ref reader))
                {
                    var root = document.RootElement;

                    if (root.TryGetProperty("utc", out var utc) && utc.ValueKind == JsonValueKind.String)
                    {
                        return Parse(utc.GetString());
                    }

                    if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        return Parse(text.GetString());
                    }
                }

                return null;

            default:
                reader.Skip();
                return null;
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value.ToString("o", CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    /// <summary>
    /// Parses the date formats published by 3Cat.
    /// </summary>
    /// <param name="value">Text to parse.</param>
    /// <returns>The date, or <c>null</c> when the text is not a date.</returns>
    internal static DateTimeOffset? Parse(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (DateTimeOffset.TryParseExact(
                text,
                Formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var parsed))
        {
            return parsed;
        }

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out parsed)
            ? parsed
            : null;
    }
}
