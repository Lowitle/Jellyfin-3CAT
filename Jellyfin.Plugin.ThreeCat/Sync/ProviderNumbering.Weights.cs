namespace Jellyfin.Plugin.ThreeCat.Sync;

/// <summary>
/// Comparison of the titles of the two catalogs across languages, with the weight every word takes from the
/// program it belongs to.
/// </summary>
/// <remarks>
/// It is what tells a coincidence from a match: two titles that only share «cas» and «assassinat» —in every other
/// title of the program— look nothing alike, while a single rare word —«Ayumi», «Gomera»— decides the episode.
/// </remarks>
internal sealed class TitleWeights
{
    /// <summary>
    /// What a title gains when the two announce the same part, and what it loses when they announce different ones.
    /// </summary>
    private const double PartBonus = 0.15;

    private const double PartPenalty = 0.35;

    private readonly Dictionary<string, int> _frequency;
    private readonly Dictionary<string, List<string>> _tokens;
    private readonly Dictionary<string, double> _weights;
    private readonly Dictionary<(string Left, string Right), double> _pairs;
    private readonly int _documents;

    private TitleWeights(Dictionary<string, int> frequency, int documents)
    {
        _frequency = frequency;
        _documents = documents;
        _tokens = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        _weights = new Dictionary<string, double>(StringComparer.Ordinal);
        _pairs = new Dictionary<(string Left, string Right), double>();
    }

    /// <summary>
    /// Counts how many titles of the program carry every word, which is what the weight is made of.
    /// </summary>
    /// <param name="titles">Normalized titles of the whole program, both catalogs.</param>
    /// <returns>The weights.</returns>
    public static TitleWeights Learn(IEnumerable<string?> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);

        var frequency = new Dictionary<string, int>(StringComparer.Ordinal);
        var documents = 1;

        foreach (var title in titles)
        {
            foreach (var word in ProviderNumbering.ReadWords(title).Distinct(StringComparer.Ordinal))
            {
                frequency[word] = frequency.TryGetValue(word, out var count) ? count + 1 : 1;
            }

            documents++;
        }

        return new TitleWeights(frequency, documents);
    }

    /// <summary>
    /// Weight of a word: the logarithm of how rare it is in the program, halved when the word is filler.
    /// </summary>
    /// <param name="word">Word to weigh.</param>
    /// <returns>The weight.</returns>
    public double Weight(string word)
    {
        if (_weights.TryGetValue(word, out var weight))
        {
            return weight;
        }

        var frequency = _frequency.TryGetValue(word, out var count) ? count : 1;
        var inverse = Math.Log((double)_documents / frequency);
        weight = ProviderNumbering.IsFillerWord(word) ? inverse * 0.5 : inverse;
        _weights[word] = weight;
        return weight;
    }

    /// <summary>
    /// Read of two titles: how much of the weight of the one finds a word that looks like it in the other.
    /// </summary>
    /// <param name="left">Normalized title of the video of 3Cat.</param>
    /// <param name="leftPart">Part the video announces, or <c>null</c>.</param>
    /// <param name="right">Normalized title of the episode of the provider.</param>
    /// <param name="rightPart">Part the episode announces, or <c>null</c>.</param>
    /// <returns>
    /// From 0 (nothing in common) to 1 (the same title), plus the bonus of the part; 0 when there is nothing to
    /// compare.
    /// </returns>
    public double Similarity(string? left, int? leftPart, string? right, int? rightPart)
    {
        var leftWords = Tokens(left);
        var rightWords = Tokens(right);
        if (leftWords.Count == 0 || rightWords.Count == 0)
        {
            return 0;
        }

        var leftTotal = leftWords.Sum(Weight);
        var rightTotal = rightWords.Sum(Weight);
        if (leftTotal <= 0 || rightTotal <= 0)
        {
            return 0;
        }

        var matched = 0.0;
        var spent = new bool[rightWords.Count];

        foreach (var word in leftWords)
        {
            var best = 0.0;
            var taken = -1;

            for (var other = 0; other < rightWords.Count; other++)
            {
                if (spent[other])
                {
                    continue;
                }

                var similarity = Pairs(word, rightWords[other]) * Weight(word);
                if (similarity > best)
                {
                    best = similarity;
                    taken = other;
                }
            }

            if (taken >= 0 && best > 0)
            {
                spent[taken] = true;
                matched += best;
            }
        }

        // The two catalogs pad the title differently —the Catalan writes «El cas de l'assassinat de» and the
        // Spanish does not—, so the share is taken over the title that weighs less.
        var score = matched / Math.Min(leftTotal, rightTotal);

        if (leftPart.HasValue && rightPart.HasValue)
        {
            // «(2a part)» against «(2)» is the same episode; against «(1)» it is never the same one.
            score = leftPart == rightPart ? score + PartBonus : score * PartPenalty;
        }

        return score;
    }

    private List<string> Tokens(string? normalized)
        => string.IsNullOrWhiteSpace(normalized)
            ? []
            : _tokens.TryGetValue(normalized, out var tokens)
                ? tokens
                : _tokens[normalized] = ProviderNumbering.ReadWords(normalized);

    /// <summary>
    /// Read of two words: they are the same when they share the root, when one is one character away from the
    /// other, or when three quarters of their pairs of letters are the same.
    /// </summary>
    private double Pairs(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return 1;
        }

        var key = string.CompareOrdinal(left, right) <= 0 ? (left, right) : (right, left);
        if (_pairs.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var value = Read(left, right);
        _pairs[key] = value;
        return value;
    }

    private static double Read(string left, string right)
    {
        var shortest = Math.Min(left.Length, right.Length);
        var prefix = 0;

        while (prefix < shortest && left[prefix] == right[prefix])
        {
            prefix++;
        }

        // «brillant»/«brillante» is one character apart, «assassinat»/«asesinato» shares the root, «peix»/«pez»
        // shares nothing and only the rest of the title can save the pair.
        if (prefix >= 5 && prefix >= shortest - 1)
        {
            return 0.9;
        }

        if (shortest >= 5 && prefix >= shortest - 2 && prefix >= 0.7 * shortest)
        {
            return 0.85;
        }

        return Dice(left, right) >= 0.75
            ? 0.8
            : Distance(left, right) <= Math.Max(1, (int)(0.3 * Math.Max(left.Length, right.Length))) ? 0.7 : 0;
    }

    /// <summary>
    /// Share of the pairs of letters of two words that are the same, in both directions.
    /// </summary>
    private static double Dice(string left, string right)
    {
        if (left.Length < 2 || right.Length < 2)
        {
            return 0;
        }

        var pairs = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index + 1 < left.Length; index++)
        {
            var pair = left.Substring(index, 2);
            pairs[pair] = pairs.TryGetValue(pair, out var count) ? count + 1 : 1;
        }

        var hits = 0;
        for (var index = 0; index + 1 < right.Length; index++)
        {
            var pair = right.Substring(index, 2);
            if (pairs.TryGetValue(pair, out var count) && count > 0)
            {
                pairs[pair] = count - 1;
                hits++;
            }
        }

        return 2.0 * hits / (left.Length + right.Length - 2);
    }

    /// <summary>
    /// Characters that have to be changed in one word to write the other one.
    /// </summary>
    private static int Distance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var index = 0; index <= right.Length; index++)
        {
            previous[index] = index;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;

            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
