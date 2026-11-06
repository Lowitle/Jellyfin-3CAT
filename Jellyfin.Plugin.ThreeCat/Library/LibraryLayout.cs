namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// The folders a library of the plugin indexes, split by the type of content Jellyfin has to import.
/// </summary>
/// <param name="Series">
/// Folders of a «Programas de TV» library: the destination folder itself, or one folder per kind of
/// program when the layout is organised.
/// </param>
/// <param name="Movies">
/// Folders of a «Películas» library. Empty when the layout is not organised, because the films are then
/// written as one-episode series straight in the destination folder, as every build before 1.9.0.0 did.
/// </param>
internal sealed record LibraryFolderLayout(IReadOnlyList<string> Series, IReadOnlyList<string> Movies);

/// <summary>
/// Resolves the folders the plugin asks Jellyfin to index.
/// </summary>
internal static class LibraryLayout
{
    /// <summary>
    /// Resolves the folders of the libraries of the destination folder.
    /// </summary>
    /// <param name="rootPath">Destination folder of the plugin.</param>
    /// <param name="organizeByKind">Whether the programs are written one folder per kind.</param>
    /// <returns>The folders of each library.</returns>
    /// <remarks>
    /// Without the layout there is a single folder and a single library, which is the behaviour of
    /// every build before 1.9.0.0 and the one a user that has not asked for the change keeps.
    /// </remarks>
    internal static LibraryFolderLayout Resolve(string rootPath, bool organizeByKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var root = rootPath.Trim();

        if (!organizeByKind)
        {
            return new LibraryFolderLayout([root], []);
        }

        return new LibraryFolderLayout(
            [.. LibraryCategory.SeriesFolders.Select(category => LibraryCategory.Folder(root, category))],
            [.. LibraryCategory.MovieFolders.Select(category => LibraryCategory.Folder(root, category))]);
    }

    /// <summary>
    /// Builds the folder of a kind of program inside the destination folder.
    /// </summary>
    /// <param name="rootPath">Destination folder of the plugin.</param>
    /// <param name="organizeByKind">Whether the programs are written one folder per kind.</param>
    /// <param name="category">Category the program belongs to.</param>
    /// <returns>The folder the program is written to.</returns>
    internal static string ProgramFolder(string rootPath, bool organizeByKind, string category)
        => organizeByKind
            ? LibraryCategory.Folder(rootPath.Trim(), category)
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath.Trim()));
}
