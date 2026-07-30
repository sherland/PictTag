using System.Text.Json;

namespace PictTag.TaxonomyBuilder.Seeding;

/// <summary>One ImageNet-1k class: a WordNet synset id ("wnid") and its class name.</summary>
public sealed record ImageNetLeaf(string SynsetId, string Name);

public static class ImageNetLeafLoader
{
    /// <summary>
    /// Loads imagenet_class_index.json, shaped <c>{"0": ["n01440764", "tench"], ...}</c> - the
    /// dictionary key (a decimal class id) isn't needed here, only the wnid/name pairs.
    /// </summary>
    public static IReadOnlyList<ImageNetLeaf> Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        Dictionary<string, string[]> raw = JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)
            ?? throw new InvalidOperationException($"Could not parse '{path}' as an ImageNet class index.");

        return raw.Values.Select(entry => new ImageNetLeaf(entry[0], entry[1].Replace('_', ' '))).ToList();
    }
}
