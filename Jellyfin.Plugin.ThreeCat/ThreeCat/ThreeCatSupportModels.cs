using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// The program a video belongs to. In the catalog it arrives as a single object with the poster
/// images, which is where the series artwork comes from.
/// </summary>
public sealed class ThreeCatProgram
{
    /// <summary>
    /// Gets or sets the program id (<c>programatv_id</c>).
    /// </summary>
    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the program name.
    /// </summary>
    [JsonPropertyName("desc")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the program title.
    /// </summary>
    [JsonPropertyName("titol")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the slug of the program page (<c>doraemon-el-gat-cosmic</c>).
    /// </summary>
    [JsonPropertyName("nom_bonic")]
    public string? Slug { get; set; }

    /// <summary>
    /// Gets or sets the typology of the program.
    /// </summary>
    [JsonPropertyName("tipologia")]
    public string? Typology { get; set; }

    /// <summary>
    /// Gets or sets the domain of the program.
    /// </summary>
    [JsonPropertyName("domini")]
    public string? Domain { get; set; }

    /// <summary>
    /// Gets or sets the poster images of the program.
    /// </summary>
    [JsonPropertyName("imatges")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatImage>))]
    public List<ThreeCatImage>? Images { get; set; }
}

/// <summary>
/// The brand of a video (<c>{ "id": "BB_S3DORAE", "desc": "Doraemon, el gat còsmic" }</c>).
/// </summary>
public sealed class ThreeCatBand
{
    /// <summary>
    /// Gets or sets the brand id.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the brand name, which is the program name.
    /// </summary>
    [JsonPropertyName("desc")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether it is the main brand.
    /// </summary>
    [JsonPropertyName("main")]
    public bool Main { get; set; }
}

/// <summary>
/// A labelled value of the catalog (<c>{ "id": "PUFICCIO", "desc": "Ficció" }</c>).
/// </summary>
public sealed class ThreeCatLabel
{
    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the human readable name.
    /// </summary>
    [JsonPropertyName("desc")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether it is the main value.
    /// </summary>
    [JsonPropertyName("main")]
    public bool Main { get; set; }
}

/// <summary>
/// The audience targets of a video (<c>{ "target": [ { "id": "PUT_INF", "desc": "Infantil" } ] }</c>).
/// </summary>
/// <remarks>
/// It is the classification the 3Cat portal builds its own rails with («Infantil», «Juvenil», «Tots els
/// Públics»), and unlike the facets of the search endpoint it arrives with the catalog of every program,
/// so reading it costs no extra request.
/// </remarks>
public sealed class ThreeCatTargets
{
    /// <summary>
    /// Gets or sets the targets of the video.
    /// </summary>
    [JsonPropertyName("target")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatLabel>))]
    public List<ThreeCatLabel>? Targets { get; set; }
}

/// <summary>
/// The channels a video was broadcast on (<c>{ "canal": [ { "id": "PUC_S3", "desc": "Super3" } ] }</c>).
/// </summary>
public sealed class ThreeCatVideoChannels
{
    /// <summary>
    /// Gets or sets the channels of the video.
    /// </summary>
    [JsonPropertyName("canal")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatLabel>))]
    public List<ThreeCatLabel>? Channels { get; set; }
}

/// <summary>
/// An image of the catalog (<c>text</c> holds the URL, <c>mida</c> the size and <c>rel_name</c> the role).
/// </summary>
public sealed class ThreeCatImage
{
    /// <summary>
    /// Gets or sets the image URL.
    /// </summary>
    [JsonPropertyName("text")]
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the size tag («MASTER», «288x422»...).
    /// </summary>
    [JsonPropertyName("mida")]
    public string? Size { get; set; }

    /// <summary>
    /// Gets or sets the alternative text.
    /// </summary>
    [JsonPropertyName("alt")]
    public string? Alt { get; set; }

    /// <summary>
    /// Gets or sets the title of the image.
    /// </summary>
    [JsonPropertyName("titol")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the role of the image (<c>IMG_POSTER</c>, <c>KEYVIDEO</c>, <c>IMAGE</c>...).
    /// </summary>
    [JsonPropertyName("rel_name")]
    public string? Relation { get; set; }
}
