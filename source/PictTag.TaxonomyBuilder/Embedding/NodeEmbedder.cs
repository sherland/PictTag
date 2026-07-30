using Microsoft.Extensions.AI;
using PictTag.TaxonomyBuilder.Taxonomy;

namespace PictTag.TaxonomyBuilder.Embedding;

public sealed record EmbeddingResult(int Dimension, IReadOnlyList<float[]> Vectors, int CacheHits, int NewlyEmbedded);

/// <summary>
/// Embeds one canonical text per taxonomy node - "{name}: {gloss}" when a gloss is available,
/// else just the name - via a local embedding model, reusing cached vectors whenever a node's
/// canonical text hasn't changed since the last run (see <see cref="EmbeddingCache"/>).
/// </summary>
public static class NodeEmbedder
{
    public static async Task<EmbeddingResult> EmbedAllAsync(
        IReadOnlyList<TaxonomyBuildNode> nodes,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        string cachePath,
        CancellationToken ct)
    {
        EmbeddingCache cache = EmbeddingCache.LoadOrEmpty(cachePath);
        List<float[]> vectors = new(nodes.Count);
        int cacheHits = 0;
        int newlyEmbedded = 0;
        int dimension = 0;

        foreach (TaxonomyBuildNode node in nodes)
        {
            string canonicalText = BuildCanonicalText(node);
            string textHash = EmbeddingCache.ComputeTextHash(canonicalText);

            if (cache.TryGet(node.Id, textHash, out float[] cachedVector))
            {
                vectors.Add(cachedVector);
                cacheHits++;
                dimension = cachedVector.Length;
                continue;
            }

            ReadOnlyMemory<float> vector = await generator.GenerateVectorAsync(canonicalText, cancellationToken: ct);
            float[] array = vector.ToArray();
            cache.Set(node.Id, textHash, array);
            vectors.Add(array);
            newlyEmbedded++;
            dimension = array.Length;
        }

        cache.Save(cachePath);
        return new EmbeddingResult(dimension, vectors, cacheHits, newlyEmbedded);
    }

    public static string BuildCanonicalText(TaxonomyBuildNode node) =>
        string.IsNullOrEmpty(node.Gloss) ? node.Name : $"{node.Name}: {node.Gloss}";
}
