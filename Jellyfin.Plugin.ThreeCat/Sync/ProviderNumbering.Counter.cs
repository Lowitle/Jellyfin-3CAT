namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Alignment of the catalog of 3Cat with the episodes of a provider from the <c>capitol</c> 3Cat publishes.
/// </summary>
/// <remarks>
/// <para>
/// The <c>capitol</c> of 3Cat is a counter of the whole program, not of the season: «Detectiu Conan» numbers
/// its 385 videos from 1 to 1.206 without a single step backwards, T1 ends at 43 and T2 begins at 44, T3 at 89
/// and — after the 720 chapters its catalog does not host — T20 begins at 857. It is the numbering of the
/// program itself, the one its website shows next to the title, and it survives what the titles and the dates
/// cannot: the dub writes its own titles in Catalan and airs years later.
/// </para>
/// <para>
/// So the pairing is a lookup: the episode of the provider that occupies the position the counter announces,
/// counting the episodes of the provider in their own order. The catalog of 3Cat has holes, and this is the
/// only pass that respects them —the passes that walk both lists side by side number 136 the video 3Cat calls
/// 857, because they count videos instead of reading numbers—.
/// </para>
/// <para>
/// The position is the same one only when both counters start at the same episode, which is the rule, not the
/// exception: an anime keeps the order of its original emission in every catalog. When it does not hold —a
/// provider that lists a recap the original did not have, a counter that starts later— one anchor is enough to
/// calibrate the difference for the whole program, because the offset is a single number: see
/// <see cref="ProviderNumbering.AlignByAnchors"/>.
/// </para>
/// </remarks>
public static partial class ProviderNumbering
{
    /// <summary>
    /// Share of the videos of a program that has to publish a <c>capitol</c> for the counter to be evidence.
    /// </summary>
    /// <remarks>
    /// A program that publishes it in half of its videos still has a counter; the ones without it are left to
    /// the other passes.
    /// </remarks>
    internal const double MinimumCounterDensity = 0.5;

    /// <summary>
    /// Share of the counter the provider has to cover for the counter to be evidence.
    /// </summary>
    /// <remarks>
    /// «Detectiu Conan» announces 1.206 chapters against the 1.150 episodes TMDB lists, which is the same
    /// series with its tail not catalogued yet, so the reach is checked against half of the counter: a program
    /// whose counter announces 800 chapters against a provider of 12 episodes is not the same series and the
    /// pass stays out of the way.
    /// </remarks>
    internal const double MinimumCounterReach = 0.5;

    /// <summary>
    /// Share of the counter that has to be a number of its own for the counter to be evidence.
    /// </summary>
    /// <remarks>
    /// A counter of the season restarts in every batch and hands out again the numbers it has already handed
    /// out, so the numbers of a program are mostly distinct and the ones of a season are not.
    /// </remarks>
    internal const double MinimumCounterDistinct = 0.9;

    /// <summary>
    /// Chapters a video may fall behind the highest counter published before it and still be a video.
    /// </summary>
    /// <remarks>
    /// A re-emission goes back to the beginning of the program; a catalog that dates its chapters wrong —and
    /// «Detectiu Conan» does, the 53 of the program dated after the 79— goes back a handful of chapters.
    /// </remarks>
    internal const int CounterReEmissionSlack = 50;

    /// <summary>
    /// Divisor of the highest counter that widens the slack of a re-emission: a tenth of the program.
    /// </summary>
    /// <remarks>
    /// The slack is fixed for a short program and grows with the long ones, where a hole of a few hundred
    /// chapters is a hole of the catalog and not a chapter emitted again.
    /// </remarks>
    internal const int CounterReEmissionShare = 10;

    /// <summary>
    /// Pairs the videos of 3Cat with the episodes of the provider by the <c>capitol</c> counter.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="pool">Episodes of the provider that may be assigned.</param>
    /// <param name="used">Episodes already assigned, updated in place.</param>
    /// <param name="assigned">Episode assigned to every video, updated in place; -1 when it has none.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice, which no pass may claim.</param>
    /// <param name="offset">
    /// Difference between the position of the counter and the position of the episode of the provider, deduced
    /// from the anchors of the user; 0 when there is no anchor to deduce it from.
    /// </param>
    /// <param name="take">Assigns an episode to a video.</param>
    /// <returns>The number of videos paired by this pass.</returns>
    private static int AlignByCounter(
        IReadOnlyList<ProviderVideo> videos,
        List<ProviderEpisode> pool,
        bool[] used,
        int[] assigned,
        bool[] duplicates,
        int offset,
        Action<int, int, ProviderMatch> take)
    {
        var matched = 0;

        for (var index = 0; index < videos.Count; index++)
        {
            if (duplicates[index] || assigned[index] >= 0 || videos[index].Chapter is not { } counter)
            {
                continue;
            }

            var position = counter - 1 + offset;
            if (position < 0 || position >= pool.Count || used[position])
            {
                continue;
            }

            take(index, position, ProviderMatch.Counter);
            matched++;
        }

        return matched;
    }

    /// <summary>
    /// Episodes a video may fall away from the one its counter announces and still be rescued.
    /// </summary>
    /// <remarks>
    /// The rescue only claims an episode no other video took and only when it is right next to the one the
    /// counter announces, which is what a catalog dated out of order leaves behind: «Detectiu Conan» dates the
    /// chapter 40 after the 41, the alignment cannot walk back and the video would be written as a special even
    /// though the episode it belongs to is still free.
    /// </remarks>
    internal const int OrphanReach = 2;

    /// <summary>
    /// Pairs the videos that no pass could place with the free episode closest to the one their counter announces.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="pool">Episodes of the provider that may be assigned.</param>
    /// <param name="used">Episodes already assigned.</param>
    /// <param name="assigned">Episode assigned to every video, updated in place; -1 when it has none.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice, which no pass may claim.</param>
    /// <param name="methods">Evidence of every video, updated in place.</param>
    /// <param name="shifts">Shift of the counter of every video, read from the titles.</param>
    /// <param name="shifted">Whether the alignment applied the shifts of the titles.</param>
    /// <param name="offset">Difference between the position of the counter and the position of the episode.</param>
    /// <param name="seriesTitle">Title of the series, to compare the titles without it.</param>
    /// <param name="take">Assigns an episode to a video.</param>
    /// <returns>The number of videos paired by this pass.</returns>
    /// <remarks>
    /// It never takes an episode another video owns: a video that 3Cat publishes and the provider does not list
    /// still ends in the season of specials, because the counter it announces falls outside the episodes of the
    /// provider and there is nothing next to it to rescue. Reading the counter as a position instead of counting
    /// the videos is what keeps its holes —from the 135 the catalog jumps to the 857— out of the way.
    /// </remarks>
    private static int RescueOrphans(
        IReadOnlyList<ProviderVideo> videos,
        List<ProviderEpisode> pool,
        bool[] used,
        int[] assigned,
        bool[] duplicates,
        ProviderMatch[] methods,
        int[] shifts,
        bool shifted,
        int offset,
        string? seriesTitle,
        Action<int, int, ProviderMatch> take)
    {
        var rescued = 0;
        var firsts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < videos.Count; index++)
        {
            var key = NormalizeTitle(EpisodeNumbering.StripPart(videos[index].Title ?? string.Empty), seriesTitle);
            if (key.Length > 0 && !firsts.ContainsKey(key))
            {
                firsts[key] = index;
            }
        }

        for (var index = 0; index < videos.Count; index++)
        {
            if (duplicates[index] || assigned[index] >= 0 || methods[index] != ProviderMatch.None)
            {
                continue;
            }

            if (videos[index].Chapter is not { } counter || counter <= 0)
            {
                continue;
            }

            // A second part is not a video the provider lists: the counter of a program that splits an episode
            // runs ahead, and the place it announces belongs to the arc of the next episode. Its first part is
            // the one that has an episode, and pass 7 writes it as the part 2 of that same episode.
            var key = NormalizeTitle(EpisodeNumbering.StripPart(videos[index].Title ?? string.Empty), seriesTitle);
            if (PartNumber(videos[index].Title) is { } number
                && number > 1
                && key.Length > 0
                && firsts.TryGetValue(key, out var owner)
                && owner != index)
            {
                continue;
            }

            var shift = shifted ? shifts[index] : offset;
            var expected = counter - 1 + shift;

            if (expected < 0 || expected >= pool.Count)
            {
                continue;
            }

            var closest = -1;
            var distance = int.MaxValue;
            var first = Math.Max(0, expected - OrphanReach);
            var last = Math.Min(pool.Count - 1, expected + OrphanReach);

            for (var candidate = first; candidate <= last; candidate++)
            {
                if (used[candidate])
                {
                    continue;
                }

                var away = Math.Abs(candidate - expected);
                if (away < distance)
                {
                    distance = away;
                    closest = candidate;
                }
            }

            if (closest < 0)
            {
                continue;
            }

            take(index, closest, shift == 0 ? ProviderMatch.Counter : ProviderMatch.CounterShift);
            rescued++;
        }

        return rescued;
    }

    /// <summary>
    /// Tells whether the <c>capitol</c> of the videos may be read as the absolute numbering of the program.
    /// </summary>
    /// <param name="videos">Videos of 3Cat, in air order.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice.</param>
    /// <param name="pool">Episodes of the provider that may be assigned.</param>
    /// <returns><c>true</c> when the counter is a counter of the program and the provider covers it.</returns>
    private static bool CounterIsUsable(
        IReadOnlyList<ProviderVideo> videos,
        bool[] duplicates,
        List<ProviderEpisode> pool)
        => CounterIsAbsolute(videos, duplicates) && CounterReachesProvider(videos, duplicates, pool);

    /// <summary>
    /// Tells whether the <c>capitol</c> of the videos is the counter of the whole program.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice.</param>
    /// <returns><c>true</c> when the counter may be used as the absolute position of the video.</returns>
    /// <remarks>
    /// <para>
    /// Three things have to hold, and each one rejects a different catalog: enough videos have to publish the
    /// counter (a program that publishes it in a video out of ten has no counter to read); it has to announce
    /// more chapters than any single season has, which is what tells a counter of the program from a season
    /// index that happens to keep growing; and it has to number the videos one by one, because a counter that
    /// restarts in every batch —<c>capitol_temporada</c>, which does restart, arrives with the same shape—
    /// hands out again the numbers it has already handed out.
    /// </para>
    /// <para>
    /// The order the videos arrive in is not judged here, and «Detectiu Conan» is why: 3Cat dates its catalog
    /// with the emission of the dub, and it dates it wrong —the chapter 53 of the program is dated after the
    /// 79, and the 40 after the 41—, so a pass that reads the counter as an order throws away the only
    /// evidence it has. What is read as a re-emission is what a re-emission is: a video that goes back to the
    /// beginning of the program, hundreds of chapters, and not a chapter the catalog dated a few days out of
    /// place.
    /// </para>
    /// </remarks>
    internal static bool CounterIsAbsolute(IReadOnlyList<ProviderVideo> videos, bool[] duplicates)
    {
        var assignable = 0;
        var known = 0;
        var highest = 0;
        var highestChapter = 0;
        var distinct = new HashSet<int>();

        for (var index = 0; index < videos.Count; index++)
        {
            if (duplicates[index])
            {
                continue;
            }

            assignable++;

            if (EpisodeNumbering.Parse(videos[index].Title) is { } numbering)
            {
                highestChapter = Math.Max(highestChapter, numbering.Episode);
            }

            if (videos[index].Chapter is not { } counter || counter <= 0)
            {
                continue;
            }

            known++;
            highest = Math.Max(highest, counter);
            distinct.Add(counter);
        }

        return known > 0
            && known >= assignable * MinimumCounterDensity
            && distinct.Count >= known * MinimumCounterDistinct
            && highest > highestChapter
            && !ReEmitsChapter(videos, duplicates, highest);
    }

    /// <summary>
    /// Tells whether the catalog publishes again a chapter it already publishes, which is not a chapter of the
    /// program and has no place to occupy.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice.</param>
    /// <param name="highest">Highest <c>capitol</c> of the catalog.</param>
    /// <returns><c>true</c> when a video falls behind the ones already published by more than the slack.</returns>
    private static bool ReEmitsChapter(IReadOnlyList<ProviderVideo> videos, bool[] duplicates, int highest)
    {
        var slack = Math.Max(CounterReEmissionSlack, highest / CounterReEmissionShare);
        var published = 0;

        for (var index = 0; index < videos.Count; index++)
        {
            if (duplicates[index] || videos[index].Chapter is not { } counter || counter <= 0)
            {
                continue;
            }

            if (counter < published - slack)
            {
                return true;
            }

            published = Math.Max(published, counter);
        }

        return false;
    }

    /// <summary>
    /// Tells whether the provider covers enough of the counter for the counter to be evidence.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice.</param>
    /// <param name="pool">Episodes of the provider that may be assigned.</param>
    /// <returns><c>true</c> when the provider lists at least half of the chapters the counter announces.</returns>
    private static bool CounterReachesProvider(
        IReadOnlyList<ProviderVideo> videos,
        bool[] duplicates,
        List<ProviderEpisode> pool)
    {
        var highest = 0;

        for (var index = 0; index < videos.Count; index++)
        {
            if (!duplicates[index] && videos[index].Chapter is { } counter)
            {
                highest = Math.Max(highest, counter);
            }
        }

        return highest > 0 && pool.Count >= highest * MinimumCounterReach;
    }
}
