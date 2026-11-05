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
    /// <param name="videos">Videos of 3Cat, in air order.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice.</param>
    /// <returns><c>true</c> when the counter may be used as the absolute position of the video.</returns>
    /// <remarks>
    /// <para>
    /// Three things have to hold, and each one rejects a different catalog: enough videos have to publish the
    /// counter (a program that publishes it in a video out of ten has no counter to read); the counter has to
    /// grow with the air order, because a counter that restarts in every batch is a counter of the season
    /// —<c>capitol_temporada</c>, which does restart, arrives with the same shape—; and it has to announce more
    /// chapters than any single season has, which is what tells a counter of the program from a season index
    /// that happens to keep growing.
    /// </para>
    /// <para>
    /// The re-emission of a chapter twenty years later is what the growth rejects, and it is on purpose: those
    /// videos are not the tail of the program, and the other passes place them with the date they carry.
    /// </para>
    /// </remarks>
    internal static bool CounterIsAbsolute(IReadOnlyList<ProviderVideo> videos, bool[] duplicates)
    {
        var assignable = 0;
        var known = 0;
        var inversions = 0;
        var previous = 0;
        var highest = 0;
        var highestChapter = 0;

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

            if (previous > 0 && counter < previous)
            {
                inversions++;
            }

            previous = counter;
            highest = Math.Max(highest, counter);
        }

        return known > 0
            && known >= assignable * MinimumCounterDensity
            && inversions == 0
            && highest > highestChapter;
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
