using System.Globalization;
using System.Security;
using System.Text;

namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// Validates the destination folder before writing anything to disk, so a container path that is
/// not mounted or not writable produces an actionable message instead of a bare
/// <see cref="UnauthorizedAccessException"/>.
/// </summary>
public static class LibraryPath
{
    private const string ProbeFileName = ".threecat-write-test";

    private static readonly string[] UnixRoots = ["/media", "/mnt", "/srv", "/data", "/volume1", "/volume2", "/var/lib/jellyfin"];

    /// <summary>
    /// Creates the destination folder (when needed) and makes sure Jellyfin can write into it.
    /// </summary>
    /// <param name="path">Configured destination folder.</param>
    /// <exception cref="InvalidOperationException">The folder cannot be created or written.</exception>
    public static void EnsureWritableRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var value = path.Trim();
        if (!Path.IsPathRooted(value))
        {
            throw new InvalidOperationException(
                Message(value, "la ruta no es absoluta; escribe la ruta del servidor Jellyfin, no la del host"));
        }

        var fullPath = Normalize(value);

        try
        {
            Directory.CreateDirectory(fullPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException or SecurityException)
        {
            throw new InvalidOperationException(Message(fullPath, Describe(ex)), ex);
        }

        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException(Message(fullPath, "la ruta apunta a un archivo, no a una carpeta"));
        }

        var (writable, error) = Probe(fullPath);
        if (!writable)
        {
            throw new InvalidOperationException(Message(fullPath, error ?? "la carpeta no admite escritura"));
        }
    }

    /// <summary>
    /// Checks the configured destination folder without writing anything to it.
    /// </summary>
    /// <param name="path">Configured destination folder.</param>
    /// <returns>The result of the check, including writable folders in the server as suggestions.</returns>
    public static LibraryPathStatus Check(string? path)
    {
        var value = path?.Trim() ?? string.Empty;

        if (value.Length == 0)
        {
            return new LibraryPathStatus(value, false, false, false, "Indica la carpeta de destino.", []);
        }

        if (!Path.IsPathRooted(value))
        {
            return new LibraryPathStatus(
                value,
                false,
                false,
                false,
                "La ruta debe ser absoluta, tal y como la ve el servidor Jellyfin (no la del host).",
                Suggest());
        }

        string fullPath;
        try
        {
            fullPath = Normalize(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return new LibraryPathStatus(value, false, false, false, "La ruta no es válida: " + ex.Message, Suggest());
        }

        if (File.Exists(fullPath))
        {
            return new LibraryPathStatus(
                fullPath,
                true,
                true,
                false,
                "La ruta apunta a un archivo, no a una carpeta.",
                Suggest());
        }

        if (!Directory.Exists(fullPath))
        {
            return new LibraryPathStatus(
                fullPath,
                true,
                false,
                false,
                "La carpeta no existe dentro del servidor Jellyfin. Si usas Docker, créala como volumen y escribe la ruta del contenedor.",
                Suggest());
        }

        var (writable, error) = Probe(fullPath);
        return new LibraryPathStatus(fullPath, true, true, writable, writable ? null : error, writable ? [] : Suggest());
    }

    /// <summary>
    /// Tells whether a folder exists and accepts new files.
    /// </summary>
    /// <param name="path">Folder to test.</param>
    /// <returns><c>true</c> when a probe file could be created and removed.</returns>
    public static bool IsWritable(string path)
        => Directory.Exists(path) && Probe(path).Writable;

    /// <summary>
    /// Tells whether two paths point at the same folder, comparing them the way the platform does.
    /// </summary>
    /// <param name="left">First path.</param>
    /// <param name="right">Second path.</param>
    /// <returns><c>true</c> when both paths are the same folder.</returns>
    public static bool SamePath(string? left, string? right)
    {
        var first = NormalizeOrNull(left);
        var second = NormalizeOrNull(right);

        if (first is null || second is null)
        {
            return false;
        }

        return string.Equals(
            first,
            second,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string? NormalizeOrNull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Normalize(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Lists the folders of the server that Jellyfin can write into. Used to help the user pick a
    /// destination when the configured one is not mounted.
    /// </summary>
    /// <returns>Writable folders (at most ten).</returns>
    public static IReadOnlyList<string> Suggest()
    {
        var suggestions = new List<string>();
        var probes = 0;

        foreach (var root in Roots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var candidates = new List<string> { root };

            try
            {
                candidates.AddRange(Directory
                    .EnumerateDirectories(root)
                    .Take(6)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // The root is mounted but cannot be listed; the root itself is still a candidate.
            }

            foreach (var candidate in candidates)
            {
                if (probes++ >= 24)
                {
                    return suggestions;
                }

                if (IsWritable(candidate))
                {
                    suggestions.Add(candidate);
                    if (suggestions.Count >= 10)
                    {
                        return suggestions;
                    }
                }
            }
        }

        return suggestions;
    }

    private static (bool Writable, string? Error) Probe(string path)
    {
        var probe = Path.Combine(path, ProbeFileName);

        try
        {
            using (File.Create(probe))
            {
            }

            return (true, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException or SecurityException)
        {
            return (false, Describe(ex));
        }
        finally
        {
            try
            {
                if (File.Exists(probe))
                {
                    File.Delete(probe);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // Nothing else to do: the probe file is hidden and harmless.
            }
        }
    }

    private static IEnumerable<string> Roots()
    {
        if (OperatingSystem.IsWindows())
        {
            return DriveInfo.GetDrives()
                .Where(drive => drive.IsReady
                    && drive.DriveType is DriveType.Fixed or DriveType.Network or DriveType.Removable)
                .Select(drive => drive.RootDirectory.FullName);
        }

        return UnixRoots;
    }

    private static string Normalize(string value)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));

    private static string Describe(Exception exception)
        => exception switch
        {
            UnauthorizedAccessException => "el usuario de Jellyfin no tiene permiso de escritura",
            DirectoryNotFoundException => "la carpeta no existe",
            IOException => "el sistema de archivos no admite escritura: " + exception.Message,
            _ => exception.Message
        };

    private static string Message(string path, string reason)
    {
        var message = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"3Cat: no se puede usar la carpeta de destino '{path}' ({reason}). ")
            .Append("Comprueba que la carpeta exista dentro del servidor Jellyfin, que esté montada y que el usuario de Jellyfin pueda escribir en ella");

        var suggestions = Suggest();
        if (suggestions.Count > 0)
        {
            message.Append("; carpetas escribibles detectadas: ")
                .Append(string.Join(", ", suggestions.Take(5)));
        }

        return message.Append('.').ToString();
    }
}

/// <summary>
/// Result of checking the destination folder.
/// </summary>
/// <param name="Path">Checked path.</param>
/// <param name="IsValid">Whether the path is an absolute, usable path.</param>
/// <param name="Exists">Whether the folder exists in the server.</param>
/// <param name="Writable">Whether Jellyfin can create files in it.</param>
/// <param name="Error">Human readable reason when the folder cannot be used.</param>
/// <param name="Suggestions">Writable folders of the server that could be used instead.</param>
public sealed record LibraryPathStatus(
    string Path,
    bool IsValid,
    bool Exists,
    bool Writable,
    string? Error,
    IReadOnlyList<string> Suggestions);
