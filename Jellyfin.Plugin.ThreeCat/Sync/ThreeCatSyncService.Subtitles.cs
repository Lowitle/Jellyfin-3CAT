using Jellyfin.Plugin.ThreeCat.Configuration;
using Jellyfin.Plugin.ThreeCat.Library;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Subtitle pipeline: picks the 3Cat tracks requested in the configuration and turns them into SRT.
/// </summary>
public sealed partial class ThreeCatSyncService
{
    private async Task<IReadOnlyList<LibrarySubtitle>> GetSubtitlesAsync(
        PluginConfiguration configuration,
        ThreeCatMedia? media,
        string videoId,
        CancellationToken cancellationToken)
    {
        if (!configuration.WriteSubtitles || media?.Subtitles is not { Count: > 0 } tracks)
        {
            return [];
        }

        // 3Cat publishes one track per language it has («Català» for most of the catalog, «Castellà» for
        // the dubbed programmes) and no video publishes more than a handful, so every track is written:
        // the page has no language picker and the user gets whatever 3Cat offers for each episode.
        var selection = SelectSubtitleTracks(tracks);
        if (selection.Count == 0)
        {
            _logger.LogInformation(
                "3Cat: el vídeo {VideoId} publica {Tracks} pista(s) de subtítulos sin URL descargable",
                videoId,
                tracks.Count);

            return [];
        }

        var subtitles = new List<LibrarySubtitle>(selection.Count);

        foreach (var track in selection)
        {
            var vtt = await _client.GetStringAsync(track.Source, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(vtt))
            {
                _logger.LogWarning(
                    "3Cat: no se pudo descargar el subtítulo {Language} del vídeo {VideoId} ({Url}); "
                    + "se reintentará en la próxima sincronización",
                    track.Language,
                    videoId,
                    track.Source);

                continue;
            }

            string srt;
            try
            {
                srt = VttToSrt.Convert(vtt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "3Cat: no se pudo convertir el subtítulo {Language} del vídeo {VideoId}",
                    track.Language,
                    videoId);

                continue;
            }

            if (srt.Length > 0)
            {
                subtitles.Add(new LibrarySubtitle(track.Language, srt));
            }
        }

        return subtitles;
    }

    /// <summary>
    /// Pairs the subtitle tracks published by 3Cat with the languages they are written in.
    /// </summary>
    /// <param name="tracks">Subtitle tracks of the video.</param>
    /// <returns>The tracks to download, at most one per language.</returns>
    /// <remarks>
    /// 3Cat publishes «Català (SPS)» tracks under <c>ca</c> but some videos arrive without an ISO code;
    /// those are written as Catalan, which is the language of the whole catalog. Two tracks of the same
    /// language (the same one in two qualities, for instance) are written once.
    /// </remarks>
    internal static IReadOnlyList<SubtitleSource> SelectSubtitleTracks(IReadOnlyList<ThreeCatSubtitle> tracks)
    {
        var selection = new List<SubtitleSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var track in tracks)
        {
            if (string.IsNullOrWhiteSpace(track.Url))
            {
                continue;
            }

            var language = TrackLanguage(track.Iso) ?? "ca";
            if (seen.Add(language))
            {
                selection.Add(new SubtitleSource(language, track.Url!));
            }
        }

        return selection;
    }

    /// <summary>
    /// Reduces a 3Cat language tag («ca», «ca-ES», «CA») to its primary subtag.
    /// </summary>
    /// <param name="value">Language tag.</param>
    /// <returns>The primary subtag, or <c>null</c> when the tag is missing.</returns>
    private static string? TrackLanguage(string? value)
    {
        var language = NormalizeLanguage(value);
        if (language is null)
        {
            return null;
        }

        var separator = language.IndexOf('-', StringComparison.Ordinal);
        return separator > 0 ? language[..separator] : language;
    }
}

/// <summary>
/// A subtitle track selected for download.
/// </summary>
/// <param name="Language">ISO language code used in the SRT file name.</param>
/// <param name="Source">WebVTT URL.</param>
public sealed record SubtitleSource(string Language, string Source);
