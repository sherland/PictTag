namespace PictTag.Core.Taxonomy;

/// <summary>
/// Semantic (embedding-based) fallback for resolving free text that doesn't literally match any
/// WordNet lemma - e.g. a paraphrase the model invented ("notebook pc") or jargon/descriptive
/// phrasing with no exact WordNet lemma ("rigid inflatable boat", "wooden boat").
/// </summary>
public interface ITaxonomyEmbeddingIndex
{
    /// <summary>
    /// Embeds <paramref name="freeText"/> and returns the nearest taxonomy node by cosine
    /// similarity, or null if nothing clears the configured similarity threshold.
    /// <paramref name="categoryHint"/>, when given, is tried first as a same-category-only
    /// search before falling back to an unrestricted search.
    /// </summary>
    Task<TaxonomyMatch?> FindNearestAsync(string freeText, EntityCategory? categoryHint, CancellationToken cancellationToken = default);
}
