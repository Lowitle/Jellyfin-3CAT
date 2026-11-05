namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Alignment of the catalog of 3Cat with the episodes of a provider from the anchors typed by the user.
/// </summary>
/// <remarks>
/// <para>
/// An anchor pins one video of 3Cat to one episode of the provider. The rest of its season is then paired by
/// order: the next video is the next episode, the previous one is the previous episode, until the season of 3Cat
/// changes or another anchor claims the terrain. It is the answer to the case the older evidence cannot solve —
/// «Detectiu Conan»: 385 videos dubbed from 2001 to 2025 against 1.150 episodes of a single TMDB season, with
/// titles in Catalan on one side and in Spanish on the other.
/// </para>
/// <para>
/// Nothing around an anchor is invented: the fill stops at the first episode already used by another video and
/// at the first video already claimed by another pass, so the anchors are the only thing the user has to worry
/// about.
/// </para>
/// </remarks>
public static partial class ProviderNumbering
{
    /// <summary>
    /// Pairs the videos of 3Cat with the episodes of the provider around the anchors typed by the user.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="pool">Episodes of the provider that may be assigned.</param>
    /// <param name="used">Episodes already assigned, updated in place.</param>
    /// <param name="assigned">Episode assigned to every video, updated in place; -1 when it has none.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice, which no pass may claim.</param>
    /// <param name="anchors">Anchors typed by the user.</param>
    /// <param name="take">Assigns an episode to a video.</param>
    /// <param name="placed">Anchors that could be placed, updated in place.</param>
    /// <returns>The number of videos paired by this pass.</returns>
    private static int AlignByAnchors(
        IReadOnlyList<ProviderVideo> videos,
        List<ProviderEpisode> pool,
        bool[] used,
        int[] assigned,
        bool[] duplicates,
        IReadOnlyList<ProviderAnchor> anchors,
        Action<int, int, ProviderMatch> take,
        out int placed)
    {
        var matched = 0;
        placed = 0;

        // The anchors are applied from the oldest episode of the provider, so the fill of one anchor does not
        // take the episode another anchor is going to claim.
        var ordered = anchors.OrderBy(item => item.TargetSeason).ThenBy(item => item.TargetEpisode).ToList();
        var pinned = new List<(int Video, int Episode)>();

        // Pass 1: every anchor takes its own pair, before any fill. «T1=1; T2=43» has to place both pins even
        // though the run of T1 reaches the episode 43: the pin of T2 wins and the fill of T1 stops in front of it.
        foreach (var anchor in ordered)
        {
            var video = FindAnchorVideo(videos, duplicates, anchor);
            var episode = FindAnchorEpisode(pool, anchor);

            if (video < 0 || episode < 0 || assigned[video] >= 0 || used[episode])
            {
                continue;
            }

            take(video, episode, ProviderMatch.Anchor);
            matched++;
            placed++;
            pinned.Add((video, episode));
        }

        // Pass 2: the run of videos around every pin.
        foreach (var (video, episode) in pinned)
        {
            matched += FillAround(videos, pool, used, assigned, duplicates, video, episode, take);
        }

        return matched;
    }

    /// <summary>
    /// Numbers the videos around an anchored pair by walking the two lists in both directions.
    /// </summary>
    /// <param name="videos">Videos of 3Cat.</param>
    /// <param name="pool">Episodes of the provider.</param>
    /// <param name="used">Episodes already assigned, updated in place.</param>
    /// <param name="assigned">Episode assigned to every video, updated in place.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice.</param>
    /// <param name="video">Video of the anchor.</param>
    /// <param name="episode">Episode of the anchor.</param>
    /// <param name="take">Assigns an episode to a video.</param>
    /// <returns>The number of videos paired around the anchor.</returns>
    private static int FillAround(
        IReadOnlyList<ProviderVideo> videos,
        List<ProviderEpisode> pool,
        bool[] used,
        int[] assigned,
        bool[] duplicates,
        int video,
        int episode,
        Action<int, int, ProviderMatch> take)
    {
        var matched = 0;
        var season = SeasonOf(videos[video]);

        var nextVideo = video + 1;
        var nextEpisode = episode + 1;

        while (nextVideo < videos.Count && nextEpisode < pool.Count)
        {
            if (duplicates[nextVideo])
            {
                nextVideo++;
                continue;
            }

            if (assigned[nextVideo] >= 0 || used[nextEpisode] || LeavesTheSeason(season, SeasonOf(videos[nextVideo])))
            {
                break;
            }

            take(nextVideo, nextEpisode, ProviderMatch.Anchor);
            matched++;
            nextVideo++;
            nextEpisode++;
        }

        nextVideo = video - 1;
        nextEpisode = episode - 1;

        while (nextVideo >= 0 && nextEpisode >= 0)
        {
            if (duplicates[nextVideo])
            {
                nextVideo--;
                continue;
            }

            if (assigned[nextVideo] >= 0 || used[nextEpisode] || LeavesTheSeason(season, SeasonOf(videos[nextVideo])))
            {
                break;
            }

            take(nextVideo, nextEpisode, ProviderMatch.Anchor);
            matched++;
            nextVideo--;
            nextEpisode--;
        }

        return matched;
    }

    /// <summary>
    /// Tells whether a video belongs to another season of 3Cat than the anchor, which ends the fill.
    /// </summary>
    /// <param name="season">Season of the anchor, or <c>null</c> when its title carries no numbering.</param>
    /// <param name="other">Season of the video, or <c>null</c> when its title carries no numbering.</param>
    /// <returns><c>true</c> when the video is outside the season of the anchor.</returns>
    /// <remarks>
    /// A video without numbering does not stop the fill: 3Cat publishes a handful of them (a special, a recap)
    /// inside a season, and stopping there would leave the rest of the season unpaired.
    /// </remarks>
    private static bool LeavesTheSeason(int? season, int? other)
        => season is { } value && other is { } candidate && candidate != value;

    /// <summary>
    /// Finds the video an anchor names.
    /// </summary>
    /// <param name="videos">Videos of 3Cat, in air order.</param>
    /// <param name="duplicates">Videos 3Cat publishes twice.</param>
    /// <param name="anchor">Anchor to place.</param>
    /// <returns>The index of the video, or -1 when the anchor names none.</returns>
    /// <remarks>
    /// The season of a video is the one 3Cat writes in its title («T22xC6»), which is the numbering its website
    /// shows; <see cref="ProviderVideo.Chapter"/> is a counter over the whole program and is not used to address
    /// the videos.
    /// </remarks>
    private static int FindAnchorVideo(
        IReadOnlyList<ProviderVideo> videos,
        bool[] duplicates,
        ProviderAnchor anchor)
    {
        for (var index = 0; index < videos.Count; index++)
        {
            if (duplicates[index] || SeasonOf(videos[index]) is not { } season || season != anchor.Season)
            {
                continue;
            }

            if (anchor.Chapter is not { } chapter
                || (EpisodeNumbering.Parse(videos[index].Title) is { } numbering && numbering.Episode == chapter))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds the episode of the provider an anchor names.
    /// </summary>
    /// <param name="pool">Episodes of the provider, in the order of its numbering.</param>
    /// <param name="anchor">Anchor to place.</param>
    /// <returns>The index of the episode, or -1 when the provider publishes none with that number.</returns>
    private static int FindAnchorEpisode(List<ProviderEpisode> pool, ProviderAnchor anchor)
    {
        for (var index = 0; index < pool.Count; index++)
        {
            if (pool[index].Season == anchor.TargetSeason && pool[index].Episode == anchor.TargetEpisode)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Reads the season 3Cat writes in the title of a video.
    /// </summary>
    /// <param name="video">Video of 3Cat.</param>
    /// <returns>The season, or <c>null</c> when the title announces none.</returns>
    private static int? SeasonOf(ProviderVideo video)
        => EpisodeNumbering.Parse(video.Title)?.Season;
}
