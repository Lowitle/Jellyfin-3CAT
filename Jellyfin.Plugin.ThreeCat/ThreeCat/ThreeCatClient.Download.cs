using System.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// File downloads of <see cref="ThreeCatClient"/>.
/// </summary>
public sealed partial class ThreeCatClient
{
    /// <summary>
    /// Downloads a file unless it already exists with content.
    /// </summary>
    /// <param name="url">Absolute URL.</param>
    /// <param name="path">Destination file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when a file was written.</returns>
    /// <remarks>
    /// The download goes to a temporary file first, so an interrupted transfer never leaves a
    /// half-written image that the next run would consider valid.
    /// </remarks>
    public async Task<bool> DownloadIfMissingAsync(
        string? url,
        string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            return false;
        }

        for (var attempt = 1; ; attempt++)
        {
            var temporary = path + ".tmp";

            try
            {
                using var response = await _httpClient
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    await using (var source = await response.Content
                        .ReadAsStreamAsync(cancellationToken)
                        .ConfigureAwait(false))
                    await using (var target = File.Create(temporary))
                    {
                        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                    }

                    File.Move(temporary, path, overwrite: true);
                    return true;
                }

                if (!HttpRetryPolicy.IsTransient(response.StatusCode) || attempt >= HttpRetryPolicy.MaxAttempts)
                {
                    _logger.LogDebug(
                        "No se pudo descargar {Url}: 3Cat respondió {Status}.",
                        url,
                        (int)response.StatusCode);
                    return false;
                }
            }
            catch (Exception ex) when (
                (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException)
                && !cancellationToken.IsCancellationRequested)
            {
                DeleteQuietly(temporary);

                if (attempt >= HttpRetryPolicy.MaxAttempts)
                {
                    _logger.LogWarning(ex, "No se pudo descargar {Url}.", url);
                    return false;
                }
            }

            await Task.Delay(HttpRetryPolicy.Delay(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next attempt overwrites the temporary file anyway.
        }
    }
}
