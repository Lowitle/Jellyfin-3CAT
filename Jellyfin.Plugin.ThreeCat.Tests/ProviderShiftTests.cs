using System.Globalization;
using Jellyfin.Plugin.ThreeCat.Sync;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the pass that reads the shift of the counter from the titles of the two catalogs.
/// </summary>
/// <remarks>
/// <para>
/// It is the other half of the answer to «Detectiu Conan»: the counter counts emissions of 3Cat, and 3Cat emits as
/// two videos the episodes it splits, so from the first split on every video is numbered one episode too far. The
/// catalog of the program is 48 % parts, and its tail ends up 57 chapters away.
/// </para>
/// <para>
/// The titles are the only thing that says where the truth is: the dub writes them in Catalan, the provider in
/// Spanish, and «La amenaza del regalo mensual» is still «L'amenaça del regal mensual» when the words are read by
/// their root instead of by their letters.
/// </para>
/// <para>
/// The catalog below is the first third of the one 3Cat really publishes, with the titles TMDB really lists: the
/// chapter 11 is the first part of «Clar de lluna», the 12 the second one, and the «Ayumi» arrives at the 13.
/// </para>
/// </remarks>
public sealed class ProviderShiftTests
{
    [Fact]
    public void Assign_FollowsTheTitlesToTheEpisodeTheCounterMoved()
    {
        var result = ProviderNumbering.Assign(CatalogVideos(), CatalogEpisodes());

        // The two parts of the sonata are one episode of TMDB —«Sonata a la Luz de la Luna (1/2)»—, so the second
        // part has no episode of its own and the counter of the program runs one place ahead from there on. The
        // «Ayumi» of the chapter 13 is the episode 12, and the 12 is the one the counter would have given it.
        Assert.Equal("El secuestro de Ayumi", result.Episodes[12]?.Title);

        // Everything before the split keeps its place: the title of the episode the counter announces is the one
        // the video says.
        Assert.Equal("El pez brillante", result.Episodes[3]?.Title);
        Assert.Equal("Sonata a la Luz de la Luna (1/2)", result.Episodes[10]?.Title);

        Assert.True(result.Votes >= 3);
        Assert.True(result.ByShift > 0);
        Assert.Equal(0, result.CounterOffset);
        Assert.False(result.UseThreeCat);

        // The second part of the sonata is written as the second part of the episode its first part took, not as
        // the episode the counter announces: Jellyfin plays the two files of an episode in order when their names
        // only differ in «- partN», so the first part carries the 1.
        Assert.Equal(new EpisodeSlot(1, 11, false, Part: 1), result.Slots[10]);
        Assert.Equal(new EpisodeSlot(1, 11, false, Part: 2), result.Slots[11]);
        Assert.Equal(ProviderMatch.Part, result.Methods[11]);
        Assert.Equal(1, result.ByPart);
        Assert.Equal(0, result.Specials);
    }

    [Fact]
    public void Assign_UsesTheCounterAsItComesWhenTheTitlesSayNothing()
    {
        // A dub that rewrote every title: no video finds an episode that looks like it, so the counter is read as
        // it comes —every video is the episode its «capitol» announces— and the shift stays at zero for all of them.
        var result = ProviderNumbering.Assign(
            [Video("Zzz", 1), Video("Qqq", 2), Video("Www", 3)],
            [Episode(1, 1, "Uno"), Episode(1, 2, "Dos"), Episode(1, 3, "Tres")]);

        Assert.Equal(1, result.Slots[0].Episode);
        Assert.Equal(2, result.Slots[1].Episode);
        Assert.Equal(3, result.Slots[2].Episode);
        Assert.Equal(3, result.ByCounter);
        Assert.Equal(0, result.ByShift);
        Assert.Equal(0, result.Votes);
    }

    [Fact]
    public void PartNumber_ReadsThePartOfBothLanguages()
    {
        Assert.Equal(1, ProviderNumbering.PartNumber("El cas de la sonata (1a part)"));
        Assert.Equal(2, ProviderNumbering.PartNumber("Els assassinats del creuer (2a part)"));
        Assert.Equal(3, ProviderNumbering.PartNumber("La foscor de la policia (3a part)"));
        Assert.Equal(4, ProviderNumbering.PartNumber("El misteri teatral d'en Conan i l'Ebizo (part 4)"));
        Assert.Equal(2, ProviderNumbering.PartNumber("El mismo escenario del crimen (2/2)"));
        Assert.Equal(2, ProviderNumbering.PartNumber("Los asesinatos del barco fantasma (2)"));
        Assert.Null(ProviderNumbering.PartNumber("El detectiu més famós d'aquest segle"));
        Assert.Null(ProviderNumbering.PartNumber("El caso de Yaiba el enmascarado"));
    }

    /// <summary>
    /// The videos of the program as 3Cat publishes them, with the <c>capitol</c> the catalog carries.
    /// </summary>
    private static List<ProviderVideo> CatalogVideos()
    {
        string[] titles =
        [
            "El detectiu més famós d'aquest segle",
            "El famós detectiu que es va encongir",
            "Compte amb els ídols",
            "El peix brillant",
            "L'explosió del tren d'alta velocitat",
            "El cas de l'assassinat del dia de Sant Valentí",
            "L'amenaça del regal mensual",
            "El cas de l'assassinat del museu d'art",
            "El cas de l'assassinat de la Festa de Primavera",
            "El cas del xantatge a un futbolista",
            "El cas de l'assassinat de la sonata Clar de lluna (1a part)",
            "El cas de l'assassinat de la sonata Clar de lluna (2a part)",
            "El segrest de l'Ayumi",
            "El cas de la desaparició misteriosa",
            "Un missatge molt misteriós",
            "El cadàver desaparegut",
            "El cas de l'assassinat del col·leccionista",
            "El cas del robatori dels grans magatzems",
            "L'assassinat de la núvia",
            "L'assassinat de l'ascensor",
            "L'assassinat de la casa encantada",
            "Un assassinat de pel·lícula",
            "Els assassinats en sèrie del creuer de luxe (1a part)",
            "Els assassinats en sèrie del creuer de luxe (2a part)",
            "El misteri de la bonica amnèsica",
            "Diners falsos en un cas de segrest",
            "En John i el cas de l'assassinat"
        ];

        return titles
            .Select((title, index) => Video(title, index + 1))
            .ToList();
    }

    /// <summary>
    /// The episodes the provider lists, with the titles TMDB writes in Spanish.
    /// </summary>
    private static List<ProviderEpisode> CatalogEpisodes()
    {
        string[] titles =
        [
            "Asesinato en la montaña rusa",
            "El secuestro de la hija del presidente",
            "El caso de la habitación cerrada",
            "El pez brillante",
            "La gran explosión del tren bala",
            "El asesinato del día de San Valentín",
            "La amenaza del regalo mensual",
            "Asesinato en el museo de arte",
            "Asesinato en el festival",
            "El caso del chantaje al jugador de fútbol",
            "Sonata a la Luz de la Luna (1/2)",
            "El secuestro de Ayumi",
            "El caso de la investigación desconcertante",
            "Mensaje en clave para un asesinato",
            "El cadáver desaparecido",
            "El caso del asesinato del anticuario",
            "Secuestro en los grandes almacenes",
            "El asesinato de la novia de junio",
            "Asesinato en el ascensor",
            "Asesinato en la casa encantada",
            "Asesinato en el plató",
            "Asesinatos en el crucero de lujo (1)",
            "Asesinatos en el crucero de lujo (2)",
            "El caso de la mujer con amnesia",
            "El dinero falso del rescate",
            "John, el pastor alemán",
            "Reunión de compañeros de Kogoro (1)"
        ];

        return titles
            .Select((title, index) => Episode(1, index + 1, title))
            .ToList();
    }

    private static ProviderVideo Video(string title, int chapter)
        => new(title, null, chapter.ToString(CultureInfo.InvariantCulture), chapter);

    private static ProviderEpisode Episode(int season, int episode, string title)
        => new(season, episode, title, null);
}

