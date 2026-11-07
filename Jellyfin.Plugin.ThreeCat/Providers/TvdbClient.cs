using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.ThreeCat.Sync;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.Providers;

/// <summary>
/// Reads the episodes of a series from TVDB, which is what lets the plugin number its files the way TVDB
/// does —in the airing order or in the absolute one— and so have Jellyfin show the online sheet of every
/// episode.
/// </summary>
/// <remarks>
/// <para>
/// The TVDB id never comes from a search: it comes from the identity dataset of <c>open-tv-ids</c> or from a
/// value typed by the user, and it is passed in. Only one endpoint is read —
/// <c>series/{id}/episodes/{season-type}</c>, page by page — and the answer is cached for
/// <see cref="CacheDuration"/>, so a run of a hundred programs pays for each series once.
/// </para>
/// <para>
/// Unlike TMDB, TVDB needs a session: the key is exchanged for a bearer token in <c>POST login</c> and the
/// token lasts a month, so it is kept in memory and reused by every run of the server. The key and the PIN
/// are secrets of the user: they are never written to the log, the warnings carry the endpoint and the
/// status, never the address with the key.
/// </para>
/// <para>
/// A failure of TVDB never aborts the synchronization: the method answers with the episodes it could read,
/// which may be none, and the caller then falls back to the numbering of 3Cat.
/// </para>
/// </remarks>
public sealed class TvdbClient
{
    /// <summary>
    /// Base address of the API, which the registrator sets on the <see cref="HttpClient"/>.
    /// </summary>
    public const string BaseAddress = "https://api4.thetvdb.com/v4/";

    /// <summary>
    /// Order TVDB publishes when nothing else is asked: the aired order, with the seasons of the broadcast.
    /// </summary>
    public const string DefaultOrder = "default";

    /// <summary>
    /// Order that lists every episode of the work once, one after the other, without seasons.
    /// </summary>
    public const string AbsoluteOrder = "absolute";

    /// <summary>
    /// Pages read for one series before giving up, which at a hundred episodes per page is ten thousand
    /// episodes: a series with more is a mistake of the id, not a list to walk.
    /// </summary>
    private const int MaxPages = 100;

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
    /// How long a token is trusted. TVDB gives it a month; the margin keeps a run from starting with a token
    /// that expires while it is pairing a thousand episodes.
    /// </summary>
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(29);

    /// <summary>
    /// Web defaults: the field names of TVDB are camelCase and the options make the serializer match them
    /// case-insensitively, like the rest of the models of the plugin.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Tokens of the server, by key and PIN: the plugin logs in once a month, not once per series.
    /// </summary>
    private static readonly ConcurrentDictionary<string, TokenEntry> Tokens = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Serializes the logins: a refresh that starts twenty programs at once must not ask for twenty tokens.
    /// </summary>
    private static readonly SemaphoreSlim LoginGate = new(1, 1);

    private readonly HttpClient _httpClient;
    private readonly ILogger<TvdbClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvdbClient"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client configured by <see cref="PluginServiceRegistrator"/>.</param>
    /// <param name="logger">Logger.</param>
    public TvdbClient(HttpClient httpClient, ILogger<TvdbClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Tells whether there is a key to read TVDB with.
    /// </summary>
    /// <param name="apiKey">Key typed by the user.</param>
    /// <returns><c>true</c> when the key is not empty.</returns>
    /// <remarks>
    /// Without a key the plugin keeps numbering with 3Cat: it is not an error, it is the default state of
    /// a fresh installation.
    /// </remarks>
    public static bool IsConfigured(string? apiKey)
        => !string.IsNullOrWhiteSpace(apiKey);

    /// <summary>
    /// Reads the episodes of a series, with the numbering and the air dates TVDB publishes.
    /// </summary>
    /// <param name="seriesId">TVDB id of the series.</param>
    /// <param name="apiKey">Key of the user.</param>
    /// <param name="pin">Subscriber PIN, when the key is subscriber supported.</param>
    /// <param name="absolute">
    /// <c>true</c> to read the absolute order (one season, one episode per work); <c>false</c> for the aired
    /// order.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The episodes, ordered by season and episode, or an empty list when TVDB cannot be read. The method
    /// never throws for a network, key or payload failure.
    /// </returns>
    public async Task<IReadOnlyList<ProviderEpisode>> GetEpisodesAsync(
        string? seriesId,
        string? apiKey,
        string? pin,
        bool absolute,
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
            _logger.LogDebug("TVDB: no hay clave configurada, no se lee la serie {SeriesId}.", id);
            return [];
        }

        var subscriberPin = string.IsNullOrWhiteSpace(pin) ? null : pin.Trim();
        var cacheKey = string.Concat(id, absolute ? "|absolute" : "|default");

        if (Cache.TryGetValue(cacheKey, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
        {
            return cached.Episodes;
        }

        var order = absolute ? AbsoluteOrder : DefaultOrder;
        var episodes = await ReadEpisodesAsync(id, order, absolute, key, subscriberPin, cancellationToken)
            .ConfigureAwait(false);

        // The absolute order of TVDB is curated by hand and some series publish none: the aired order still
        // carries the absolute number of every episode, so it is the second place to look.
        if (episodes.Count == 0 && absolute)
        {
            episodes = await ReadEpisodesAsync(id, DefaultOrder, true, key, subscriberPin, cancellationToken)
                .ConfigureAwait(false);
        }

        if (episodes.Count == 0)
        {
            // An empty answer is not cached: a season TVDB is still filling is read again on the next run.
            return episodes;
        }

        Cache[cacheKey] = new CacheEntry(DateTimeOffset.UtcNow.Add(CacheDuration), episodes);
        Sweep();

        _logger.LogDebug(
            "TVDB: {Episodes} episodios de la serie {SeriesId} en el orden {Order}.",
            episodes.Count,
            id,
            order);

        return episodes;
    }

    /// <summary>
    /// Reads every page of one order of a series.
    /// </summary>
    /// <param name="seriesId">TVDB id of the series.</param>
    /// <param name="order">Order of the answer (<see cref="DefaultOrder"/> or <see cref="AbsoluteOrder"/>).</param>
    /// <param name="absolute">Whether the numbering written is the absolute one.</param>
    /// <param name="apiKey">Key of the user.</param>
    /// <param name="pin">Subscriber PIN, when there is one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The episodes of the order, without the specials when the order has seasons.</returns>
    private async Task<IReadOnlyList<ProviderEpisode>> ReadEpisodesAsync(
        string seriesId,
        string order,
        bool absolute,
        string apiKey,
        string? pin,
        CancellationToken cancellationToken)
    {
        var collected = new List<ProviderEpisode>();

        for (var page = 0; page < MaxPages; page++)
        {
            var answer = await ReadPageAsync(seriesId, order, page, apiKey, pin, cancellationToken)
                .ConfigureAwait(false);

            if (answer?.Data?.Episodes is not { Count: > 0 } batch)
            {
                break;
            }

            foreach (var episode in batch)
            {
                if (ToProviderEpisode(episode, absolute) is { } providerEpisode)
                {
                    collected.Add(providerEpisode);
                }
            }

            // `next` is the whole URL of the following page: it is only asked whether there is one.
            if (string.IsNullOrEmpty(answer.Links?.Next))
            {
                break;
            }
        }

        return collected
            .DistinctBy(episode => (episode.Season, episode.Episode))
            .OrderBy(episode => episode.Season)
            .ThenBy(episode => episode.Episode)
            .ToList();
    }

    /// <summary>
    /// Translates an episode of TVDB into the numbering the plugin pairs and writes.
    /// </summary>
    /// <param name="episode">Episode as TVDB publishes it.</param>
    /// <param name="absolute">Whether the absolute numbering is the one wanted.</param>
    /// <returns>
    /// The episode, or <c>null</c> when it has no place in the numbering (a special of the aired order, a
    /// record without a number).
    /// </returns>
    /// <remarks>
    /// The specials of the aired order (season 0) are not emissions of 3Cat and would steal the match of a
    /// regular episode, so they are left out. In the absolute order every episode is written as the season 1
    /// of the work, which is the shape anime releases have in Jellyfin.
    /// </remarks>
    private static ProviderEpisode? ToProviderEpisode(TvdbEpisode episode, bool absolute)
    {
        if (absolute)
        {
            var number = episode.AbsoluteNumber is > 0 ? episode.AbsoluteNumber.Value : episode.Number;
            return number > 0
                ? new ProviderEpisode(1, number, episode.Name, ParseDate(episode.Aired))
                : null;
        }

        return episode.SeasonNumber > 0 && episode.Number > 0
            ? new ProviderEpisode(episode.SeasonNumber, episode.Number, episode.Name, ParseDate(episode.Aired))
            : null;
    }

    /// <summary>
    /// Reads the <c>aired</c> date of TVDB, which is a plain date (<c>yyyy-MM-dd</c>) and sometimes carries a
    /// time after it.
    /// </summary>
    /// <param name="value">Value published by TVDB.</param>
    /// <returns>The date, or <c>null</c> when TVDB publishes none.</returns>
    private static DateOnly? ParseDate(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > 10)
        {
            text = text[..10];
        }

        return DateOnly.TryParseExact(
            text,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    /// <summary>
    /// Reads one page of the episodes of a series, logging in again when the token has expired.
    /// </summary>
    /// <param name="seriesId">TVDB id of the series.</param>
    /// <param name="order">Order of the answer.</param>
    /// <param name="page">Page, from 0.</param>
    /// <param name="apiKey">Key of the user.</param>
    /// <param name="pin">Subscriber PIN, when there is one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page, or <c>null</c> when TVDB does not serve it.</returns>
    private async Task<TvdbEpisodesResponse?> ReadPageAsync(
        string seriesId,
        string order,
        int page,
        string apiKey,
        string? pin,
        CancellationToken cancellationToken)
    {
        var relativeUrl = string.Format(
            CultureInfo.InvariantCulture,
            "series/{0}/episodes/{1}?page={2}",
            Uri.EscapeDataString(seriesId),
            order,
            page.ToString(CultureInfo.InvariantCulture));

        var loggedInAgain = false;

        for (var attempt = 1; ; attempt++)
        {
            var token = await GetTokenAsync(apiKey, pin, cancellationToken).ConfigureAwait(false);
            if (token is null)
            {
                return null;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content
                        .ReadAsStreamAsync(cancellationToken)
                        .ConfigureAwait(false);

                    return await JsonSerializer
                        .DeserializeAsync<TvdbEpisodesResponse>(stream, JsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && !loggedInAgain)
                {
                    // The token lasts a month and the user may have rotated the key: the next attempt logs
                    // in again instead of leaving the series without a numbering.
                    Tokens.TryRemove(TokenKey(apiKey, pin), out _);
                    loggedInAgain = true;
                    continue;
                }

                if (!HttpRetryPolicy.IsTransient(response.StatusCode)
                    || attempt >= HttpRetryPolicy.MaxAttempts)
                {
                    // The address of the log is the one without the key or the token.
                    _logger.LogWarning(
                        "TVDB respondió {Status} a {Url}.",
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
                    _logger.LogWarning(ex, "TVDB no respondió a {Url}.", relativeUrl);
                    return null;
                }
            }

            await Task.Delay(HttpRetryPolicy.Delay(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Exchanges the key of the user for a bearer token, reusing the one of the session when it is still good.
    /// </summary>
    /// <param name="apiKey">Key of the user.</param>
    /// <param name="pin">Subscriber PIN, when there is one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The token, or <c>null</c> when TVDB rejects the key or does not answer.</returns>
    /// <remarks>
    /// The login is serialized: a refresh that starts twenty programs at once asks for one token, not
    /// twenty. A rejected key is not retried —retrying a wrong key only annoys the service— and it is never
    /// written to the log.
    /// </remarks>
    private async Task<string?> GetTokenAsync(string apiKey, string? pin, CancellationToken cancellationToken)
    {
        var cacheKey = TokenKey(apiKey, pin);
        if (Tokens.TryGetValue(cacheKey, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
        {
            return cached.Token;
        }

        await LoginGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Tokens.TryGetValue(cacheKey, out cached) && cached.Expires > DateTimeOffset.UtcNow)
            {
                return cached.Token;
            }

            var payload = JsonSerializer.Serialize(
                new TvdbLoginRequest { ApiKey = apiKey, Pin = pin },
                JsonOptions);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "login")
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    };

                    using var response = await _httpClient
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        await using var stream = await response.Content
                            .ReadAsStreamAsync(cancellationToken)
                            .ConfigureAwait(false);

                        var answer = await JsonSerializer
                            .DeserializeAsync<TvdbLoginResponse>(stream, JsonOptions, cancellationToken)
                            .ConfigureAwait(false);

                        if (answer?.Data?.Token is not { Length: > 0 } token)
                        {
                            _logger.LogWarning("TVDB aceptó la petición pero no devolvió ningún token.");
                            return null;
                        }

                        Tokens[cacheKey] = new TokenEntry(DateTimeOffset.UtcNow.Add(TokenLifetime), token);
                        return token;
                    }

                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        _logger.LogWarning(
                            "TVDB rechazó la clave de API ({Status}): revisa la clave y, si es de suscriptor,"
                            + " el PIN.",
                            (int)response.StatusCode);

                        return null;
                    }

                    if (!HttpRetryPolicy.IsTransient(response.StatusCode)
                        || attempt >= HttpRetryPolicy.MaxAttempts)
                    {
                        _logger.LogWarning(
                            "TVDB respondió {Status} al iniciar sesión.",
                            (int)response.StatusCode);

                        return null;
                    }
                }
                catch (Exception ex) when (
                    (ex is HttpRequestException || ex is TaskCanceledException)
                    && !cancellationToken.IsCancellationRequested)
                {
                    if (attempt >= HttpRetryPolicy.MaxAttempts)
                    {
                        _logger.LogWarning(ex, "TVDB no respondió al iniciar sesión.");
                        return null;
                    }
                }

                await Task.Delay(HttpRetryPolicy.Delay(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            LoginGate.Release();
        }
    }

    /// <summary>
    /// Key of the session: the token belongs to the pair of key and PIN, and never to the key alone.
    /// </summary>
    /// <param name="apiKey">Key of the user.</param>
    /// <param name="pin">Subscriber PIN, when there is one.</param>
    /// <returns>The key of the cache.</returns>
    private static string TokenKey(string apiKey, string? pin)
        => string.Concat(apiKey, "|", pin ?? string.Empty);

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
    /// A token of TVDB, with the instant it stops being trusted.
    /// </summary>
    /// <param name="Expires">Instant the token is dropped.</param>
    /// <param name="Token">Bearer token.</param>
    private readonly record struct TokenEntry(DateTimeOffset Expires, string Token);

    /// <summary>
    /// The episodes of a series in the cache, with the instant they stop being fresh.
    /// </summary>
    /// <param name="Expires">Instant the entry expires.</param>
    /// <param name="Episodes">Episodes read from TVDB.</param>
    private readonly record struct CacheEntry(DateTimeOffset Expires, IReadOnlyList<ProviderEpisode> Episodes);
}
