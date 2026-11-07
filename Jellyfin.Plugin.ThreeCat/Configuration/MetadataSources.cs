namespace Jellyfin.Plugin.ThreeCat.Configuration;

/// <summary>
/// What every value of <see cref="MetadataSource"/> implies, asked once here instead of in a chain of
/// comparisons in every caller.
/// </summary>
/// <remarks>
/// The plugin grew from one source to three and the questions are always the same three: whether a program
/// is numbered by an external provider at all, whether that provider is TVDB, and whether the numbering is
/// the absolute one. A new value of the enum only has to answer them in one place.
/// </remarks>
public static class MetadataSources
{
    /// <summary>
    /// Tells whether the episodes are numbered by an external provider instead of by 3Cat.
    /// </summary>
    /// <param name="source">Source of the numbering.</param>
    /// <returns><c>true</c> when the plugin has to read the provider to number the series.</returns>
    public static bool IsProvider(this MetadataSource source)
        => source != MetadataSource.ThreeCat;

    /// <summary>
    /// Tells whether the numbering comes from TVDB, in any of its orders.
    /// </summary>
    /// <param name="source">Source of the numbering.</param>
    /// <returns><c>true</c> for <c>Tvdb</c> and <c>TvdbAbsolute</c>.</returns>
    public static bool UsesTvdb(this MetadataSource source)
        => source is MetadataSource.Tvdb or MetadataSource.TvdbAbsolute;

    /// <summary>
    /// Tells whether the numbering is the absolute one of TVDB: one season, one episode per work.
    /// </summary>
    /// <param name="source">Source of the numbering.</param>
    /// <returns><c>true</c> for <c>TvdbAbsolute</c>.</returns>
    public static bool IsAbsolute(this MetadataSource source)
        => source == MetadataSource.TvdbAbsolute;

    /// <summary>
    /// Names the provider of a source the way the page and the log spell it.
    /// </summary>
    /// <param name="source">Source of the numbering.</param>
    /// <returns><c>TMDB</c>, <c>TVDB</c> or <c>TVDB absoluta</c>; <c>3Cat</c> when there is no provider.</returns>
    public static string ProviderName(this MetadataSource source)
        => source switch
        {
            MetadataSource.Tmdb => "TMDB",
            MetadataSource.Tvdb => "TVDB",
            MetadataSource.TvdbAbsolute => "TVDB absoluta",
            _ => "3Cat"
        };
}
