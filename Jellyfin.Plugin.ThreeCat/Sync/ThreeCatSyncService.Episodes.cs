using Jellyfin.Plugin.ThreeCat.Configuration;
using Jellyfin.Plugin.ThreeCat.Library;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Episode level synchronization: resolves the stream URL and the metadata of every video.
/// </summary>
public sealed partial class ThreeCatSyncService
{
    private async Task<LibraryEpisode?> BuildEpisodeAsync(
        PluginConfiguration configuration,
        string seriesTitle,
        ThreeCatVideo video,
        int season,
        int number,
        CancellationToken cancellationToken)
    {
        var videoId = video.Id!;

        // The catalog publishes the title, the plot and the duration, but not the stream URL nor the
        // subtitle URLs: both only exist in the media endpoint, so one request per episode is needed.
        var media = await _client.GetMediaAsync(videoId, cancellationToken).ConfigureAwait(false);
        var info = media?.Info;
        var url = ThreeCatClient.PickFile(media);
        var title = FirstNonEmpty(info?.Title, video.Title, video.FriendlyName, $"Episodio {number}");

        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogWarning(
                "3Cat: el vídeo {VideoId} de «{Series}» ({Title}) no se puede reproducir: {Reason}. "
                + "Se omite y se reintenta en la próxima actualización",
                videoId,
                seriesTitle,
                title,
                info?.Rights?.Text
                    ?? "3Cat no publica ninguna URL, normalmente porque ha caducado o está geobloqueado");

            return null;
        }

        var subtitles = await GetSubtitlesAsync(configuration, media, videoId, cancellationToken)
            .ConfigureAwait(false);

        return new LibraryEpisode
        {
            Season = season,
            Episode = number,
            Title = StripSeriesPrefix(title, seriesTitle),
            Plot = HtmlText.FromHtml(info?.Description),
            Url = url,
            Subtitles = subtitles,
            Aired = info?.Broadcast ?? video.Broadcast ?? video.Published,

            // informacio.durada.milisegons is the reliable duration; the catalog «durada» arrives as
            // hh:mm:ss:ff and 3Cat itself flags it as unreliable.
            Runtime = info?.Duration ?? video.Duration,
            ThumbnailUrl = PickThumbnail(video, media),
            VideoId = videoId
        };
    }

    /// <summary>
    /// Picks the biggest frame available: the media endpoint publishes the images with their size,
    /// and the catalog tags them («MASTER», «1280x720»...).
    /// </summary>
    /// <param name="video">Catalog video.</param>
    /// <param name="media">Detail of the video.</param>
    /// <returns>The image URL, or <c>null</c> when 3Cat publishes none.</returns>
    internal static string? PickThumbnail(ThreeCatVideo video, ThreeCatMedia? media)
    {
        var fromMedia = media?.Images?
            .Where(image => !string.IsNullOrWhiteSpace(image.Url))
            .OrderByDescending(image => (long)(image.Width ?? 0) * (image.Height ?? 0))
            .Select(image => image.Url)
            .FirstOrDefault();

        if (fromMedia is not null)
        {
            return fromMedia;
        }

        var keyVideo = video.Images?
            .Where(image => !string.IsNullOrWhiteSpace(image.Url)
                && string.Equals(image.Relation, "KEYVIDEO", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(image => SizeRank(image.Size))
            .Select(image => image.Url)
            .FirstOrDefault();

        return keyVideo ?? video.Images?
            .Where(image => !string.IsNullOrWhiteSpace(image.Url))
            .OrderByDescending(image => SizeRank(image.Size))
            .Select(image => image.Url)
            .FirstOrDefault();
    }
}
