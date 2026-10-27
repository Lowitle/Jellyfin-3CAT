using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Media reads of <see cref="ThreeCatClient"/>.
/// </summary>
public sealed partial class ThreeCatClient
{
    internal const string MediaUrl =
        "https://api-media.3cat.cat/pvideo/media.jsp?media=video&idint={0}&profile=pc_3cat";

    /// <summary>
    /// Reads the detail of a video: metadata, MP4 URL and subtitle tracks.
    /// </summary>
    /// <param name="videoId">Video id (<c>idint</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The detail, or <c>null</c> when 3Cat does not serve it.</returns>
    public async Task<ThreeCatMedia?> GetMediaAsync(string videoId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return null;
        }

        var url = string.Format(
            CultureInfo.InvariantCulture,
            MediaUrl,
            Uri.EscapeDataString(videoId));

        var media = await GetJsonAsync<ThreeCatMedia>(url, cancellationToken).ConfigureAwait(false);

        if (media is not null && PickFile(media) is null)
        {
            // Not an error: 3Cat publishes the video without a stream when it is geoblocked, when
            // the rights expired or when it has been unpublished. The caller counts it and logs the
            // notice once per program.
            _logger.LogDebug(
                "3Cat no publica URL reproducible para el vídeo {VideoId} ({Rights}).",
                videoId,
                media.Info?.Rights?.Text ?? "sin aviso de derechos");
        }

        return media;
    }

    /// <summary>
    /// Gets the progressive MP4 URL of a video.
    /// </summary>
    /// <param name="media">Detail of the video.</param>
    /// <returns>The URL, or <c>null</c> when 3Cat publishes no playable file.</returns>
    public static string? PickFile(ThreeCatMedia? media)
    {
        var files = media?.Playback?.Files;
        if (files is null || files.Count == 0)
        {
            return null;
        }

        foreach (var file in files)
        {
            if (file.Active && !string.IsNullOrWhiteSpace(file.File))
            {
                return file.File;
            }
        }

        return files
            .Select(file => file.File)
            .FirstOrDefault(file => !string.IsNullOrWhiteSpace(file));
    }
}
