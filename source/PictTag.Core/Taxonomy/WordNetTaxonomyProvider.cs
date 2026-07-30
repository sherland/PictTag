using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PictTag.Core.Taxonomy;

/// <summary>
/// Loads the embedded, WordNet-derived <c>taxonomy.json</c> (built offline by
/// <c>PictTag.TaxonomyBuilder</c> - see docs/TAXONOMY.md) once, and answers exact-lemma-match and
/// ancestor-chain queries against it from in-memory dictionaries built at construction time.
/// </summary>
public sealed class WordNetTaxonomyProvider : ITaxonomyProvider
{
    private readonly IReadOnlyDictionary<string, TaxonomyNodeDto> _nodesById;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _synsetIdsByNormalizedLemma;
    private readonly IReadOnlyDictionary<string, EntityCategory> _categoryByRootAnchorId;
    private readonly Dictionary<string, EntityCategory?> _categoryByNodeId = new();

    public static readonly Lazy<WordNetTaxonomyProvider> Shared = new(LoadFromEmbeddedResource);

    public WordNetTaxonomyProvider(Stream taxonomyJsonStream)
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        TaxonomyDocumentDto document = JsonSerializer.Deserialize<TaxonomyDocumentDto>(taxonomyJsonStream, options)
            ?? throw new InvalidOperationException("taxonomy.json deserialized to null.");

        _nodesById = document.Nodes.ToDictionary(n => n.Id);

        Dictionary<string, List<string>> byLemma = new();
        foreach (TaxonomyNodeDto node in document.Nodes)
        {
            foreach (string lemma in node.Lemmas)
            {
                string normalized = Normalize(lemma);
                if (!byLemma.TryGetValue(normalized, out List<string>? ids))
                {
                    ids = [];
                    byLemma[normalized] = ids;
                }

                ids.Add(node.Id);
            }
        }

        _synsetIdsByNormalizedLemma = byLemma.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);

        Dictionary<string, EntityCategory> categoryByRootAnchorId = new();
        foreach ((string categoryName, List<string> anchorIds) in document.CategoryRoots)
        {
            if (!Enum.TryParse(categoryName, out EntityCategory category))
            {
                continue;
            }

            foreach (string anchorId in anchorIds)
            {
                categoryByRootAnchorId[anchorId] = category;
            }
        }

        _categoryByRootAnchorId = categoryByRootAnchorId;
    }

    public bool TryExactMatch(string freeText, EntityCategory? categoryHint, out TaxonomyMatch match)
    {
        if (_synsetIdsByNormalizedLemma.TryGetValue(Normalize(freeText), out IReadOnlyList<string>? candidateIds) && candidateIds.Count > 0)
        {
            string chosenId = candidateIds.Count == 1
                ? candidateIds[0]
                : candidateIds.FirstOrDefault(id => categoryHint is not null && GetCategory(id) == categoryHint, candidateIds[0]);

            IReadOnlyList<TaxonomyNode> ancestors = GetAncestorChain(chosenId);
            if (ancestors.Count > 0)
            {
                match = new TaxonomyMatch(ancestors, TaxonomyMatchQuality.Exact, Confidence: 1.0, MatchedLemma: Normalize(freeText));
                return true;
            }
        }

        match = null!;
        return false;
    }

    public IReadOnlyList<TaxonomyNode> GetAncestorChain(string synsetId)
    {
        List<TaxonomyNode> chain = [];
        string? currentId = synsetId;
        HashSet<string> visited = [];

        while (currentId is not null && visited.Add(currentId) && _nodesById.TryGetValue(currentId, out TaxonomyNodeDto? node))
        {
            chain.Add(new TaxonomyNode(node.Id, node.Name));
            currentId = node.PrimaryParentId;
        }

        chain.Reverse();
        return chain;
    }

    public EntityCategory? GetCategory(string synsetId)
    {
        if (_categoryByNodeId.TryGetValue(synsetId, out EntityCategory? cached))
        {
            return cached;
        }

        // A node's category is whichever category's anchor is its topmost ancestor - walk to the
        // root (GetAncestorChain already stops there) and look that root up.
        IReadOnlyList<TaxonomyNode> chain = GetAncestorChain(synsetId);
        EntityCategory? category = chain.Count > 0 && _categoryByRootAnchorId.TryGetValue(chain[0].SynsetId, out EntityCategory found)
            ? found
            : null;

        _categoryByNodeId[synsetId] = category;
        return category;
    }

    private static string Normalize(string text) => text.Trim().ToLowerInvariant();

    private static WordNetTaxonomyProvider LoadFromEmbeddedResource()
    {
        Assembly assembly = typeof(WordNetTaxonomyProvider).Assembly;
        string resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith("taxonomy.json", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded resource '{resourceName}'.");
        return new WordNetTaxonomyProvider(stream);
    }

    private sealed record TaxonomyDocumentDto(
        string Version,
        string License,
        string GeneratedFromWordNetVersion,
        List<TaxonomyNodeDto> Nodes,
        Dictionary<string, List<string>> CategoryRoots);

    private sealed record TaxonomyNodeDto(
        string Id,
        string Name,
        List<string> Lemmas,
        string Gloss,
        [property: JsonPropertyName("primaryParentId")] string? PrimaryParentId);
}
