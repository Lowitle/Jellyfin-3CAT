using System.Text.Json;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// Creates (or reuses) a «Programas de TV» library that points at the destination folder, so a fresh
/// installation does not need a trip through the «Add media library» wizard.
/// </summary>
/// <remarks>
/// <para>
/// 3Cat publishes series only, so the library always indexes the destination root: every program is a
/// series folder straight under it. Folders are not exclusive (Jellyfin indexes a folder in every
/// library that points at it), so a destination that already belongs to another library is not a
/// conflict.
/// </para>
/// <para>
/// The plugin only writes <c>.strm</c>, <c>.nfo</c> and <c>.srt</c> files: the library has to read the
/// local metadata (<c>Nfo</c>) and, because 3Cat numbering does not match TVDB/TMDB, it should not be
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

    /// <summary>
    /// Creates the library (or adds the folder to the library with the same name) and asks Jellyfin to
    /// scan it.
    /// </summary>
    /// <param name="libraryManager">Library manager of the server.</param>
    /// <param name="path">Destination folder.</param>
    /// <param name="name">Library name; <see cref="DefaultLibraryName"/> when empty.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="libraryMonitor">Library monitor used to tell Jellyfin about the folder.</param>
    /// <returns>The result of the operation.</returns>
    public static LibraryProvisionStatus Provision(
        ILibraryManager libraryManager,
        string? path,
        string? name,
        ILogger logger,
        ILibraryMonitor? libraryMonitor = null)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(logger);

        var check = LibraryPath.Check(path);
        if (!check.IsValid || !check.Exists || !check.Writable)
        {
            return new LibraryProvisionStatus(
                false,
                "invalid",
                null,
                check.Path,
                check.Error ?? "La carpeta de destino no se puede usar.",
                []);
        }

        var folder = check.Path;
        var libraries = libraryManager.GetVirtualFolders() ?? [];
        var libraryName = string.IsNullOrWhiteSpace(name) ? DefaultLibraryName : name.Trim();

        var indexed = libraries.FirstOrDefault(library => Owns(library, folder));
        if (indexed is not null)
        {
            return new LibraryProvisionStatus(
                true,
                "existing",
                indexed.Name,
                folder,
                $"La carpeta ya está indexada por la biblioteca «{indexed.Name}».",
                Folders(indexed));
        }

        var sameName = libraries.FirstOrDefault(
            library => string.Equals(library.Name, libraryName, StringComparison.OrdinalIgnoreCase));

        if (sameName is not null)
        {
            return AppendToExisting(sameName, folder, libraryManager, libraryMonitor, logger);
        }

        return Create(libraryName, folder, libraries, libraryManager, libraryMonitor, logger);
    }

    private static LibraryProvisionStatus AppendToExisting(
        VirtualFolderInfo library,
        string folder,
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
                folder,
                $"Ya existe una biblioteca «{library.Name}» con otras carpetas. Añade «{folder}» a mano desde el panel de bibliotecas.",
                Folders(library));
        }

        try
        {
            libraryManager.AddMediaPath(library.Name, new MediaPathInfo(folder));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "3Cat: no se pudo añadir {Folder} a la biblioteca {Library}", folder, library.Name);

            return new LibraryProvisionStatus(
                false,
                "conflict",
                library.Name,
                folder,
                $"No se pudo añadir «{folder}» a la biblioteca «{library.Name}»: {ex.Message}",
                Folders(library));
        }

        libraryMonitor?.ReportFileSystemChanged(folder);
        logger.LogInformation("3Cat: carpeta {Folder} añadida a la biblioteca {Library}", folder, library.Name);

        return new LibraryProvisionStatus(
            true,
            "updated",
            library.Name,
            folder,
            $"La biblioteca «{library.Name}» ahora indexa también «{folder}».",
            [.. Folders(library), folder]);
    }

    private static LibraryProvisionStatus Create(
        string libraryName,
        string folder,
        IReadOnlyList<VirtualFolderInfo> libraries,
        ILibraryManager libraryManager,
        ILibraryMonitor? libraryMonitor,
        ILogger logger)
    {
        try
        {
            libraryManager.AddVirtualFolder(
                libraryName,
                CollectionTypeOptions.tvshows,
                BuildOptions([folder], libraries),
                refreshLibrary: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "3Cat: no se pudo crear la biblioteca {Library}", libraryName);

            return new LibraryProvisionStatus(
                false,
                "invalid",
                libraryName,
                folder,
                $"No se pudo crear la biblioteca «{libraryName}»: {ex.Message}",
                []);
        }

        libraryMonitor?.ReportFileSystemChanged(folder);
        logger.LogInformation("3Cat: biblioteca {Library} creada para {Folder}", libraryName, folder);

        return new LibraryProvisionStatus(
            true,
            "created",
            libraryName,
            folder,
            $"Biblioteca «{libraryName}» creada; Jellyfin indexará «{folder}».",
            [folder]);
    }

    /// <summary>
    /// Builds the options of the library: the local <c>.nfo</c> files are the source of truth (3Cat
    /// numbering does not match TVDB/TMDB) and a template of an existing TV library is reused so the
    /// image and metadata providers the user already tuned are kept.
    /// </summary>
    /// <param name="locations">Folders of the new library.</param>
    /// <param name="libraries">Libraries of the server.</param>
    /// <returns>The options of the library.</returns>
    private static LibraryOptions BuildOptions(
        IReadOnlyList<string> locations,
        IReadOnlyList<VirtualFolderInfo> libraries)
    {
        var template = libraries
            .FirstOrDefault(library =>
                library.CollectionType == CollectionTypeOptions.tvshows && library.LibraryOptions is not null)
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

    private static IReadOnlyList<string> Folders(VirtualFolderInfo library)
        => library.Locations is { Length: > 0 } locations ? [.. locations] : [];

    private static bool Owns(VirtualFolderInfo library, string folder)
        => Folders(library).Any(location => SamePath(location, folder));

    private static bool SamePath(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);

        if (a is null || b is null)
        {
            return false;
        }

        return string.Equals(
            a,
            b,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string? Normalize(string? path)
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
}

/// <summary>
/// Result of the «create library» request.
/// </summary>
/// <param name="Success">Whether the folder ended up in a library.</param>
/// <param name="Outcome">
/// Machine readable outcome: <c>created</c>, <c>updated</c>, <c>existing</c>, <c>conflict</c> or <c>invalid</c>.
/// </param>
/// <param name="LibraryName">Library that was created or updated.</param>
/// <param name="Path">Normalized destination folder.</param>
/// <param name="Message">Message shown to the user.</param>
/// <param name="LibraryPaths">Folders the library indexes after the request.</param>
public sealed record LibraryProvisionStatus(
    bool Success,
    string Outcome,
    string? LibraryName,
    string Path,
    string Message,
    IReadOnlyList<string> LibraryPaths);

