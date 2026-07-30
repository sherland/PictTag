using PictTag.Core.Taxonomy;

namespace PictTag.Core.Tests.Taxonomy;

/// <summary>A tiny, hand-built stand-in for <see cref="WordNetTaxonomyProvider"/> - no real WordNet data.</summary>
public sealed class FakeTaxonomyProvider : ITaxonomyProvider
{
    public sealed record Node(string SynsetId, string Name, string? ParentId, EntityCategory Category);

    private readonly Dictionary<string, Node> _nodesById = new();
    private readonly Dictionary<string, List<Node>> _nodesByLemma = new(StringComparer.OrdinalIgnoreCase);

    public FakeTaxonomyProvider AddNode(string synsetId, string lemma, string? parentId, EntityCategory category)
    {
        Node node = new(synsetId, lemma, parentId, category);
        _nodesById[synsetId] = node;
        if (!_nodesByLemma.TryGetValue(lemma, out List<Node>? nodes))
        {
            nodes = [];
            _nodesByLemma[lemma] = nodes;
        }

        nodes.Add(node);
        return this;
    }

    public bool TryExactMatch(string freeText, EntityCategory? categoryHint, out TaxonomyMatch match)
    {
        if (_nodesByLemma.TryGetValue(freeText, out List<Node>? candidates) && candidates.Count > 0)
        {
            Node chosen = candidates.Count == 1
                ? candidates[0]
                : candidates.FirstOrDefault(n => categoryHint is not null && n.Category == categoryHint, candidates[0]);

            match = new TaxonomyMatch(GetAncestorChain(chosen.SynsetId), TaxonomyMatchQuality.Exact, 1.0, freeText);
            return true;
        }

        match = null!;
        return false;
    }

    public IReadOnlyList<TaxonomyNode> GetAncestorChain(string synsetId)
    {
        List<TaxonomyNode> chain = [];
        string? currentId = synsetId;
        while (currentId is not null && _nodesById.TryGetValue(currentId, out Node? node))
        {
            chain.Add(new TaxonomyNode(node.SynsetId, node.Name));
            currentId = node.ParentId;
        }

        chain.Reverse();
        return chain;
    }

    public EntityCategory? GetCategory(string synsetId) => _nodesById.TryGetValue(synsetId, out Node? node) ? node.Category : null;
}
