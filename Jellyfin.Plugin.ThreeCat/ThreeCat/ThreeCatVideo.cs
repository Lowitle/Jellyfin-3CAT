using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// A video (emission) of the 3Cat catalog.
/// </summary>
public sealed class ThreeCatVideo
{
    /// <summary>
    /// Gets or sets the video id (<c>idint</c>), the value the media endpoint expects.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the chapter number. It is a counter that runs over the whole program
    /// («907» for the chapter 6 of the season 22), so the synchronization does not use it: see
    /// <see cref="Sync.EpisodeNumbering"/>.
    /// </summary>
    [JsonPropertyName("capitol")]
    [JsonConverter(typeof(FlexibleInt32Converter))]
    public int? Chapter { get; set; }

    /// <summary>
    /// Gets or sets the number 3Cat publishes in <c>capitol_temporada</c>, which is the chapter
    /// inside its season and not the season (-1 when 3Cat publishes none). The synchronization does
    /// not use it: the season comes from the title, which is the numbering the 3Cat website shows.
    /// </summary>
    [JsonPropertyName("capitol_temporada")]
    [JsonConverter(typeof(FlexibleInt32Converter))]
    public int? Season { get; set; }

    /// <summary>
    /// Gets or sets the title. A double emission arrives as two titles separated by « / ».
    /// </summary>
    [JsonPropertyName("titol")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the friendly name.
    /// </summary>
    [JsonPropertyName("nom_friendly")]
    public string? FriendlyName { get; set; }

    /// <summary>
    /// Gets or sets the permanent title.
    /// </summary>
    [JsonPropertyName("permatitle")]
    public string? PermaTitle { get; set; }

    /// <summary>
    /// Gets or sets the content type code published by 3Cat (for instance <c>PPD</c>).
    /// </summary>
    [JsonPropertyName("tipus_contingut")]
    public string? ContentType { get; set; }

    /// <summary>
    /// Gets or sets the duration published by the catalog, which 3Cat marks as unreliable.
    /// </summary>
    /// <remarks>
    /// The media endpoint publishes the reliable value in <c>informacio.durada.milisegons</c>, so the
    /// synchronization prefers that one.
    /// </remarks>
    [JsonPropertyName("durada")]
    [JsonConverter(typeof(ThreeCatDurationConverter))]
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Gets or sets the publication date.
    /// </summary>
    [JsonPropertyName("data_publicacio")]
    [JsonConverter(typeof(ThreeCatDateConverter))]
    public DateTimeOffset? Published { get; set; }

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
    /// Gets or sets the program the video belongs to.
    /// </summary>
    [JsonPropertyName("programes_tv")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatProgram>))]
    public List<ThreeCatProgram>? Programs { get; set; }

    /// <summary>
    /// Gets or sets the brand of the video (its <c>desc</c> is the program name).
    /// </summary>
    [JsonPropertyName("bbands")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatBand>))]
    public List<ThreeCatBand>? Bands { get; set; }

    /// <summary>
    /// Gets or sets the themes of the video («Ficció», «Esports»...).
    /// </summary>
    [JsonPropertyName("tematiques")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatLabel>))]
    public List<ThreeCatLabel>? Themes { get; set; }

    /// <summary>
    /// Gets or sets the audience targets of the video («Infantil», «Juvenil», «Tots els Públics»).
    /// </summary>
    /// <remarks>
    /// 3Cat files every video under one target, and it is the classification the portal itself uses to
    /// build the SX3 rails, so it tells a children's program from the rest without any extra request.
    /// </remarks>
    [JsonPropertyName("targets")]
    [JsonConverter(typeof(ThreeCatTargetsConverter))]
    public ThreeCatTargets? Targets { get; set; }

    /// <summary>
    /// Gets or sets the channels the video was broadcast on («Super3», «S3», «X3»...).
    /// </summary>
    /// <remarks>
    /// It is the fallback of <see cref="Targets"/>: a video of the SX3 family without a published target
    /// is still a children's program.
    /// </remarks>
    [JsonPropertyName("canals")]
    [JsonConverter(typeof(ThreeCatVideoChannelsConverter))]
    public ThreeCatVideoChannels? Channels { get; set; }

    /// <summary>
    /// Gets or sets the audio languages.
    /// </summary>
    [JsonPropertyName("idiomes")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatLabel>))]
    public List<ThreeCatLabel>? Languages { get; set; }

    /// <summary>
    /// Gets or sets the regions the video is published to («ESPANYA»).
    /// </summary>
    [JsonPropertyName("geolocalitzacions")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatLabel>))]
    public List<ThreeCatLabel>? Geolocations { get; set; }

    /// <summary>
    /// Gets or sets the images of the video.
    /// </summary>
    [JsonPropertyName("imatges")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatImage>))]
    public List<ThreeCatImage>? Images { get; set; }
}
