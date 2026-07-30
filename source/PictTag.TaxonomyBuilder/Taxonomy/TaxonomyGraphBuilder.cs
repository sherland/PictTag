using PictTag.TaxonomyBuilder.Seeding;
using PictTag.TaxonomyBuilder.WordNet;

namespace PictTag.TaxonomyBuilder.Taxonomy;

/// <summary>
/// Turns the full parsed WordNet noun graph into the small, tree-shaped subset that actually
/// ships: seed from ImageNet-1k leaves + manual single-node seeds + domain-expansion subtrees,
/// walk upward from every seed to a category anchor (or true root), and resolve WordNet's
/// occasional multi-hypernym DAG down to a single linear parent per node once and for all here -
/// runtime code never has to deal with more than one parent per synset.
/// </summary>
public static class TaxonomyGraphBuilder
{
    public static IReadOnlyList<TaxonomyBuildNode> Build(
        WordNetDatabase db,
        IReadOnlyList<ImageNetLeaf> imageNetLeaves,
        IReadOnlyList<ManualSeed> manualSeeds,
        IReadOnlyList<DomainExpansion> domainExpansions,
        TrimConfig trimConfig)
    {
        IReadOnlySet<string> anchorIds = trimConfig.AllAnchorIds;
        HashSet<string> excludeSynsets = trimConfig.ExcludeSynsets.ToHashSet();
        HashSet<string> excludeSubtreeRoots = trimConfig.ExcludeSubtrees.ToHashSet();

        HashSet<string> seedIds = new();
        foreach (ImageNetLeaf leaf in imageNetLeaves)
        {
            seedIds.Add(leaf.SynsetId);
        }

        foreach (ManualSeed seed in manualSeeds)
        {
            seedIds.Add(seed.SynsetId);
        }

        foreach (DomainExpansion expansion in domainExpansions)
        {
            ExpandDownward(db, expansion.AnchorSynsetId, expansion.Depth, excludeSubtreeRoots, seedIds);
        }

        HashSet<string> reachable = new();
        foreach (string seedId in seedIds)
        {
            WalkUp(db, seedId, anchorIds, reachable);
        }

        List<TaxonomyBuildNode> nodes = new();
        foreach (string id in reachable)
        {
            if (excludeSynsets.Contains(id) || !db.SynsetsById.TryGetValue(id, out WordNetSynset? synset))
            {
                continue;
            }

            string? primaryParentId = anchorIds.Contains(id)
                ? null
                : FindNextIncludedAncestor(db, synset, excludeSynsets);

            IReadOnlyList<string> lemmas = trimConfig.StripLatinLemmas
                ? StripLatinLemmas(synset.Lemmas)
                : synset.Lemmas;

            nodes.Add(new TaxonomyBuildNode(id, lemmas[0], lemmas, synset.Gloss, primaryParentId));
        }

        return nodes.OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Follows the primary (first-listed) hypernym pointer from <paramref name="id"/> up to a
    /// configured category anchor or a true WordNet root (no hypernym at all), adding every node
    /// visited along the way to <paramref name="reachable"/>. Only the first hypernym is ever
    /// followed - this is where a synset with more than one hypernym gets resolved to a single
    /// linear ancestor path, once, at build time.
    /// </summary>
    private static void WalkUp(WordNetDatabase db, string id, IReadOnlySet<string> anchorIds, HashSet<string> reachable)
    {
        while (true)
        {
            if (!reachable.Add(id))
            {
                return;
            }

            if (anchorIds.Contains(id))
            {
                return;
            }

            if (!db.SynsetsById.TryGetValue(id, out WordNetSynset? synset) || synset.HypernymIds.Count == 0)
            {
                return;
            }

            id = synset.HypernymIds[0];
        }
    }

    /// <summary>
    /// The emitted <c>primaryParentId</c> for a non-anchor node: <paramref name="synset"/>'s
    /// primary hypernym, or - if that hypernym is in <paramref name="excludeSynsets"/> - the
    /// nearest ancestor above it that isn't excluded, so a flagged oddity is spliced out of the
    /// chain rather than breaking it.
    /// </summary>
    private static string? FindNextIncludedAncestor(WordNetDatabase db, WordNetSynset synset, HashSet<string> excludeSynsets)
    {
        string? candidateId = synset.HypernymIds.Count > 0 ? synset.HypernymIds[0] : null;
        while (candidateId is not null && excludeSynsets.Contains(candidateId))
        {
            candidateId = db.SynsetsById.TryGetValue(candidateId, out WordNetSynset? excluded) && excluded.HypernymIds.Count > 0
                ? excluded.HypernymIds[0]
                : null;
        }

        return candidateId;
    }

    private static void ExpandDownward(WordNetDatabase db, string anchorId, int depth, HashSet<string> excludeSubtreeRoots, HashSet<string> seedIds)
    {
        if (depth < 0 || excludeSubtreeRoots.Contains(anchorId) || !db.SynsetsById.TryGetValue(anchorId, out WordNetSynset? synset))
        {
            return;
        }

        seedIds.Add(anchorId);
        if (depth == 0)
        {
            return;
        }

        foreach (string hyponymId in synset.HyponymIds)
        {
            ExpandDownward(db, hyponymId, depth - 1, excludeSubtreeRoots, seedIds);
        }
    }

    /// <summary>
    /// Drops WordNet's Latin binomial-nomenclature lemmas (e.g. "Canis familiaris") from a
    /// synset's alias list, keeping only common names for matching. Never returns an empty list -
    /// if every lemma looks like a binomial, the original list is kept as-is.
    /// </summary>
    private static IReadOnlyList<string> StripLatinLemmas(IReadOnlyList<string> lemmas)
    {
        List<string> stripped = lemmas.Where(l => !LooksLikeLatinBinomial(l)).ToList();
        return stripped.Count > 0 ? stripped : lemmas;
    }

    private static bool LooksLikeLatinBinomial(string lemma)
    {
        // Binomial nomenclature capitalizes only the genus (first word), e.g. "Canis familiaris"
        // - unlike a capitalized two-word proper noun (e.g. "New York"), the second word here is
        // always lowercase.
        string[] parts = lemma.Split(' ');
        return parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0
            && char.IsUpper(parts[0][0]) && char.IsLower(parts[1][0]);
    }
}
