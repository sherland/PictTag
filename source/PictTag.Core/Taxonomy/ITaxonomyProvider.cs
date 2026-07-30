namespace PictTag.Core.Taxonomy;

/// <summary>
/// Read-only access to the WordNet-derived taxonomy shipped with PictTag.Core (see
/// docs/TAXONOMY.md and source/PictTag.TaxonomyBuilder). Abstracted so the shipped dataset can be
/// swapped or tuned without touching <see cref="ImageDetectionService"/> or the resolver.
/// </summary>
public interface ITaxonomyProvider
{
    /// <summary>
    /// Looks up <paramref name="freeText"/> as a WordNet lemma (case-insensitive, exact match
    /// only - no stemming/fuzzy matching, that's what <see cref="ITaxonomyEmbeddingIndex"/> is
    /// for). When more than one surviving node shares the same lemma (e.g. "crane" the bird vs.
    /// "crane" the lifting device), <paramref name="categoryHint"/> disambiguates by preferring a
    /// candidate whose resolved <see cref="EntityCategory"/> matches it.
    /// </summary>
    bool TryExactMatch(string freeText, EntityCategory? categoryHint, out TaxonomyMatch match);

    /// <summary>Root-to-leaf ancestor chain for a known synset id. Empty if the id is unknown.</summary>
    IReadOnlyList<TaxonomyNode> GetAncestorChain(string synsetId);

    /// <summary>
    /// The <see cref="EntityCategory"/> a synset ultimately belongs to (the category whose
    /// configured root-collapse anchor is this synset's topmost ancestor), or null if the id is
    /// unknown or its root isn't a configured category anchor.
    /// </summary>
    EntityCategory? GetCategory(string synsetId);
}
