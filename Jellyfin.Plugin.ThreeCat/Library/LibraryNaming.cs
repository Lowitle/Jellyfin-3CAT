using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// Builds file and folder names that are safe on every platform Jellyfin can run on.
/// </summary>
public static partial class LibraryNaming
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
    /// Removes from the title of a program the format 3Cat appends to the programs it publishes whole.
    /// </summary>
    /// <param name="title">Title of the program («Crackòvia - Programa sencer», «Polònia – Programa sencer»).</param>
    /// <returns>
    /// The name of the program («Crackòvia»), or the title itself when it announces no whole-program format.
    /// </returns>
    /// <remarks>
    /// The suffix names the shape of the video, not the program: the series of the library is the program, so
    /// «Crackòvia - Programa sencer» is the same show as «Crackòvia». A title that carries no separator before
    /// the suffix («Programa sencer», «El programa sencer») is a program of its own and is kept whole, and so
    /// is a title that is nothing but the suffix.
    /// </remarks>
    public static string StripWholeProgramSuffix(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title ?? string.Empty;
        }

        var match = WholeProgramSuffixRegex().Match(title.Trim());
        if (!match.Success)
        {
            return title;
        }

        var name = match.Groups["name"].Value.TrimEnd(' ', '-', '–', '—', '|', ':', ',', '.');
        return name.Length == 0 ? title : name;
    }

    /// <summary>
    /// Gets the base file name of an episode: <c>Serie - S01E02 - Título</c>.
    /// </summary>
    /// <param name="seriesTitle">Series title.</param>
    /// <param name="season">Season number.</param>
    /// <param name="episode">Episode number.</param>
    /// <param name="title">Episode title.</param>
    /// <param name="part">
    /// Part of the episode, or 0 when the file is the whole episode. Jellyfin groups the files whose names end in
    /// <c>- part1</c>, <c>- part2</c> as the parts of one episode and plays them in order, which is what a video of
    /// 3Cat is when the provider lists the episode once.
    /// </param>
    /// <returns>The base file name (without extension).</returns>
    public static string EpisodeFileName(string seriesTitle, int season, int episode, string? title, int part = 0)
    {
        var code = string.Format(CultureInfo.InvariantCulture, "S{0:00}E{1:00}", season, episode);
        var suffix = Sanitize(title, 120, string.Empty);
        var name = suffix.Length == 0
            ? $"{Sanitize(seriesTitle, 100, "Serie")} - {code}"
            : $"{Sanitize(seriesTitle, 100, "Serie")} - {code} - {suffix}";

        if (part > 0)
        {
            name = $"{name} - part{part.ToString(CultureInfo.InvariantCulture)}";
        }

        return Sanitize(name, 160, code);
    }

    /// <summary>
    /// Gets the folder name of a film: <c>Título (2020)</c>, the name Jellyfin reads the year from.
    /// </summary>
    /// <param name="title">Film title.</param>
    /// <param name="year">Year of the film, or <c>null</c> when 3Cat publishes no air date.</param>
    /// <returns>The folder name.</returns>
    public static string MovieFolderName(string title, int? year)
    {
        var name = Sanitize(title, 100, "Pelicula");

        return year is > 0
            ? Sanitize(string.Format(CultureInfo.InvariantCulture, "{0} ({1})", name, year.Value), 120, name)
            : name;
    }

    /// <summary>
    /// Gets the base file name of a film: the name of its folder, plus <c>- part2</c> when 3Cat publishes
    /// the film in several videos.
    /// </summary>
    /// <param name="title">Film title.</param>
    /// <param name="year">Year of the film, or <c>null</c> when 3Cat publishes no air date.</param>
    /// <param name="part">Part of the film, or 0 when the file is the whole film.</param>
    /// <returns>The base file name (without extension).</returns>
    public static string MovieFileName(string title, int? year, int part = 0)
    {
        var name = MovieFolderName(title, year);

        if (part > 0)
        {
            name = string.Format(
                CultureInfo.InvariantCulture,
                "{0} - part{1}",
                name,
                part.ToString(CultureInfo.InvariantCulture));
        }

        return Sanitize(name, 180, MovieFolderName(title, year));
    }

    /// <summary>
    /// Keeps the first subtitle of every language so two 3Cat tracks of the same language never write the
    /// same file twice, and normalizes the language to the ISO code Jellyfin reads from the file name.
    /// </summary>
    /// <param name="subtitles">Subtitles of an episode or a film.</param>
    /// <returns>The subtitles to write, one per language.</returns>
    internal static IEnumerable<LibrarySubtitle> CleanSubtitles(IReadOnlyList<LibrarySubtitle>? subtitles)
    {
        if (subtitles is null)
        {
            yield break;
        }

        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var subtitle in subtitles)
        {
            if (string.IsNullOrWhiteSpace(subtitle.Srt))
            {
                continue;
            }

            var language = string.IsNullOrWhiteSpace(subtitle.Language) ? "ca" : subtitle.Language.Trim().ToLowerInvariant();

            // The language ends up in a file name: keep only ASCII letters and digits.
            language = new string(language.Where(char.IsAsciiLetterOrDigit).ToArray());
            if (language.Length == 0)
            {
                // 3Cat publishes practically everything in Catalan, so it is the least wrong guess.
                language = "ca";
            }

            if (languages.Add(language))
            {
                yield return subtitle with { Language = language };
            }
        }
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

    /// <summary>
    /// Matches a title that ends with the whole-program format of 3Cat: the name, a separator —«-», «–», «—»,
    /// «|» or «:»— or an opening parenthesis, and the format itself, optionally closed by a parenthesis.
    /// </summary>
    /// <remarks>
    /// The separator is what tells «Crackòvia - Programa sencer» —a program published whole— from a program
    /// called «Programa sencer» or «El programa sencer», which carry no separator and are kept whole.
    /// </remarks>
    [GeneratedRegex(
        @"^(?<name>.+?)\s*(?:[-–—|:]\s*|\(\s*)programa\s+sencer\s*\)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WholeProgramSuffixRegex();
}
