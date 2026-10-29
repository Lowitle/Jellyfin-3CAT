using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Resolves the season and the episode of a video from the numbering 3Cat writes in the title
/// («T22xC6 - Visita als inferns de l'amor»), which is the one its website shows.
/// </summary>
/// <remarks>
/// The catalog also publishes <c>capitol</c> (a counter that runs over the whole program) and
/// <c>capitol_temporada</c> (an index that restarts in every published batch). Neither matches the
/// title nor TVDB, so both are ignored on purpose: the title is the only numbering 3Cat keeps
/// consistent with its own catalog.
/// </remarks>
public static partial class EpisodeNumbering
{
    /// <summary>
    /// Season of the videos 3Cat publishes without a season and an episode number. Jellyfin shows it
    /// as «Especiales».
    /// </summary>
    public const int SpecialsSeason = 0;

    /// <summary>
    /// Season used when a program numbers none of its videos, where <see cref="SpecialsSeason"/>
    /// would turn the whole series into specials.
    /// </summary>
    public const int DefaultSeason = 1;

    /// <summary>
    /// Reads the numbering of a title.
    /// </summary>
    /// <param name="title">Video title, for instance «T22xC6 - Visita als inferns de l'amor».</param>
    /// <returns>The season and the episode, or <c>null</c> when the title announces none.</returns>
    public static (int Season, int Episode)? Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var match = NumberedRegex().Match(title);
        if (!match.Success
            || !int.TryParse(match.Groups["season"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var season)
            || !int.TryParse(match.Groups["episode"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var episode))
        {
            return null;
        }

        return (season, episode);
    }

    /// <summary>
    /// Removes the numbering from a title, because the file name already carries it as
    /// <c>SxxEyy</c> («T22xC6 - Visita als inferns» → «Visita als inferns»).
    /// </summary>
    /// <param name="title">Video title.</param>
    /// <returns>The title without the numbering, or the title itself when it has none.</returns>
    public static string StripNumber(string title)
    {
        var match = NumberedRegex().Match(title);
        if (!match.Success)
        {
            return title;
        }

        var rest = title[match.Length..].TrimStart(' ', '-', ':', '|', '.', ',', '–', '—');
        return rest.Length > 0 ? rest : title;
    }

    /// <summary>
    /// Puts the air date first in the title of a video that belongs to the season of specials, so
    /// Jellyfin lists the specials in the order they were aired.
    /// </summary>
    /// <param name="title">Episode title.</param>
    /// <param name="aired">Air date of the video.</param>
    /// <returns>«2026-10-27 - Especial confinament».</returns>
    public static string WithAirDate(string title, DateTimeOffset? aired)
    {
        if (aired is null)
        {
            return title;
        }

        var day = aired.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (string.IsNullOrWhiteSpace(title))
        {
            return day;
        }

        return LeadingDateRegex().IsMatch(title) ? title : day + " - " + title;
    }


    /// <summary>
    /// Assigns the season and the episode of every video of a program.
    /// </summary>
    /// <param name="titles">Titles of the videos, oldest first.</param>
    /// <returns>One slot per title, in the same order.</returns>
    /// <remarks>
    /// <para>
    /// A title without numbering goes to <see cref="SpecialsSeason"/>, numbered in air order, unless
    /// the program numbers none of its videos: that is a daily magazine, and it keeps
    /// <see cref="DefaultSeason"/> with the air order instead of moving the whole series to
    /// «Especiales».
    /// </para>
    /// <para>
    /// 3Cat publishes some chapters twice or repeats the numbering of an emission in a recap. The
    /// video announced twice with the same title is dropped (<see cref="EpisodeSlot.IsDuplicate"/>)
    /// and a different video goes to the specials, so Jellyfin never sees two files of the same
    /// episode.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<EpisodeSlot> Assign(IReadOnlyList<string?> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);

        var slots = new EpisodeSlot[titles.Count];
        var owners = new Dictionary<(int Season, int Episode), string>();
        var pending = new List<int>();
        var duplicates = new bool[titles.Count];
        var numbered = false;

        for (var index = 0; index < titles.Count; index++)
        {
            if (Parse(titles[index]) is not { } numbering)
            {
                pending.Add(index);
                continue;
            }

            numbered = true;

            var key = (numbering.Season, numbering.Episode);
            var title = Normalize(titles[index]);

            if (!owners.TryGetValue(key, out var owner))
            {
                owners[key] = title;
                slots[index] = new EpisodeSlot(numbering.Season, numbering.Episode, false);
                continue;
            }

            if (string.Equals(owner, title, StringComparison.Ordinal))
            {
                duplicates[index] = true;
            }
            else
            {
                pending.Add(index);
            }
        }

        var season = numbered ? SpecialsSeason : DefaultSeason;
        for (var index = 0; index < pending.Count; index++)
        {
            slots[pending[index]] = new EpisodeSlot(season, index + 1, false);
        }

        for (var index = 0; index < duplicates.Length; index++)
        {
            if (duplicates[index])
            {
                slots[index] = slots[index] with { IsDuplicate = true };
            }
        }

        return slots;
    }

    private static string Normalize(string? title)
        => string.IsNullOrWhiteSpace(title)
            ? string.Empty
            : WhitespaceRegex().Replace(title.Trim(), " ").ToUpperInvariant();

    // The numbering always opens the title; it is written «T22xC6», «T 22 x C 6» and, in a handful of
    // old videos, «T22C6».
    [GeneratedRegex(
        @"^\s*T\s*(?<season>\d{1,3})\s*x?\s*C\s*(?<episode>\d{1,5})(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumberedRegex();

    [GeneratedRegex(@"^\s*\d{1,4}[-/.]\d{1,2}[-/.]\d{1,4}", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingDateRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}

/// <summary>
/// Where the video of a program is written.
/// </summary>
/// <param name="Season">Season number; <see cref="EpisodeNumbering.SpecialsSeason"/> for specials.</param>
/// <param name="Episode">Episode number inside its season.</param>
/// <param name="IsDuplicate">
/// <c>true</c> when 3Cat publishes the same video twice and it must not be written again.
/// </param>
public readonly record struct EpisodeSlot(int Season, int Episode, bool IsDuplicate);
