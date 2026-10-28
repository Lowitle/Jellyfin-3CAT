namespace Jellyfin.Plugin.ThreeCat.Library;

/// <summary>
/// Turns the formats and themes 3Cat files a program under into a Jellyfin genre, so the library can
/// be filtered by kind («Sèrie», «Documental», «Animació»...) with the native filters, without extra
/// plugins.
/// </summary>
/// <remarks>
/// 3Cat publishes no endpoint that lists the classification of a program: the search endpoint accepts
/// the facets <c>format_contingut</c> and <c>tematica</c> and returns the programs of one format, so
/// the classification is the sweep of those facets. A program appears in several of them, and
/// <c>PUFACTUALITA</c> holds almost everything that is not fiction, so the tables below are read in
/// order and the first facet the program appears in is the one that names its kind.
/// </remarks>
internal static class ProgramClassifier
{
    /// <summary>Fiction series, the kind of every «sèrie» of 3Cat.</summary>
    internal const string Series = "Sèrie";

    /// <summary>Animated series and films («Doraemon», «Com ensinistrar un drac»).</summary>
    internal const string Animation = "Animació";

    /// <summary>Films and TV movies.</summary>
    internal const string Cinema = "Cinema";

    /// <summary>Documentaries and docu-series.</summary>
    internal const string Documentary = "Documental";

    /// <summary>News and current affairs programmes.</summary>
    internal const string News = "Actualitat";

    /// <summary>Magazines («La solució», «Nervi»).</summary>
    internal const string Magazine = "Magazín";

    /// <summary>One-off broadcasts: elections, sports specials, year reviews.</summary>
    internal const string Special = "Especial";

    /// <summary>Contests and talent shows.</summary>
    internal const string Contest = "Concurs";

    /// <summary>Humour programmes.</summary>
    internal const string Humour = "Humor";

    /// <summary>Concerts and music programmes.</summary>
    internal const string Music = "Música";

    /// <summary>Theatre plays.</summary>
    internal const string Theatre = "Teatre";

    /// <summary>Sports, the kind of the theme <c>PUE3</c>.</summary>
    internal const string Sports = "Esports";

    /// <summary>Entertainment, the kind of the theme <c>PUENTRETENIM</c>.</summary>
    internal const string Entertainment = "Entreteniment";

    /// <summary>
    /// Facets of <c>format_contingut</c> in matching order, with the kind of program each one names.
    /// </summary>
    /// <remarks>
    /// The specific formats come first, because a program is filed under several and the coarse ones
    /// («PUFACTUALITA», «PUFA_DOC») would otherwise name everything: «Ermessenda» is <c>PUFF_FILM</c> and
    /// <c>PUFF_SERMINI</c> and it is a film; «Naruto» is <c>PUFF_SDIBU</c> and <c>PUF_SER</c> and it is
    /// animation; «La Puntual» is <c>PUFA_CONCURS</c> and <c>PUFF_TEATRE</c> and it is theatre, so the
    /// contests come before the theatre. <c>PUF_SS</c> is the second family of series («Crims al
    /// Bàltic», «La casa d'Anubis»), and <c>PUF_PAN</c> the animated specials («Com ensinistrar un
    /// drac: El retorn»). The labels below are the ones the catalog publishes in <c>formats.desc</c>.
    /// </remarks>
    private static readonly (string Code, string Kind)[] KindByFormat =
    [
        ("PUFF_SDIBU", Animation),
        ("PUF_PAN", Animation),
        ("PUFF_FILM", Cinema),
        ("PUF_PDO", Documentary),
        ("PUF_SER", Series),
        ("PUF_SS", Series),
        ("PUFF_SDRAM", Series),
        ("PUFF_SERMINI", Series),
        ("PUFF_SITCOM", Series),
        ("PUFA_CONCURS", Contest),
        ("PUFF_TEATRE", Theatre),
        ("PUFA_DOC", Documentary),
        ("PUFA_DOCUENT", Documentary),
        ("PUFA_GAGHUM", Humour),
        ("PUFM_CONC", Music),
        ("PUFA_MAGZ", Magazine),
        ("PUFA_ESP", Special),
        ("PUFACTUALITA", News)
    ];

    /// <summary>
    /// Facets of <c>tematica</c>, used when the program appears in none of the formats above: the web
    /// and radio programs of the catalog have no <c>format_contingut</c>.
    /// </summary>
    private static readonly (string Code, string Kind)[] KindByTheme =
    [
        ("PUFICCIO", Series),
        ("PUDIVULGACIO", Documentary),
        ("PUENTRETENIM", Entertainment),
        ("PUMUSICA", Music),
        ("PUE3", Sports),
        ("PUACTUALITAT", News)
    ];

    /// <summary>
    /// Gets the facets of <c>format_contingut</c> the classifier knows, which are the ones the sweep of
    /// the catalog has to read.
    /// </summary>
    internal static IReadOnlyList<string> FormatFacets { get; } =
        KindByFormat.Select(entry => entry.Code).ToList();

    /// <summary>
    /// Gets the facets of <c>tematica</c> the classifier knows, which are the ones the sweep of the
    /// catalog has to read.
    /// </summary>
    internal static IReadOnlyList<string> ThemeFacets { get; } =
        KindByTheme.Select(entry => entry.Code).ToList();

    /// <summary>
    /// Classifies a program with the facets 3Cat files it under.
    /// </summary>
    /// <param name="formats">Codes of <c>format_contingut</c> the program appears in.</param>
    /// <param name="themes">Codes of <c>tematica</c> the program appears in.</param>
    /// <param name="genres">Genres already known for the program (the themes of its videos).</param>
    /// <returns>The kind of program and the genres to write to the NFO.</returns>
    internal static ProgramClassification Classify(
        IEnumerable<string?>? formats,
        IEnumerable<string?>? themes,
        IEnumerable<string?>? genres)
    {
        var kind = ResolveKind(formats, themes);
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (kind is not null && seen.Add(kind))
        {
            values.Add(kind);
        }

        foreach (var candidate in genres ?? [])
        {
            var genre = candidate?.Trim();
            if (!string.IsNullOrEmpty(genre) && seen.Add(genre))
            {
                values.Add(genre);
            }
        }

        return new ProgramClassification(kind, values);
    }

    /// <summary>
    /// Resolves the kind of program from the facets it appears in.
    /// </summary>
    /// <param name="formats">Codes of <c>format_contingut</c> the program appears in.</param>
    /// <param name="themes">Codes of <c>tematica</c> the program appears in.</param>
    /// <returns>The normalized kind, or <c>null</c> when 3Cat files the program under no known facet.</returns>
    internal static string? ResolveKind(IEnumerable<string?>? formats, IEnumerable<string?>? themes)
        => Match(KindByFormat, formats) ?? Match(KindByTheme, themes);

    private static string? Match((string Code, string Kind)[] table, IEnumerable<string?>? codes)
    {
        var published = Codes(codes);
        if (published.Count == 0)
        {
            return null;
        }

        foreach (var (code, kind) in table)
        {
            if (published.Contains(code))
            {
                return kind;
            }
        }

        return null;
    }

    private static HashSet<string> Codes(IEnumerable<string?>? codes)
    {
        var published = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var code in codes ?? [])
        {
            var value = code?.Trim();
            if (!string.IsNullOrEmpty(value))
            {
                published.Add(value);
            }
        }

        return published;
    }
}

/// <summary>
/// Classification of a 3Cat program derived from the facets it appears in.
/// </summary>
/// <param name="Kind">
/// Normalized kind of program («Sèrie», «Documental», «Animació»...), or <c>null</c> when 3Cat files it
/// under no known facet.
/// </param>
/// <param name="Genres">Genres to write to the NFO: the kind first, then the themes of the program.</param>
internal sealed record ProgramClassification(string? Kind, IReadOnlyList<string> Genres);
