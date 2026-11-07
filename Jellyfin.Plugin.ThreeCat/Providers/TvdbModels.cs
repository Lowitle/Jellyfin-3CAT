using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ThreeCat.Providers;

/// <summary>
/// Body of <c>POST login</c>: the API key of the user and, when the key is subscriber supported, its PIN.
/// </summary>
internal sealed class TvdbLoginRequest
{
    /// <summary>
    /// Gets or sets the API key of the user.
    /// </summary>
    [JsonPropertyName("apikey")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the subscriber PIN, which TVDB only asks for when the key belongs to a subscription.
    /// </summary>
    [JsonPropertyName("pin")]
    public string? Pin { get; set; }
}

/// <summary>
/// Answer of <c>POST login</c>: the bearer token that authenticates every other call.
/// </summary>
internal sealed class TvdbLoginResponse
{
    /// <summary>
    /// Gets or sets the status of the answer (<c>success</c> or <c>failure</c>).
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the body, which holds the token.
    /// </summary>
    [JsonPropertyName("data")]
    public TvdbLoginData? Data { get; set; }
}

/// <summary>
/// The token of a session. It is valid for a month.
/// </summary>
internal sealed class TvdbLoginData
{
    /// <summary>
    /// Gets or sets the bearer token.
    /// </summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

/// <summary>
/// Answer of <c>series/{id}/episodes/{season-type}</c>: one page of episodes, the series and the pagination.
/// </summary>
internal sealed class TvdbEpisodesResponse
{
    /// <summary>
    /// Gets or sets the status of the answer.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the body, which holds the episodes of the page.
    /// </summary>
    [JsonPropertyName("data")]
    public TvdbEpisodesData? Data { get; set; }

    /// <summary>
    /// Gets or sets the pagination of the answer.
    /// </summary>
    [JsonPropertyName("links")]
    public TvdbPageLinks? Links { get; set; }
}

/// <summary>
/// The episodes of one page.
/// </summary>
internal sealed class TvdbEpisodesData
{
    /// <summary>
    /// Gets or sets the episodes of the page, in the order TVDB numbers them.
    /// </summary>
    [JsonPropertyName("episodes")]
    public List<TvdbEpisode>? Episodes { get; set; }
}

/// <summary>
/// Pagination of TVDB: <c>next</c> is the whole URL of the next page, or <c>null</c> on the last one.
/// </summary>
internal sealed class TvdbPageLinks
{
    /// <summary>
    /// Gets or sets the URL of the next page, or <c>null</c> when this is the last one.
    /// </summary>
    [JsonPropertyName("next")]
    public string? Next { get; set; }

    /// <summary>
    /// Gets or sets the number of records of the whole list.
    /// </summary>
    [JsonPropertyName("total_items")]
    public int? TotalItems { get; set; }
}

/// <summary>
/// An episode as TVDB publishes it. Only the numbering, the title and the air date are read: the images,
/// the cast and the overview are what Jellyfin downloads by itself once the episode is numbered.
/// </summary>
internal sealed class TvdbEpisode
{
    /// <summary>
    /// Gets or sets the TVDB id of the episode.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the title of the episode.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the season number of the episode in the order the answer was asked for.
    /// </summary>
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the number of the episode inside its season.
    /// </summary>
    [JsonPropertyName("number")]
    public int Number { get; set; }

    /// <summary>
    /// Gets or sets the absolute number of the episode, which is the one the absolute numbering writes.
    /// </summary>
    /// <remarks>
    /// It is missing in some records: the absolute order of TVDB is curated by hand, and a series that was
    /// never reviewed lists its episodes with the number of the season and nothing else.
    /// </remarks>
    [JsonPropertyName("absoluteNumber")]
    public int? AbsoluteNumber { get; set; }

    /// <summary>
    /// Gets or sets the air date (<c>yyyy-MM-dd</c>) as TVDB publishes it.
    /// </summary>
    [JsonPropertyName("aired")]
    public string? Aired { get; set; }
}
