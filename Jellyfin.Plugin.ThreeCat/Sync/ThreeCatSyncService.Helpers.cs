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
    /// Normalizes a subtitle language tag published by 3Cat.
    /// </summary>
    /// <param name="value">Language tag.</param>
    /// <returns>The lower case tag, or <c>null</c> when 3Cat publishes none.</returns>
    internal static string? NormalizeLanguage(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

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
