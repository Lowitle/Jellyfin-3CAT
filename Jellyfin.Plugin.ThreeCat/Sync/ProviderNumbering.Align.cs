namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Alignment of the videos of 3Cat with the episodes of the provider once the shift of the counter is known.
/// </summary>
/// <remarks>
/// <para>
/// The shift is an estimate taken from the titles, and the counter with the shift applied is already a good
/// answer —«Detectiu Conan» goes from 0 % to 85 % of the catalog in the right episode—, but the border between
/// two runs falls somewhere and the episodes a title is split into do not always line up. So the last step is not
/// a lookup but an alignment: the two ordered lists are paired maximising how much the titles look alike.
/// </para>
/// <para>
/// Three moves are allowed, and each one is the answer to a real catalog: pair a video with an episode (the
/// normal case), skip an episode (3Cat does not publish it: its catalog jumps from 135 to 857), and skip a video
/// (3Cat split an episode the provider lists once, so the second part has no episode of its own). Skipping is
/// cheap, and the counter with its shift decides the ties: it is the only thing that keeps an alignment without
/// similar titles from wandering off.
/// </para>
/// </remarks>
public static partial class ProviderNumbering
{
    /// <summary>
    /// Pairs the videos left with the episodes of the provider, moving along the order of both lists.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="pool">Episodes of the provider that may be assigned.</param>
    /// <param name="used">Episodes already assigned, updated in place.</param>
    /// <param name="assigned">Episode assigned to every video, or -1; updated in place.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice, which no pass may claim.</param>
    /// <param name="shifts">Shift of every video, as <see cref="CounterShifts"/> read it.</param>
    /// <param name="offset">Difference the anchors deduced between the counter and the provider.</param>
    /// <param name="seriesTitle">Title of the series, dropped from the titles of 3Cat before comparing.</param>
    /// <param name="take">Assigns an episode to a video.</param>
    /// <returns>The number of videos paired by this pass.</returns>
    internal static int AlignByShift(
        IReadOnlyList<ProviderVideo> videos,
        List<ProviderEpisode> pool,
        bool[] used,
        int[] assigned,
        bool[] duplicates,
        int[] shifts,
        int offset,
        string? seriesTitle,
        Action<int, int, ProviderMatch> take)
    {
        var candidates = new List<int>();
        for (var index = 0; index < videos.Count; index++)
        {
            if (!duplicates[index] && assigned[index] < 0 && videos[index].Chapter is { } counter && counter > 0)
            {
                candidates.Add(index);
            }
        }

        var free = new List<int>();
        for (var index = 0; index < pool.Count; index++)
        {
            if (!used[index])
            {
                free.Add(index);
            }
        }

        if (candidates.Count == 0 || free.Count == 0)
        {
            return 0;
        }

        var titles = new string?[pool.Count];
        var parts = new int?[pool.Count];
        for (var index = 0; index < pool.Count; index++)
        {
            titles[index] = NormalizeTitle(pool[index].Title, null);
            parts[index] = PartNumber(pool[index].Title);
        }

        var normalized = new string?[candidates.Count];
        var expected = new int[candidates.Count];
        var videoParts = new int?[candidates.Count];
        for (var index = 0; index < candidates.Count; index++)
        {
            var video = videos[candidates[index]];
            normalized[index] = NormalizeTitle(video.Title, seriesTitle);
            videoParts[index] = PartNumber(video.Title);
            expected[index] = video.Chapter!.Value - 1 + offset + shifts[candidates[index]];
        }

        var weights = TitleWeights.Learn(normalized.Concat(titles));
        var path = Align(candidates.Count, free, normalized, videoParts, titles, parts, expected, weights);

        var matched = 0;
        for (var index = 0; index < candidates.Count; index++)
        {
            var slot = path[index];
            if (slot < 0 || slot >= free.Count || used[free[slot]])
            {
                continue;
            }

            take(candidates[index], free[slot], ProviderMatch.CounterShift);
            matched++;
        }

        return matched;
    }

    /// <summary>
    /// Walks both lists looking for the pairing that makes the titles look the most alike.
    /// </summary>
    /// <param name="videos">Videos to place, in air order.</param>
    /// <param name="free">Positions of the provider that are still free, in order.</param>
    /// <param name="normalized">Normalized title of every video.</param>
    /// <param name="videoParts">Part of every video, or <c>null</c>.</param>
    /// <param name="titles">Normalized title of every episode of the pool.</param>
    /// <param name="parts">Part of every episode of the pool, or <c>null</c>.</param>
    /// <param name="expected">Position of the provider the corrected counter announces for every video.</param>
    /// <param name="weights">Weight of every word of the program.</param>
    /// <returns>The slot of <paramref name="free"/> every video goes to, or -1 when it has none.</returns>
    /// <remarks>
    /// The three moves are paired (both lists advance), skip an episode (only the provider advances: 3Cat does not
    /// publish that chapter) and skip a video (only 3Cat advances: its video is a second part the provider lists
    /// inside the first one). The pairing is only considered a dozen episodes around the corrected counter, which
    /// is what keeps a title that repeats —«el objetivo es el departamento de tráfico (1)», «(2)»— from dragging a
    /// video to the other end of the catalog.
    /// </remarks>
    private static int[] Align(
        int videos,
        List<int> free,
        string?[] normalized,
        int?[] videoParts,
        string?[] titles,
        int?[] parts,
        int[] expected,
        TitleWeights weights)
    {
        var episodes = free.Count;
        var stride = episodes + 1;
        var step = new byte[(videos + 1) * stride];
        var previous = new double[stride];
        var current = new double[stride];

        previous[0] = 0;
        for (var slot = 1; slot <= episodes; slot++)
        {
            previous[slot] = previous[slot - 1] - ShiftGap;
            step[slot] = 2;
        }

        for (var video = 0; video < videos; video++)
        {
            var center = Nearest(free, expected[video]);
            var first = Math.Max(1, center + 1 - ShiftWindow);
            var last = Math.Min(episodes, center + 1 + ShiftWindow);

            for (var slot = 0; slot <= episodes; slot++)
            {
                var carried = previous[slot] - ShiftGap;
                var continued = slot > 0 ? current[slot - 1] - ShiftGap : double.NegativeInfinity;

                var best = Math.Max(carried, continued);
                var move = carried >= continued ? (byte)3 : (byte)2;

                // The column is how many episodes have been walked, so the episode this pair takes is the last one
                // of the walk: the column minus one.
                if (slot >= first && slot <= last && !double.IsNegativeInfinity(previous[slot - 1]))
                {
                    var episode = free[slot - 1];
                    var score = previous[slot - 1]
                        + weights.Similarity(normalized[video], videoParts[video], titles[episode], parts[episode])
                        - ShiftDrift * Math.Abs(episode - expected[video]);

                    if (score > best)
                    {
                        best = score;
                        move = 1;
                    }
                }

                current[slot] = best;
                step[((video + 1) * stride) + slot] = move;
            }

            (previous, current) = (current, previous);
        }

        var end = 0;
        for (var slot = 1; slot <= episodes; slot++)
        {
            if (previous[slot] > previous[end])
            {
                end = slot;
            }
        }

        var path = new int[videos];
        Array.Fill(path, -1);

        var row = videos;
        var column = end;
        while (row > 0)
        {
            switch (step[(row * stride) + column])
            {
                case 1:
                    path[row - 1] = column - 1;
                    row--;
                    column--;
                    break;
                case 3:
                    row--;
                    break;
                case 2 when column > 0:
                    column--;
                    break;
                default:
                    row = 0;
                    break;
            }
        }

        return path;
    }

    /// <summary>
    /// Position of the list closest to a value, which is where the window of a video is centred.
    /// </summary>
    /// <param name="values">Ordered positions of the provider.</param>
    /// <param name="target">Position the counter announces.</param>
    /// <returns>The index of the closest position.</returns>
    private static int Nearest(List<int> values, int target)
    {
        var low = 0;
        var high = values.Count - 1;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (values[middle] < target)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low > 0 && Math.Abs(values[low - 1] - target) <= Math.Abs(values[low] - target) ? low - 1 : low;
    }
}
