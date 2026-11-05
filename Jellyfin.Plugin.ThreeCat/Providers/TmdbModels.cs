using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ThreeCat.Providers;

/// <summary>
/// Answer of <c>tv/{id}</c>: the series and its seasons.
/// </summary>
internal sealed class TmdbSeriesResponse
{
    /// <summary>
    /// Gets or sets the TMDB id of the series.
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the series in the language of the request.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the seasons of the series, the season 0 (specials) included.
    /// </summary>
    [JsonPropertyName("seasons")]
    public List<TmdbSeasonResponse>? Seasons { get; set; }
}

/// <summary>
/// A season as the series answer publishes it: only what is needed to ask for its episodes.
/// </summary>
internal sealed class TmdbSeasonResponse
{
    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the number of episodes the season publishes.
    /// </summary>
    [JsonPropertyName("episode_count")]
    public int EpisodeCount { get; set; }

    /// <summary>
    /// Gets or sets the name of the season.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Answer of <c>tv/{id}/season/{number}</c>: the episodes of a season.
/// </summary>
internal sealed class TmdbSeasonDetailResponse
{
    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episodes of the season.
    /// </summary>
    [JsonPropertyName("episodes")]
    public List<TmdbEpisodeResponse>? Episodes { get; set; }
}

/// <summary>
/// An episode as TMDB publishes it. Only the numbering, the title and the air date are read: the images,
/// the cast and the overview are what Jellyfin downloads by itself once the episode is numbered.
/// </summary>
internal sealed class TmdbEpisodeResponse
{
    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode number inside its season, which is the number that has to end up in the
    /// file name.
    /// </summary>
    [JsonPropertyName("episode_number")]
    public int EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode title in the language of the request.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the air date (<c>yyyy-MM-dd</c>) as TMDB publishes it, which is a plain date.
    /// </summary>
    [JsonPropertyName("air_date")]
    public string? AirDate { get; set; }
}
