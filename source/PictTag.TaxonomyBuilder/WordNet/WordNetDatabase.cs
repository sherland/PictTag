namespace PictTag.TaxonomyBuilder.WordNet;

/// <summary>The full parsed WordNet noun database: every synset, plus the lemma -> senses index.</summary>
public sealed class WordNetDatabase
{
    public WordNetDatabase(
        IReadOnlyDictionary<string, WordNetSynset> synsetsById,
        IReadOnlyDictionary<string, IReadOnlyList<string>> synsetIdsByLemma)
    {
        SynsetsById = synsetsById;
        SynsetIdsByLemma = synsetIdsByLemma;
    }

    public IReadOnlyDictionary<string, WordNetSynset> SynsetsById { get; }

    /// <summary>Lemma (spaces, not underscores) -> synset ids, ordered sense-1-first as WordNet itself orders them.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SynsetIdsByLemma { get; }
}
