using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Jellyfin.Plugin.ThreeCat.Sync;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.Providers;

/// <summary>
/// Reads the episodes of a series from TMDB, which is what lets the plugin number its files the way the
/// provider does and so have Jellyfin show the online sheet of every episode.
/// </summary>
/// <remarks>
/// <para>
/// The TMDB id never comes from a search: it comes from the identity dataset of <c>open-tv-ids</c> or from
/// a value typed by the user, and it is passed in. Only two endpoints are read — the series, to know its
/// seasons, and each season, to list its episodes — and the answer is cached for
/// <see cref="CacheDuration"/>, so a run of a hundred programs pays for each series once.
/// </para>
/// <para>
/// The API key is a secret of the user: it is read from the plugin configuration, it is sent either as the
/// <c>api_key</c> parameter (v3 key) or as a bearer token (v4 read token) and it is <b>never</b> written to
/// the log — the warnings carry the endpoint, not the address with the key.
/// </para>
/// <para>
/// A failure of TMDB never aborts the synchronization: the method answers with the episodes it could read,
/// which may be none, and the caller then falls back to the numbering of 3Cat.
/// </para>
/// </remarks>
public sealed class TmdbClient
{
    /// <summary>
    /// Base address of the API, which the registrator sets on the <see cref="HttpClient"/>.
    /// </summary>
    public const string BaseAddress = "https://api.themoviedb.org/3/";

    /// <summary>
    /// Language of the episode titles when the configuration does not set another one.
    /// </summary>
    /// <remarks>
    /// It only affects the titles, never the matching: the pairing goes by air date, which is the same in
    /// every language. <c>ca-ES</c> does not exist for most of the catalog of 3Cat, so Spanish is the
    /// default that returns something.
    /// </remarks>
    public const string DefaultLanguage = "es-ES";

    /// <summary>
    /// Seasons read at the same time. TMDB allows about fifty requests per second, so four is polite.
    /// </summary>
    private const int SeasonConcurrency = 4;

    /// <summary>
    /// Series kept in memory before the expired ones are swept out.
    /// </summary>
    private const int CacheCapacity = 256;

    /// <summary>
    /// How long the episodes of a series are kept. A season of an old series does not change; one that is
    /// on air changes when the next episode is published, and half a day is short enough.
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    /// <summary>
    /// Web defaults: the field names of TMDB are snake_case and the options make the serializer match them
    /// case-insensitively, like the rest of the models of the plugin.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);

    private readonly HttpClient _httpClient;
    private readonly ILogger<TmdbClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbClient"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client configured by <see cref="PluginServiceRegistrator"/>.</param>
    /// <param name="logger">Logger.</param>
    public TmdbClient(HttpClient httpClient, ILogger<TmdbClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Tells whether there is a key to read TMDB with.
    /// </summary>
    /// <param name="apiKey">Key or token typed by the user.</param>
    /// <returns><c>true</c> when the key is not empty.</returns>
    /// <remarks>
    /// Without a key the plugin keeps numbering with 3Cat: it is not an error, it is the default state of
    /// a fresh installation.
    /// </remarks>
    public static bool IsConfigured(string? apiKey)
        => !string.IsNullOrWhiteSpace(apiKey);

    /// <summary>
    /// Reads the episodes of a series, with the numbering and the air dates TMDB publishes.
    /// </summary>
    /// <param name="seriesId">TMDB id of the series.</param>
    /// <param name="apiKey">TMDB key (v3) or read token (v4).</param>
    /// <param name="language">Language of the titles; <see cref="DefaultLanguage"/> when empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The episodes of every season but the specials, ordered by season and episode, or an empty list when
    /// TMDB cannot be read. The method never throws for a network, key or payload failure.
    /// </returns>
    public async Task<IReadOnlyList<ProviderEpisode>> GetEpisodesAsync(
        string? seriesId,
        string? apiKey,
        string? language,
        CancellationToken cancellationToken)
    {
        var id = seriesId?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            return [];
        }

        var key = apiKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            _logger.LogDebug("TMDB: no hay clave configurada, no se lee la serie {SeriesId}.", id);
            return [];
        }

        var lang = string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language.Trim();
        var cacheKey = string.Concat(id, "|", lang);

        if (Cache.TryGetValue(cacheKey, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
        {
            return cached.Episodes;
        }

        var series = await GetJsonAsync<TmdbSeriesResponse>(SeriesUrl(id, lang), key, cancellationToken)
            .ConfigureAwait(false);

        if (series?.Seasons is not { Count: > 0 } seasons)
        {
            return [];
        }

        // The specials of the provider (season 0) are not emissions of 3Cat and would steal the match of a
        // regular episode: they are not even read.
        var wanted = seasons
            .Where(season => season.SeasonNumber > 0)
            .OrderBy(season => season.SeasonNumber)
            .ToList();

        var episodes = new ConcurrentBag<ProviderEpisode>();

        using var gate = new SemaphoreSlim(SeasonConcurrency);

        var requests = wanted.Select(async season =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var detail = await GetJsonAsync<TmdbSeasonDetailResponse>(
                    SeasonUrl(id, season.SeasonNumber, lang),
                    key,
                    cancellationToken).ConfigureAwait(false);

                foreach (var episode in detail?.Episodes ?? [])
                {
                    if (episode.SeasonNumber <= 0 || episode.EpisodeNumber <= 0)
                    {
                        continue;
                    }

                    episodes.Add(new ProviderEpisode(
                        episode.SeasonNumber,
                        episode.EpisodeNumber,
                        episode.Name,
                        ParseDate(episode.AirDate)));
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(requests).ConfigureAwait(false);

        var result = episodes
            .OrderBy(episode => episode.Season)
            .ThenBy(episode => episode.Episode)
            .ToList();

        if (result.Count == 0)
        {
            // An empty answer is not cached: a season TMDB is still filling is read again on the next run.
            return result;
        }

        Cache[cacheKey] = new CacheEntry(DateTimeOffset.UtcNow.Add(CacheDuration), result);
        Sweep();

        _logger.LogDebug(
            "TMDB: {Episodes} episodios de {Seasons} temporadas de la serie {SeriesId}.",
            result.Count,
            wanted.Count,
            id);

        return result;
    }

    /// <summary>
    /// Builds the relative URL of the series, without the key: <see cref="GetJsonAsync{T}"/> adds it, so it
    /// is never part of an address written to the log.
    /// </summary>
    /// <param name="seriesId">TMDB id of the series.</param>
    /// <param name="language">Language of the titles.</param>
    /// <returns>The relative URL.</returns>
    private static string SeriesUrl(string seriesId, string language)
        => string.Format(
            CultureInfo.InvariantCulture,
            "tv/{0}?language={1}",
            Uri.EscapeDataString(seriesId),
            Uri.EscapeDataString(language));

    /// <summary>
    /// Builds the relative URL of a season.
    /// </summary>
    /// <param name="seriesId">TMDB id of the series.</param>
    /// <param name="season">Season number.</param>
    /// <param name="language">Language of the titles.</param>
    /// <returns>The relative URL.</returns>
    private static string SeasonUrl(string seriesId, int season, string language)
        => string.Format(
            CultureInfo.InvariantCulture,
            "tv/{0}/season/{1}?language={2}",
            Uri.EscapeDataString(seriesId),
            season.ToString(CultureInfo.InvariantCulture),
            Uri.EscapeDataString(language));

    /// <summary>
    /// Reads an <c>air_date</c> of TMDB, which is a plain date (<c>yyyy-MM-dd</c>).
    /// </summary>
    /// <param name="value">Value published by TMDB.</param>
    /// <returns>The date, or <c>null</c> when TMDB publishes none or an empty string.</returns>
    private static DateOnly? ParseDate(string? value)
        => DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;

    /// <summary>
    /// Tells whether the key is a TMDB v4 read token, which travels as a bearer token, or a v3 key, which
    /// travels as the <c>api_key</c> parameter.
    /// </summary>
    /// <param name="apiKey">Key or token typed by the user.</param>
    /// <returns><c>true</c> when it is a read token.</returns>
    private static bool IsBearer(string apiKey)
        => apiKey.StartsWith("eyJ", StringComparison.Ordinal);

    /// <summary>
    /// Sends a GET request to TMDB and deserializes the answer, retrying the transient failures.
    /// </summary>
    /// <typeparam name="T">Type of the answer.</typeparam>
    /// <param name="relativeUrl">URL relative to <see cref="BaseAddress"/>, without the key.</param>
    /// <param name="apiKey">Key or token of the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The answer, or <c>null</c> when TMDB does not serve it.</returns>
    private async Task<T?> GetJsonAsync<T>(string relativeUrl, string apiKey, CancellationToken cancellationToken)
        where T : class
    {
        var bearer = IsBearer(apiKey);
        var address = bearer
            ? relativeUrl
            : string.Concat(
                relativeUrl,
                relativeUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?",
                "api_key=",
                Uri.EscapeDataString(apiKey));

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, address);
                if (bearer)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }

                using var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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

                if (!HttpRetryPolicy.IsTransient(response.StatusCode)
                    || attempt >= HttpRetryPolicy.MaxAttempts)
                {
                    // The address of the log is the one without the key.
                    _logger.LogWarning(
                        "TMDB respondió {Status} a {Url}.",
                        (int)response.StatusCode,
                        relativeUrl);

                    return null;
                }
            }
            catch (Exception ex) when (
                (ex is HttpRequestException || ex is TaskCanceledException)
                && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= HttpRetryPolicy.MaxAttempts)
                {
                    _logger.LogWarning(ex, "TMDB no respondió a {Url}.", relativeUrl);
                    return null;
                }
            }

            await Task.Delay(HttpRetryPolicy.Delay(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Drops the expired entries once the cache has grown past <see cref="CacheCapacity"/>.
    /// </summary>
    private static void Sweep()
    {
        if (Cache.Count < CacheCapacity)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in Cache)
        {
            if (entry.Value.Expires <= now)
            {
                Cache.TryRemove(entry.Key, out _);
            }
        }
    }

    /// <summary>
    /// The episodes of a series in the cache, with the instant they stop being fresh.
    /// </summary>
    /// <param name="Expires">Instant the entry expires.</param>
    /// <param name="Episodes">Episodes read from TMDB.</param>
    private readonly record struct CacheEntry(DateTimeOffset Expires, IReadOnlyList<ProviderEpisode> Episodes);
}
