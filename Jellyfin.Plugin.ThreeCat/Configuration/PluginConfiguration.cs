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
    public const int CurrentVersion = 1;

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
        SubtitleLanguages = ["ca"];
        DownloadAllSubtitleLanguages = false;
        DeleteObsolete = false;
        PruneExpired = true;
        MaxItemsPerProgram = 0;
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
    public bool WriteSubtitles { get; set; }

    /// <summary>
    /// Gets or sets the subtitle languages to download («ca», «es»...).
    /// </summary>
    /// <remarks>
    /// 3Cat publishes Catalan tracks («Català») for most of its catalog. The list is only used when
    /// <see cref="DownloadAllSubtitleLanguages"/> is disabled.
    /// </remarks>
    public List<string> SubtitleLanguages { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every subtitle track published by 3Cat is downloaded,
    /// regardless of <see cref="SubtitleLanguages"/>.
    /// </summary>
    public bool DownloadAllSubtitleLanguages { get; set; }

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
}
