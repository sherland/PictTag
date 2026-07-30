using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace PictTag.Core.Taxonomy;

/// <summary>
/// Semantic fallback tier backed by a local Ollama embedding model (default: nomic-embed-text),
/// doing a brute-force cosine-similarity scan over the taxonomy's precomputed node embeddings -
/// a few thousand short-vector dot products is microseconds, no vector database needed at this
/// corpus size.
/// </summary>
public sealed class OllamaTaxonomyEmbeddingIndex : ITaxonomyEmbeddingIndex
{
    /// <summary>
    /// Empirically tuned against nomic-embed-text during design validation, not picked blind:
    /// legitimate paraphrase matches measured ~0.69-0.78 ("wooden boat" -> boat 0.739, "rigid
    /// inflatable boat" -> motorboat 0.693, "notebook pc" -> notebook 0.776), while a genuinely
    /// wrong guess ("macbook", which the model very briefly confused with "macaque") maxed out
    /// at ~0.60. 0.65 sits cleanly in the gap between those two clusters. Expect this to need
    /// further tuning once real detection fixtures are reviewed (see docs/TESTING.md) - it's a
    /// constructor parameter specifically so that can happen without a code change.
    /// </summary>
    public const double DefaultMinSimilarity = 0.65;

    private readonly ITaxonomyProvider _provider;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly IReadOnlyList<string> _nodeIds;
    private readonly float[][] _vectors;
    private readonly double _minSimilarity;

    /// <summary>
    /// Defaults to the same "http://localhost:11434" Ollama instance <see cref="ImageDetectionService"/>
    /// defaults to. A caller using a non-default Ollama URL should construct its own
    /// <see cref="OllamaTaxonomyEmbeddingIndex"/> pointed at the same server instead of relying on
    /// this shared instance.
    /// </summary>
    public static readonly Lazy<OllamaTaxonomyEmbeddingIndex> Shared = new(() => new OllamaTaxonomyEmbeddingIndex(
        WordNetTaxonomyProvider.Shared.Value,
        new OllamaApiClient(new Uri("http://localhost:11434"), "nomic-embed-text")));

    public OllamaTaxonomyEmbeddingIndex(
        ITaxonomyProvider provider,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        double minSimilarity = DefaultMinSimilarity)
    {
        _provider = provider;
        _generator = generator;
        _minSimilarity = minSimilarity;
        (_nodeIds, _vectors) = LoadEmbeddedVectors();
    }

    public async Task<TaxonomyMatch?> FindNearestAsync(string freeText, EntityCategory? categoryHint, CancellationToken cancellationToken = default)
    {
        ReadOnlyMemory<float> queryMemory = await _generator.GenerateVectorAsync(freeText, cancellationToken: cancellationToken);
        float[] query = queryMemory.ToArray();

        // Try same-category-only first so categoryHint has real teeth here too (not just for the
        // exact-match tier's homonym disambiguation) - fall back to an unrestricted search if
        // nothing in that category clears the threshold, since the model's own category guess
        // can be wrong.
        if (categoryHint is not null)
        {
            TaxonomyMatch? sameCategory = FindBest(query, id => _provider.GetCategory(id) == categoryHint);
            if (sameCategory is not null)
            {
                return sameCategory;
            }
        }

        return FindBest(query, static _ => true);
    }

    private TaxonomyMatch? FindBest(float[] query, Func<string, bool> filter)
    {
        int bestIndex = -1;
        double bestScore = double.NegativeInfinity;

        for (int i = 0; i < _nodeIds.Count; i++)
        {
            if (!filter(_nodeIds[i]))
            {
                continue;
            }

            double score = CosineSimilarity(query, _vectors[i]);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        if (bestIndex < 0 || bestScore < _minSimilarity)
        {
            return null;
        }

        IReadOnlyList<TaxonomyNode> ancestors = _provider.GetAncestorChain(_nodeIds[bestIndex]);
        return ancestors.Count == 0 ? null : new TaxonomyMatch(ancestors, TaxonomyMatchQuality.Semantic, bestScore, ancestors[^1].Name);
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    /// <summary>
    /// Loads taxonomy.json (for the node id order) and taxonomy-embeddings.bin (the packed
    /// vectors) as two independent embedded resources - see EmbeddingsBinWriter in
    /// PictTag.TaxonomyBuilder for the exact binary format this must match: int32 nodeCount,
    /// int32 dimension, then nodeCount*dimension float32 values in the same order as
    /// taxonomy.json's "nodes" array.
    /// </summary>
    private static (IReadOnlyList<string> NodeIds, float[][] Vectors) LoadEmbeddedVectors()
    {
        Assembly assembly = typeof(OllamaTaxonomyEmbeddingIndex).Assembly;
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

        string taxonomyJsonResource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("taxonomy.json", StringComparison.Ordinal));
        using Stream jsonStream = assembly.GetManifestResourceStream(taxonomyJsonResource)
            ?? throw new InvalidOperationException($"Could not open embedded resource '{taxonomyJsonResource}'.");
        NodeIdListDto document = JsonSerializer.Deserialize<NodeIdListDto>(jsonStream, options)
            ?? throw new InvalidOperationException("taxonomy.json deserialized to null.");
        List<string> nodeIds = document.Nodes.Select(n => n.Id).ToList();

        string binResource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("taxonomy-embeddings.bin", StringComparison.Ordinal));
        using Stream binStream = assembly.GetManifestResourceStream(binResource)
            ?? throw new InvalidOperationException($"Could not open embedded resource '{binResource}'.");
        using BinaryReader reader = new(binStream);

        int nodeCount = reader.ReadInt32();
        int dimension = reader.ReadInt32();
        if (nodeCount != nodeIds.Count)
        {
            throw new InvalidOperationException(
                $"taxonomy-embeddings.bin has {nodeCount} vectors but taxonomy.json has {nodeIds.Count} nodes - they must always be regenerated together.");
        }

        float[][] vectors = new float[nodeCount][];
        for (int i = 0; i < nodeCount; i++)
        {
            float[] vector = new float[dimension];
            for (int d = 0; d < dimension; d++)
            {
                vector[d] = reader.ReadSingle();
            }

            vectors[i] = vector;
        }

        return (nodeIds, vectors);
    }

    private sealed record NodeIdListDto(List<NodeIdEntryDto> Nodes);

    private sealed record NodeIdEntryDto(string Id);
}
