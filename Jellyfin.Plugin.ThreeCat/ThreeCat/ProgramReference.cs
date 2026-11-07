using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Reads the references the configuration page accepts for a program of 3Cat and for a series of TMDB or
/// TVDB.
/// </summary>
/// <remarks>
/// 3Cat publishes no endpoint that maps the URL of a program page to its <c>programatv_id</c>, but the
/// search endpoint publishes the slug of every program next to its id, so the catalog that the page
/// already keeps in memory resolves a URL, a slug or a title without another request.
/// </remarks>
public static partial class ProgramReference
{
    /// <summary>
    /// Maximum number of programs a match returns, so a vague reference does not fill the page.
    /// </summary>
    private const int DefaultLimit = 5;

    /// <summary>
    /// Reads the slug of a program page out of a reference.
    /// </summary>
    /// <param name="reference">Reference typed by the user.</param>
    /// <returns>
    /// The slug in lowercase, or <c>null</c> when the reference is neither a URL of 3Cat nor a slug.
    /// </returns>
    /// <remarks>
    /// <c>https://www.3cat.cat/3cat/detectiu-conan/</c>, <c>www.3cat.cat/3cat/detectiu-conan</c> and
    /// <c>detectiu-conan</c> all give <c>detectiu-conan</c>.
    /// </remarks>
    public static string? ProgramSlug(string? reference)
    {
        var value = reference?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var url = ProgramUrlRegex().Match(value);
        if (url.Success)
        {
            return Unescape(url.Groups["slug"].Value).ToLowerInvariant();
        }

        if (value.Contains(' ') || value.Contains('.') || value.Contains('/'))
        {
            return null;
        }

        return SlugRegex().IsMatch(value) ? value.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Reads the id of a series of TMDB out of a reference.
    /// </summary>
    /// <param name="reference">Reference typed by the user.</param>
    /// <returns>The numeric id, or <c>null</c> when the reference holds none.</returns>
    /// <remarks>
    /// <c>https://www.themoviedb.org/tv/30983-case-closed?language=ca</c> and <c>30983</c> both give
    /// <c>30983</c>.
    /// </remarks>
    public static string? TmdbId(string? reference)
    {
        var value = reference?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var url = TmdbUrlRegex().Match(value);
        if (url.Success)
        {
            return url.Groups["id"].Value;
        }

        return value.All(char.IsAsciiDigit) ? value : null;
    }

    /// <summary>
    /// Reads the id of a series of TVDB out of a reference.
    /// </summary>
    /// <param name="reference">Reference typed by the user.</param>
    /// <returns>The numeric id, or <c>null</c> when the reference holds none.</returns>
    /// <remarks>
    /// <c>https://thetvdb.com/series/295068</c> and <c>295068</c> both give <c>295068</c>.
    /// </remarks>
    public static string? TvdbId(string? reference)
    {
        var value = reference?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var url = TvdbUrlRegex().Match(value);
        if (url.Success)
        {
            return url.Groups["id"].Value;
        }

        return value.All(char.IsAsciiDigit) ? value : null;
    }

    /// <summary>
    /// Whether a reference is the numeric id of a program or of a video.
    /// </summary>
    /// <param name="reference">Reference typed by the user.</param>
    /// <returns><c>true</c> when the reference holds digits and nothing else.</returns>
    public static bool IsId(string? reference)
        => !string.IsNullOrWhiteSpace(reference) && reference.Trim().All(char.IsAsciiDigit);

    /// <summary>
    /// Finds the programs of the catalog a reference could name, most likely first.
    /// </summary>
    /// <param name="programs">Programs of the catalog of 3Cat.</param>
    /// <param name="reference">Reference typed by the user: a URL, a slug or a title.</param>
    /// <param name="limit">Maximum number of programs to return.</param>
    /// <returns>
    /// The programs the reference could name, with the score that paired them; empty when none does.
    /// </returns>
    /// <remarks>
    /// The slug of the URL is the strongest evidence, then the title written out. The other scores cover
    /// what a person types: the title without its article («detectiu conan» against «El detectiu Conan»)
    /// and the words of the title in any order, which is enough to offer a short list of candidates
    /// instead of failing. A title is never scored against a longer reference: with the whole catalog of
    /// 3Cat in front, «detectiu conan» would score the program «Detectiu» above «El detectiu Conan», and
    /// the URL of a program would make «.CAT» a candidate.
    /// </remarks>
    public static IReadOnlyList<ProgramMatch> Match(
        IReadOnlyList<ThreeCatCatalogProgram>? programs,
        string? reference,
        int limit = DefaultLimit)
    {
        if (programs is not { Count: > 0 })
        {
            return [];
        }

        var text = Normalize(reference);
        var slug = ProgramSlug(reference);
        if (text.Length == 0 && slug is null)
        {
            return [];
        }

        var matches = new List<ProgramMatch>();

        foreach (var program in programs)
        {
            if (program is null || string.IsNullOrWhiteSpace(program.Id))
            {
                continue;
            }

            var score = Score(program, text, slug);
            if (score > 0)
            {
                matches.Add(new ProgramMatch(
                    program.Id,
                    string.IsNullOrWhiteSpace(program.Title) ? program.Id : program.Title.Trim(),
                    score));
            }
        }

        return matches
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(match => match.Id, StringComparer.Ordinal)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    /// <summary>
    /// Scores how well a program of the catalog fits a reference.
    /// </summary>
    /// <param name="program">Program of the catalog.</param>
    /// <param name="text">Reference normalized to letters, digits and single spaces.</param>
    /// <param name="slug">Slug of the reference, or <c>null</c>.</param>
    /// <returns>The score, from 0 (no evidence) to 100 (the slug of the program page).</returns>
    private static int Score(ThreeCatCatalogProgram program, string text, string? slug)
    {
        if (slug is not null
            && string.Equals(SlugOf(program.Url), slug, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        var title = Normalize(program.Title);
        if (title.Length == 0 || text.Length == 0)
        {
            return 0;
        }

        if (string.Equals(title, text, StringComparison.Ordinal))
        {
            return 90;
        }

        if (text.Length >= 3 && title.StartsWith(text, StringComparison.Ordinal))
        {
            return 70;
        }

        if (text.Length >= 3 && title.Contains(text, StringComparison.Ordinal))
        {
            return 60;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 1 && words.All(word => title.Contains(word, StringComparison.Ordinal))
            ? 40
            : 0;
    }

    /// <summary>
    /// Reads the slug of the public page of a program.
    /// </summary>
    /// <param name="url">Public page of the program.</param>
    /// <returns>The slug, or <c>null</c> when the URL names no program.</returns>
    private static string? SlugOf(string? url)
    {
        var match = ProgramUrlRegex().Match(url ?? string.Empty);
        return match.Success ? Unescape(match.Groups["slug"].Value) : null;
    }

    /// <summary>
    /// Decodes the escapes of a URL path segment.
    /// </summary>
    /// <param name="value">Segment of the URL.</param>
    /// <returns>The decoded segment, or the segment itself when it holds no valid escape.</returns>
    private static string Unescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    /// <summary>
    /// Normalizes a text to letters, digits and single spaces, without accents and in lowercase.
    /// </summary>
    /// <param name="value">Text to normalize.</param>
    /// <returns>The normalized text, empty when there is nothing to compare.</returns>
    /// <remarks>
    /// «El detectiu Conan» and «el detectiu conan» normalize to the same text, and so do «L'últim estiu»
    /// and «l ultim estiu»: the page and the catalog do not always punctuate the same way.
    /// </remarks>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex(
        @"3cat\.cat/3cat/(?<slug>[^/?#]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProgramUrlRegex();

    [GeneratedRegex(@"^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    [GeneratedRegex(
        @"themoviedb\.org/(?:tv|movie)/(?<id>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TmdbUrlRegex();

    [GeneratedRegex(
        @"thetvdb\.com/series/(?<id>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TvdbUrlRegex();
}

/// <summary>
/// A program of the catalog that fits a reference typed by the user.
/// </summary>
/// <param name="Id">Program id (<c>programatv_id</c>).</param>
/// <param name="Title">Title of the program as the catalog publishes it.</param>
/// <param name="Score">Evidence that paired it: 100 is the exact slug of its page.</param>
public sealed record ProgramMatch(string Id, string Title, int Score);
