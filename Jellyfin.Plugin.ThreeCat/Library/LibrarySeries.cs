namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// A series ready to be written to disk, with the metadata Jellyfin uses to classify it.
/// </summary>
public sealed class LibrarySeries
{
    /// <summary>
    /// Gets or sets the series title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the 3Cat program id (written as the default unique id of the <c>tvshow.nfo</c>).
    /// </summary>
    public string ProgramId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the plot.
    /// </summary>
    public string? Plot { get; set; }

    /// <summary>
    /// Gets or sets the genres written to the <c>tvshow.nfo</c>. The first one is the kind of
    /// program ("Serie", "Documental", "Infantil"...); Jellyfin shows them as filters.
    /// </summary>
    public IReadOnlyCollection<string> Genres { get; set; } = [];

    /// <summary>
    /// Gets or sets the tags written to the <c>tvshow.nfo</c> (the raw 3Cat program type).
    /// </summary>
    public IReadOnlyCollection<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the IMDb id, when 3Cat publishes one.
    /// </summary>
    public string? ImdbId { get; set; }

    /// <summary>
    /// Gets or sets the production country ("ES").
    /// </summary>
    public string? Country { get; set; }
}
