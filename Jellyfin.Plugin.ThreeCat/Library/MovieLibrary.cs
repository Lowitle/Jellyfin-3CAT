namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// Writes a film folder (<c>.strm</c> + <c>movie.nfo</c> + <c>.srt</c>) that Jellyfin can import as a
/// movie.
/// </summary>
/// <remarks>
/// <para>
/// The folder is named <c>Título (Año)</c>, which is the layout the movie resolver of Jellyfin reads, and
/// the <c>movie.nfo</c> next to the stream carries the local metadata. No provider id is written on
/// purpose: the identity dataset of the plugin resolves the ids of the <em>series</em> of 3Cat, and a
/// TMDB id of the television record on a film would make Jellyfin fetch the wrong metadata.
/// </para>
/// <para>
/// A film 3Cat publishes in several videos keeps a single folder: the parts are written as versions of
/// the same film —<c>- part1</c>, <c>- part2</c>—, because Jellyfin has no notion of the parts of a
/// movie. A program that files several films under it gets a folder per title, which is what makes the
/// catalog usable without a mapping by hand.
/// </para>
/// </remarks>
public static class MovieLibrary
{
    /// <summary>
    /// Name of the NFO document of a film. Jellyfin reads it as the local metadata of the folder.
    /// </summary>
    internal const string MovieNfo = "movie.nfo";

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Gets the folder that holds a film.
    /// </summary>
    /// <param name="rootPath">Folder the films are written to.</param>
    /// <param name="title">Film title.</param>
    /// <param name="year">Year of the film, written between parentheses.</param>
    /// <returns>The folder of the film.</returns>
    public static string MovieFolder(string rootPath, string title, int? year)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return Path.Combine(rootPath, LibraryNaming.MovieFolderName(title, year));
    }

    /// <summary>
    /// Writes a film into <paramref name="rootPath"/>.
    /// </summary>
    /// <param name="rootPath">Folder the films are written to.</param>
    /// <param name="movie">Film metadata (title, year, plot, genres...).</param>
    /// <param name="files">Videos of the film: the whole film or its parts.</param>
    /// <param name="options">Write options.</param>
    /// <returns>The result of the operation.</returns>
    public static LibraryWriteResult Write(
        string rootPath,
        LibraryMovie movie,
        IEnumerable<LibraryMovieFile> files,
        LibraryWriteOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(movie);
        ArgumentException.ThrowIfNullOrWhiteSpace(movie.Title);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(options);

        var folder = MovieFolder(rootPath, movie.Title, movie.Year);
        Directory.CreateDirectory(folder);

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesWritten = 0;

        if (options.WriteNfo)
        {
            filesWritten += LibraryIO.WriteText(Path.Combine(folder, MovieNfo), NfoBuilder.BuildMovie(movie), written) ? 1 : 0;
        }

        var count = 0;

        foreach (var file in files.OrderBy(file => file.Part))
        {
            var baseName = LibraryNaming.MovieFileName(movie.Title, movie.Year, file.Part);
            filesWritten += LibraryIO.WriteText(Path.Combine(folder, baseName + ".strm"), file.Url + "\n", written) ? 1 : 0;

            if (options.WriteSubtitles)
            {
                foreach (var subtitle in LibraryNaming.CleanSubtitles(file.Subtitles))
                {
                    filesWritten += LibraryIO.WriteText(
                        Path.Combine(folder, $"{baseName}.{subtitle.Language}.srt"),
                        LibraryIO.ToCrlf(subtitle.Srt),
                        written) ? 1 : 0;
                }
            }

            count++;
        }

        var removed = options.DeleteObsolete ? DeleteObsolete(folder, written) : 0;

        return new LibraryWriteResult(folder, count, filesWritten, removed);
    }

    /// <summary>
    /// Deletes a film folder written by this plugin, so a film that is not published anymore leaves
    /// nothing behind.
    /// </summary>
    /// <param name="folder">Film folder to delete.</param>
    /// <param name="programId">3Cat program id the film has to belong to.</param>
    /// <returns><c>true</c> when the folder was deleted.</returns>
    /// <remarks>
    /// It is as conservative as the deletion of a series: the folder has to hold a <c>movie.nfo</c> whose
    /// <c>uniqueid type="threecat"</c> is the program, and nothing but files of the plugin.
    /// </remarks>
    public static bool TryDeleteMovieFolder(string? folder, string? programId)
    {
        var path = LibraryIO.Normalize(folder);
        if (path is null || !Directory.Exists(path))
        {
            return false;
        }

        if (!LibraryIO.BelongsTo(Path.Combine(path, MovieNfo), programId))
        {
            return false;
        }

        try
        {
            if (!LibraryIO.HoldsOnlyManagedFiles(path))
            {
                return false;
            }

            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes the film folders of a program that the run did not write, which is how a film that 3Cat
    /// has expired or renamed stops filling the library.
    /// </summary>
    /// <param name="rootPath">Folder that holds the film folders.</param>
    /// <param name="programId">3Cat program id, used to make sure a folder is the one of this program.</param>
    /// <param name="keep">Folders written by this run.</param>
    /// <returns>The number of folders deleted.</returns>
    public static int DeleteUnwrittenMovies(string? rootPath, string? programId, IReadOnlyCollection<string> keep)
    {
        var root = LibraryIO.Normalize(rootPath);

        if (root is null || string.IsNullOrWhiteSpace(programId) || !Directory.Exists(root))
        {
            return 0;
        }

        var written = new HashSet<string>(keep.Select(LibraryIO.Normalize).OfType<string>(), PathComparer);
        var removed = 0;

        try
        {
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                var path = LibraryIO.Normalize(folder);

                if (path is null || written.Contains(path))
                {
                    continue;
                }

                if (TryDeleteMovieFolder(path, programId))
                {
                    removed++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that disappears while it is being enumerated is exactly what this wants.
        }

        return removed;
    }

    private static int DeleteObsolete(string folder, HashSet<string> written)
    {
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(file) is not (".strm" or ".nfo" or ".srt"))
            {
                continue;
            }

            if (string.Equals(Path.GetFileName(file), MovieNfo, StringComparison.OrdinalIgnoreCase)
                || written.Contains(file))
            {
                continue;
            }

            File.Delete(file);
            removed++;
        }

        return removed;
    }
}
