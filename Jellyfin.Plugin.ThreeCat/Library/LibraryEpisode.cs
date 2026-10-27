namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// An episode ready to be written to disk.
/// </summary>
public sealed class LibraryEpisode
{
    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    public int Season { get; set; } = 1;

    /// <summary>
    /// Gets or sets the episode number.
    /// </summary>
    public int Episode { get; set; } = 1;

    /// <summary>
    /// Gets or sets the episode title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the plot.
    /// </summary>
    public string? Plot { get; set; }

    /// <summary>
    /// Gets or sets the playback URL written to the .strm file.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the subtitles to write next to the episode (one SRT file per language).
    /// </summary>
    public IReadOnlyList<LibrarySubtitle> Subtitles { get; set; } = [];

    /// <summary>
    /// Gets or sets the air date.
    /// </summary>
    public DateTimeOffset? Aired { get; set; }

    /// <summary>
    /// Gets or sets the runtime.
    /// </summary>
    public TimeSpan? Runtime { get; set; }

    /// <summary>
    /// Gets or sets the thumbnail URL.
    /// </summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// Gets or sets the 3Cat video id (written as a unique id in the .nfo).
    /// </summary>
    public string? VideoId { get; set; }

    /// <summary>
    /// Gets or sets the genres.
    /// </summary>
    public IReadOnlyCollection<string> Genres { get; set; } = [];

    /// <summary>
    /// Gets or sets the director names.
    /// </summary>
    public string? Director { get; set; }
}

/// <summary>
/// A subtitle track ready to be written next to an episode.
/// </summary>
/// <param name="Language">ISO language code of the track ("es", "en", "ca", "gl", "eu"...).</param>
/// <param name="Srt">SRT content.</param>
public sealed record LibrarySubtitle(string Language, string Srt);

/// <summary>
/// Options of <see cref="StrmLibrary.Write"/>.
/// </summary>
public sealed class LibraryWriteOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether .nfo files are written.
    /// </summary>
    public bool WriteNfo { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether subtitles are written.
    /// </summary>
    public bool WriteSubtitles { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether obsolete files are deleted.
    /// </summary>
    public bool DeleteObsolete { get; set; }
}

/// <summary>
/// Result of a library write operation.
/// </summary>
/// <param name="SeriesPath">Folder of the series.</param>
/// <param name="EpisodesWritten">Number of episodes written.</param>
/// <param name="FilesWritten">Number of files actually created or updated.</param>
/// <param name="FilesRemoved">Number of obsolete files removed.</param>
public sealed record LibraryWriteResult(string SeriesPath, int EpisodesWritten, int FilesWritten, int FilesRemoved);
