namespace PictTag.TaxonomyBuilder.WordNet;

/// <summary>
/// One noun synset as parsed from WordNet's <c>data.noun</c>, before any seeding/trimming.
/// <see cref="Lemmas"/> is the synset's own alias/synonym list (e.g. "dog", "domestic dog",
/// "Canis familiaris" all point at the same synset) - this becomes the exact-match lookup
/// table for <see cref="PictTag.Core.Taxonomy.ITaxonomyProvider"/>.
/// </summary>
public sealed record WordNetSynset(
    string SynsetId,
    IReadOnlyList<string> Lemmas,
    string Gloss,
    IReadOnlyList<string> HypernymIds,
    IReadOnlyList<string> HyponymIds);
