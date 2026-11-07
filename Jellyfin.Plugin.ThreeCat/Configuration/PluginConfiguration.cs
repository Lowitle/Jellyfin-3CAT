using Jellyfin.Plugin.ThreeCat.Providers;
using Jellyfin.Plugin.ThreeCat.Sync;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ThreeCat.Configuration;

/// <summary>
/// A reference to a 3Cat program, either by its <c>programatv_id</c> («119987484») or by the URL of
/// any of its videos, from which the program id is resolved through the media endpoint.
/// </summary>
public class ThreeCatProgramReference
{
    /// <summary>
    /// Gets or sets the program id or the video URL as typed by the user.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cached program title (optional, filled in by the refresh task).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the metadata source of this program, which overrides
    /// <see cref="PluginConfiguration.MetadataSource"/> when it has a value.
    /// </summary>
    /// <remarks>
    /// <c>null</c> means «use the global source». The ids below can be typed by hand for the programs the
    /// identity dataset does not resolve yet, and they take precedence over the dataset.
    /// </remarks>
    public MetadataSource? Override { get; set; }

    /// <summary>
    /// Gets or sets the TMDB id of the program typed by the user (optional).
    /// </summary>
    /// <remarks>
    /// It is never validated against the network: it is a decision of the user and it wins over the
    /// identity dataset, which is how a wrong or missing id is corrected from the configuration page.
    /// </remarks>
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the TVDB id of the program typed by the user (optional).
    /// </summary>
    public string? TvdbId { get; set; }

    /// <summary>
    /// Gets or sets the anchors of the mapping typed by the user (optional).
    /// </summary>
    /// <remarks>
    /// <para>
    /// An anchor says which episode of the provider a video of 3Cat is, and the rest of the season is numbered by
    /// walking the provider list up and down from it. The syntax is the one 3Cat already writes in its titles:
    /// <c>T1=1</c> (the season 1 of 3Cat starts at the episode 1 of the provider), <c>T20=856</c>, <c>T22xC6=906</c>
    /// or <c>T1xC1=S01E01</c>, separated by semicolons.
    /// </para>
    /// <para>
    /// It is the only evidence that works for a series dubbed years after the original: the titles are
    /// translated and the air dates are the ones of the dub, so neither of them ties the two catalogs.
    /// </para>
    /// </remarks>
    public string? Anchors { get; set; }

    /// <summary>
    /// Gets or sets the kind of program the user pins for this program («Anime», «Infantil»,
    /// «Documentals»...), instead of the one the classifier resolves from the catalog of 3Cat.
    /// </summary>
    /// <remarks>
    /// <c>null</c> or empty means «automatic», which is what the page saves by default. It only changes the
    /// folder of the program: the genres of the series keep following the classification, so a library
    /// filtered by genre and the folder never disagree about what the program is. «Movies» is not offered
    /// by the page —the shape of the files is not a folder decision— and is ignored here too.
    /// </remarks>
    public string? CategoryOverride { get; set; }

    /// <summary>
    /// Gets or sets the kind of program the last run resolved (cache, filled in by the refresh task).
    /// </summary>
    /// <remarks>
    /// It is what the configuration page shows in the «Clasificación» column: the classification is a
    /// sweep of the facets of the 3Cat search endpoint, so the page cannot compute it on its own.
    /// </remarks>
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets the folder of a second library the program is mirrored to, on top of the destination
    /// folder of the plugin (for instance the folder of an «Anime» or «Infantil» library).
    /// </summary>
    /// <remarks>
    /// It is the root of the series folder, the folder the library indexes, not the series folder itself.
    /// Empty means the program is written only once, in <see cref="PluginConfiguration.DestinationPath"/>.
    /// </remarks>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the series folder the last run wrote (cache, filled in by the refresh task).
    /// </summary>
    /// <remarks>
    /// It is what lets a program that changes its folder leave nothing behind: the next run removes that
    /// folder —only when it holds nothing but files of this plugin and its <c>tvshow.nfo</c> names this
    /// program— before writing the series in its new folder.
    /// </remarks>
    public string WrittenPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mirrored series folder the last run wrote (cache, filled in by the refresh task).
    /// </summary>
    /// <remarks>
    /// Same as <see cref="WrittenPath"/> for the copy in the second library: it is what removes the mirror
    /// when the user clears the folder, and it also catches the copy left in the old folder when the
    /// series is renamed.
    /// </remarks>
    public string WrittenMirror { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source the last run numbered this program with (cache, filled in by the refresh task).
    /// </summary>
    /// <remarks>
    /// It is what tells the next run that the files of the folder carry the numbering of another source and
    /// have to be rewritten — after deleting the folder, which is only done when it is a folder of this
    /// plugin for this program. <c>null</c> means «the run has not written this program yet», so a fresh
    /// installation never deletes anything.
    /// </remarks>
    public MetadataSource? WrittenSource { get; set; }
}

/// <summary>
/// A library of Jellyfin that also indexes the folder of a kind of program.
/// </summary>
/// <remarks>
/// The library is named, not addressed by folder: the plugin owns <c>&lt;destination&gt;/&lt;kind&gt;</c>
/// and the folder of the library is where that folder is added, so renaming the library keeps working and
/// a library with several folders does not have to pick one.
/// </remarks>
public class ThreeCatKindLibrary
{
    /// <summary>
    /// Gets or sets the kind of program, one of the folders of the layout («Anime», «Infantil»...).
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the library of Jellyfin the folder is added to. Empty means none.
    /// </summary>
    public string Library { get; set; } = string.Empty;
}

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Schema version of the configuration written by this build.
    /// </summary>
    /// <remarks>
    /// Version 2 dropped the subtitle language picker: every track published by 3Cat is downloaded now,
    /// so <c>SubtitleLanguages</c> and <c>DownloadAllSubtitleLanguages</c> no longer exist. Version 3 adds
    /// the metadata source (<see cref="MetadataSource"/>, the dataset URL, the manual ids of a program and
    /// the TMDB key and language it is read with) and the source each program was written with; its
    /// defaults keep the behaviour of version 2 —3Cat numbering and the plugin's own metadata— so the
    /// migration is only a version stamp. Version 4 adds <see cref="OrganizeByKind"/>, which is turned
    /// off when a configuration written by an older build is migrated: a plugin update must never move
    /// the library of the user behind its back. Version 5 adds <see cref="KindLibraries"/> (the library
    /// of Jellyfin each kind of program is also indexed by) and the kind of program pinned per program;
    /// both are empty in a migrated configuration, so nothing moves and the version is only a stamp.
    /// Version 6 adds the TVDB key and PIN (<see cref="TvdbApiKey"/>, <see cref="TvdbPin"/>), which are
    /// empty in a migrated configuration: a program whose source is TVDB keeps the numbering of 3Cat until
    /// the user writes them, exactly as it did in 1.10.0.0. The XML serializer ignores the elements of a
    /// configuration written by an older build.
    /// </remarks>
    public const int CurrentVersion = 6;

    /// <summary>
    /// Version of the file naming scheme written by this build.
    /// </summary>
    /// <remarks>
    /// It is stored in <see cref="NamingVersion"/> and it is not part of
    /// <see cref="ConfigurationVersion"/>: it records what the library on disk looks like, so a plugin
    /// update that renames the episodes makes the next run rewrite the series folders.
    /// </remarks>
    public const int CurrentNamingVersion = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        ConfigurationVersion = CurrentVersion;
        Programs = [];
        DestinationPath = string.Empty;
        RefreshIntervalHours = 12;
        WriteNfo = true;
        WriteSubtitles = true;
        DeleteObsolete = false;
        PruneExpired = true;
        MaxItemsPerProgram = 0;
        ClassifyPrograms = true;
        SeparateAnime = true;
        SeparateChildren = true;
        OrganizeByKind = true;
        KindLibraries = [];
        MetadataSource = MetadataSource.ThreeCat;
        OpenTvIdsUrl = OpenTvIdsClient.DefaultUrl;
        TmdbApiKey = string.Empty;
        TmdbLanguage = TmdbClient.DefaultLanguage;
        TvdbApiKey = string.Empty;
        TvdbPin = string.Empty;
    }

    /// <summary>
    /// Gets or sets the version of the configuration schema, used to migrate stored settings.
    /// </summary>
    public int ConfigurationVersion { get; set; }

    /// <summary>
    /// Gets or sets the version of the file naming scheme the library was last written with.
    /// </summary>
    /// <remarks>
    /// The scheme changed in 1.5.0.0: the episodes are numbered with the season and the chapter of the
    /// title («T22xC6 - Visita als inferns» → <c>Serie - S22E06 - Visita als inferns</c>), so the files of
    /// a library written by an older build carry another name. The synchronization deletes the series
    /// folder before writing it again while this value is lower than <see cref="CurrentNamingVersion"/>,
    /// so the library does not keep the two schemes side by side.
    /// </remarks>
    public int NamingVersion { get; set; }

    /// <summary>
    /// Gets or sets the programs to mirror.
    /// </summary>
    public List<ThreeCatProgramReference> Programs { get; set; }

    /// <summary>
    /// Gets or sets the library root folder where the series folders are written.
    /// </summary>
    public string DestinationPath { get; set; }

    /// <summary>
    /// Gets or sets the refresh interval, in hours.
    /// </summary>
    public int RefreshIntervalHours { get; set; }

    /// <summary>
    /// Gets or sets the metadata source used by the programs that do not override it.
    /// </summary>
    /// <remarks>
    /// <see cref="Configuration.MetadataSource.ThreeCat"/> is the default and the only one that works
    /// without the identity dataset. With <see cref="Configuration.MetadataSource.Tmdb"/> or
    /// <see cref="Configuration.MetadataSource.Tvdb"/> the plugin writes the provider id in the
    /// <c>tvshow.nfo</c> so Jellyfin can fetch the series sheet, and numbers the episodes as the provider
    /// does; a program the dataset does not resolve keeps the numbering of 3Cat and no provider id is
    /// written.
    /// </remarks>
    public MetadataSource MetadataSource { get; set; }

    /// <summary>
    /// Gets or sets the URL of the identity dataset.
    /// </summary>
    /// <remarks>
    /// It is <see cref="OpenTvIdsClient.DefaultUrl"/> unless the user points the plugin at a fork or a
    /// local copy. The dataset is downloaded in the refresh task and cached in the data folder of the
    /// plugin; a failed download keeps the previous copy and never aborts the synchronization.
    /// </remarks>
    public string OpenTvIdsUrl { get; set; }

    /// <summary>
    /// Gets or sets the TMDB key the plugin reads the episodes of a series with.
    /// </summary>
    /// <remarks>
    /// It is a secret of the user: it is never written to the log and it is only sent to TMDB. Both forms the
    /// service accepts are valid — a v3 key (sent as the <c>api_key</c> parameter) and a v4 read token (sent
    /// as a bearer token), and the plugin tells them apart by their shape. Without a key, a program whose
    /// source is TMDB keeps the numbering of 3Cat, which is the default state of the plugin.
    /// </remarks>
    public string TmdbApiKey { get; set; }

    /// <summary>
    /// Gets or sets the language the episode titles are read from TMDB with.
    /// </summary>
    /// <remarks>
    /// It only affects the titles, never the matching, which goes by air date. Most of the catalog of 3Cat
    /// has no Catalan sheet in TMDB, so <see cref="Providers.TmdbClient.DefaultLanguage"/> is the default
    /// that returns something.
    /// </remarks>
    public string TmdbLanguage { get; set; }

    /// <summary>
    /// Gets or sets the TVDB key the plugin reads the episodes of a series with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is a secret of the user: it is never written to the log and it is only sent to TVDB, where it is
    /// exchanged for a session token. TheTVDB issues the key from its dashboard and, for a project that reads
    /// the API directly on behalf of its users, it requires a subscription or a licence with attribution:
    /// the page says so next to the field. Without a key, a program whose source is TVDB keeps the numbering
    /// of 3Cat.
    /// </para>
    /// <para>
    /// The language of the titles is not a setting: the plugin pairs the videos by air date, by the counter
    /// of 3Cat and by the anchors, and writes the titles of 3Cat in the <c>.nfo</c>, so the titles TVDB
    /// returns are only evidence in the table of the preview.
    /// </para>
    /// </remarks>
    public string TvdbApiKey { get; set; }

    /// <summary>
    /// Gets or sets the subscriber PIN of TVDB, which some keys need to log in.
    /// </summary>
    /// <remarks>
    /// TheTVDB asks for it only when the key belongs to a subscription: it travels in the same login as the
    /// key, it is a secret just like it, and it is empty for a licence with attribution.
    /// </remarks>
    public string TvdbPin { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether .nfo files are written.
    /// </summary>
    /// <remarks>
    /// Jellyfin reads the .nfo through the «Nfo» metadata provider. Without it the library still
    /// plays, but the titles, the plots and the episode numbers come from the file names and from
    /// whatever online provider the library has enabled.
    /// </remarks>
    public bool WriteNfo { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the WebVTT subtitles published by 3Cat are downloaded
    /// and converted to SRT.
    /// </summary>
    /// <remarks>
    /// Every track published for the video is written (<c>*.ca.srt</c>, <c>*.es.srt</c>...), one file per
    /// language. 3Cat publishes Catalan for most of its catalog and a second track for the programmes
    /// that are dubbed or subtitled in Spanish, so there is no language picker: the page follows the
    /// tracks of each video instead of the other way around.
    /// </remarks>
    public bool WriteSubtitles { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether .strm files that no longer exist in the catalog are removed.
    /// </summary>
    /// <remarks>
    /// It is the blunt instrument: every .strm/.nfo/.srt of the series folder that this run did not
    /// write is deleted, so a catalog response that comes back empty or half-read also empties the
    /// folder. Prefer <see cref="PruneExpired"/> and enable this only to clean up by hand.
    /// </remarks>
    public bool DeleteObsolete { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the videos whose <c>data_caducitat</c> has already
    /// passed are skipped and their previously written files are removed.
    /// </summary>
    /// <remarks>
    /// 3Cat publishes a hard expiry date for every video (the whole Doraemon catalog expires on
    /// 07/06/2027, for instance). Once that date passes the MP4 answers 404, so the .strm would be a
    /// broken episode. Enabled by default.
    /// </remarks>
    public bool PruneExpired { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of videos per program (0 means no limit).
    /// </summary>
    /// <remarks>
    /// The catalog is read newest first, so a positive value keeps the most recent emissions.
    /// </remarks>
    public int MaxItemsPerProgram { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the programs are written one folder per kind of program
    /// («Series», «Documentals», «Anime», «Infantil», «Programes TV» and «Movies»).
    /// </summary>
    /// <remarks>
    /// <para>
    /// With the layout on, the destination folder holds a folder per kind and Jellyfin indexes each of
    /// them: the «3Cat» library covers the folders of the series, a second library of type «Películas»
    /// covers the films, and a curated library («Anime», «Infantil»...) can point at one folder without a
    /// copy. The films stop being one-episode series and are written as movies
    /// (<c>movie.nfo</c>, <c>Título (Año)</c>).
    /// </para>
    /// <para>
    /// The folders are created when the library is prepared, and the first refresh after turning the
    /// layout on moves each program to its folder and deletes the old one —only when the folder is one of
    /// the plugin for that program—. Jellyfin reads the moved series again, which is why the option is
    /// off when a configuration written before 1.9.0.0 is migrated: the change has to be asked for.
    /// </para>
    /// </remarks>
    public bool OrganizeByKind { get; set; }

    /// <summary>
    /// Gets or sets the libraries of Jellyfin, by name, that also index the folder of a kind of program.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is the curated half of the organised layout: the «3Cat» library indexes every folder of the
    /// series, and the library of each kind («Anime», «Infantil», «Documentals»...) is pointed at the
    /// folder of that kind so it shows its programs with its own settings. Jellyfin indexes a folder in
    /// every library that points at it, so the programmes are not copied: one set of files, two libraries.
    /// </para>
    /// <para>
    /// The folders are attached when the user asks for it from the page («Aplicar a las bibliotecas»),
    /// never as a side effect of a refresh: adding an indexed folder to somebody's library is a decision
    /// of the user, not of the task.
    /// </para>
    /// </remarks>
    public List<ThreeCatKindLibrary> KindLibraries { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the programs are classified by the format 3Cat files
    /// them under («Sèrie», «Documental», «Animació»...) and that kind is written as the first genre of
    /// the series.
    /// </summary>
    /// <remarks>
    /// Jellyfin shows the genres as filters of the library, so a «Programas de TV» library filtered by
    /// «Sèrie» or «Documental» works without extra plugins. The classification is a sweep of the facets
    /// of the 3Cat search endpoint (24 requests, cached half an hour), which is why it can be turned
    /// off: the series then keep only the themes of their videos as genres.
    /// </remarks>
    public bool ClassifyPrograms { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether anime is told apart from the rest of the animation and
    /// «Anime» is written as the first genre of its series, ahead of «Animació».
    /// </summary>
    /// <remarks>
    /// 3Cat publishes no anime facet: it files «Bola de Drac» and «Doraemon» under the same
    /// <c>PUFF_SDIBU</c> format as «Mironins» or «Louie». The plugin reads it from the page of the
    /// program instead — the country of origin («Japó») and the internal collections the 3Cat web builds
    /// its anime rails with — so no external database and no TVDB key are needed. The page of each
    /// animated program is read once per refresh (six at a time, cached six hours) and it also brings the
    /// original title of the series. Requires <see cref="ClassifyPrograms"/>. Enabled by default.
    /// </remarks>
    public bool SeparateAnime { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the children's programmes are told apart and «Infantil» is
    /// written as a genre of their series, right after the kind.
    /// </summary>
    /// <remarks>
    /// 3Cat files every video under an audience («Infantil», «Juvenil», «Tots els Públics») and builds the
    /// SX3 rails with it, so the plugin reads it from the catalog it already downloads: no extra request.
    /// The channel of the SX3 family is the fallback for a video without an audience. The genre is written
    /// after the kind —«Anime», «Animació»— so a library filtered by «Infantil» groups the children's
    /// programmes and keeps the animation filters working. Requires <see cref="ClassifyPrograms"/>.
    /// Enabled by default.
    /// </remarks>
    public bool SeparateChildren { get; set; }
}
