using Jellyfin.Plugin.ThreeCat.Configuration;

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
    /// Gets or sets the original title of the program, when 3Cat publishes one and it differs from the
    /// Catalan title. Jellyfin shows it as the original title of the series.
    /// </summary>
    public string? OriginalTitle { get; set; }

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
    /// Gets or sets the TMDB id of the series, when the identity dataset (or the user) resolves one.
    /// </summary>
    /// <remarks>
    /// It is only written to the <c>tvshow.nfo</c> when <see cref="NumberingSource"/> is
    /// <see cref="MetadataSource.Tmdb"/>: a series numbered with 3Cat must not carry a provider id, or
    /// Jellyfin would fetch the episodes of a numbering the files do not follow.
    /// </remarks>
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the TVDB id of the series, when the identity dataset (or the user) resolves one.
    /// </summary>
    public string? TvdbId { get; set; }

    /// <summary>
    /// Gets or sets the source the episodes were numbered with.
    /// </summary>
    /// <remarks>
    /// It decides which <c>uniqueid</c> the <c>tvshow.nfo</c> carries, so the id written to disk always
    /// matches the numbering of the files next to it. The default, <see cref="MetadataSource.ThreeCat"/>,
    /// writes no provider id and keeps the output of 1.5.0.0 byte for byte.
    /// </remarks>
    public MetadataSource NumberingSource { get; set; }

    /// <summary>
    /// Gets or sets the production country ("ES").
    /// </summary>
    public string? Country { get; set; }
}
