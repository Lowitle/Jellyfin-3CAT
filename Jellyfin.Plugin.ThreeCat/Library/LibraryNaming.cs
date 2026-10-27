using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// Builds file and folder names that are safe on every platform Jellyfin can run on.
/// </summary>
public static class LibraryNaming
{
    private static readonly char[] Forbidden =
    [
        '<', '>', ':', '"', '/', '\\', '|', '?', '*'
    ];

    /// <summary>
    /// Sanitizes a value so it can be used as a single file or folder name.
    /// </summary>
    /// <param name="value">Original value.</param>
    /// <param name="maxLength">Maximum length.</param>
    /// <param name="fallback">Value returned when nothing usable remains.</param>
    /// <returns>The sanitized value.</returns>
    public static string Sanitize(string? value, int maxLength, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormC))
        {
            if (char.IsControl(character) || Array.IndexOf(Forbidden, character) >= 0)
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(character);
            }
        }

        var collapsed = CollapseWhitespace(builder.ToString()).Trim(' ', '.', '-', '_');

        if (collapsed.Length == 0)
        {
            return fallback;
        }

        if (collapsed.Length > maxLength)
        {
            collapsed = collapsed[..maxLength].Trim(' ', '.', '-', '_');
        }

        return collapsed.Length == 0 ? fallback : collapsed;
    }

    /// <summary>
    /// Gets the folder name of a series.
    /// </summary>
    /// <param name="seriesTitle">Series title.</param>
    /// <returns>The folder name.</returns>
    public static string SeriesFolderName(string seriesTitle)
        => Sanitize(seriesTitle, 100, "Serie");

    /// <summary>
    /// Gets the base file name of an episode: <c>Serie - S01E02 - Título</c>.
    /// </summary>
    /// <param name="seriesTitle">Series title.</param>
    /// <param name="season">Season number.</param>
    /// <param name="episode">Episode number.</param>
    /// <param name="title">Episode title.</param>
    /// <returns>The base file name (without extension).</returns>
    public static string EpisodeFileName(string seriesTitle, int season, int episode, string? title)
    {
        var code = string.Format(CultureInfo.InvariantCulture, "S{0:00}E{1:00}", season, episode);
        var suffix = Sanitize(title, 120, string.Empty);
        var name = suffix.Length == 0
            ? $"{Sanitize(seriesTitle, 100, "Serie")} - {code}"
            : $"{Sanitize(seriesTitle, 100, "Serie")} - {code} - {suffix}";

        return Sanitize(name, 160, code);
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var character in value)
        {
            var isSpace = char.IsWhiteSpace(character);
            if (isSpace)
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }
            }
            else
            {
                builder.Append(character);
            }

            lastWasSpace = isSpace;
        }

        return builder.ToString();
    }
}
