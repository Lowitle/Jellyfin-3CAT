using System.Text.Json;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// Creates (or reuses) the libraries that point at the destination folder, so a fresh installation does
/// not need a trip through the «Add media library» wizard.
/// </summary>
/// <remarks>
/// <para>
/// With the organised layout the programs are written one folder per kind, so the «3Cat» library indexes
/// the folders of the series and a second library of type «Películas» indexes the folder of the films.
/// Without it there is a single folder and a single «Programas de TV» library, which is what every build
/// before 1.9.0.0 created.
/// </para>
/// <para>
/// Folders are not exclusive —Jellyfin indexes a folder in every library that points at it—, so a
/// destination that already belongs to another library is not a conflict, and a curated library
/// («Anime», «Infantil»...) can point at one of the folders of the layout without any copy.
/// </para>
/// <para>
/// The plugin only writes <c>.strm</c>, <c>.nfo</c> and <c>.srt</c> files: the libraries have to read the
/// local metadata (<c>Nfo</c>) and, because 3Cat numbering does not match TVDB/TMDB, they should not be
/// asked to fetch online metadata for the episodes.
/// </para>
/// </remarks>
public static class LibraryProvisioner
{
    /// <summary>
    /// Library name used when the page does not send one.
    /// </summary>
    public const string DefaultLibraryName = "3Cat";

    private static readonly JsonSerializerOptions CloneOptions = new();

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Creates the library (or adds the folders to the library with the same name) and asks Jellyfin to
    /// scan it.
    /// </summary>
    /// <param name="libraryManager">Library manager of the server.</param>
    /// <param name="request">Folders to index, name and type of the library.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="libraryMonitor">Library monitor used to tell Jellyfin about the folders.</param>
    /// <returns>The result of the operation.</returns>
    public static LibraryProvisionStatus Provision(
        ILibraryManager libraryManager,
        LibraryProvisionRequest request,
        ILogger logger,
        ILibraryMonitor? libraryMonitor = null)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(logger);

        var folders = Folders(request.Folders);
        if (folders.Count == 0)
        {
            return new LibraryProvisionStatus(
                false,
                "invalid",
                null,
                "No hay ninguna carpeta que indexar.",
                []);
        }

        // The destination folder has to be a writable folder of the server; the folders of the layout
        // are created under it, so the check is made on the root and never on a folder that this very
        // call is going to create.
        var check = LibraryPath.Check(request.RootPath);
        if (!check.IsValid || !check.Exists || !check.Writable)
        {
            return new LibraryProvisionStatus(
                false,
                "invalid",
                null,
                check.Error ?? "La carpeta de destino no se puede usar.",
                []);
        }

        try
        {
            foreach (var folder in folders)
            {
                Directory.CreateDirectory(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogError(ex, "3Cat: no se pudieron crear las carpetas de la biblioteca");

            return new LibraryProvisionStatus(
                false,
                "invalid",
                null,
                $"No se pudieron crear las carpetas de la biblioteca: {ex.Message}",
                []);
        }

        var libraries = libraryManager.GetVirtualFolders() ?? [];
        var libraryName = string.IsNullOrWhiteSpace(request.Name) ? DefaultLibraryName : request.Name.Trim();

        var indexed = libraries.FirstOrDefault(library => folders.All(folder => Owns(library, folder)));
        if (indexed is not null)
        {
            return new LibraryProvisionStatus(
                true,
                "existing",
                indexed.Name,
                $"Las carpetas de 3Cat ya están indexadas por la biblioteca «{indexed.Name}».",
                Folders(indexed));
        }

        var sameName = libraries.FirstOrDefault(
            library => string.Equals(library.Name, libraryName, StringComparison.OrdinalIgnoreCase));

        if (sameName is not null)
        {
            return AppendToExisting(sameName, folders, libraryManager, libraryMonitor, logger);
        }

        return Create(libraryName, folders, request.CollectionType, libraries, libraryManager, libraryMonitor, logger);
    }

    /// <summary>
    /// Adds the folders the library does not index yet, and takes the destination folder out when the
    /// library pointed at it and the programs now live one level down.
    /// </summary>
    /// <param name="library">Library of the same name.</param>
    /// <param name="folders">Folders the library has to index.</param>
    /// <param name="libraryManager">Library manager of the server.</param>
    /// <param name="libraryMonitor">Library monitor used to tell Jellyfin about the folders.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The result of the operation.</returns>
    private static LibraryProvisionStatus AppendToExisting(
        VirtualFolderInfo library,
        List<string> folders,
        ILibraryManager libraryManager,
        ILibraryMonitor? libraryMonitor,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(library.Name))
        {
            return new LibraryProvisionStatus(
                false,
                "conflict",
                library.Name,
                $"Ya existe una biblioteca «{library.Name}» con otras carpetas. Añade las carpetas de 3Cat a mano desde el panel de bibliotecas.",
                Folders(library));
        }

        var indexed = Folders(library);
        var missing = folders.Where(folder => !Owns(library, folder)).ToList();

        // A library written by an older build indexes the destination folder, and the programs now live
        // one level down: a folder that holds the folders of the layout is replaced by them, or Jellyfin
        // would keep reading a library whose series folder is the parent of the real ones.
        var parents = indexed
            .Where(location => folders.Any(folder => IsAncestor(location, folder)))
            .ToList();

        if (missing.Count == 0 && parents.Count == 0)
        {
            return new LibraryProvisionStatus(
                true,
                "existing",
                library.Name,
                $"La biblioteca «{library.Name}» ya indexa las carpetas de 3Cat.",
                [.. indexed]);
        }

        var current = new List<string>(indexed);

        foreach (var parent in parents)
        {
            try
            {
                libraryManager.RemoveMediaPath(library.Name, parent);
                current.RemoveAll(location => PathComparer.Equals(location, parent));
                logger.LogInformation("3Cat: carpeta {Folder} retirada de la biblioteca {Library}", parent, library.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "3Cat: no se pudo retirar {Folder} de la biblioteca {Library}", parent, library.Name);
            }
        }

        foreach (var folder in missing)
        {
            try
            {
                libraryManager.AddMediaPath(library.Name, new MediaPathInfo(folder));
                current.Add(folder);
                logger.LogInformation("3Cat: carpeta {Folder} añadida a la biblioteca {Library}", folder, library.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "3Cat: no se pudo añadir {Folder} a la biblioteca {Library}", folder, library.Name);

                return new LibraryProvisionStatus(
                    false,
                    "conflict",
                    library.Name,
                    $"No se pudo añadir «{folder}» a la biblioteca «{library.Name}»: {ex.Message}",
                    [.. current]);
            }
        }

        foreach (var folder in folders)
        {
            libraryMonitor?.ReportFileSystemChanged(folder);
        }

        return new LibraryProvisionStatus(
            true,
            "updated",
            library.Name,
            $"La biblioteca «{library.Name}» indexa ahora {folders.Count} carpeta(s) de 3Cat.",
            [.. current]);
    }

    /// <summary>
    /// Creates the library with every folder of the layout.
    /// </summary>
    /// <param name="libraryName">Name of the library.</param>
    /// <param name="folders">Folders to index.</param>
    /// <param name="collectionType">Type of content of the library.</param>
    /// <param name="libraries">Libraries of the server, used as a template.</param>
    /// <param name="libraryManager">Library manager of the server.</param>
    /// <param name="libraryMonitor">Library monitor used to tell Jellyfin about the folders.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The result of the operation.</returns>
    private static LibraryProvisionStatus Create(
        string libraryName,
        List<string> folders,
        CollectionTypeOptions collectionType,
        IReadOnlyList<VirtualFolderInfo> libraries,
        ILibraryManager libraryManager,
        ILibraryMonitor? libraryMonitor,
        ILogger logger)
    {
        try
        {
            libraryManager.AddVirtualFolder(
                libraryName,
                collectionType,
                BuildOptions(folders, collectionType, libraries),
                refreshLibrary: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "3Cat: no se pudo crear la biblioteca {Library}", libraryName);

            return new LibraryProvisionStatus(
                false,
                "invalid",
                libraryName,
                $"No se pudo crear la biblioteca «{libraryName}»: {ex.Message}",
                []);
        }

        foreach (var folder in folders)
        {
            libraryMonitor?.ReportFileSystemChanged(folder);
        }

        logger.LogInformation(
            "3Cat: biblioteca {Library} creada para {Folders}",
            libraryName,
            string.Join(", ", folders));

        return new LibraryProvisionStatus(
            true,
            "created",
            libraryName,
            $"Biblioteca «{libraryName}» creada; Jellyfin indexará {folders.Count} carpeta(s).",
            [.. folders]);
    }

    /// <summary>
    /// Builds the options of the library: the local <c>.nfo</c> files are the source of truth (3Cat
    /// numbering does not match TVDB/TMDB) and a template of an existing library of the same type is
    /// reused so the image and metadata providers the user already tuned are kept.
    /// </summary>
    /// <param name="locations">Folders of the new library.</param>
    /// <param name="collectionType">Type of content of the library.</param>
    /// <param name="libraries">Libraries of the server.</param>
    /// <returns>The options of the library.</returns>
    private static LibraryOptions BuildOptions(
        List<string> locations,
        CollectionTypeOptions collectionType,
        IReadOnlyList<VirtualFolderInfo> libraries)
    {
        var template = libraries
            .FirstOrDefault(library =>
                library.CollectionType == collectionType && library.LibraryOptions is not null)
            ?.LibraryOptions;

        var options = template is null ? new LibraryOptions() : Clone(template);

        options.Enabled = true;
        options.EnablePhotos = false;
        options.EnableRealtimeMonitor = true;
        options.SaveLocalMetadata = true;
        options.SaveSubtitlesWithMedia = true;
        options.EnableAutomaticSeriesGrouping = true;
        options.MetadataSavers ??= ["Nfo"];
        options.LocalMetadataReaderOrder ??= ["Nfo"];

        // The folders of this library are set here, never copied from the template.
        options.PathInfos = locations.Select(location => new MediaPathInfo(location)).ToArray();
        return options;
    }

    private static LibraryOptions Clone(LibraryOptions options)
        => JsonSerializer.Deserialize<LibraryOptions>(
            JsonSerializer.Serialize(options, CloneOptions),
            CloneOptions) ?? new LibraryOptions();

    /// <summary>
    /// Normalizes a list of folders, dropping the repeated and the unreadable ones.
    /// </summary>
    /// <param name="folders">Folders to normalize.</param>
    /// <returns>The folders, without duplicates, in the original order.</returns>
    private static List<string> Folders(IEnumerable<string>? folders)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(PathComparer);

        foreach (var folder in folders ?? [])
        {
            var normalized = LibraryIO.Normalize(folder);
            if (normalized is not null && seen.Add(normalized))
            {
                values.Add(normalized);
            }
        }

        return values;
    }

    private static IReadOnlyList<string> Folders(VirtualFolderInfo library)
        => library.Locations is { Length: > 0 } locations ? [.. locations] : [];

    private static bool Owns(VirtualFolderInfo library, string folder)
        => Folders(library).Any(location => LibraryPath.SamePath(location, folder));

    /// <summary>
    /// Tells whether a folder is a parent of another one, which is how the destination folder a library
    /// indexed before the layout existed is told apart from the folders of the layout.
    /// </summary>
    /// <param name="parent">Folder that may be the parent.</param>
    /// <param name="child">Folder that may be inside it.</param>
    /// <returns><c>true</c> when <paramref name="child"/> is inside <paramref name="parent"/>.</returns>
    internal static bool IsAncestor(string? parent, string child)
    {
        var outer = LibraryIO.Normalize(parent);
        var inner = LibraryIO.Normalize(child);

        if (outer is null || inner is null || PathComparer.Equals(outer, inner))
        {
            return false;
        }

        var comparison = PathComparer == StringComparer.OrdinalIgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return inner.StartsWith(outer + Path.DirectorySeparatorChar, comparison)
            || inner.StartsWith(outer + Path.AltDirectorySeparatorChar, comparison);
    }
}

/// <summary>
/// Request to create or update a library of the plugin.
/// </summary>
/// <param name="RootPath">Destination folder of the plugin, which has to be a writable folder.</param>
/// <param name="Folders">Folders of the layout the library has to index.</param>
/// <param name="Name">Library name; «3Cat» when empty.</param>
/// <param name="CollectionType">Type of content of the library.</param>
public sealed record LibraryProvisionRequest(
    string RootPath,
    IReadOnlyList<string> Folders,
    string? Name,
    CollectionTypeOptions CollectionType);

/// <summary>
/// Result of the «create library» request.
/// </summary>
/// <param name="Success">Whether the folders ended up in a library.</param>
/// <param name="Outcome">
/// Machine readable outcome: <c>created</c>, <c>updated</c>, <c>existing</c>, <c>conflict</c> or <c>invalid</c>.
/// </param>
/// <param name="LibraryName">Library that was created or updated.</param>
/// <param name="Message">Message shown to the user.</param>
/// <param name="LibraryPaths">Folders the library indexes after the request.</param>
public sealed record LibraryProvisionStatus(
    bool Success,
    string Outcome,
    string? LibraryName,
    string Message,
    IReadOnlyList<string> LibraryPaths);
