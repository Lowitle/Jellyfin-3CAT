using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Answer of <c>api-media.3cat.cat/pvideo/media.jsp</c>.
/// </summary>
public sealed class ThreeCatMedia
{
    /// <summary>
    /// Gets or sets the description of the video (title, chapter, plot, dates...).
    /// </summary>
    [JsonPropertyName("informacio")]
    public ThreeCatMediaInfo? Info { get; set; }

    /// <summary>
    /// Gets or sets the playback URLs.
    /// </summary>
    [JsonPropertyName("media")]
    public ThreeCatPlayback? Playback { get; set; }

    /// <summary>
    /// Gets or sets the subtitle tracks.
    /// </summary>
    [JsonPropertyName("subtitols")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatSubtitle>))]
    public List<ThreeCatSubtitle>? Subtitles { get; set; }

    /// <summary>
    /// Gets or sets the images of the video.
    /// </summary>
    [JsonPropertyName("imatges")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatMediaImage>))]
    public List<ThreeCatMediaImage>? Images { get; set; }
}

/// <summary>
/// The <c>media</c> node with the stream URLs.
/// </summary>
public sealed class ThreeCatPlayback
{
    /// <summary>
    /// Gets or sets the region the stream is published to («ESPANYA»).
    /// </summary>
    [JsonPropertyName("geo")]
    public string? Geo { get; set; }

    /// <summary>
    /// Gets or sets the container of the stream («MP4»).
    /// </summary>
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    /// <summary>
    /// Gets or sets the available files. It is empty when 3Cat geoblocks or unpublishes the video.
    /// </summary>
    [JsonPropertyName("url")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatMediaFile>))]
    public List<ThreeCatMediaFile>? Files { get; set; }
}

/// <summary>
/// A stream file of the media endpoint.
/// </summary>
public sealed class ThreeCatMediaFile
{
    /// <summary>
    /// Gets or sets the playback URL (progressive MP4).
    /// </summary>
    [JsonPropertyName("file")]
    public string? File { get; set; }

    /// <summary>
    /// Gets or sets the quality label («720p»).
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the file is active.
    /// </summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the file is an OTT stream.
    /// </summary>
    [JsonPropertyName("ott")]
    public bool Ott { get; set; }
}

/// <summary>
/// An image of the media endpoint (<c>{ "url": "...", "amplada": 670, "alcada": 378 }</c>).
/// </summary>
public sealed class ThreeCatMediaImage
{
    /// <summary>
    /// Gets or sets the image URL.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    [JsonPropertyName("amplada")]
    [JsonConverter(typeof(FlexibleInt32Converter))]
    public int? Width { get; set; }

    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    [JsonPropertyName("alcada")]
    [JsonConverter(typeof(FlexibleInt32Converter))]
    public int? Height { get; set; }
}
