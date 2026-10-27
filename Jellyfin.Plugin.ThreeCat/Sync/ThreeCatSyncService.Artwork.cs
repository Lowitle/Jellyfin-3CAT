using Jellyfin.Plugin.ThreeCat.Library;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Series artwork and cleanup of the episodes that 3Cat has expired.
/// </summary>
public sealed partial class ThreeCatSyncService
{
    /// <summary>
    /// Saves the poster, the fanart and the logo of the program in the series folder. Jellyfin reads
    /// them as local images, so the library shows artwork without any online metadata provider.
    /// </summary>
    /// <param name="program">The <c>programes_tv</c> node of the catalog.</param>
    /// <param name="seriesPath">Series folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task SaveArtworkAsync(
        ThreeCatProgram program,
        string seriesPath,
        CancellationToken cancellationToken)
    {
        var images = program.Images;
        if (images is null || images.Count == 0)
        {
            return;
        }

        await SaveImageAsync(PickImage(images, "IMG_POSTER"), seriesPath, "poster", cancellationToken)
            .ConfigureAwait(false);

        await SaveImageAsync(PickImage(images, "IMATGE_169", "IMAGE"), seriesPath, "fanart", cancellationToken)
            .ConfigureAwait(false);

        await SaveImageAsync(PickImage(images, "IMG_LOGO"), seriesPath, "logo", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SaveImageAsync(
        string? url,
        string folder,
        string name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var path = Path.Combine(folder, name + ImageExtension(url));
        try
        {
            await _client.DownloadIfMissingAsync(url, path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "3Cat: no se pudo guardar la imagen {Name} de {Series}", name, folder);
        }
    }

    /// <summary>
    /// Picks the biggest image of the requested role, falling back to the next role of the list.
    /// </summary>
    /// <param name="images">Images of the program.</param>
    /// <param name="relations">Roles to try, most important first.</param>
    /// <returns>The image URL, or <c>null</c> when none matches.</returns>
    private static string? PickImage(IEnumerable<ThreeCatImage> images, params string[] relations)
    {
        foreach (var relation in relations)
        {
            var candidates = images
                .Where(image => !string.IsNullOrWhiteSpace(image.Url)
                    && string.Equals(image.Relation, relation, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count > 0)
            {
                return candidates
                    .OrderByDescending(image => SizeRank(image.Size))
                    .Select(image => image.Url)
                    .First();
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the extension Jellyfin expects for a local image. The extension is kept from the URL so
    /// the file name and the content always agree.
    /// </summary>
    /// <param name="url">Image URL.</param>
    /// <returns>The extension, including the dot.</returns>
    private static string ImageExtension(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (extension is ".jpg" or ".jpeg" or ".png" or ".webp")
            {
                return extension;
            }
        }

        return ".jpg";
    }

    /// <summary>
    /// Deletes the files of the episodes that 3Cat has expired. The catalog keeps publishing them
    /// until the program is refreshed, but their MP4 answers 404 from the expiry date on.
    /// </summary>
    /// <param name="seriesPath">Series folder.</param>
    /// <param name="seriesTitle">Series title, used to rebuild the file name.</param>
    /// <param name="expired">Expired episodes.</param>
    /// <returns>The number of files removed.</returns>
    private static int PruneExpiredFiles(
        string seriesPath,
        string seriesTitle,
        List<(int Season, int Episode, string Title)> expired)
    {
        if (expired.Count == 0 || !Directory.Exists(seriesPath))
        {
            return 0;
        }

        var removed = 0;

        foreach (var (season, episode, title) in expired)
        {
            var seasonFolder = Path.Combine(seriesPath, $"Season {season}");
            if (!Directory.Exists(seasonFolder))
            {
                continue;
            }

            var baseName = LibraryNaming.EpisodeFileName(seriesTitle, season, episode, title);

            // The pattern also matches the « [2]» suffix that StrmLibrary appends to a repeated name.
            foreach (var file in Directory.EnumerateFiles(seasonFolder, baseName + "*"))
            {
                if (Path.GetExtension(file) is not (".strm" or ".nfo" or ".srt"))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The next run retries; a locked file must not abort the whole synchronization.
                }
            }
        }

        return removed;
    }
}
