using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// HTTP plumbing of <see cref="ThreeCatClient"/>: JSON and text reads, retrying the transient
/// failures (3Cat throttles bursts of demand with 429/503 answers).
/// </summary>
public sealed partial class ThreeCatClient
{
    /// <summary>
    /// Sends a GET request and deserializes the JSON answer, retrying the transient failures.
    /// </summary>
    /// <typeparam name="T">Type of the answer.</typeparam>
    /// <param name="url">Absolute URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The answer, or <c>null</c> when 3Cat does not serve it.</returns>
    internal async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await _httpClient
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content
                        .ReadAsStreamAsync(cancellationToken)
                        .ConfigureAwait(false);

                    return await JsonSerializer
                        .DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!HttpRetryPolicy.IsTransient(response.StatusCode) || attempt >= HttpRetryPolicy.MaxAttempts)
                {
                    _logger.LogWarning("3Cat respondió {Status} a {Url}.", (int)response.StatusCode, url);
                    return null;
                }
            }
            catch (Exception ex) when (
                (ex is HttpRequestException || ex is TaskCanceledException)
                && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= HttpRetryPolicy.MaxAttempts)
                {
                    _logger.LogWarning(ex, "3Cat no respondió a {Url}.", url);
                    return null;
                }
            }

            await Task.Delay(HttpRetryPolicy.Delay(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads a text resource (the WebVTT subtitles).
    /// </summary>
    /// <param name="url">Absolute URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The body, or <c>null</c> when it cannot be read.</returns>
    public async Task<string?> GetStringAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await _httpClient
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }

                if (response.StatusCode == HttpStatusCode.NotFound
                    || !HttpRetryPolicy.IsTransient(response.StatusCode)
                    || attempt >= HttpRetryPolicy.MaxAttempts)
                {
                    _logger.LogDebug("3Cat respondió {Status} a {Url}.", (int)response.StatusCode, url);
                    return null;
                }
            }
            catch (Exception ex) when (
                (ex is HttpRequestException || ex is TaskCanceledException)
                && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= HttpRetryPolicy.MaxAttempts)
                {
                    _logger.LogDebug(ex, "No se pudo leer {Url}.", url);
                    return null;
                }
            }

            await Task.Delay(HttpRetryPolicy.Delay(attempt), cancellationToken).ConfigureAwait(false);
        }
    }
}
