namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Small text helpers shared by the synchronization steps.
/// </summary>
public sealed partial class ThreeCatSyncService
{
    private static string FirstNonEmpty(params string?[] values)
        => FirstNonEmptyOrNull(values) ?? string.Empty;

    private static string? FirstNonEmptyOrNull(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    /// <summary>
    /// Removes the repeated series name from an episode title
    /// («Doraemon, el gat còsmic - El gat còsmic» → «El gat còsmic»).
    /// </summary>
    /// <param name="title">Episode title.</param>
    /// <param name="seriesTitle">Series title.</param>
    /// <returns>The title without the series name.</returns>
    internal static string StripSeriesPrefix(string title, string seriesTitle)
    {
        if (seriesTitle.Length > 0
            && title.Length > seriesTitle.Length
            && title.StartsWith(seriesTitle, StringComparison.OrdinalIgnoreCase))
        {
            var rest = title[seriesTitle.Length..].TrimStart(' ', '-', ':', '|', '–', '—');
            if (rest.Length > 0)
            {
                return rest;
            }
        }

        return title;
    }

    /// <summary>
    /// Normalizes a subtitle language configuration value.
    /// </summary>
    /// <param name="value">Configured value.</param>
    /// <returns>An ISO language code, or <c>null</c> when subtitles are disabled.</returns>
    internal static string? NormalizeLanguage(string? value)
    {
        var language = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(language))
        {
            return null;
        }

        return language switch
        {
            "none" or "ninguno" or "ningú" or "disabled" => null,
            _ => language
        };
    }

    /// <summary>
    /// Normalizes the list of subtitle languages selected in the configuration.
    /// </summary>
    /// <param name="configured">Languages selected with the check boxes.</param>
    /// <returns>The distinct ISO codes to download, in the configured order.</returns>
    internal static IReadOnlyList<string> NormalizeLanguages(IEnumerable<string>? configured)
    {
        var languages = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in configured ?? [])
        {
            var language = NormalizeLanguage(value);
            if (language is not null && seen.Add(language))
            {
                languages.Add(language);
            }
        }

        return languages;
    }

    /// <summary>
    /// Ranks the size tag 3Cat publishes («MASTER», «1014x570», «288x422»...) to pick the biggest image.
    /// </summary>
    /// <param name="size">Size tag.</param>
    /// <returns>A comparable rank; bigger is better.</returns>
    internal static long SizeRank(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
        {
            return 0;
        }

        if (size.Equals("MASTER", StringComparison.OrdinalIgnoreCase))
        {
            return long.MaxValue;
        }

        var parts = size.Split('x');
        if (parts.Length == 2
            && long.TryParse(parts[0], out var width)
            && long.TryParse(parts[1], out var height))
        {
            return width * height;
        }

        return 0;
    }
}
