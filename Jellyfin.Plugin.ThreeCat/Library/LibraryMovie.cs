namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// A film ready to be written to disk, with the metadata Jellyfin uses to classify it.
/// </summary>
/// <remarks>
/// The films of 3Cat are the programs the catalog files under <c>PUFF_FILM</c>. Instead of the season
/// layout of the series they get the one the movie resolver of Jellyfin reads: a folder named
/// <c>Título (Año)</c> with a <c>.strm</c> and a <c>movie.nfo</c> inside.
/// </remarks>
public sealed class LibraryMovie
{
    /// <summary>
    /// Gets or sets the film title, which is the title 3Cat publishes for the video.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the 3Cat program id (written as the default unique id of the <c>movie.nfo</c>).
    /// </summary>
    public string ProgramId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the year of the film, taken from its air date.
    /// </summary>
    /// <remarks>
    /// 3Cat publishes no production year, so the year of the broadcast is the closest thing: it is what
    /// names the folder (<c>Título (2021)</c>) and what Jellyfin shows next to the title.
    /// </remarks>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the plot.
    /// </summary>
    public string? Plot { get; set; }

    /// <summary>
    /// Gets or sets the runtime.
    /// </summary>
    public TimeSpan? Runtime { get; set; }

    /// <summary>
    /// Gets or sets the air date, written as the premiere date of the film.
    /// </summary>
    public DateTimeOffset? PremiereDate { get; set; }

    /// <summary>
    /// Gets or sets the genres written to the <c>movie.nfo</c>: the kind of program («Cinema») and the
    /// themes of the program.
    /// </summary>
    public IReadOnlyCollection<string> Genres { get; set; } = [];

    /// <summary>
    /// Gets or sets the tags written to the <c>movie.nfo</c> (the raw 3Cat program type).
    /// </summary>
    public IReadOnlyCollection<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the production country ("ES").
    /// </summary>
    public string? Country { get; set; }
}

/// <summary>
/// A video of a film folder: the whole film, or one of the parts 3Cat publishes it in.
/// </summary>
/// <remarks>
/// Jellyfin has no notion of the parts of a film —that is an Emby feature—, so the parts are written as
/// versions of the same film (<c>- part1</c>, <c>- part2</c>) and the user picks the one to play.
/// </remarks>
public sealed class LibraryMovieFile
{
    /// <summary>
    /// Gets or sets the playback URL written to the .strm file.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the part of the film the video is, or 0 when the file is the whole film.
    /// </summary>
    public int Part { get; set; }

    /// <summary>
    /// Gets or sets the subtitles to write next to the film (one SRT file per language).
    /// </summary>
    public IReadOnlyList<LibrarySubtitle> Subtitles { get; set; } = [];

    /// <summary>
    /// Gets or sets the 3Cat video id (written as a unique id of the file).
    /// </summary>
    public string? VideoId { get; set; }
}
