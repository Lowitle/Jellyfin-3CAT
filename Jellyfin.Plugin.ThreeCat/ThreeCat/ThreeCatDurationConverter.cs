using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Reads a duration of the 3Cat feed. The catalog publishes <c>"00:24:07:00"</c> (hours, minutes,
/// seconds and frames, which the 3Cat documentation itself marks as unreliable) and the media
/// endpoint publishes <c>{ "text": "00:24:07", "milisegons": 1447000 }</c>.
/// </summary>
public sealed class ThreeCatDurationConverter : JsonConverter<TimeSpan?>
{
    /// <inheritdoc />
    public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null or JsonTokenType.False:
                return null;

            case JsonTokenType.Number:
                return reader.TryGetInt64(out var milliseconds) && milliseconds > 0
                    ? TimeSpan.FromMilliseconds(milliseconds)
                    : null;

            case JsonTokenType.String:
                return Parse(reader.GetString());

            case JsonTokenType.StartObject:
                using (var document = JsonDocument.ParseValue(ref reader))
                {
                    var root = document.RootElement;

                    if (root.TryGetProperty("milisegons", out var millis))
                    {
                        var value = millis.ValueKind == JsonValueKind.Number
                            ? (millis.TryGetInt64(out var number) ? number : null)
                            : long.TryParse(
                                millis.GetString(),
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out number)
                                ? number
                                : (long?)null;

                        if (value is > 0)
                        {
                            return TimeSpan.FromMilliseconds(value.Value);
                        }
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
    public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(value.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    /// <summary>
    /// Parses «hh:mm:ss:ff», «hh:mm:ss» or «mm:ss».
    /// </summary>
    /// <param name="value">Text to parse.</param>
    /// <returns>The duration, or <c>null</c> when the text is not a time.</returns>
    internal static TimeSpan? Parse(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var parts = text.Split(':');
        if (parts.Length is < 2 or > 4 || !parts.All(IsDigits))
        {
            return null;
        }

        var numbers = parts.Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray();

        var (hours, minutes, seconds) = numbers.Length switch
        {
            2 => (0, numbers[0], numbers[1]),
            _ => (numbers[0], numbers[1], numbers[2])
        };

        var duration = new TimeSpan(hours, minutes, seconds);
        return duration > TimeSpan.Zero ? duration : null;
    }

    private static bool IsDigits(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
