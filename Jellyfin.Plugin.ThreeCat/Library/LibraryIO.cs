using System.Text;
using System.Xml.Linq;

namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// File helpers shared by the series and the movie libraries.
/// </summary>
internal static class LibraryIO
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// Gets the extensions of the files this plugin writes, plus the artwork it saves next to them.
    /// </summary>
    /// <remarks>
    /// It is what tells a folder of the plugin from a folder the user filled with something else: the
    /// cleanup only deletes a folder that holds nothing outside this list.
    /// </remarks>
    internal static IReadOnlySet<string> ManagedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".strm", ".nfo", ".srt", ".jpg", ".jpeg", ".png", ".webp"
    };

    /// <summary>
    /// Writes a text file unless it already holds the very same content.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <param name="content">Content to write.</param>
    /// <param name="written">Files written by the run, used later to delete the obsolete ones.</param>
    /// <returns><c>true</c> when the file was created or updated.</returns>
    internal static bool WriteText(string path, string content, HashSet<string> written)
    {
        written.Add(path);

        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, Utf8NoBom);
        return true;
    }

    /// <summary>
    /// Normalizes the line endings of a subtitle document to the CRLF the <c>.srt</c> format expects.
    /// </summary>
    /// <param name="value">Document to normalize.</param>
    /// <returns>The document with CRLF line endings.</returns>
    internal static string ToCrlf(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);

    /// <summary>
    /// Tells whether an NFO names a 3Cat program in its <c>threecat</c> unique id.
    /// </summary>
    /// <param name="nfo">Path of the <c>tvshow.nfo</c> or <c>movie.nfo</c>.</param>
    /// <param name="programId">Program id.</param>
    /// <returns>Whether the file belongs to the program.</returns>
    internal static bool BelongsTo(string? nfo, string? programId)
    {
        if (string.IsNullOrWhiteSpace(nfo) || string.IsNullOrWhiteSpace(programId) || !File.Exists(nfo))
        {
            return false;
        }

        try
        {
            var id = XDocument
                .Load(nfo)
                .Descendants("uniqueid")
                .FirstOrDefault(element => string.Equals(
                    (string?)element.Attribute("type"),
                    "threecat",
                    StringComparison.OrdinalIgnoreCase))
                ?.Value
                .Trim();

            return string.Equals(id, programId.Trim(), StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Normalizes a folder path, or returns <c>null</c> when it cannot be read.
    /// </summary>
    /// <param name="path">Path to normalize.</param>
    /// <returns>The full path without the trailing separator.</returns>
    internal static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Tells whether a folder holds nothing but files of this plugin.
    /// </summary>
    /// <param name="folder">Folder to check.</param>
    /// <returns>Whether the folder can be deleted without losing anything of the user.</returns>
    internal static bool HoldsOnlyManagedFiles(string folder)
        => !Directory
            .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Any(file => !ManagedExtensions.Contains(Path.GetExtension(file)));
}
