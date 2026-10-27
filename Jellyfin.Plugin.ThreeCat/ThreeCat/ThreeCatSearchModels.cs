using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Root of the answer of <c>api.3cat.cat/cercador/tot</c>, the search endpoint of the 3Cat portal.
/// </summary>
/// <remarks>
/// The payload is nested under <c>resposta</c>, as in the catalog endpoint, but the programs arrive with
/// their own field names (<c>nombonic</c>, <c>titol</c>...) and the pagination is the same one the
/// catalog publishes.
/// </remarks>
public sealed class ThreeCatSearchEnvelope
{
    /// <summary>
    /// Gets or sets the body of the answer.
    /// </summary>
    [JsonPropertyName("resposta")]
    public ThreeCatSearchResponse? Response { get; set; }
}

/// <summary>
/// Body of the answer of the search endpoint.
/// </summary>
public sealed class ThreeCatSearchResponse
{
    /// <summary>
    /// Gets or sets the status published by 3Cat («OK» on success).
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the programs of the page.
    /// </summary>
    [JsonPropertyName("items")]
    public ThreeCatProgramList? Items { get; set; }

    /// <summary>
    /// Gets or sets the pagination information.
    /// </summary>
    [JsonPropertyName("paginacio")]
    public ThreeCatPagination? Pagination { get; set; }
}

/// <summary>
/// The <c>items</c> node of the search, which holds the programs under <c>item</c>.
/// </summary>
public sealed class ThreeCatProgramList
{
    /// <summary>
    /// Gets or sets the programs of the page.
    /// </summary>
    [JsonPropertyName("item")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatSearchProgram>))]
    public List<ThreeCatSearchProgram>? Items { get; set; }
}

/// <summary>
/// A program of the search endpoint. Its <c>id</c> is the <c>programatv_id</c> the catalog is read with.
/// </summary>
public sealed class ThreeCatSearchProgram
{
    /// <summary>
    /// Gets or sets the program id (<c>programatv_id</c>). It arrives as a number, unlike the catalog.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the program title.
    /// </summary>
    [JsonPropertyName("titol")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the slug of the program page (<c>doraemon-el-gat-cosmic</c>).
    /// </summary>
    [JsonPropertyName("nombonic")]
    public string? Slug { get; set; }

    /// <summary>
    /// Gets or sets the typology of the program (<c>PTVC_PROGRAMA</c>, <c>WCR_PROGRAMA</c>).
    /// </summary>
    [JsonPropertyName("tipologia")]
    public string? Typology { get; set; }

    /// <summary>
    /// Gets or sets the publication date of the program page («23/10/2026 08:00:00»).
    /// </summary>
    [JsonPropertyName("data_publicacio")]
    public string? Published { get; set; }

    /// <summary>
    /// Gets or sets the channels that broadcast the program.
    /// </summary>
    [JsonPropertyName("canals")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatLabel>))]
    public List<ThreeCatLabel>? Channels { get; set; }

    /// <summary>
    /// Gets or sets the languages of the program.
    /// </summary>
    [JsonPropertyName("idiomes")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatLabel>))]
    public List<ThreeCatLabel>? Languages { get; set; }

    /// <summary>
    /// Gets or sets the age ratings of the program («Tots els públics», «+7»...).
    /// </summary>
    [JsonPropertyName("codis_etics")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatLabel>))]
    public List<ThreeCatLabel>? AgeRatings { get; set; }
}

/// <summary>
/// A program of the 3Cat catalog as exposed by the plugin API and shown by the configuration page.
/// </summary>
/// <param name="Id">Program id (<c>programatv_id</c>).</param>
/// <param name="Title">Title of the program, the name of the series folder.</param>
/// <param name="Url">Public page of the program on 3cat.cat.</param>
/// <param name="Kind">«TV» for the television programs, «Web/radio» for the rest.</param>
/// <param name="Channel">Main channel of the program («TV3», «33», «Catalunya Ràdio»...).</param>
/// <param name="Languages">Languages published for the program.</param>
/// <param name="AgeRating">Age rating published for the program.</param>
/// <param name="Published">Publication date normalized to <c>yyyy-MM-dd</c>, which sorts as text.</param>
/// <param name="HasVideo">
/// Whether the catalog of the program can be read. The web and radio programs of 3Cat have no videos,
/// so the page can hide them instead of offering a program that would write an empty series.
/// </param>
public sealed record ThreeCatCatalogProgram(
    string Id,
    string Title,
    string? Url,
    string Kind,
    string? Channel,
    IReadOnlyList<string> Languages,
    string? AgeRating,
    string? Published,
    bool HasVideo);
