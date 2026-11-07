namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// Folders the programs of 3Cat are written to, one per kind of program.
/// </summary>
/// <remarks>
/// <para>
/// A destination folder that holds every program straight under it forces the user to keep a single
/// library: Jellyfin filters by genre, but «Anime», «Infantil» or «Documentals» cannot be libraries of
/// their own, and the films share the season layout of the series. With one folder per kind the plugin
/// writes each program where its kind belongs, the default «3Cat» library indexes the folders of the
/// series and a curated library can point at just one of them —Jellyfin indexes a folder in every
/// library that points at it, so no file is duplicated.
/// </para>
/// <para>
/// The folders are only written when the user turns the layout on (<see cref="Configuration.PluginConfiguration.OrganizeByKind"/>):
/// a library written by an older build keeps its programs straight under the destination folder until
/// the user asks for the change, which rewrites the series once (Jellyfin reads them again).
/// </para>
/// </remarks>
internal static class LibraryCategory
{
    /// <summary>The miscellany: news, magazines, contests, sport, theatre, music and the unknown.</summary>
    internal const string Shows = "Programes TV";

    /// <summary>Fiction series.</summary>
    internal const string Series = "Series";

    /// <summary>Documentaries and docu-series.</summary>
    internal const string Documentaries = "Documentals";

    /// <summary>Japanese animation.</summary>
    internal const string Anime = "Anime";

    /// <summary>Children's programmes, whatever their kind.</summary>
    internal const string Children = "Infantil";

    /// <summary>Films, written as movies instead of as a one-episode series.</summary>
    internal const string Movies = "Movies";

    /// <summary>
    /// Gets the folders the «3Cat» library indexes: every series of the destination folder, one per kind.
    /// </summary>
    /// <remarks>
    /// The order is the one the configuration page shows, most specific first, and it is stable: the
    /// provisioning adds the folders that are missing and never renames them.
    /// </remarks>
    internal static IReadOnlyList<string> SeriesFolders { get; } =
        [Shows, Series, Documentaries, Anime, Children];

    /// <summary>
    /// Gets the folders the movie library indexes.
    /// </summary>
    internal static IReadOnlyList<string> MovieFolders { get; } = [Movies];

    /// <summary>
    /// Gets every folder of the layout, series and films.
    /// </summary>
    internal static IReadOnlyList<string> All { get; } =
        [.. SeriesFolders, .. MovieFolders];

    /// <summary>
    /// Resolves the folder a program is written to.
    /// </summary>
    /// <param name="classification">Kind of program and genres of the series.</param>
    /// <returns>The folder of the program, relative to the destination folder.</returns>
    /// <remarks>
    /// The order of the rules is the precedence of the layout: an anime is an anime even when it is also
    /// for children, a film is a film and goes to its own library, and the children's programmes come
    /// before the kind because the audience of the SX3 channels is what groups «Doraemon» with
    /// «Mironins». A program 3Cat files under no known facet —or every program, when the classification
    /// is turned off— lands in <see cref="Shows"/>, never straight in the destination folder: the library
    /// does not index that folder anymore.
    /// </remarks>
    internal static string Resolve(ProgramClassification? classification)
    {
        if (classification is null)
        {
            return Shows;
        }

        var kind = classification.Kind;

        if (Same(kind, ProgramClassifier.Anime))
        {
            return Anime;
        }

        if (Same(kind, ProgramClassifier.Cinema))
        {
            return Movies;
        }

        if (classification.Genres.Any(genre => Same(genre, ProgramClassifier.Children)))
        {
            return Children;
        }

        if (Same(kind, ProgramClassifier.Documentary))
        {
            return Documentaries;
        }

        if (Same(kind, ProgramClassifier.Series) || Same(kind, ProgramClassifier.Animation))
        {
            return Series;
        }

        return Shows;
    }

    /// <summary>
    /// Resolves the folder a program is written to, honouring the kind the user pinned for it.
    /// </summary>
    /// <param name="classification">Kind of program and genres of the series.</param>
    /// <param name="pinned">
    /// Kind of program the user pinned, or <c>null</c> to keep the classification. «Movies» is ignored: the
    /// shape of the files (a series or a film) follows the catalog of 3Cat, never a folder choice.
    /// </param>
    /// <returns>The folder of the program, relative to the destination folder.</returns>
    internal static string Resolve(ProgramClassification? classification, string? pinned)
    {
        var normalized = Normalize(pinned);

        if (normalized is not null && IsSeriesCategory(normalized))
        {
            return normalized;
        }

        return Resolve(classification);
    }

    /// <summary>
    /// Normalizes the name of a kind of program to the folder of the layout.
    /// </summary>
    /// <param name="category">Name typed or stored in the configuration, in any casing.</param>
    /// <returns>The folder of the kind, or <c>null</c> when it is not one of the folders of the layout.</returns>
    internal static string? Normalize(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        foreach (var folder in All)
        {
            if (Same(category, folder))
            {
                return folder;
            }
        }

        return null;
    }

    /// <summary>
    /// Tells whether a kind of program is written as a series, which is every folder but the films.
    /// </summary>
    /// <param name="category">Name of the kind, in any casing.</param>
    /// <returns><c>true</c> when the programs of that kind are series.</returns>
    internal static bool IsSeriesCategory(string? category)
    {
        var normalized = Normalize(category);

        return normalized is not null && SeriesFolders.Any(folder => Same(folder, normalized));
    }

    /// <summary>
    /// Builds the folder a category is written to.
    /// </summary>
    /// <param name="rootPath">Destination folder of the plugin.</param>
    /// <param name="category">Category of the program.</param>
    /// <returns>The folder of the category.</returns>
    internal static string Folder(string rootPath, string category)
        => Path.Combine(rootPath, category);

    private static bool Same(string? left, string right)
        => string.Equals(left?.Trim(), right, StringComparison.OrdinalIgnoreCase);
}
