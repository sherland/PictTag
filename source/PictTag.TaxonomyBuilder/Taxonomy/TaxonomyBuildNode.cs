namespace PictTag.TaxonomyBuilder.Taxonomy;

/// <summary>
/// One node of the trimmed, tree-shaped (never DAG) taxonomy that gets emitted to
/// <c>source/PictTag.Core/Taxonomy/taxonomy.json</c>. <see cref="PrimaryParentId"/> is null for a
/// root node - either a configured category anchor (see TrimConfig.RootCollapseAnchors) or a true
/// WordNet root with no hypernym at all.
/// </summary>
public sealed record TaxonomyBuildNode(string Id, string Name, IReadOnlyList<string> Lemmas, string Gloss, string? PrimaryParentId);

/// <summary>The full document written to taxonomy.json, including WordNet's required attribution.</summary>
public sealed record TaxonomyDocument(
    string Version,
    string License,
    string GeneratedFromWordNetVersion,
    IReadOnlyList<TaxonomyBuildNode> Nodes,
    IReadOnlyDictionary<string, IReadOnlyList<string>> CategoryRoots);
