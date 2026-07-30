namespace PictTag.TaxonomyBuilder.WordNet;

/// <summary>
/// Parses Princeton WordNet 3.0's <c>data.noun</c>/<c>index.noun</c> database files, per the
/// format documented in <c>wndb.5</c> and verified empirically against the real committed files
/// under <c>data/wordnet/raw/</c> (see that folder's SOURCES.md) - not guessed from the spec
/// alone, since a couple of the count fields turned out to need real examples to pin down:
/// <c>w_cnt</c> is a two-digit HEX count (confirmed via a real 11-lemma synset encoded as "0b"),
/// while <c>p_cnt</c> is decimal (confirmed via a real synset with p_cnt "400" that has exactly
/// 400 decimal pointer records - 1024, hex 0x400, would not match the line's actual field count).
/// </summary>
public static class WordNetParser
{
    private const string HypernymPointer = "@";
    private const string InstanceHypernymPointer = "@i";
    private const string HyponymPointer = "~";
    private const string InstanceHyponymPointer = "~i";

    /// <summary>
    /// Parses one line of <c>data.noun</c>. Returns null for the 29-line copyright header at the
    /// top of the file (detected by shape, not a hardcoded line count, in case a future WordNet
    /// version's header length differs) or a blank line.
    /// </summary>
    public static WordNetSynset? ParseDataNounLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        int barIndex = line.IndexOf('|');
        string fieldsPart = barIndex >= 0 ? line[..barIndex] : line;
        string gloss = barIndex >= 0 ? line[(barIndex + 1)..].Trim() : string.Empty;

        string[] tokens = fieldsPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // A real data line's first token is always an 8-digit zero-padded synset offset; header
        // lines start with a bare (unpadded) line number instead.
        if (tokens.Length < 4 || tokens[0].Length != 8 || !IsAllDigits(tokens[0]))
        {
            return null;
        }

        string synsetId = "n" + tokens[0];

        // tokens[1] = lex_filenum, tokens[2] = ss_type ("n"), tokens[3] = w_cnt (hex).
        int wordCount = Convert.ToInt32(tokens[3], 16);
        int pos = 4;

        List<string> lemmas = new(wordCount);
        for (int i = 0; i < wordCount; i++)
        {
            // Each entry is "word lex_id" - lex_id (hex) isn't needed for taxonomy purposes.
            lemmas.Add(tokens[pos].Replace('_', ' '));
            pos += 2;
        }

        int pointerCount = int.Parse(tokens[pos]);
        pos += 1;

        List<string> hypernymIds = new();
        List<string> hyponymIds = new();
        for (int i = 0; i < pointerCount; i++)
        {
            // Each pointer record is "symbol target_offset target_pos source/target".
            string symbol = tokens[pos];
            string targetId = "n" + tokens[pos + 1];

            if (symbol is HypernymPointer or InstanceHypernymPointer)
            {
                hypernymIds.Add(targetId);
            }
            else if (symbol is HyponymPointer or InstanceHyponymPointer)
            {
                hyponymIds.Add(targetId);
            }

            pos += 4;
        }

        return new WordNetSynset(synsetId, lemmas, gloss, hypernymIds, hyponymIds);
    }

    /// <summary>
    /// Parses one line of <c>index.noun</c> into its lemma and the synset ids it belongs to,
    /// ordered sense-1-first (WordNet's own file order - the natural "most common sense wins"
    /// tiebreaker for an ambiguous lemma). Returns null for the copyright header or a blank line.
    /// </summary>
    public static (string Lemma, IReadOnlyList<string> SynsetIds)? ParseIndexNounLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Every real index.noun line's second token is the part-of-speech "n"; header lines'
        // second token is arbitrary header prose instead.
        if (tokens.Length < 4 || tokens[1] != "n")
        {
            return null;
        }

        string lemma = tokens[0].Replace('_', ' ');
        int synsetCount = int.Parse(tokens[2]);
        int pointerSymbolCount = int.Parse(tokens[3]);

        // Layout: lemma pos synset_cnt p_cnt {ptr_symbol}*p_cnt sense_cnt tagsense_cnt {offset}*synset_cnt
        int offsetsStart = 4 + pointerSymbolCount + 2;

        List<string> synsetIds = new(synsetCount);
        for (int i = 0; i < synsetCount; i++)
        {
            synsetIds.Add("n" + tokens[offsetsStart + i]);
        }

        return (lemma, synsetIds);
    }

    public static WordNetDatabase ParseNounDatabase(string dataNounPath, string indexNounPath)
    {
        Dictionary<string, WordNetSynset> synsetsById = new();
        foreach (string line in File.ReadLines(dataNounPath))
        {
            if (ParseDataNounLine(line) is { } synset)
            {
                synsetsById[synset.SynsetId] = synset;
            }
        }

        Dictionary<string, IReadOnlyList<string>> synsetIdsByLemma = new();
        foreach (string line in File.ReadLines(indexNounPath))
        {
            if (ParseIndexNounLine(line) is { } entry)
            {
                synsetIdsByLemma[entry.Lemma] = entry.SynsetIds;
            }
        }

        return new WordNetDatabase(synsetsById, synsetIdsByLemma);
    }

    private static bool IsAllDigits(string s)
    {
        foreach (char c in s)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
