using System.Text.Json;
using System.Text.Json.Serialization;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Downloads and caches the identity dataset of
/// <see href="https://github.com/Lowitle/open-tv-ids">open-tv-ids</see>, which maps the
/// <c>programatv_id</c> of 3Cat to the id of the same work in Wikidata, IMDb, TMDB and TVDB.
/// </summary>
/// <remarks>
/// <para>
/// The dataset is a JSONL file with one program per line (about 180 KB). It is read in the refresh task
/// and cached in the data folder of the plugin, next to its download date, so the synchronization never
/// depends on GitHub being reachable: when the download fails the previous copy is used, and when there
/// is none the plugin keeps working with the numbering of 3Cat.
/// </para>
/// <para>
/// The identity of a series is never guessed: an id only comes from a line of this file or from a value
/// typed by the user in the configuration page. The dataset publishes an entry only when its match
/// reaches a confidence of 0.90 or was confirmed by hand, so a published id can be used as is.
/// </para>
/// </remarks>
public sealed class OpenTvIdsClient
{
    /// <summary>
    /// Dataset the plugin reads when the configuration does not set another one. It is the JSONL file
    /// the <c>open-tv-ids</c> workflow republishes every Monday at 04:00 UTC.
    /// </summary>
    public const string DefaultUrl =
        "https://raw.githubusercontent.com/Lowitle/open-tv-ids/main/data/3cat/series.jsonl";

    /// <summary>
    /// Name of the local copy, inside the data folder of the plugin.
    /// </summary>
    internal const string CacheFileName = "open-tv-ids-3cat.jsonl";

    /// <summary>
    /// Options used to read the lines. Web defaults are case-insensitive, which keeps the reader working
    /// if the dataset changes the casing of a field.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IApplicationPaths _paths;
    private readonly ILogger<OpenTvIdsClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenTvIdsClient"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client configured by <see cref="PluginServiceRegistrator"/>.</param>
    /// <param name="paths">Application paths, used to locate the data folder of the plugin.</param>
    /// <param name="logger">Logger.</param>
    public OpenTvIdsClient(
        HttpClient httpClient,
        IApplicationPaths paths,
        ILogger<OpenTvIdsClient> logger)
    {
        _httpClient = httpClient;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Gets the path of the local copy of the dataset.
    /// </summary>
    public string CachePath => Path.Combine(_paths.DataPath, "3cat", CacheFileName);

    /// <summary>
    /// Reads the dataset, from the cache or, when it is missing or a refresh is asked for, from the
    /// network.
    /// </summary>
    /// <param name="url">Dataset URL; <see cref="DefaultUrl"/> when empty.</param>
    /// <param name="forceRefresh">Whether to download even when a local copy exists.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The identities, empty when neither the download nor the local copy could be read. The method
    /// never throws for a network or disk failure: a synchronization must not be aborted by the dataset.
    /// </returns>
    public async Task<OpenTvIdsDataset> LoadAsync(
        string? url,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var address = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url.Trim();

        if (!forceRefresh && File.Exists(CachePath))
        {
            return await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var response = await _httpClient
                .GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "open-tv-ids respondió {Status} a {Url}; se usa la copia anterior si existe",
                    (int)response.StatusCode,
                    address);

                return await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
            }

            var jsonl = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            var records = Parse(jsonl);
            if (records.Count == 0)
            {
                _logger.LogWarning(
                    "open-tv-ids devolvió un fichero vacío desde {Url}; se usa la copia anterior si existe",
                    address);

                return await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
            }

            await WriteCacheAsync(jsonl, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "open-tv-ids: {Count} identidades descargadas desde {Url}",
                records.Count,
                address);

            return new OpenTvIdsDataset(records, DateTimeOffset.UtcNow, fromCache: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogWarning(
                ex,
                "No se pudo descargar open-tv-ids desde {Url}; se usa la copia anterior si existe",
                address);

            return await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads a JSONL document of <c>open-tv-ids</c> and indexes it by <c>emitterId</c>.
    /// </summary>
    /// <param name="jsonl">Content of the dataset.</param>
    /// <returns>The identities, keyed by <c>programatv_id</c>.</returns>
    /// <remarks>
    /// A corrupt line is skipped instead of failing the whole read, and the first line that claims a
    /// <c>programatv_id</c> wins: the dataset is published by a machine and one bad line must not leave
    /// the plugin without identities.
    /// </remarks>
    public static IReadOnlyDictionary<string, OpenTvIdsRecord> Parse(string? jsonl)
    {
        var records = new Dictionary<string, OpenTvIdsRecord>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(jsonl))
        {
            return records;
        }

        foreach (var line in jsonl.Split('\n'))
        {
            // The first line of a file saved with a BOM would otherwise fail to parse and be skipped.
            var text = line.Trim().TrimStart('\uFEFF');
            if (text.Length == 0)
            {
                continue;
            }

            OpenTvIdsRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<OpenTvIdsRecord>(text, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            var emitterId = record?.EmitterId?.Trim();
            if (record is null || string.IsNullOrEmpty(emitterId))
            {
                continue;
            }

            records.TryAdd(emitterId, record);
        }

        return records;
    }

    /// <summary>
    /// Reads the local copy of the dataset, or returns an empty dataset when it cannot be read.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached identities.</returns>
    private async Task<OpenTvIdsDataset> ReadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(CachePath))
        {
            return OpenTvIdsDataset.Empty;
        }

        try
        {
            var jsonl = await File.ReadAllTextAsync(CachePath, cancellationToken).ConfigureAwait(false);
            var records = Parse(jsonl);
            var updated = File.GetLastWriteTimeUtc(CachePath);

            return new OpenTvIdsDataset(
                records,
                new DateTimeOffset(updated, TimeSpan.Zero),
                fromCache: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "No se pudo leer la copia local de open-tv-ids");
            return OpenTvIdsDataset.Empty;
        }
    }

    /// <summary>
    /// Saves the downloaded document so the next run does not need the network.
    /// </summary>
    /// <param name="jsonl">Content of the dataset.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    /// <remarks>
    /// A cache that cannot be written is only a lost optimization: the identities read from the network
    /// are used for this run anyway.
    /// </remarks>
    private async Task WriteCacheAsync(string jsonl, CancellationToken cancellationToken)
    {
        try
        {
            var folder = Path.GetDirectoryName(CachePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            await File.WriteAllTextAsync(CachePath, jsonl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "No se pudo guardar la copia local de open-tv-ids en {Path}", CachePath);
        }
    }
}

/// <summary>
/// One program of the identity dataset.
/// </summary>
public sealed class OpenTvIdsRecord
{
    /// <summary>
    /// Gets or sets the dataset identifier («3cat:73476»).
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the <c>programatv_id</c> of 3Cat, which is the key the plugin joins by.
    /// </summary>
    [JsonPropertyName("emitterId")]
    public string? EmitterId { get; set; }

    /// <summary>
    /// Gets or sets the title the dataset publishes for the program.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the original title, when the dataset publishes one.
    /// </summary>
    [JsonPropertyName("originalTitle")]
    public string? OriginalTitle { get; set; }

    /// <summary>
    /// Gets or sets the kind of program the dataset records («anime», «series»...).
    /// </summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>
    /// Gets or sets the external identifiers of the same work.
    /// </summary>
    [JsonPropertyName("ids")]
    public OpenTvIdsIds? Ids { get; set; }
}

/// <summary>
/// External identifiers of a program. <c>wikidata</c> is always present; the others are optional.
/// </summary>
public sealed class OpenTvIdsIds
{
    /// <summary>
    /// Gets or sets the Wikidata id («Q19839121»).
    /// </summary>
    [JsonPropertyName("wikidata")]
    public string? Wikidata { get; set; }

    /// <summary>
    /// Gets or sets the IMDb id («tt4644488»).
    /// </summary>
    [JsonPropertyName("imdb")]
    public string? Imdb { get; set; }

    /// <summary>
    /// Gets or sets the TMDB id («62715»).
    /// </summary>
    [JsonPropertyName("tmdb")]
    public string? Tmdb { get; set; }

    /// <summary>
    /// Gets or sets the TVDB id («295068»).
    /// </summary>
    [JsonPropertyName("tvdb")]
    public string? Tvdb { get; set; }
}

/// <summary>
/// The identities of a dataset read, with the date of the copy they come from.
/// </summary>
public sealed class OpenTvIdsDataset
{
    /// <summary>
    /// Dataset used when there is no local copy and the download fails.
    /// </summary>
    public static OpenTvIdsDataset Empty { get; } = new(
        new Dictionary<string, OpenTvIdsRecord>(StringComparer.Ordinal),
        null,
        fromCache: false);

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenTvIdsDataset"/> class.
    /// </summary>
    /// <param name="records">Identities keyed by <c>programatv_id</c>.</param>
    /// <param name="updated">Date of the copy, or <c>null</c> when there is none.</param>
    /// <param name="fromCache">Whether the identities come from the local copy.</param>
    public OpenTvIdsDataset(
        IReadOnlyDictionary<string, OpenTvIdsRecord> records,
        DateTimeOffset? updated,
        bool fromCache)
    {
        Records = records;
        Updated = updated;
        FromCache = fromCache;
    }

    /// <summary>
    /// Gets the identities, keyed by <c>programatv_id</c>.
    /// </summary>
    public IReadOnlyDictionary<string, OpenTvIdsRecord> Records { get; }

    /// <summary>
    /// Gets the date of the copy, or <c>null</c> when there is none.
    /// </summary>
    public DateTimeOffset? Updated { get; }

    /// <summary>
    /// Gets a value indicating whether the identities come from the local copy.
    /// </summary>
    public bool FromCache { get; }

    /// <summary>
    /// Gets the number of identities.
    /// </summary>
    public int Count => Records.Count;

    /// <summary>
    /// Finds the record of a program.
    /// </summary>
    /// <param name="programId">3Cat <c>programatv_id</c>.</param>
    /// <returns>The record, or <c>null</c> when the dataset has no entry for the program.</returns>
    public OpenTvIdsRecord? Find(string? programId)
    {
        if (string.IsNullOrWhiteSpace(programId))
        {
            return null;
        }

        return Records.TryGetValue(programId.Trim(), out var record) ? record : null;
    }
}
