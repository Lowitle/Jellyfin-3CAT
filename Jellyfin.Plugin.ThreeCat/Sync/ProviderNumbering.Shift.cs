namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// The shift between the counter of 3Cat and the absolute numbering of the provider, read from the titles.
/// </summary>
/// <remarks>
/// It is what makes the counter survive a catalog that splits episodes: the counter says where every video would
/// go if the two catalogs counted the same episodes, and the titles say how far that is from the truth, one run of
/// videos at a time.
/// </remarks>
public static partial class ProviderNumbering
{
    /// <summary>
    /// A video that found a title it looks like, and how far it is from the position the counter announces.
    /// </summary>
    /// <param name="Index">Video of 3Cat.</param>
    /// <param name="Shift">Positions the episode is away from the one the counter announces; negative is behind.</param>
    /// <param name="Score">How much the two titles look alike.</param>
    /// <param name="Margin">Distance to the second best candidate, which is what makes the vote name one episode.</param>
    internal readonly record struct CounterVote(int Index, int Shift, double Score, double Margin);

    /// <summary>
    /// Reads the shift of every video from the titles of the two catalogs.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="pool">Episodes of the provider.</param>
    /// <param name="used">Episodes already assigned, which no vote may claim.</param>
    /// <param name="assigned">Episode assigned to every video, or -1.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice.</param>
    /// <param name="offset">Difference the anchors deduced between the counter and the provider.</param>
    /// <param name="seriesTitle">Title of the series, dropped from the titles of 3Cat before comparing.</param>
    /// <returns>The shift of every video and the number of votes it was read from.</returns>
    /// <remarks>
    /// A shift of 0 for every video is the answer when the titles say nothing —a dub that rewrote them all—, and it
    /// is also the answer when the two catalogs count the same episodes: then the counter is used as it comes.
    /// </remarks>
    internal static (int[] Shifts, int Votes) CounterShifts(
        IReadOnlyList<ProviderVideo> videos,
        List<ProviderEpisode> pool,
        bool[] used,
        int[] assigned,
        bool[] duplicates,
        int offset,
        string? seriesTitle)
    {
        var shifts = new int[videos.Count];
        if (pool.Count == 0)
        {
            return (shifts, 0);
        }

        var titles = new string?[pool.Count];
        var parts = new int?[pool.Count];
        for (var index = 0; index < pool.Count; index++)
        {
            titles[index] = NormalizeTitle(pool[index].Title, null);
            parts[index] = PartNumber(pool[index].Title);
        }

        var normalized = new string?[videos.Count];
        for (var index = 0; index < videos.Count; index++)
        {
            normalized[index] = NormalizeTitle(videos[index].Title, seriesTitle);
        }

        if (titles.All(title => string.IsNullOrEmpty(title)))
        {
            return (shifts, 0);
        }

        var weights = TitleWeights.Learn(normalized.Concat(titles));
        var votes = Votes(videos, pool, used, assigned, duplicates, offset, normalized, titles, parts, weights);

        if (votes.Count < MinimumKeywordVotes)
        {
            return (shifts, votes.Count);
        }

        // A vote its neighbours do not share is a coincidence —«trobada d'antics alumnes» and «del anticuario» share
        // a word— and it would turn into a step of its own; the neighbours are what tell the two apart.
        var kept = new List<CounterVote>();
        for (var index = 0; index < votes.Count; index++)
        {
            for (var other = Math.Max(0, index - 2); other <= Math.Min(votes.Count - 1, index + 2); other++)
            {
                if (other != index && votes[other].Shift == votes[index].Shift)
                {
                    kept.Add(votes[index]);
                    break;
                }
            }
        }

        if (kept.Count == 0)
        {
            return (shifts, votes.Count);
        }

        for (var index = 0; index < kept.Count; index++)
        {
            var vote = kept[index];
            var from = index == 0 ? 0 : Boundary(videos, kept[index - 1], vote) + 1;
            var to = index == kept.Count - 1
                ? videos.Count - 1
                : Boundary(videos, vote, kept[index + 1]);

            for (var video = from; video <= to && video < videos.Count; video++)
            {
                shifts[video] = vote.Shift;
            }
        }

        return (shifts, votes.Count);
    }

    /// <summary>
    /// Every video that found a title it looks like, in the order of the catalog.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="pool">Episodes of the provider.</param>
    /// <param name="used">Episodes already assigned.</param>
    /// <param name="assigned">Episode assigned to every video, or -1.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice.</param>
    /// <param name="offset">Difference the anchors deduced between the counter and the provider.</param>
    /// <param name="normalized">Normalized title of every video.</param>
    /// <param name="titles">Normalized title of every episode of the pool.</param>
    /// <param name="parts">Part of every episode of the pool, or <c>null</c>.</param>
    /// <param name="weights">Weight of every word of the program.</param>
    /// <returns>The votes, ordered by video.</returns>
    private static List<CounterVote> Votes(
        IReadOnlyList<ProviderVideo> videos,
        List<ProviderEpisode> pool,
        bool[] used,
        int[] assigned,
        bool[] duplicates,
        int offset,
        string?[] normalized,
        string?[] titles,
        int?[] parts,
        TitleWeights weights)
    {
        var votes = new List<CounterVote>();

        for (var index = 0; index < videos.Count; index++)
        {
            if (duplicates[index]
                || assigned[index] >= 0
                || videos[index].Chapter is not { } counter
                || counter <= 0
                || string.IsNullOrEmpty(normalized[index]))
            {
                continue;
            }

            var position = counter - 1 + offset;
            var videoPart = PartNumber(videos[index].Title);
            var first = Math.Max(0, position - KeywordWindow);
            var last = Math.Min(pool.Count - 1, position + KeywordWindow);

            var best = 0.0;
            var bestIndex = -1;
            var second = 0.0;

            for (var candidate = first; candidate <= last; candidate++)
            {
                if (used[candidate] || string.IsNullOrEmpty(titles[candidate]))
                {
                    continue;
                }

                var score = weights.Similarity(normalized[index], videoPart, titles[candidate], parts[candidate]);
                if (score > best)
                {
                    second = best;
                    best = score;
                    bestIndex = candidate;
                }
                else if (score > second)
                {
                    second = score;
                }
            }

            if (bestIndex >= 0 && best >= MinimumKeywordScore && best - second >= MinimumKeywordMargin)
            {
                votes.Add(new CounterVote(index, bestIndex - position, best, best - second));
            }
        }

        return votes;
    }

    /// <summary>
    /// Last video of the run of the left vote, which is where the next run begins.
    /// </summary>
    /// <param name="videos">Videos of 3Cat, in air order.</param>
    /// <param name="left">Vote that opens the run.</param>
    /// <param name="right">Vote that closes it.</param>
    /// <returns>The index of the last video of the run.</returns>
    /// <remarks>
    /// A step does not begin halfway between two votes: it begins at the video that 3Cat emits as the second part
    /// of an episode the provider lists once, which is the video that has no episode of its own. Without this, the
    /// two parts of «Clar de lluna» are numbered as if they were two episodes, and both are wrong.
    /// </remarks>
    private static int Boundary(IReadOnlyList<ProviderVideo> videos, CounterVote left, CounterVote right)
    {
        for (var index = left.Index + 1; index < right.Index; index++)
        {
            if (PartNumber(videos[index].Title) > 1)
            {
                return index - 1;
            }
        }

        return (left.Index + right.Index) / 2;
    }
}
