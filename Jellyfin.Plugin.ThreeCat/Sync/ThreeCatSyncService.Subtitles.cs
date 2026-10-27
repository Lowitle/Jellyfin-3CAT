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

        var languages = NormalizeLanguages(configuration.SubtitleLanguages);
        if (!configuration.DownloadAllSubtitleLanguages && languages.Count == 0)
        {
            _logger.LogDebug(
                "3Cat: la descarga de subtítulos está activa pero no hay idiomas marcados; "
                + "no se escribe ningún .srt del vídeo {VideoId}",
                videoId);

            return [];
        }

        var selection = SelectSubtitleTracks(tracks, languages, configuration.DownloadAllSubtitleLanguages);
        if (selection.Count == 0)
        {
            _logger.LogInformation(
                "3Cat: el vídeo {VideoId} publica subtítulos en {Published} pero ninguno coincide con los idiomas configurados ({Requested})",
                videoId,
                string.Join(", ", tracks.Select(track => track.Iso).Where(iso => !string.IsNullOrWhiteSpace(iso))),
                string.Join(", ", languages));

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
    /// Pairs the requested languages with the subtitle tracks published by 3Cat.
    /// </summary>
    /// <param name="tracks">Subtitle tracks of the video.</param>
    /// <param name="languages">Requested languages (already normalized).</param>
    /// <param name="allLanguages">When <c>true</c> every published track is selected.</param>
    /// <returns>The tracks to download, at most one per language.</returns>
    internal static IReadOnlyList<SubtitleSource> SelectSubtitleTracks(
        IReadOnlyList<ThreeCatSubtitle> tracks,
        IReadOnlyList<string> languages,
        bool allLanguages)
    {
        var usable = tracks.Where(track => !string.IsNullOrWhiteSpace(track.Url)).ToList();
        if (usable.Count == 0)
        {
            return [];
        }

        var selection = new List<SubtitleSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (allLanguages)
        {
            foreach (var track in usable)
            {
                var language = TrackLanguage(track.Iso) ?? "ca";
                if (seen.Add(language))
                {
                    selection.Add(new SubtitleSource(language, track.Url!));
                }
            }

            return selection;
        }

        foreach (var language in languages)
        {
            var track = usable.FirstOrDefault(
                candidate => string.Equals(TrackLanguage(candidate.Iso), language, StringComparison.OrdinalIgnoreCase));

            // 3Cat publishes «Català (SPS)» tracks under «ca» but some videos arrive without an iso
            // code: when a single language is configured and a single track is published, keep it
            // under the requested name.
            track ??= usable.Count == 1 && languages.Count == 1 ? usable[0] : null;

            if (track is null || !seen.Add(language))
            {
                continue;
            }

            selection.Add(new SubtitleSource(language, track.Url!));
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
