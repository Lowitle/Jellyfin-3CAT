using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// An anchor of the mapping: «el vídeo T1C1 de 3Cat es el episodio S01E01 del proveedor».
/// </summary>
/// <remarks>
/// It is the only evidence that survives a change of language: 3Cat titles a dub in Catalan and the providers
/// publish the original titles, so when a series was dubbed years after it aired there is nothing in the text
/// nor in the air dates that ties both catalogs. The <c>capitol</c> of 3Cat usually does the work on its own (see
/// <see cref="ProviderNumbering"/>); the anchor is what calibrates the difference when the counter of 3Cat and the
/// absolute numbering of the provider do not start at the same episode: «T22xC6=906» fixes how far apart both
/// counters are, and the whole program follows from that single number.
/// </remarks>
/// <param name="Season">Season of 3Cat the anchor names («T22» → 22).</param>
/// <param name="Chapter">
/// Chapter of 3Cat the anchor names («T22xC6» → 6); <c>null</c> anchors the first video of the season, which
/// is the oldest one in air order.
/// </param>
/// <param name="TargetSeason">Season of the provider the video is (<c>S01</c> → 1).</param>
/// <param name="TargetEpisode">Episode of the provider the video is.</param>
/// <param name="Text">The text the user typed, so it can be quoted back in a message.</param>
public sealed record ProviderAnchor(
    int Season,
    int? Chapter,
    int TargetSeason,
    int TargetEpisode,
    string Text);

/// <summary>
/// Reads the anchors the user types in the configuration page.
/// </summary>
/// <remarks>
/// <para>
/// The syntax is the one the titles of 3Cat already use, so there is nothing to learn: a left hand side
/// <c>T22</c> (the season) or <c>T22xC6</c> (the chapter) and a right hand side <c>S01E906</c>, <c>E906</c> or
/// a bare <c>906</c> — the bare number, and <c>E</c>, mean the first season of the provider, which is where the
/// providers keep the absolute numbering of an anime («Detectiu Conan» is <c>S01E1..S01E1150</c> in TMDB).
/// </para>
/// <para>
/// Several anchors are separated by a semicolon, a comma, a pipe or a line break, and the entries that do not
/// parse are ignored instead of failing the whole preview: a typo in one line must not hide the other nine.
/// </para>
/// </remarks>
public static partial class ProviderAnchors
{
    /// <summary>
    /// Separators accepted between anchors.
    /// </summary>
    private static readonly char[] Separators = [';', ',', '|', '\n', '\r'];

    /// <summary>
    /// Reads the anchors of a text.
    /// </summary>
    /// <param name="text">Text typed by the user («T1=1; T20=856»), or <c>null</c>.</param>
    /// <returns>The anchors that could be read, in the order they were written.</returns>
    public static IReadOnlyList<ProviderAnchor> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var anchors = new List<ProviderAnchor>();

        foreach (var token in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ParseOne(token) is { } anchor)
            {
                anchors.Add(anchor);
            }
        }

        return anchors;
    }

    /// <summary>
    /// Reads one anchor.
    /// </summary>
    /// <param name="token">Text of the anchor («T1xC1=S01E01»).</param>
    /// <returns>The anchor, or <c>null</c> when the text does not name one.</returns>
    internal static ProviderAnchor? ParseOne(string token)
    {
        var match = AnchorRegex().Match(token.Trim());
        if (!match.Success
            || !int.TryParse(match.Groups["season"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var season))
        {
            return null;
        }

        int? chapter = null;
        if (match.Groups["chapter"].Success)
        {
            if (!int.TryParse(
                match.Groups["chapter"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedChapter))
            {
                return null;
            }

            chapter = parsedChapter;
        }

        var target = match.Groups["target"].Value.Trim();

        var full = TargetRegex().Match(target);
        if (full.Success
            && int.TryParse(full.Groups["season"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var targetSeason)
            && int.TryParse(full.Groups["episode"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var targetEpisode))
        {
            return new ProviderAnchor(season, chapter, targetSeason, targetEpisode, token.Trim());
        }

        var partial = EpisodeRegex().Match(target);
        if (partial.Success
            && int.TryParse(partial.Groups["episode"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var episode))
        {
            return new ProviderAnchor(season, chapter, EpisodeNumbering.DefaultSeason, episode, token.Trim());
        }

        return null;
    }

    // «T22», «T22xC6», «T 22 x C 6», «T22C6» (3Cat writes all four) and then «=».
    [GeneratedRegex(
        @"^T\s*(?<season>\d{1,3})\s*(?:x?\s*C\s*(?<chapter>\d{1,5}))?\s*=\s*(?<target>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnchorRegex();

    // «S01E906», «S1xE906», «s01e906».
    [GeneratedRegex(
        @"^S\s*(?<season>\d{1,3})\s*x?\s*E\s*(?<episode>\d{1,5})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TargetRegex();

    // «E906» and «906», which is the absolute numbering of a series with a single season in the provider.
    [GeneratedRegex(
        @"^E?\s*(?<episode>\d{1,5})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeRegex();
}
