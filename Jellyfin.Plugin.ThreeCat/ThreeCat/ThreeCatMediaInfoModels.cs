using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// The <c>informacio</c> node of the media endpoint: the reliable metadata of the video.
/// </summary>
public sealed class ThreeCatMediaInfo
{
    /// <summary>
    /// Gets or sets the video id.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the program id (<c>programatv_id</c>), used to resolve a program from a video URL.
    /// </summary>
    [JsonPropertyName("programa_id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? ProgramId { get; set; }

    /// <summary>
    /// Gets or sets the program name.
    /// </summary>
    [JsonPropertyName("programa")]
    public string? Program { get; set; }

    /// <summary>
    /// Gets or sets the chapter number.
    /// </summary>
    [JsonPropertyName("capitol")]
    [JsonConverter(typeof(FlexibleInt32Converter))]
    public int? Chapter { get; set; }

    /// <summary>
    /// Gets or sets the title of the emission.
    /// </summary>
    [JsonPropertyName("titol")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the complete title («Programa - 884 - Título»).
    /// </summary>
    [JsonPropertyName("titol_complet")]
    public string? CompleteTitle { get; set; }

    /// <summary>
    /// Gets or sets the slug of the video page.
    /// </summary>
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    /// <summary>
    /// Gets or sets the plot.
    /// </summary>
    [JsonPropertyName("descripcio")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the reliable duration of the video.
    /// </summary>
    [JsonPropertyName("durada")]
    [JsonConverter(typeof(ThreeCatDurationConverter))]
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Gets or sets the broadcast date.
    /// </summary>
    [JsonPropertyName("data_emissio")]
    [JsonConverter(typeof(ThreeCatDateConverter))]
    public DateTimeOffset? Broadcast { get; set; }

    /// <summary>
    /// Gets or sets the date from which the video stops being available.
    /// </summary>
    [JsonPropertyName("data_caducitat")]
    [JsonConverter(typeof(ThreeCatDateConverter))]
    public DateTimeOffset? Expires { get; set; }

    /// <summary>
    /// Gets or sets the content type code (<c>PPD</c>...).
    /// </summary>
    [JsonPropertyName("tipus_contingut")]
    public string? ContentType { get; set; }

    /// <summary>
    /// Gets or sets the state of the video.
    /// </summary>
    [JsonPropertyName("estat")]
    public ThreeCatStatus? Status { get; set; }

    /// <summary>
    /// Gets or sets the rights notice published by 3Cat
    /// («Emissió no disponible fora de l'Estat Espanyol»).
    /// </summary>
    [JsonPropertyName("drets")]
    public ThreeCatNotice? Rights { get; set; }
}

/// <summary>
/// The state of a video in the media endpoint.
/// </summary>
public sealed class ThreeCatStatus
{
    /// <summary>
    /// Gets or sets a value indicating whether the video is currently active.
    /// </summary>
    [JsonPropertyName("actiu")]
    public bool Active { get; set; }
}

/// <summary>
/// A free text notice of the media endpoint.
/// </summary>
public sealed class ThreeCatNotice
{
    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

/// <summary>
/// A subtitle track of the media endpoint (<c>{ "iso": "ca", "url": "...vtt" }</c>).
/// </summary>
public sealed class ThreeCatSubtitle
{
    /// <summary>
    /// Gets or sets the name of the track («Català (SPS)»).
    /// </summary>
    [JsonPropertyName("text")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the ISO language code («ca»).
    /// </summary>
    [JsonPropertyName("iso")]
    public string? Iso { get; set; }

    /// <summary>
    /// Gets or sets the URL of the WebVTT file.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the format of the file («vtt»).
    /// </summary>
    [JsonPropertyName("format")]
    public string? Format { get; set; }
}
