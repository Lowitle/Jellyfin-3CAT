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
    /// so <c>SubtitleLanguages</c> and <c>DownloadAllSubtitleLanguages</c> no longer exist. The XML
    /// serializer ignores the elements of a configuration written by an older build.
    /// </remarks>
    public const int CurrentVersion = 2;

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
    }

    /// <summary>
    /// Gets or sets the version of the configuration schema, used to migrate stored settings.
    /// </summary>
    public int ConfigurationVersion { get; set; }

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
}
