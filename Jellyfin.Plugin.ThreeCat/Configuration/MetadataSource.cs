namespace Jellyfin.Plugin.ThreeCat.Configuration;

/// <summary>
/// Database the metadata of a series is taken from, and therefore the numbering its episodes are
/// written with.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ThreeCat"/> is the default and the behaviour the plugin has always had: the season and
/// the episode are the ones 3Cat writes in the title («T22xC6 - …»). With <see cref="Tmdb"/> or
/// <see cref="Tvdb"/> the plugin still writes the same local <c>.nfo</c> (title, plot, genres, art),
/// but the episodes are numbered as the provider numbers them, so Jellyfin's online metadata matches
/// episode by episode instead of by chance.
/// </para>
/// <para>
/// The values are stored by name in the configuration, so new values can be appended but the existing
/// ones must never be renamed.
/// </para>
/// </remarks>
public enum MetadataSource
{
    /// <summary>
    /// The numbering 3Cat publishes in the title (default). Requires no external service.
    /// </summary>
    ThreeCat = 0,

    /// <summary>
    /// The numbering of TMDB, matched internally by air date and title. Only available for the
    /// programs the identity dataset maps to a TMDB id.
    /// </summary>
    Tmdb = 1,

    /// <summary>
    /// The numbering of TVDB, matched internally by air date and title. Only available for the
    /// programs the identity dataset maps to a TVDB id.
    /// </summary>
    Tvdb = 2
}
