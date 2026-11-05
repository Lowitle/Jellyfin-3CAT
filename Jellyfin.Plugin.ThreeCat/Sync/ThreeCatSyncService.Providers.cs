using Jellyfin.Plugin.ThreeCat.Configuration;
using Jellyfin.Plugin.ThreeCat.Providers;
using Jellyfin.Plugin.ThreeCat.ThreeCat;

namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Provider level synchronization: decides the source every program is numbered with and pairs its videos
/// with the episodes of that provider.
/// </summary>
public sealed partial class ThreeCatSyncService
{
    /// <summary>
    /// The numbering a program is going to be written with, and the external ids that go with it.
    /// </summary>
    /// <param name="Source">Source the episodes are numbered with; it decides the <c>uniqueid</c> written.</param>
    /// <param name="Slots">Slot of every video of the program.</param>
    /// <param name="TmdbId">TMDB id, from the user or from the identity dataset.</param>
    /// <param name="TvdbId">TVDB id, from the user or from the identity dataset.</param>
    /// <param name="ImdbId">IMDb id published by the identity dataset.</param>
    /// <param name="Provider">
    /// Result of the matching, with the evidence of every video, or <c>null</c> when the series is numbered
    /// with 3Cat because that is the source or because the provider was not read.
    /// </param>
    /// <param name="Note">Why the numbering of 3Cat is kept, ready for the log; <c>null</c> when it is not.</param>
    private sealed record ProgramNumbering(
        MetadataSource Source,
        IReadOnlyList<EpisodeSlot> Slots,
        string? TmdbId,
        string? TvdbId,
        string? ImdbId,
        ProviderNumberingResult? Provider,
        string? Note);

    /// <summary>
    /// Decides the source of a program and numbers its videos with it.
    /// </summary>
    /// <param name="configuration">Plugin configuration.</param>
    /// <param name="reference">Program reference, with the source and the ids typed by the user.</param>
    /// <param name="programId">3Cat program id (<c>programatv_id</c>).</param>
    /// <param name="seriesTitle">Title of the series, used to compare the titles.</param>
    /// <param name="ordered">Videos of the program, in air order.</param>
    /// <param name="dataset">Identity dataset of <c>open-tv-ids</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The numbering and the ids to write.</returns>
    /// <remarks>
    /// The numbering of 3Cat is always computed, because it is the default and the fallback of everything
    /// else: a missing id, a missing key, a TMDB that does not answer or a coverage below the threshold all
    /// end in the same place —the series is written as it was before the option existed— and never in a
    /// series half numbered with the provider.
    /// </remarks>
    private async Task<ProgramNumbering> ResolveNumberingAsync(
        PluginConfiguration configuration,
        ThreeCatProgramReference reference,
        string programId,
        string seriesTitle,
        IReadOnlyList<ThreeCatVideo> ordered,
        OpenTvIdsDataset dataset,
        CancellationToken cancellationToken)
    {
        var titles = ordered
            .Select(video => FirstNonEmptyOrNull(video.Title, video.FriendlyName))
            .ToList();

        var threeCat = EpisodeNumbering.Assign(titles);

        var record = dataset.Find(programId)?.Ids;
        var tmdbId = PickId(reference.TmdbId, record?.Tmdb);
        var tvdbId = PickId(reference.TvdbId, record?.Tvdb);
        var imdbId = PickId(null, record?.Imdb);
        var preferred = reference.Override ?? configuration.MetadataSource;

        ProgramNumbering Fallback(string note)
            => new(MetadataSource.ThreeCat, threeCat, tmdbId, tvdbId, imdbId, null, note);

        if (preferred != MetadataSource.Tmdb)
        {
            return preferred == MetadataSource.Tvdb
                // TVDB numbering arrives with its own client (phase C). Until then the series keeps the
                // numbering of 3Cat and no tvdb uniqueid is written: it would make Jellyfin ask for episodes
                // the files do not follow.
                ? Fallback("la numeración de TVDB todavía no está disponible")
                : new ProgramNumbering(MetadataSource.ThreeCat, threeCat, tmdbId, tvdbId, imdbId, null, null);
        }

        if (tmdbId is null)
        {
            return Fallback("el dataset no resuelve un id de TMDB y no hay ninguno escrito a mano");
        }

        if (!TmdbClient.IsConfigured(configuration.TmdbApiKey))
        {
            return Fallback("no hay clave de TMDB configurada");
        }

        var episodes = await _tmdb
            .GetEpisodesAsync(tmdbId, configuration.TmdbApiKey, configuration.TmdbLanguage, cancellationToken)
            .ConfigureAwait(false);

        if (episodes.Count == 0)
        {
            return Fallback($"TMDB no publica los episodios de la serie {tmdbId}");
        }

        var videos = ordered
            .Select(video => new ProviderVideo(
                FirstNonEmptyOrNull(video.Title, video.FriendlyName),
                video.Broadcast ?? video.Published,
                video.Id,
                video.Chapter))
            .ToList();

        var provider = ProviderNumbering.Assign(
            videos,
            episodes,
            seriesTitle,
            ProviderAnchors.Parse(reference.Anchors));
        if (provider.UseThreeCat)
        {
            return Fallback($"la numeración de TMDB no cubre la serie ({provider.Reason})");
        }

        return new ProgramNumbering(MetadataSource.Tmdb, provider.Slots, tmdbId, tvdbId, imdbId, provider, null);
    }

    /// <summary>
    /// Picks an external id: the one written by the user wins over the one of the dataset.
    /// </summary>
    /// <param name="manual">Id typed by the user.</param>
    /// <param name="dataset">Id published by the dataset.</param>
    /// <returns>The id to use, or <c>null</c> when there is none.</returns>
    /// <remarks>
    /// The manual value is never validated against the network: it is a decision of the user and it is how a
    /// wrong or missing id of the dataset is corrected from the configuration page.
    /// </remarks>
    private static string? PickId(string? manual, string? dataset)
        => !string.IsNullOrWhiteSpace(manual)
            ? manual.Trim()
            : string.IsNullOrWhiteSpace(dataset) ? null : dataset.Trim();
}
