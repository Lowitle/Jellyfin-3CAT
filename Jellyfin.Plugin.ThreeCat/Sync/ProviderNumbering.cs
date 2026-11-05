using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// A video of 3Cat as the matcher sees it: the title and the air date 3Cat publishes, which are the
/// only evidence available to pair it with an episode of a metadata provider.
/// </summary>
/// <param name="Title">Title published by 3Cat, with its numbering («T22xC6 - Visita als inferns»).</param>
/// <param name="Aired">Air date published by 3Cat, or <c>null</c> when it publishes none.</param>
/// <param name="Id">3Cat video id. It is only used to keep the rows of the report identifiable.</param>
public sealed record ProviderVideo(string? Title, DateTimeOffset? Aired, string? Id)
{
    /// <summary>
    /// Gets the day the video was aired, when 3Cat publishes one.
    /// </summary>
    /// <remarks>
    /// The day is taken as 3Cat writes it (midnight in Spain) instead of converting it to UTC: the videos
    /// aired at midnight would otherwise move to the previous day and stop matching the <c>air_date</c>
    /// of the provider, which is a plain date.
    /// </remarks>
    public DateOnly? AirDate => Aired is { } aired ? DateOnly.FromDateTime(aired.Date) : null;
}

/// <summary>
/// An episode of a metadata provider: the season and the episode number the provider gives it, with the
/// title and the air date it publishes for it.
/// </summary>
/// <param name="Season">Season number of the provider (always 1 or more).</param>
/// <param name="Episode">Episode number inside its season, as the provider numbers it.</param>
/// <param name="Title">Episode title of the provider.</param>
/// <param name="AirDate">Air date of the provider, or <c>null</c> when it publishes none.</param>
public sealed record ProviderEpisode(int Season, int Episode, string? Title, DateOnly? AirDate);

/// <summary>
/// How a video of 3Cat was paired with an episode of the provider.
/// </summary>
public enum ProviderMatch
{
    /// <summary>
    /// No evidence: the video has no place in the numbering of the provider and goes to the season of
    /// specials with its air date in front of the title.
    /// </summary>
    None = 0,

    /// <summary>
    /// The air dates match (the same day, or one day apart for time zones and last minute schedule
    /// changes), which is the strongest evidence there is.
    /// </summary>
    Date = 1,

    /// <summary>
    /// The normalized titles match, by equality or by containment.
    /// </summary>
    Title = 2,

    /// <summary>
    /// The video and the episode fall in the same gap between two already matched pairs and the gap has
    /// the same number of videos and episodes, so the order of the provider is the order of 3Cat.
    /// </summary>
    Order = 3
}

/// <summary>
/// Result of numbering the videos of a program with the episodes of a metadata provider.
/// </summary>
/// <remarks>
/// It keeps the same <see cref="EpisodeSlot"/> list <see cref="EpisodeNumbering.Assign"/> returns, so the
/// synchronization only changes where the list of slots comes from, and it adds the evidence used for
/// every video so the run can be audited — and shown in the configuration page — before a file is written.
/// </remarks>
public sealed class ProviderNumberingResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderNumberingResult"/> class.
    /// </summary>
    /// <param name="slots">Slot of every video, in the order the videos were given.</param>
    /// <param name="methods">Evidence used for every video, in the same order.</param>
    /// <param name="episodes">Episode of the provider paired with every video, or <c>null</c>.</param>
    /// <param name="byDate">Videos paired by air date.</param>
    /// <param name="byTitle">Videos paired by title.</param>
    /// <param name="byOrder">Videos paired by order inside a gap.</param>
    /// <param name="duplicates">Videos dropped because 3Cat publishes them twice.</param>
    /// <param name="useThreeCat">Whether the coverage is below the threshold and 3Cat numbering wins.</param>
    /// <param name="reason">Why 3Cat numbering wins, ready for the log; <c>null</c> when it does not.</param>
    public ProviderNumberingResult(
        IReadOnlyList<EpisodeSlot> slots,
        IReadOnlyList<ProviderMatch> methods,
        IReadOnlyList<ProviderEpisode?> episodes,
        int byDate,
        int byTitle,
        int byOrder,
        int duplicates,
        bool useThreeCat,
        string? reason)
    {
        Slots = slots;
        Methods = methods;
        Episodes = episodes;
        ByDate = byDate;
        ByTitle = byTitle;
        ByOrder = byOrder;
        Duplicates = duplicates;
        UseThreeCat = useThreeCat;
        Reason = reason;

        var specials = 0;
        for (var index = 0; index < methods.Count; index++)
        {
            if (methods[index] == ProviderMatch.None && slots[index] is { IsDuplicate: false })
            {
                specials++;
            }
        }

        Specials = specials;
    }

    /// <summary>
    /// Gets the slot of every video, in the order the videos were given.
    /// </summary>
    public IReadOnlyList<EpisodeSlot> Slots { get; }

    /// <summary>
    /// Gets the evidence used for every video, in the same order as <see cref="Slots"/>.
    /// </summary>
    public IReadOnlyList<ProviderMatch> Methods { get; }

    /// <summary>
    /// Gets the episode of the provider paired with every video, or <c>null</c> when it has none.
    /// </summary>
    public IReadOnlyList<ProviderEpisode?> Episodes { get; }

    /// <summary>
    /// Gets the number of videos paired by air date.
    /// </summary>
    public int ByDate { get; }

    /// <summary>
    /// Gets the number of videos paired by title.
    /// </summary>
    public int ByTitle { get; }

    /// <summary>
    /// Gets the number of videos paired by order inside a gap between matched pairs.
    /// </summary>
    public int ByOrder { get; }

    /// <summary>
    /// Gets the number of videos 3Cat publishes twice, which are dropped as before.
    /// </summary>
    public int Duplicates { get; }

    /// <summary>
    /// Gets the number of videos that end up in the season of specials because there was no evidence.
    /// </summary>
    public int Specials { get; }

    /// <summary>
    /// Gets a value indicating whether the series has to keep the numbering of 3Cat.
    /// </summary>
    /// <remarks>
    /// It is <c>true</c> when the coverage is below <see cref="ProviderNumbering.MinimumCoverage"/>: a
    /// series numbered half with the provider and half with the season of specials would leave fake gaps
    /// and duplicate episodes in Jellyfin, so the whole series keeps the numbering it had.
    /// </remarks>
    public bool UseThreeCat { get; }

    /// <summary>
    /// Gets why the numbering of 3Cat has to win, ready for the log; <c>null</c> when it must not.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Gets the number of videos the matcher had to place (<see cref="Slots"/> minus duplicates).
    /// </summary>
    public int Assignable => Slots.Count - Duplicates;

    /// <summary>
    /// Gets the number of videos paired with an episode of the provider.
    /// </summary>
    public int Matched => ByDate + ByTitle + ByOrder;

    /// <summary>
    /// Gets the share of videos paired with an episode of the provider (0 to 1).
    /// </summary>
    public double Coverage => Assignable <= 0 ? 0 : (double)Matched / Assignable;
}

/// <summary>
/// Numbers the videos of a program with the episode list of a metadata provider (TMDB, TVDB), instead of
/// the numbering 3Cat writes in its titles.
/// </summary>
/// <remarks>
/// <para>
/// Jellyfin never compares episode titles: it identifies an episode by <c>(season, episode)</c> and asks
/// the provider for that index. That is why the work of matching by name happens here, inside the plugin,
/// and its output is a <c>SxxEyy</c> that belongs to the provider — the file names, the <c>.nfo</c> and the
/// <c>Season NN</c> folders have to carry it, or the online metadata lands on the wrong episode.
/// </para>
/// <para>
/// The evidence is used in this order, and every video keeps the one that decided it: air date (the same
/// day, then one day apart) → normalized title (equality, then containment) → order inside the gap between
/// two already matched pairs. A provider episode is never assigned twice, the duplicated videos of 3Cat are
/// still dropped, and a video with no evidence goes to the season of specials with its air date in front of
/// the title, as the catalog mode already does.
/// </para>
/// <para>
/// When the coverage stays below <see cref="MinimumCoverage"/> the result says so
/// (<see cref="ProviderNumberingResult.UseThreeCat"/>) and the caller numbers with
/// <see cref="EpisodeNumbering"/>: a series half numbered with the provider and half sent to the specials
/// would show fake gaps and duplicated episodes in Jellyfin, which is worse than keeping the numbering of
/// 3Cat. This is the same promise the plugin made before the option existed.
/// </para>
/// </remarks>
public static class ProviderNumbering
{
    /// <summary>
    /// Share of videos that must be paired with an episode of the provider for its numbering to be used.
    /// </summary>
    /// <remarks>
    /// Half the series, as agreed in the design: below it, one series consistent with 3Cat is better than
    /// one series with holes Jellyfin cannot fill.
    /// </remarks>
    public const double MinimumCoverage = 0.5;

    /// <summary>
    /// Days of tolerance of the air date, which cover a time zone and a last minute change of the grid.
    /// </summary>
    internal const int DateToleranceDays = 1;

    /// <summary>
    /// Length from which a title may be matched by containment («La nit més llarga» inside
    /// «La nit més llarga de l'any»). Shorter titles match half the catalog by accident.
    /// </summary>
    internal const int MinimumContainmentLength = 8;

    /// <summary>
    /// Days of distance allowed between the dates of a gap paired by order.
    /// </summary>
    /// <remarks>
    /// A gap is only paired by order when its dates do not contradict it: 3Cat re-emits old seasons with
    /// the date of the re-emission, and pairing those with the episodes of 1996 because the count matches
    /// would be worse than leaving them in the season of specials.
    /// </remarks>
    internal const int OrderDateToleranceDays = 365;

    /// <summary>
    /// Pairs of a gap that must have a date to judge whether the order is compatible.
    /// </summary>
    internal const int OrderDateSample = 3;

    /// <summary>
    /// Numbers the videos of a program with the episodes of a provider.
    /// </summary>
    /// <param name="videos">Videos of 3Cat, in air order.</param>
    /// <param name="episodes">Episodes of the provider; the specials (season 0) are already left out.</param>
    /// <param name="seriesTitle">
    /// Title of the series, used to drop the prefix of an episode title before comparing it
    /// («Doraemon, el gat còsmic - El gat còsmic» → «El gat còsmic»). Optional.
    /// </param>
    /// <returns>The slots, the evidence and the counters of the matching.</returns>
    public static ProviderNumberingResult Assign(
        IReadOnlyList<ProviderVideo> videos,
        IReadOnlyList<ProviderEpisode> episodes,
        string? seriesTitle = null)
    {
        ArgumentNullException.ThrowIfNull(videos);
        ArgumentNullException.ThrowIfNull(episodes);

        var slots = new EpisodeSlot[videos.Count];
        var methods = new ProviderMatch[videos.Count];
        var pairs = new ProviderEpisode?[videos.Count];
        var assigned = new int[videos.Count];
        Array.Fill(assigned, -1);

        var duplicates = FindDuplicates(videos, seriesTitle);

        // The duplicated videos are marked so every pass skips them: they would otherwise take the episode
        // of a video that is written.
        for (var index = 0; index < duplicates.Length; index++)
        {
            if (duplicates[index])
            {
                assigned[index] = -2;
            }
        }

        // The specials of the provider (season 0) are not emissions of 3Cat: they would steal the match of
        // a regular episode with the same date. The gaps of the provider numbering (S27 starting at E9) are
        // respected because every episode keeps its own number.
        var pool = episodes
            .Where(episode => episode.Season > 0 && episode.Episode > 0)
            .GroupBy(episode => (episode.Season, episode.Episode))
            .Select(group => group.First())
            .OrderBy(episode => episode.Season)
            .ThenBy(episode => episode.Episode)
            .ToList();

        if (pool.Count == 0)
        {
            return Finish(slots, methods, pairs, duplicates, 0, 0, 0, "el proveedor no publica episodios");
        }

        var used = new bool[pool.Count];
        var byDate = new Dictionary<DateOnly, List<int>>();
        var byTitle = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (var index = 0; index < pool.Count; index++)
        {
            if (pool[index].AirDate is { } date)
            {
                if (!byDate.TryGetValue(date, out var sameDay))
                {
                    sameDay = [];
                    byDate[date] = sameDay;
                }

                sameDay.Add(index);
            }

            var poolTitle = NormalizeTitle(pool[index].Title, null);
            if (poolTitle.Length > 0)
            {
                if (!byTitle.TryGetValue(poolTitle, out var sameTitle))
                {
                    sameTitle = [];
                    byTitle[poolTitle] = sameTitle;
                }

                sameTitle.Add(index);
            }
        }

        void Take(int video, int episode, ProviderMatch method)
        {
            assigned[video] = episode;
            pairs[video] = pool[episode];
            methods[video] = method;
            slots[video] = new EpisodeSlot(pool[episode].Season, pool[episode].Episode, false);
            used[episode] = true;
        }

        // Passes 1 and 2, by air date.
        var byDateCount = MatchByDate(videos, pool, used, byDate, assigned, seriesTitle, Take)
            + MatchNextToTheDate(videos, pool, used, byDate, assigned, seriesTitle, Take);

        // Passes 3 and 4, by title.
        var byTitleCount = MatchByTitle(videos, pool, used, byTitle, assigned, seriesTitle, Take)
            + MatchByContainment(videos, pool, used, assigned, seriesTitle, Take);

        // Pass 5, by order inside the gaps left between the matched pairs.
        var byOrderCount = MatchByOrder(videos, pool, used, assigned, duplicates, Take);

        return Finish(slots, methods, pairs, duplicates, byDateCount, byTitleCount, byOrderCount, null);
    }

    /// <summary>
    /// Days checked around the air date of a video, in the order they are preferred.
    /// </summary>
    private static readonly int[] DateOffsets = [-1, 1];

    /// <summary>
    /// Pairs the videos whose air date is the same day as the one of an episode of the provider.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="pool">Episodes of the provider that may be assigned.</param>
    /// <param name="used">Episodes already assigned, updated in place.</param>
    /// <param name="byDate">Episodes indexed by air date.</param>
    /// <param name="assigned">Episode assigned to every video, or -1.</param>
    /// <param name="seriesTitle">Title of the series, to drop it from the titles before comparing.</param>
    /// <param name="take">Assigns an episode to a video.</param>
    /// <returns>The number of videos paired by this pass.</returns>
    private static int MatchByDate(
        IReadOnlyList<ProviderVideo> videos,
        List<ProviderEpisode> pool,
        bool[] used,
        Dictionary<DateOnly, List<int>> byDate,
        int[] assigned,
        string? seriesTitle,
        Action<int, int, ProviderMatch> take)
    {
        var matched = 0;

        for (var index = 0; index < videos.Count; index++)
        {
            if (assigned[index] != -1 || videos[index].AirDate is not { } date)
            {
                continue;
            }

            if (!byDate.TryGetValue(date, out var candidates))
            {
                continue;
            }

            var free = candidates.Where(candidate => !used[candidate]).ToList();
            if (free.Count == 0)
            {
                continue;
            }

            if (free.Count == 1)
            {
                take(index, free[0], ProviderMatch.Date);
                matched++;
                continue;
            }

            // Two episodes aired the same day (a double emission): the title decides between them.
            var title = NormalizeTitle(videos[index].Title, seriesTitle);
            List<int> sameTitle = title.Length == 0
                ? []
                : free
                    .Where(candidate => string.Equals(
                        NormalizeTitle(pool[candidate].Title, null),
                        title,
                        StringComparison.Ordinal))
                    .ToList();

            if (sameTitle.Count == 1)
            {
                take(index, sameTitle[0], ProviderMatch.Date);
                matched++;
            }
        }

        return matched;
    }

    /// <summary>
    /// Pairs the videos whose air date is one day away from the one of an episode of the provider, which
    /// covers a time zone and a last minute change of the grid.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="pool">Episodes of the provider that may be assigned.</param>
    /// <param name="used">Episodes already assigned, updated in place.</param>
    /// <param name="byDate">Episodes indexed by air date.</param>
    /// <param name="assigned">Episode assigned to every video, or -1.</param>
    /// <param name="seriesTitle">Title of the series, to drop it from the titles before comparing.</param>
    /// <param name="take">Assigns an episode to a video.</param>
    /// <returns>The number of videos paired by this pass.</returns>
    private static int MatchNextToTheDate(
        IReadOnlyList<ProviderVideo> videos,
        List<ProviderEpisode> pool,
        bool[] used,
        Dictionary<DateOnly, List<int>> byDate,
        int[] assigned,
        string? seriesTitle,
        Action<int, int, ProviderMatch> take)
    {
        var matched = 0;

        for (var index = 0; index < videos.Count; index++)
        {
            if (assigned[index] != -1 || videos[index].AirDate is not { } date)
            {
                continue;
            }

            var title = NormalizeTitle(videos[index].Title, seriesTitle);
            var best = -1;
            var bestByTitle = false;

            foreach (var offset in DateOffsets)
            {
                if (!byDate.TryGetValue(date.AddDays(offset), out var candidates))
                {
                    continue;
                }

                foreach (var candidate in candidates)
                {
                    if (used[candidate])
                    {
                        continue;
                    }

                    var sameTitle = title.Length > 0
                        && string.Equals(
                            NormalizeTitle(pool[candidate].Title, null),
                            title,
                            StringComparison.Ordinal);

                    // Both days are one day away, so the candidate that also matches the title wins; the
                    // first one found keeps the episode when there is no title to decide.
                    if (best < 0 || (sameTitle && !bestByTitle))
                    {
                        best = candidate;
                        bestByTitle = sameTitle;
                    }
                }
            }

            if (best >= 0)
            {
                take(index, best, ProviderMatch.Date);
                matched++;
            }
        }

        return matched;
    }

    /// <summary>
    /// Pairs the videos whose normalized title is, exactly, the title of an episode of the provider.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="pool">Episodes of the provider that may be assigned.</param>
    /// <param name="used">Episodes already assigned, updated in place.</param>
    /// <param name="byTitle">Episodes indexed by normalized title.</param>
    /// <param name="assigned">Episode assigned to every video, or -1.</param>
    /// <param name="seriesTitle">Title of the series, to drop it from the titles before comparing.</param>
    /// <param name="take">Assigns an episode to a video.</param>
    /// <returns>The number of videos paired by this pass.</returns>
    /// <remarks>
    /// 3Cat titles in Catalan and the providers often publish in Spanish or English, so a title only
    /// decides when it is the same title: the equality is done on the normalized form, and when the same
    /// text names several episodes the closest air date breaks the tie, and only if it is unique.
    /// </remarks>
    private static int MatchByTitle(
        IReadOnlyList<ProviderVideo> videos,
        List<ProviderEpisode> pool,
        bool[] used,
        Dictionary<string, List<int>> byTitle,
        int[] assigned,
        string? seriesTitle,
        Action<int, int, ProviderMatch> take)
    {
        var matched = 0;

        for (var index = 0; index < videos.Count; index++)
        {
            if (assigned[index] != -1)
            {
                continue;
            }

            var title = NormalizeTitle(videos[index].Title, seriesTitle);
            if (title.Length == 0 || !byTitle.TryGetValue(title, out var candidates))
            {
                continue;
            }

            var free = candidates.Where(candidate => !used[candidate]).ToList();
            if (free.Count == 1)
            {
                take(index, free[0], ProviderMatch.Title);
                matched++;
                continue;
            }

            if (free.Count == 0 || videos[index].AirDate is not { } date)
            {
                continue;
            }

            var byDistance = free
                .Select(candidate => (Candidate: candidate, Distance: Distance(pool[candidate].AirDate, date)))
                .OrderBy(entry => entry.Distance)
                .ToList();

            if (byDistance[0].Distance < byDistance[1].Distance)
            {
                take(index, byDistance[0].Candidate, ProviderMatch.Title);
                matched++;
            }
        }

        return matched;
    }

    /// <summary>
    /// Pairs the remaining videos whose normalized title is contained in the title of an episode of the
    /// provider, or the other way around, and only when a single episode claims it.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="pool">Episodes of the provider that may be assigned.</param>
    /// <param name="used">Episodes already assigned, updated in place.</param>
    /// <param name="assigned">Episode assigned to every video, or -1.</param>
    /// <param name="seriesTitle">Title of the series, to drop it from the titles before comparing.</param>
    /// <param name="take">Assigns an episode to a video.</param>
    /// <returns>The number of videos paired by this pass.</returns>
    /// <remarks>
    /// It is the last resort of the titles: 3Cat writes «La nit més llarga» where the provider writes
    /// «La nit més llarga de l'any». Titles shorter than <see cref="MinimumContainmentLength"/> are not
    /// compared, or half the catalog would match by accident.
    /// </remarks>
    private static int MatchByContainment(
        IReadOnlyList<ProviderVideo> videos,
        List<ProviderEpisode> pool,
        bool[] used,
        int[] assigned,
        string? seriesTitle,
        Action<int, int, ProviderMatch> take)
    {
        var matched = 0;

        for (var index = 0; index < videos.Count; index++)
        {
            if (assigned[index] != -1)
            {
                continue;
            }

            var title = NormalizeTitle(videos[index].Title, seriesTitle);
            if (title.Length < MinimumContainmentLength)
            {
                continue;
            }

            var candidates = new List<int>();

            for (var candidate = 0; candidate < pool.Count; candidate++)
            {
                if (used[candidate])
                {
                    continue;
                }

                var other = NormalizeTitle(pool[candidate].Title, null);
                if (other.Length < MinimumContainmentLength)
                {
                    continue;
                }

                if (other.Contains(title, StringComparison.Ordinal)
                    || title.Contains(other, StringComparison.Ordinal))
                {
                    candidates.Add(candidate);
                }
            }

            if (candidates.Count == 1)
            {
                take(index, candidates[0], ProviderMatch.Title);
                matched++;
            }
        }

        return matched;
    }

    /// <summary>
    /// Pairs the videos left inside a gap between two already matched pairs, when the gap has the same
    /// number of videos of 3Cat and episodes of the provider, so the order of one is the order of the other.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="pool">Episodes of the provider that may be assigned.</param>
    /// <param name="used">Episodes already assigned, updated in place.</param>
    /// <param name="assigned">Episode assigned to every video, or -1.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice, which are not part of any gap.</param>
    /// <param name="take">Assigns an episode to a video.</param>
    /// <returns>The number of videos paired by this pass.</returns>
    /// <remarks>
    /// The matched pairs are the anchors, and a gap only decides when it is forced: the same number of
    /// videos and episodes between two anchors means there is a single way to pair them. When the counts
    /// differ —the provider lists episodes 3Cat never published, or 3Cat re-emits an old season— the gap is
    /// left alone and its videos go to the season of specials, which is the honest answer.
    /// </remarks>
    private static int MatchByOrder(
        IReadOnlyList<ProviderVideo> videos,
        List<ProviderEpisode> pool,
        bool[] used,
        int[] assigned,
        bool[] duplicates,
        Action<int, int, ProviderMatch> take)
    {
        // The anchors have to describe the same two orders: an anchor that goes backwards in the list of
        // the provider would make the gaps meaningless, so it is left out.
        var boundaries = new List<(int Video, int Episode)> { (-1, -1) };
        var last = -1;

        for (var index = 0; index < videos.Count; index++)
        {
            if (assigned[index] <= last)
            {
                continue;
            }

            boundaries.Add((index, assigned[index]));
            last = assigned[index];
        }

        boundaries.Add((videos.Count, pool.Count));

        var matched = 0;

        for (var index = 0; index + 1 < boundaries.Count; index++)
        {
            var left = boundaries[index];
            var right = boundaries[index + 1];

            var gapVideos = new List<int>();
            for (var video = left.Video + 1; video < right.Video; video++)
            {
                if (!duplicates[video] && assigned[video] < 0)
                {
                    gapVideos.Add(video);
                }
            }

            var gapEpisodes = new List<int>();
            for (var episode = left.Episode + 1; episode < right.Episode; episode++)
            {
                if (!used[episode])
                {
                    gapEpisodes.Add(episode);
                }
            }

            if (gapVideos.Count == 0
                || gapVideos.Count != gapEpisodes.Count
                || !GapIsCompatible(videos, pool, gapVideos, gapEpisodes))
            {
                continue;
            }

            for (var pair = 0; pair < gapVideos.Count; pair++)
            {
                take(gapVideos[pair], gapEpisodes[pair], ProviderMatch.Order);
                matched++;
            }
        }

        return matched;
    }

    /// <summary>
    /// Tells whether the dates of a gap allow pairing it by order.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="pool">Episodes of the provider.</param>
    /// <param name="gapVideos">Videos of the gap, in order.</param>
    /// <param name="gapEpisodes">Episodes of the gap, in the order of the provider.</param>
    /// <returns><c>true</c> when the dates do not contradict the order.</returns>
    /// <remarks>
    /// 3Cat re-emits old seasons with the date of the re-emission, and pairing those with the episodes of
    /// 1996 because the counts match would be a silent error: when at least
    /// <see cref="OrderDateSample"/> pairs have a date, at least half of them have to be within
    /// <see cref="OrderDateToleranceDays"/> for the gap to be paired.
    /// </remarks>
    private static bool GapIsCompatible(
        IReadOnlyList<ProviderVideo> videos,
        List<ProviderEpisode> pool,
        List<int> gapVideos,
        List<int> gapEpisodes)
    {
        var dated = 0;
        var close = 0;

        for (var index = 0; index < gapVideos.Count; index++)
        {
            if (videos[gapVideos[index]].AirDate is not { } videoDate
                || pool[gapEpisodes[index]].AirDate is not { } episodeDate)
            {
                continue;
            }

            dated++;
            if (Math.Abs(videoDate.DayNumber - episodeDate.DayNumber) <= OrderDateToleranceDays)
            {
                close++;
            }
        }

        // A single date that contradicts the pairing is enough to leave the gap alone: it is how a season
        // 3Cat re-emits twenty years later is kept out of the numbering of the provider.
        return dated == 0 || (close > 0 && (dated < OrderDateSample || close * 2 >= dated));
    }

    /// <summary>
    /// Fills the slot of the videos no pass claimed and builds the result.
    /// </summary>
    /// <param name="slots">Slot of every video, filled in place.</param>
    /// <param name="methods">Evidence of every video, filled in place.</param>
    /// <param name="pairs">Episode paired with every video.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice.</param>
    /// <param name="byDate">Videos paired by air date.</param>
    /// <param name="byTitle">Videos paired by title.</param>
    /// <param name="byOrder">Videos paired by order.</param>
    /// <param name="forcedReason">Why 3Cat numbering wins, when the caller already knows it.</param>
    /// <returns>The result, with the coverage and the decision about the numbering.</returns>
    private static ProviderNumberingResult Finish(
        EpisodeSlot[] slots,
        ProviderMatch[] methods,
        ProviderEpisode?[] pairs,
        bool[] duplicates,
        int byDate,
        int byTitle,
        int byOrder,
        string? forcedReason)
    {
        var special = 1;
        var duplicateCount = 0;

        for (var index = 0; index < slots.Length; index++)
        {
            if (duplicates[index])
            {
                // The same video published twice is not written again, exactly as in the catalog mode.
                slots[index] = new EpisodeSlot(EpisodeNumbering.SpecialsSeason, 0, IsDuplicate: true);
                methods[index] = ProviderMatch.None;
                duplicateCount++;
                continue;
            }

            if (methods[index] == ProviderMatch.None && pairs[index] is null)
            {
                slots[index] = new EpisodeSlot(EpisodeNumbering.SpecialsSeason, special++, false);
            }
        }

        var assignable = slots.Length - duplicateCount;
        var matched = byDate + byTitle + byOrder;
        var coverage = assignable <= 0 ? 0 : (double)matched / assignable;

        var reason = forcedReason;
        if (reason is null && assignable == 0)
        {
            reason = "3Cat no publica ningún vídeo que numerar";
        }
        else if (reason is null && coverage < MinimumCoverage)
        {
            reason = string.Format(
                CultureInfo.InvariantCulture,
                "solo {0} de {1} vídeos se emparejan con la numeración del proveedor ({2:0} %)",
                matched,
                assignable,
                coverage * 100);
        }

        return new ProviderNumberingResult(
            slots,
            methods,
            pairs,
            byDate,
            byTitle,
            byOrder,
            duplicateCount,
            reason is not null,
            reason);
    }

    /// <summary>
    /// Flags the videos 3Cat publishes twice with the same title and the same air date.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="seriesTitle">Title of the series, dropped from the titles before comparing.</param>
    /// <returns>The videos that must not be written again.</returns>
    /// <remarks>
    /// It is the same rule the catalog mode applies, restricted to the same day: two episodes of different
    /// seasons may share the title of the provider, so the date is what tells a duplicate from a namesake.
    /// </remarks>
    private static bool[] FindDuplicates(IReadOnlyList<ProviderVideo> videos, string? seriesTitle)
    {
        var duplicates = new bool[videos.Count];
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < videos.Count; index++)
        {
            var title = NormalizeTitle(videos[index].Title, seriesTitle);
            var date = videos[index].AirDate;

            // A video without title and without date cannot be told apart from another one, so it is only
            // a duplicate when 3Cat publishes the very same row, which the id already catches.
            var key = title.Length == 0 && date is null
                ? string.Concat("#", index.ToString(CultureInfo.InvariantCulture))
                : string.Concat(
                    title,
                    "|",
                    date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty);

            if (!seen.TryAdd(key, index))
            {
                duplicates[index] = true;
            }
        }

        return duplicates;
    }

    /// <summary>
    /// Normalizes a title so two spellings of the same title compare equal: it drops the numbering of 3Cat
    /// and the name of the series, removes the accents, lowercases and keeps only letters and digits.
    /// </summary>
    /// <param name="title">Title to normalize.</param>
    /// <param name="seriesTitle">Title of the series, dropped from the beginning when present.</param>
    /// <returns>The normalized title, empty when there is nothing to compare.</returns>
    /// <remarks>
    /// «T22xC6 - Visita als inferns de l'amor» and «Visita als inferns de l'amor» normalize to the same
    /// text, and so do «L'últim estiu» and «l ultim estiu»: the punctuation and the accents of the providers
    /// are not the same as the ones of 3Cat.
    /// </remarks>
    internal static string NormalizeTitle(string? title, string? seriesTitle)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var value = StripSeriesPrefix(EpisodeNumbering.StripNumber(title.Trim()), seriesTitle);
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Removes the repeated series name from the beginning of an episode title
    /// («Doraemon, el gat còsmic - El gat còsmic» → «El gat còsmic»).
    /// </summary>
    /// <param name="value">Episode title.</param>
    /// <param name="seriesTitle">Title of the series.</param>
    /// <returns>The title without the series name, or the title itself when it does not start with it.</returns>
    private static string StripSeriesPrefix(string value, string? seriesTitle)
    {
        if (string.IsNullOrWhiteSpace(seriesTitle))
        {
            return value;
        }

        var prefix = seriesTitle.Trim();
        if (value.Length <= prefix.Length || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var rest = value[prefix.Length..].TrimStart(' ', '-', ':', '|', '.', ',', '–', '—');
        return rest.Length > 0 ? rest : value;
    }

    /// <summary>
    /// Days between the air date of an episode and the air date of a video.
    /// </summary>
    /// <param name="candidate">Air date of the episode, or <c>null</c>.</param>
    /// <param name="date">Air date of the video.</param>
    /// <returns>The distance in days, or <see cref="int.MaxValue"/> when the episode has no date.</returns>
    private static int Distance(DateOnly? candidate, DateOnly date)
        => candidate is { } day ? Math.Abs(day.DayNumber - date.DayNumber) : int.MaxValue;
}
