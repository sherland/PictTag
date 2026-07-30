namespace PictTag.Core.Taxonomy;

/// <summary>
/// Resolves one raw LLM detection to a canonical WordNet taxonomy match: an exact lemma match
/// against <see cref="Label"/> then <see cref="RawDetection.Group"/> (cheap, unambiguous when it
/// hits), falling back to <see cref="ITaxonomyEmbeddingIndex"/>'s semantic search when neither
/// literally matches a WordNet lemma. Returns null (unresolved) when nothing clears the
/// embedding index's similarity threshold either - callers must handle that case by falling back
/// to the raw Category/Group/Label shape rather than treating it as an error.
/// </summary>
public sealed class EntityTaxonomyResolver(ITaxonomyProvider provider, ITaxonomyEmbeddingIndex embeddingIndex)
{
    public async Task<TaxonomyMatch?> ResolveAsync(RawDetection raw, CancellationToken cancellationToken = default)
    {
        if (provider.TryExactMatch(raw.Label, raw.Category, out TaxonomyMatch exactLabelMatch))
        {
            return exactLabelMatch;
        }

        if (provider.TryExactMatch(raw.Group, raw.Category, out TaxonomyMatch exactGroupMatch))
        {
            return exactGroupMatch;
        }

        TaxonomyMatch? semanticLabelMatch = await embeddingIndex.FindNearestAsync(raw.Label, raw.Category, cancellationToken);
        if (semanticLabelMatch is not null)
        {
            return semanticLabelMatch;
        }

        // Found empirically: a detection with Label "treetops" (too specific/compound a phrase
        // to embed close to anything) and Group "trees" (which embeds well above threshold
        // against "tree") resolved to nothing at all, because only Label was ever tried here -
        // mirror the exact-match tier's Label-then-Group fallback for the semantic tier too.
        return string.Equals(raw.Label, raw.Group, StringComparison.OrdinalIgnoreCase)
            ? null
            : await embeddingIndex.FindNearestAsync(raw.Group, raw.Category, cancellationToken);
    }
}
