namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// Removes what the plugin wrote for a program, whatever the shape of the folder: a series folder, a film
/// folder, or a category folder that holds the film folders of the program.
/// </summary>
/// <remarks>
/// The cleanup is what keeps the library honest when a program changes its folder —a new title, another
/// kind of program, the film layout—, so both writes (series and films) go through this class instead of
/// knowing the shape of each other.
/// </remarks>
internal static class LibraryCleanup
{
    /// <summary>
    /// Deletes the folders the plugin wrote for a program inside <paramref name="folder"/>.
    /// </summary>
    /// <param name="folder">Folder to clean: a series folder, a film folder or a category folder.</param>
    /// <param name="programId">3Cat program id the folders have to belong to.</param>
    /// <returns>The number of folders deleted.</returns>
    /// <remarks>
    /// A folder that is none of those —or that holds anything the plugin did not write— is left alone: the
    /// folders of the library can be shared with the user.
    /// </remarks>
    internal static int TryDeleteProgram(string? folder, string? programId)
    {
        var path = LibraryIO.Normalize(folder);

        if (path is null || string.IsNullOrWhiteSpace(programId) || !Directory.Exists(path))
        {
            return 0;
        }

        if (StrmLibrary.TryDeleteSeriesFolder(path, programId) || MovieLibrary.TryDeleteMovieFolder(path, programId))
        {
            return 1;
        }

        // A category folder of the film layout holds a folder per film, so only the films of this program
        // are removed: an empty destination is left in place, because the library indexes it.
        return MovieLibrary.DeleteUnwrittenMovies(path, programId, keep: []);
    }
}
