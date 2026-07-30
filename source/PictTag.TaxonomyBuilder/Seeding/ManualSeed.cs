using System.Text.Json;

namespace PictTag.TaxonomyBuilder.Seeding;

/// <summary>
/// A hand-picked single-node seed for a domain ImageNet-1k barely covers (see
/// data/wordnet/seeds/manual-seeds.json). <see cref="SynsetId"/> is the already-disambiguated
/// synset id chosen by hand against the real data.noun/index.noun - <see cref="Comment"/> records
/// why that particular sense was picked, for anyone re-reviewing or extending the list later.
/// </summary>
public sealed record ManualSeed(string Lemma, string SynsetId, string Comment);

public static class ManualSeedLoader
{
    public static IReadOnlyList<ManualSeed> Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        return JsonSerializer.Deserialize<List<ManualSeed>>(stream, options)
            ?? throw new InvalidOperationException($"Could not parse '{path}' as a manual seed list.");
    }
}
