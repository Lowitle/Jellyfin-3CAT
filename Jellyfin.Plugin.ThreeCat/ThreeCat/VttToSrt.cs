using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Converts WebVTT subtitles (as published by 3Cat) to SubRip (SRT), which Jellyfin reads for external subtitles.
/// </summary>
public static partial class VttToSrt
{
    [GeneratedRegex(
        @"^(?<start>(?:\d{1,}:)?\d{2}:\d{2}\.\d{1,3})\s*-->\s*(?<end>(?:\d{1,}:)?\d{2}:\d{2}\.\d{1,3})",
        RegexOptions.CultureInvariant)]
    private static partial Regex TimingRegex();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex SpacesRegex();

    /// <summary>
    /// Converts a WebVTT document into an SRT document.
    /// </summary>
    /// <param name="vtt">WebVTT content.</param>
    /// <returns>SRT content (empty when the document has no cues).</returns>
    public static string Convert(string vtt)
    {
        ArgumentNullException.ThrowIfNull(vtt);

        var cues = Parse(vtt);
        if (cues.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < cues.Count; i++)
        {
            builder.Append(i + 1).Append('\n');
            builder.Append(cues[i].Start).Append(" --> ").Append(cues[i].End).Append('\n');
            builder.Append(cues[i].Text).Append('\n').Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes the WebVTT content as SRT in the given file (UTF-8 without BOM, CRLF line endings).
    /// </summary>
    /// <param name="vtt">WebVTT content.</param>
    /// <param name="destinationPath">Destination file.</param>
    /// <returns><c>true</c> when the file was written; <c>false</c> when there was nothing to write.</returns>
    public static bool ConvertToFile(string vtt, string destinationPath)
    {
        var srt = Convert(vtt);
        if (srt.Length == 0)
        {
            return false;
        }

        var content = srt.Replace("\n", "\r\n", StringComparison.Ordinal);
        File.WriteAllText(destinationPath, content, new UTF8Encoding(false));
        return true;
    }

    private static List<(string Start, string End, string Text)> Parse(string vtt)
    {
        var cues = new List<(string Start, string End, string Text)>();
        var lines = vtt.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i].Trim();
            var match = TimingRegex().Match(line);
            if (!match.Success)
            {
                i++;
                continue;
            }

            if (!TryFormatTimestamp(match.Groups["start"].Value, out var start)
                || !TryFormatTimestamp(match.Groups["end"].Value, out var end))
            {
                i++;
                continue;
            }

            i++;

            // Collect the cue payload until a blank line or the next timing line.
            var textLines = new List<string>();
            while (i < lines.Length)
            {
                var candidate = lines[i];
                if (string.IsNullOrWhiteSpace(candidate) || TimingRegex().IsMatch(candidate.Trim()))
                {
                    break;
                }

                textLines.Add(CleanText(candidate));
                i++;
            }

            while (textLines.Count > 0 && textLines[0].Length == 0)
            {
                textLines.RemoveAt(0);
            }

            while (textLines.Count > 0 && textLines[^1].Length == 0)
            {
                textLines.RemoveAt(textLines.Count - 1);
            }

            if (textLines.Count > 0)
            {
                cues.Add((start, end, string.Join('\n', textLines)));
            }
        }

        return cues;
    }

    private static string CleanText(string value)
    {
        // Drop inline cue timestamps (<00:00:01.000>) and styling tags (<c>, <i>, <b>, <v Name>...).
        var withoutTags = TagRegex().Replace(value, string.Empty);
        var decoded = WebUtility.HtmlDecode(withoutTags).Replace('\u200b', ' ');
        return SpacesRegex().Replace(decoded, " ").Trim();
    }

    private static bool TryFormatTimestamp(string value, out string formatted)
    {
        formatted = string.Empty;

        var parts = value.Split(':');
        string hours;
        string minutes;
        string secondsWithMillis;

        if (parts.Length == 3)
        {
            hours = parts[0];
            minutes = parts[1];
            secondsWithMillis = parts[2];
        }
        else if (parts.Length == 2)
        {
            hours = "00";
            minutes = parts[0];
            secondsWithMillis = parts[1];
        }
        else
        {
            return false;
        }

        var dot = secondsWithMillis.IndexOf('.');
        if (dot < 0)
        {
            return false;
        }

        var seconds = secondsWithMillis[..dot];
        var millis = secondsWithMillis[(dot + 1)..];

        if (!IsDigits(hours) || !IsDigits(minutes) || !IsDigits(seconds) || !IsDigits(millis))
        {
            return false;
        }

        millis = millis.PadRight(3, '0')[..3];
        formatted = string.Format(
            CultureInfo.InvariantCulture,
            "{0:00}:{1:00}:{2:00},{3}",
            int.Parse(hours, CultureInfo.InvariantCulture),
            int.Parse(minutes, CultureInfo.InvariantCulture),
            int.Parse(seconds, CultureInfo.InvariantCulture),
            millis);

        return true;
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
