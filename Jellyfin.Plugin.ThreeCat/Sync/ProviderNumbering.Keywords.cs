using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Reads what the titles of the two catalogs say when they are written in different languages, and turns it into
/// the shift between the <c>capitol</c> of 3Cat and the absolute numbering of the provider.
/// </summary>
/// <remarks>
/// <para>
/// The counter is the best evidence a dubbed series has, but it counts emissions of 3Cat, not episodes of the
/// original: when 3Cat splits a long episode in two —«(1a part)», «(2a part)»— and the provider lists it once,
/// every video after it is one position ahead of where it belongs, and the error piles up. «Detectiu Conan» is
/// the case: 385 videos, 183 of them a part of something, and a counter that ends up 57 chapters away.
/// </para>
/// <para>
/// The titles still say something across the two languages: «L'assassinat de l'ascensor» and «Asesinato en el
/// ascensor» are the same episode even if no word is written the same. The comparison is done word by word,
/// weighting each word by how rare it is in the program —«assassinat» is in a hundred titles and tells nothing,
/// «Ayumi» is in one—, and two words are the same word when they share the root, when they are one character
/// apart, or when three quarters of their pairs of letters are the same.
/// </para>
/// <para>
/// A video that finds a title it looks like is a vote: «this video is <c>delta</c> positions away from the one the
/// counter announces». The votes of a neighbourhood are worth more than a lone one, and the shift of a run of
/// videos is the shift most of its votes agree on; a vote none of its neighbours share is a coincidence and is
/// thrown away.
/// </para>
/// </remarks>
public static partial class ProviderNumbering
{
    /// <summary>
    /// Episodes at each side of the counter that are looked at when a video votes.
    /// </summary>
    /// <remarks>
    /// Enough to hold the shift a long program reaches —«Detectiu Conan» needs 57— without the search wandering
    /// into the rest of the catalog, where a title repeats and the vote would be a coincidence.
    /// </remarks>
    internal const int KeywordWindow = 80;

    /// <summary>
    /// Similarity from which a title counts as the same title across two languages.
    /// </summary>
    /// <remarks>
    /// Two titles of two words that share one of them —«El peix brillant» and «El pez brillante»— land on 0.45, and
    /// that is a match: the words of a title are few and one of them has to be written differently. The margin is
    /// what keeps the coincidence out, not this value.
    /// </remarks>
    internal const double MinimumKeywordScore = 0.45;

    /// <summary>
    /// Distance the second best title has to be behind the best one for the vote to name a single episode.
    /// </summary>
    internal const double MinimumKeywordMargin = 0.2;

    /// <summary>
    /// Votes a program needs for the shift to be read at all.
    /// </summary>
    /// <remarks>
    /// Below it the titles said nothing —the dub rewrote every one of them— and the counter is used as it comes,
    /// exactly as before this evidence existed.
    /// </remarks>
    internal const int MinimumKeywordVotes = 3;

    /// <summary>
    /// Episodes at each side of the corrected position that the alignment may visit.
    /// </summary>
    /// <remarks>
    /// The shift is an estimate; the alignment decides the exact episode, and a dozen episodes are enough to
    /// cover the border between two runs and the episodes of a title that the two catalogs split differently.
    /// </remarks>
    internal const int ShiftWindow = 12;

    /// <summary>
    /// What it costs a video to sit further from the position the corrected counter announces.
    /// </summary>
    private const double ShiftDrift = 0.05;

    /// <summary>
    /// What it costs to leave an episode without a video and a video without an episode of its own.
    /// </summary>
    /// <remarks>
    /// Small on purpose: both catalogs have legitimate holes (3Cat does not publish 721 of the chapters and splits
    /// episodes the provider lists once), so the alignment must not punish them, only prefer the pair of titles
    /// that look the same.
    /// </remarks>
    private const double ShiftGap = 0.02;

    /// <summary>
    /// Words that say nothing about which episode a title is: articles, prepositions and conjunctions of both
    /// languages, which the catalogs write in every other title.
    /// </summary>
    private static readonly HashSet<string> EmptyWords = new(StringComparer.Ordinal)
    {
        "el", "la", "els", "les", "lo", "los", "las", "un", "una", "uns", "unes", "uno", "unos", "unas",
        "de", "del", "dels", "des", "al", "als", "a", "e", "i", "y", "o", "u", "amb", "con", "per", "pel",
        "pels", "por", "para", "que", "se", "es", "sa", "son", "sons", "su", "sus", "no", "en", "na", "the",
        "of", "and", "com", "como", "mes", "mas", "muy", "tot", "todo", "tots", "todos"
    };

    /// <summary>
    /// Words every other title of a program carries, which distinguish none of them.
    /// </summary>
    private static readonly HashSet<string> FillerWords = new(StringComparer.Ordinal)
    {
        "cas", "caso", "capitol", "capitulo", "episodi", "episodio", "assassinat", "assassinats", "asesinato",
        "asesinatos", "misteri", "misterio", "misterios", "secret", "secreto", "home", "hombre", "dona", "nit",
        "noche", "dia", "part", "parte"
    };

    /// <summary>
    /// Reads the number of part a title announces: «(1a part)», «(2a parte)», «(part 3)», «(2/2)», «(1)».
    /// </summary>
    /// <param name="title">Title of the video or of the episode.</param>
    /// <returns>The number of the part, or <c>null</c> when the title announces none.</returns>
    /// <remarks>
    /// It is the word that separates two arcs named the same: «El objetivo es el departamento de tráfico (3)» can
    /// only be the third part, and without reading it the first part falls anywhere in the arc.
    /// </remarks>
    internal static int? PartNumber(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var match = PartRegex().Match(title);
        if (!match.Success)
        {
            return null;
        }

        for (var group = 1; group < match.Groups.Count; group++)
        {
            if (match.Groups[group].Success
                && int.TryParse(match.Groups[group].Value, out var part)
                && part > 0)
            {
                return part;
            }
        }

        return null;
    }

    /// <summary>
    /// Splits a normalized title into the words that say something about it.
    /// </summary>
    /// <param name="title">Title, as <see cref="NormalizeTitle"/> returns it.</param>
    /// <returns>The words worth comparing, in order.</returns>
    internal static List<string> ReadWords(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        var words = new List<string>();
        foreach (var word in title.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length < 3 || word.All(char.IsDigit) || EmptyWords.Contains(word))
            {
                continue;
            }

            words.Add(word);
        }

        return words;
    }

    // The part always closes the title and it is written with the two languages of the catalogs; the number is
    // the only thing read, and «(1-4)» is left out because it names a range and not a part.
    [GeneratedRegex(
        @"\(\s*(?:part|parte)\s*(?<number>\d+)\s*\)|\(\s*(?<number2>\d+)\s*a?\s*parts?\s*\)|\(\s*(?<number3>\d+)\s*/\s*\d+\s*\)|\(\s*(?<number4>\d+)\s*\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartRegex();

    /// <summary>
    /// Tells whether a word is filler, which is written in every other title of the program and distinguishes none.
    /// </summary>
    /// <param name="word">Normalized word.</param>
    /// <returns><c>true</c> when the word is filler.</returns>
    internal static bool IsFillerWord(string word) => FillerWords.Contains(word);
}
