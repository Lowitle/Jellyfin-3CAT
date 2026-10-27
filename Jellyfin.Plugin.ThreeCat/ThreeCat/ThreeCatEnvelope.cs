using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Root of the answer of <c>api.3cat.cat/videos</c>. The payload is nested under <c>resposta</c>.
/// </summary>
public sealed class ThreeCatEnvelope
{
    /// <summary>
    /// Gets or sets the body of the answer.
    /// </summary>
    [JsonPropertyName("resposta")]
    public ThreeCatResponse? Response { get; set; }
}

/// <summary>
/// Body of the answer of the catalog endpoint.
/// </summary>
public sealed class ThreeCatResponse
{
    /// <summary>
    /// Gets or sets the status published by 3Cat («OK» on success).
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the videos of the page.
    /// </summary>
    [JsonPropertyName("items")]
    public ThreeCatItemList? Items { get; set; }

    /// <summary>
    /// Gets or sets the pagination information.
    /// </summary>
    [JsonPropertyName("paginacio")]
    public ThreeCatPagination? Pagination { get; set; }
}

/// <summary>
/// The <c>items</c> node, which holds the videos under <c>item</c>.
/// </summary>
public sealed class ThreeCatItemList
{
    /// <summary>
    /// Gets or sets the videos of the page.
    /// </summary>
    [JsonPropertyName("item")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatVideo>))]
    public List<ThreeCatVideo>? Items { get; set; }
}

/// <summary>
/// Pagination of the catalog.
/// </summary>
public sealed class ThreeCatPagination
{
    /// <summary>
    /// Gets or sets the total number of videos of the program.
    /// </summary>
    [JsonPropertyName("total_items")]
    [JsonConverter(typeof(FlexibleInt64Converter))]
    public long? TotalItems { get; set; }

    /// <summary>
    /// Gets or sets the page size used by the server.
    /// </summary>
    [JsonPropertyName("items_pagina")]
    [JsonConverter(typeof(FlexibleInt32Converter))]
    public int? PageSize { get; set; }

    /// <summary>
    /// Gets or sets the page that was returned (1 based).
    /// </summary>
    [JsonPropertyName("pagina_actual")]
    [JsonConverter(typeof(FlexibleInt32Converter))]
    public int? CurrentPage { get; set; }

    /// <summary>
    /// Gets or sets the total number of pages.
    /// </summary>
    [JsonPropertyName("total_pagines")]
    [JsonConverter(typeof(FlexibleInt32Converter))]
    public int? TotalPages { get; set; }
}

/// <summary>
/// A page of the 3Cat catalog.
/// </summary>
/// <param name="Items">Videos of the program.</param>
/// <param name="TotalPages">Pages reported by 3Cat.</param>
/// <param name="TotalItems">Videos reported by 3Cat (0 when it does not say).</param>
/// <param name="Truncated">Whether <c>MaxItemsPerProgram</c> cut the catalog short.</param>
public sealed record ThreeCatCatalogPage(
    IReadOnlyList<ThreeCatVideo> Items,
    int TotalPages,
    int TotalItems,
    bool Truncated);
