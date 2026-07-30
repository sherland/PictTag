using PictTag.TaxonomyBuilder.WordNet;

namespace PictTag.TaxonomyBuilder.Tests.WordNet;

public class WordNetParserTests
{
    // Every line literal below is copied verbatim from the real, committed
    // data/wordnet/raw/{data.noun,index.noun} - not an idealized format guessed from the spec.

    [Fact]
    public void ParseDataNounLine_HeaderLine_ReturnsNull()
    {
        // Line 14 of the real file's 29-line copyright header.
        const string headerLine = "  14 WordNet 3.0 Copyright 2006 by Princeton University.  All rights reserved.  ";

        Assert.Null(WordNetParser.ParseDataNounLine(headerLine));
    }

    [Fact]
    public void ParseDataNounLine_EntityRoot_ParsesOffsetAndHyponymsWithNoHypernyms()
    {
        const string line = "00001740 03 n 01 entity 0 003 ~ 00001930 n 0000 ~ 00002137 n 0000 ~ 04424418 n 0000 | that which is perceived or known or inferred to have its own distinct existence (living or nonliving)  ";

        PictTag.TaxonomyBuilder.WordNet.WordNetSynset? synset = WordNetParser.ParseDataNounLine(line);

        Assert.NotNull(synset);
        Assert.Equal("n00001740", synset.SynsetId);
        Assert.Equal(["entity"], synset.Lemmas);
        Assert.Empty(synset.HypernymIds);
        Assert.Equal(["n00001930", "n00002137", "n04424418"], synset.HyponymIds);
        Assert.StartsWith("that which is perceived", synset.Gloss);
    }

    [Fact]
    public void ParseDataNounLine_GoldenRetriever_MatchesTheImageNetSynsetIdFromTheUsersOwnExample()
    {
        // n02099601 is the literal ImageNet/WordNet synset id the user cited as the motivating
        // example (Entity -> Organism -> ... -> Retriever -> Golden Retriever).
        const string line = "02099601 05 n 01 golden_retriever 0 001 @ 02099029 n 0000 | an English breed having a long silky golden coat  ";

        PictTag.TaxonomyBuilder.WordNet.WordNetSynset? synset = WordNetParser.ParseDataNounLine(line);

        Assert.NotNull(synset);
        Assert.Equal("n02099601", synset.SynsetId);
        Assert.Equal(["golden retriever"], synset.Lemmas);
        Assert.Equal(["n02099029"], synset.HypernymIds);
        Assert.Empty(synset.HyponymIds);
    }

    [Fact]
    public void ParseDataNounLine_WordCountIsHexadecimal_ElevenLemmaSynsetParsesAllOfThem()
    {
        // w_cnt here is "0b" (hex 11) - a real synset with exactly 11 lemmas, used to prove
        // w_cnt must be parsed as hex, not decimal (decimal "0b" would fail to parse at all).
        const string line = "00074790 04 n 0b blunder 0 blooper 0 bloomer 0 bungle 0 pratfall 0 foul-up 0 fuckup 0 flub 0 botch 0 boner 0 boo-boo 0 019 @ 00070965 n 0000 + 02229000 a 0901 + 02527651 v 0901 + 02527651 v 0808 + 02527651 v 0718 + 02527651 v 0616 + 02527651 v 040d + 00013172 v 0401 + 02566227 v 0103 ~ 00071864 n 0000 ~ 00075283 n 0000 ~ 00075471 n 0000 ~ 00075790 n 0000 ~ 00075912 n 0000 ~ 00076072 n 0000 ~ 00076196 n 0000 ~ 00076323 n 0000 ~ 00076393 n 0000 ~ 00076563 n 0000 | an embarrassing mistake  ";

        PictTag.TaxonomyBuilder.WordNet.WordNetSynset? synset = WordNetParser.ParseDataNounLine(line);

        Assert.NotNull(synset);
        Assert.Equal(11, synset.Lemmas.Count);
        Assert.Equal("blunder", synset.Lemmas[0]);
        Assert.Equal("boo-boo", synset.Lemmas[^1]);
        // p_cnt is "019" (decimal 19, not hex 25) - only the single "@" and ten "~" pointers are
        // hypernym/hyponym; the "+" (derivationally related) pointers to other parts of speech
        // are correctly ignored.
        Assert.Equal(["n00070965"], synset.HypernymIds);
        Assert.Equal(10, synset.HyponymIds.Count);
    }

    [Fact]
    public void ParseDataNounLine_MultiWordLemmaWithUnderscore_BecomesSpaceSeparated()
    {
        const string line = "02099601 05 n 01 golden_retriever 0 001 @ 02099029 n 0000 | an English breed having a long silky golden coat  ";

        PictTag.TaxonomyBuilder.WordNet.WordNetSynset? synset = WordNetParser.ParseDataNounLine(line);

        Assert.Equal("golden retriever", Assert.Single(synset!.Lemmas));
    }

    [Fact]
    public void ParseIndexNounLine_HeaderLine_ReturnsNull()
    {
        const string headerLine = "  14 WordNet 3.0 Copyright 2006 by Princeton University.  All rights reserved.  ";

        Assert.Null(WordNetParser.ParseIndexNounLine(headerLine));
    }

    [Fact]
    public void ParseIndexNounLine_SingleSenseLemma_ReturnsLemmaAndOneSynsetId()
    {
        const string line = "golden_retriever n 1 1 @ 1 0 02099601  ";

        (string Lemma, IReadOnlyList<string> SynsetIds)? result = WordNetParser.ParseIndexNounLine(line);

        Assert.NotNull(result);
        Assert.Equal("golden retriever", result.Value.Lemma);
        Assert.Equal(["n02099601"], result.Value.SynsetIds);
    }

    [Fact]
    public void ParseIndexNounLine_MultiSenseLemma_ReturnsAllSynsetIdsInSenseOrder()
    {
        // "run" has 16 senses (synset_cnt) and 4 distinct pointer symbols (p_cnt) - a real,
        // heavily polysemous lemma used to prove the offsetsStart skip-count (4 + p_cnt + 2)
        // correctly lands on the first synset offset regardless of how many pointer symbols
        // precede it.
        const string line = "run n 16 4 @ ~ + ; 16 7 00189565 00791078 07460104 08460585 00558883 00308871 00293916 15262120 13995935 13760129 09415938 07472929 07443010 07407777 05045841 00309011  ";

        (string Lemma, IReadOnlyList<string> SynsetIds)? result = WordNetParser.ParseIndexNounLine(line);

        Assert.NotNull(result);
        Assert.Equal("run", result.Value.Lemma);
        Assert.Equal(16, result.Value.SynsetIds.Count);
        Assert.Equal("n00189565", result.Value.SynsetIds[0]);
        Assert.Equal("n00309011", result.Value.SynsetIds[^1]);
    }

    [Fact]
    public void ParseNounDatabase_RealCommittedFiles_ParsesGoldenRetrieverChainStart()
    {
        string wordNetDir = FindWordNetRawDir();
        WordNetDatabase db = WordNetParser.ParseNounDatabase(
            Path.Combine(wordNetDir, "data.noun"), Path.Combine(wordNetDir, "index.noun"));

        Assert.True(db.SynsetsById.Count > 80_000);
        Assert.True(db.SynsetIdsByLemma.ContainsKey("golden retriever"));

        string synsetId = db.SynsetIdsByLemma["golden retriever"][0];
        Assert.Equal("n02099601", synsetId);

        PictTag.TaxonomyBuilder.WordNet.WordNetSynset synset = db.SynsetsById[synsetId];
        Assert.Single(synset.HypernymIds);
        Assert.True(db.SynsetsById.ContainsKey(synset.HypernymIds[0]));
    }

    private static string FindWordNetRawDir()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "data", "wordnet", "raw");
            if (File.Exists(Path.Combine(candidate, "data.noun")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate data/wordnet/raw from the test output directory.");
    }
}
