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

    /// <summary>
    /// Japanese animation («Bola de Drac», «Naruto», «El detectiu Conan»), which 3Cat files under
    /// <see cref="Animation"/> and has no facet for.
    /// </summary>
    internal const string Anime = "Anime";

    /// <summary>
    /// Children's programmes, the audience of the SX3 channels («Doraemon», «Mironins», «Daniel Tigre»).
    /// </summary>
    internal const string Children = "Infantil";

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
    /// Collections (<c>agrupacions</c>) of the program page that hold nothing but anime. 3Cat publishes no
    /// anime facet, so these rails are the label it gives it.
    /// </summary>
    /// <remarks>
    /// <c>PUAG_3CANEVA</c> («3Cat Nova») mixes animation with Catalan live shows («Arròs covat», «Món
    /// 3XL»), so it says nothing about the kind of program and is deliberately left out: with it in, both
    /// would end up tagged as anime.
    /// </remarks>
    private static readonly string[] AnimeGroups =
    [
        "PUA_ANIMEX3",
        "PUAG_3PPANIM",
        "PUAG_ANI3CAT",
        "PUAGR_CIANIM"
    ];

    /// <summary>
    /// Prefix of the country of an anime. The card of the program writes it in Catalan («Japó»), so any
    /// value that starts with «Jap» is Japan.
    /// </summary>
    private const string AnimeCountry = "Jap";

    /// <summary>
    /// Audience target 3Cat files the children's programmes under. It is the value the portal uses to
    /// build the SX3 rails, and every video of the catalog publishes it.
    /// </summary>
    private const string ChildrenTarget = "PUT_INF";

    /// <summary>
    /// Prefix of the channels of the SX3 family (<c>PUC_S3</c> Super3, <c>PUC_S3_S3</c> S3 and
    /// <c>PUC_S3_X3</c> X3). Used when a video publishes no audience target.
    /// </summary>
    private const string ChildrenChannel = "PUC_S3";

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
    /// <param name="anime">Whether the program is anime, as <see cref="IsAnime"/> reads it.</param>
    /// <param name="children">Whether the program is for children, as <see cref="IsChildren"/> reads it.</param>
    /// <returns>The kind of program and the genres to write to the NFO.</returns>
    /// <remarks>
    /// An anime is given <see cref="Anime"/> as its kind and <see cref="Animation"/> right after it, so the
    /// library keeps both filters: «Anime» groups the 29 programs that are anime and «Animació» still
    /// finds every animated program of the catalog. A children's program follows with
    /// <see cref="Children"/>, which is the audience 3Cat publishes for the SX3 programs.
    /// </remarks>
    internal static ProgramClassification Classify(
        IEnumerable<string?>? formats,
        IEnumerable<string?>? themes,
        IEnumerable<string?>? genres,
        bool anime = false,
        bool children = false)
    {
        var kind = anime ? Anime : ResolveKind(formats, themes);
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (kind is not null && seen.Add(kind))
        {
            values.Add(kind);
        }

        if (anime && seen.Add(Animation))
        {
            values.Add(Animation);
        }

        if (children && seen.Add(Children))
        {
            values.Add(Children);
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
    /// Tells whether a program is anime, with what 3Cat publishes in the page of the program.
    /// </summary>
    /// <param name="groups">Codes of <c>agrupacions</c> the program is filed under.</param>
    /// <param name="countries">Countries of origin published in the card of the program.</param>
    /// <returns>Whether the program is Japanese animation.</returns>
    /// <remarks>
    /// 3Cat files anime and the rest of the animation under the same format and publishes no anime facet,
    /// so it has to be read from two signs it does publish: the collections of the program page, and the
    /// country of origin — a Japanese production is anime whichever rail it is filed under, and the other
    /// way round, a program in an anime rail is anime even when its card publishes no country.
    /// </remarks>
    internal static bool IsAnime(IEnumerable<string?>? groups, IEnumerable<string?>? countries)
    {
        var published = Codes(groups);
        if (AnimeGroups.Any(published.Contains))
        {
            return true;
        }

        return (countries ?? [])
            .Any(country => country?.Trim().StartsWith(AnimeCountry, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Tells whether the facets of a program describe an animated program.
    /// </summary>
    /// <param name="formats">Codes of <c>format_contingut</c> the program appears in.</param>
    /// <param name="themes">Codes of <c>tematica</c> the program appears in.</param>
    /// <returns>Whether the program is animation.</returns>
    internal static bool IsAnimation(IEnumerable<string?>? formats, IEnumerable<string?>? themes)
        => string.Equals(ResolveKind(formats, themes), Animation, StringComparison.Ordinal);

    /// <summary>
    /// Tells whether a program is for children.
    /// </summary>
    /// <param name="targets">Audience targets published by the videos («PUT_INF», «PUT_JUV»...).</param>
    /// <param name="channels">Channels the videos were broadcast on («PUC_S3», «PUC_TV3»...).</param>
    /// <returns>Whether the program is a children's program.</returns>
    /// <remarks>
    /// 3Cat publishes the audience of every video in <c>targets</c> and uses it to build the SX3 rails, so
    /// it is read first. The channel is the fallback for a video that publishes no target: the SX3 family
    /// (<c>PUC_S3</c>, <c>PUC_S3_S3</c> and <c>PUC_S3_X3</c>) is the children's channel of 3Cat.
    /// </remarks>
    internal static bool IsChildren(IEnumerable<string?>? targets, IEnumerable<string?>? channels)
    {
        if (Codes(targets).Contains(ChildrenTarget))
        {
            return true;
        }

        return Codes(channels)
            .Any(channel => channel.StartsWith(ChildrenChannel, StringComparison.OrdinalIgnoreCase));
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
