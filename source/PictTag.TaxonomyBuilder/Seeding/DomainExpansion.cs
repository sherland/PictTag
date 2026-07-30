using System.Text.Json;

namespace PictTag.TaxonomyBuilder.Seeding;

/// <summary>
/// A "domain anchor" (see data/wordnet/seeds/expand-domains.json) expanded downward through its
/// own hyponym pointers, <see cref="Depth"/> levels deep, to sweep a domain's standard subtypes
/// into the seed set automatically instead of hand-listing every one (e.g. seeding "boat" this
/// way pulls in kayak, canoe, rowboat, sailboat, motorboat -> speedboat, dinghy, houseboat, ...).
/// </summary>
public sealed record DomainExpansion(string AnchorSynsetId, string AnchorLemma, int Depth, string Comment);

public static class DomainExpansionLoader
{
    public static IReadOnlyList<DomainExpansion> Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        return JsonSerializer.Deserialize<List<DomainExpansion>>(stream, options)
            ?? throw new InvalidOperationException($"Could not parse '{path}' as a domain expansion list.");
    }
}
