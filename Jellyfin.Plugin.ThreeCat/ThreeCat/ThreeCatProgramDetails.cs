using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Root of the answer of <c>api.3cat.cat/programestv/{id}</c>, the endpoint the 3Cat portal publishes
/// the page of a program with. The payload is nested under <c>resposta</c>, as in the other endpoints.
/// </summary>
public sealed class ThreeCatProgramDetailEnvelope
{
    /// <summary>
    /// Gets or sets the body of the answer.
    /// </summary>
    [JsonPropertyName("resposta")]
    public ThreeCatProgramDetailResponse? Response { get; set; }
}

/// <summary>
/// Body of the answer of the program endpoint.
/// </summary>
public sealed class ThreeCatProgramDetailResponse
{
    /// <summary>
    /// Gets or sets the status published by 3Cat («OK» on success).
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the program.
    /// </summary>
    [JsonPropertyName("item")]
    public ThreeCatProgramDetail? Item { get; set; }
}

/// <summary>
/// A program of the 3Cat portal. It publishes what the search endpoint does not: the collections
/// (<c>agrupacions</c>) the program is filed under and the free text sections (<c>info</c>) of its page,
/// which is where the original title and the country of origin are written.
/// </summary>
public sealed class ThreeCatProgramDetail
{
    /// <summary>
    /// Gets or sets the program id (<c>programatv_id</c>).
    /// </summary>
    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the title of the program, in Catalan.
    /// </summary>
    [JsonPropertyName("titol")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the formats of <c>format_contingut</c> the program is filed under («Sèrie animació»...).
    /// </summary>
    [JsonPropertyName("formats")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatLabel>))]
    public List<ThreeCatLabel>? Formats { get; set; }

    /// <summary>
    /// Gets or sets the collections (<c>agrupacions</c>) of the portal the program is filed under
    /// («Anime 3XL», «Estrenes S3»...), which are the rails the 3Cat web is built with.
    /// </summary>
    [JsonPropertyName("agrupacions")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatLabel>))]
    public List<ThreeCatLabel>? Groups { get; set; }

    /// <summary>
    /// Gets or sets the free text sections of the program page («De què va?», «Fitxa tècnica»...).
    /// </summary>
    [JsonPropertyName("info")]
    [JsonConverter(typeof(SingleOrArrayConverter<ThreeCatProgramInfoSection>))]
    public List<ThreeCatProgramInfoSection>? Info { get; set; }
}

/// <summary>
/// A free text section of the program page. The text is an HTML fragment.
/// </summary>
public sealed class ThreeCatProgramInfoSection
{
    /// <summary>
    /// Gets or sets the label of the section («De què va?»).
    /// </summary>
    [JsonPropertyName("desc")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the HTML of the section. Beyond the synopsis it holds the card of the program
    /// («Títol original», «País d'origen», «Any de producció»).
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

/// <summary>
/// What the page of a program publishes about it: the formats and collections it is filed under, the
/// original title and the country of origin.
/// </summary>
/// <param name="Title">Title of the program, in Catalan.</param>
/// <param name="OriginalTitle">Original title published in the card of the program, or <c>null</c>.</param>
/// <param name="Formats">Codes of <c>format_contingut</c> the program is filed under («PUFF_SDIBU»...).</param>
/// <param name="Groups">
/// Codes of <c>agrupacions</c> the program is filed under («PUA_ANIMEX3», «PUSX3_S3»...). They are the
/// rails of the 3Cat web rather than a taxonomy, so most of them say nothing about the kind of program.
/// </param>
/// <param name="Countries">
/// Countries of origin published in the card of the program («Japó», «Catalunya»...), or an empty list
/// when 3Cat publishes none.
/// </param>
/// <remarks>
/// This is the only place where 3Cat says where a program comes from, which is what tells an anime from
/// the rest of the animation: 3Cat files both under <c>PUFF_SDIBU</c> and publishes no anime facet.
/// </remarks>
public sealed record ThreeCatProgramDetails(
    string? Title,
    string? OriginalTitle,
    IReadOnlyList<string> Formats,
    IReadOnlyList<string> Groups,
    IReadOnlyList<string> Countries)
{
    /// <summary>
    /// Projects the program read from 3Cat.
    /// </summary>
    /// <param name="item">Program published by the endpoint.</param>
    /// <returns>The program, or <c>null</c> when 3Cat published no body.</returns>
    internal static ThreeCatProgramDetails? From(ThreeCatProgramDetail? item)
    {
        if (item is null)
        {
            return null;
        }

        var sections = item.Info?
            .Select(section => section.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        return new ThreeCatProgramDetails(
            item.Title?.Trim(),
            ProgramDetailText.OriginalTitle(sections),
            Codes(item.Formats),
            Codes(item.Groups),
            ProgramDetailText.Countries(sections));
    }

    private static List<string> Codes(List<ThreeCatLabel>? labels)
        => labels?
            .Select(label => label.Id?.Trim())
            .Where(code => !string.IsNullOrEmpty(code))
            .Select(code => code!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
}

/// <summary>
/// Reads the card of a program out of the HTML sections of its page: the original title and the country
/// of origin. 3Cat writes it as a paragraph of the synopsis
/// («<c>&lt;strong&gt;País d'origen:&lt;/strong&gt; Japó</c>»), without a field of its own.
/// </summary>
internal static partial class ProgramDetailText
{
    /// <summary>
    /// Label of the country, with the variants the catalog publishes: «País d'origen», «País de origen»,
    /// the short «Origen» of the older pages and a plain «País». The value is the text up to the next tag.
    /// </summary>
    /// <remarks>
    /// The catalog writes the label in three ways: with the colon inside the tag
    /// («<c>&lt;strong&gt;País d'origen:&lt;/strong&gt; Japó</c>»), with the colon after it
    /// («<c>&lt;strong&gt;País d'origen&lt;/strong&gt;: Japó</c>») and on a plain line
    /// («<c>País d'origen: Japó</c>»). It also writes the spaces of a label as <c>&amp;nbsp;</c>, which is
    /// why the pattern reads a space as either kind. The value is decoded before it is split into
    /// countries, so the semicolon of an entity is not taken as a separator.
    /// </remarks>
    [GeneratedRegex(
        "(?<![A-Za-zÀ-ÿ])(?<!T[ií]tol\\s)(?<!T[ií]tol&nbsp;)"
        + "(?:Pa[ií]s(?:\\s|&nbsp;)+(?:d['’]|de(?:\\s|&nbsp;)+)?origen|Origen|Pa[ií]s)"
        + "(?:\\s|&nbsp;)*:?(?:\\s|&nbsp;)*(?:</?[A-Za-z][^>]*>(?:\\s|&nbsp;)*:?(?:\\s|&nbsp;)*)*"
        + "(?<value>[^<]{2,80})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CountryRegex();

    /// <summary>
    /// Label of the original title, which the catalog writes as «Títol original» and, in the older
    /// pages, as «Títol origen».
    /// </summary>
    [GeneratedRegex(
        "T[ií]tol(?:\\s|&nbsp;)+(?:original|origen)"
        + "(?:\\s|&nbsp;)*:?(?:\\s|&nbsp;)*(?:</?[A-Za-z][^>]*>(?:\\s|&nbsp;)*:?(?:\\s|&nbsp;)*)*"
        + "(?<value>[^<]{1,120})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OriginalTitleRegex();

    /// <summary>
    /// Separators of a co-production: «Japó, EUA», «França/Japó», «Japó i Corea».
    /// </summary>
    [GeneratedRegex(@"[,;/()]|\s+i\s+|\s+y\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CountrySeparatorRegex();

    /// <summary>
    /// Brackets of the card of the program, which hold the year of production and follow the country
    /// («Japó (2007-2013)»).
    /// </summary>
    [GeneratedRegex(@"\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex ParenthesisRegex();

    /// <summary>
    /// Reads the country of origin of a program.
    /// </summary>
    /// <param name="sections">HTML of the sections of the program page.</param>
    /// <returns>The countries, in the order 3Cat publishes them; empty when it publishes none.</returns>
    internal static List<string> Countries(IEnumerable<string?>? sections)
    {
        var countries = new List<string>();

        foreach (var value in Values(CountryRegex(), sections))
        {
            foreach (var part in CountrySeparatorRegex().Split(ParenthesisRegex().Replace(value, " ")))
            {
                var country = HtmlText.FromHtml(part);

                // A card that reads «País: vegeu la fitxa tècnica del programa» would otherwise pass as
                // a country, and so would the year of production that follows the country.
                if (country is null
                    || country.Length > 30
                    || country.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 5
                    || !country.Any(char.IsLetter)
                    || country.Contains(':', StringComparison.Ordinal)
                    || country.Contains('&', StringComparison.Ordinal)
                    || countries.Contains(country, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                countries.Add(country);
            }
        }

        return countries;
    }

    /// <summary>
    /// Quotes of the card of the program, which wrap the original title and separate the two a card
    /// publishes when it lists both («"Ranma 1/2" o "Ranma nibun no ichi"»).
    /// </summary>
    private static readonly char[] TitleQuotes = ['"', '\u201C', '\u201D', '\u00AB', '\u00BB'];

    /// <summary>
    /// Reads the original title of a program.
    /// </summary>
    /// <param name="sections">HTML of the sections of the program page.</param>
    /// <returns>The title, or <c>null</c> when 3Cat publishes none.</returns>
    /// <remarks>
    /// A card that publishes two original titles keeps the first one, which is the title the series is
    /// known by («Ranma 1/2»), instead of joining both.
    /// </remarks>
    internal static string? OriginalTitle(IEnumerable<string?>? sections)
        => Values(OriginalTitleRegex(), sections)
            .Select(title => title
                .Split(TitleQuotes, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault())
            .FirstOrDefault(title => title is not null && title.Count(char.IsLetter) >= 2);

    private static IEnumerable<string> Values(Regex regex, IEnumerable<string?>? sections)
    {
        foreach (var section in sections ?? [])
        {
            if (string.IsNullOrWhiteSpace(section))
            {
                continue;
            }

            foreach (Match match in regex.Matches(section))
            {
                var value = HtmlText.FromHtml(match.Groups["value"].Value);
                if (!string.IsNullOrEmpty(value))
                {
                    yield return value;
                }
            }
        }
    }
}
