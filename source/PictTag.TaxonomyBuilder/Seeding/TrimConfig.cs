using System.Text.Json;

namespace PictTag.TaxonomyBuilder.Seeding;

/// <summary>
/// Config-driven trimming rules (see data/wordnet/seeds/trim-config.json). The JSON file also
/// carries a few "*Comment" fields alongside the real ones, documenting the reasoning behind each
/// choice for future tuning - those are simply ignored by deserialization (extra JSON members are
/// ignored by default), not part of this type.
/// </summary>
public sealed record TrimConfig(
    IReadOnlyDictionary<string, IReadOnlyList<string>> RootCollapseAnchors,
    IReadOnlyList<string> ExcludeSynsets,
    IReadOnlyList<string> ExcludeSubtrees,
    bool StripLatinLemmas)
{
    public IReadOnlySet<string> AllAnchorIds => RootCollapseAnchors.Values.SelectMany(ids => ids).ToHashSet();
}

public static class TrimConfigLoader
{
    public static TrimConfig Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        return JsonSerializer.Deserialize<TrimConfig>(stream, options)
            ?? throw new InvalidOperationException($"Could not parse '{path}' as a trim config.");
    }
}
