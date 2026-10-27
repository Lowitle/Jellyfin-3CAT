using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Reads the public endpoints of 3Cat: the catalog of a program (<c>api.3cat.cat/videos</c>) and the
/// detail of a video (<c>api-media.3cat.cat/pvideo/media.jsp</c>).
/// </summary>
/// <remarks>
/// Both endpoints are the ones the 3Cat web player uses. The catalog publishes at most
/// <see cref="PageSize"/> videos per page, newest first, and the media endpoint is the only place
/// that publishes the progressive MP4 URL and the WebVTT subtitle URL: the catalog only lists the
/// languages of the subtitles, without a URL.
/// </remarks>
public sealed partial class ThreeCatClient
{
    /// <summary>
    /// Videos requested per catalog page. 3Cat accepts 100 and the Doraemon catalog (199 emissions)
    /// fits in two pages.
    /// </summary>
    public const int PageSize = 100;

    /// <summary>
    /// Hard cap of catalog pages, so a wrong <c>total_pagines</c> never turns into an endless loop.
    /// </summary>
    internal const int MaxPages = 200;

    internal const string CatalogUrl =
        "https://api.3cat.cat/videos?_format=json&origen=auto&perfil=pc&programatv_id={0}"
        + "&items_pagina={1}&pagina={2}&sdom=img&version=2.0&cache=180&https=true&master=yes"
        + "&perfils_extra=imatges_minim_master";

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<ThreeCatClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThreeCatClient"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client configured by <see cref="PluginServiceRegistrator"/>.</param>
    /// <param name="logger">Logger.</param>
    public ThreeCatClient(HttpClient httpClient, ILogger<ThreeCatClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    [GeneratedRegex(@"video/(\d{4,})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VideoIdRegex();

    [GeneratedRegex(@"\d{5,}", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsRegex();

    /// <summary>
    /// Normalizes a program reference typed by the user.
    /// </summary>
    /// <param name="reference">
    /// Either the numeric <c>programatv_id</c>, the id of any of the videos of the program, or the
    /// URL of the video page.
    /// </param>
    /// <returns>The numeric id found in the reference, or the trimmed reference when it holds no digits.</returns>
    /// <remarks>
    /// 3Cat has no endpoint that maps a program page URL to its <c>programatv_id</c> (the page is a
    /// single page application and the id is not in the served HTML), so the configuration accepts
    /// the URL of any video of the program: the media endpoint publishes <c>informacio.programa_id</c>.
    /// </remarks>
    public static string NormalizeReference(string? reference)
    {
        var value = reference?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (value.All(char.IsAsciiDigit))
        {
            return value;
        }

        var video = VideoIdRegex().Match(value);
        if (video.Success)
        {
            return video.Groups[1].Value;
        }

        var digits = DigitsRegex().Match(value);
        return digits.Success ? digits.Value : value;
    }
}
