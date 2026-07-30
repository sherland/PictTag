using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PictTag.TaxonomyBuilder.Embedding;

/// <summary>
/// Persists node embeddings across builder runs, keyed by (synset id, canonical-text hash), so
/// re-running the builder after a trim-config tweak only re-embeds nodes that are new or whose
/// canonical text actually changed - not the whole graph. Not committed to git (see
/// data/wordnet/build-debug/ in .gitignore) - purely a rerun-speed optimization, not source data.
/// </summary>
public sealed class EmbeddingCache
{
    private readonly Dictionary<string, CacheEntry> _entriesById;

    private EmbeddingCache(Dictionary<string, CacheEntry> entriesById)
    {
        _entriesById = entriesById;
    }

    public static EmbeddingCache LoadOrEmpty(string path)
    {
        if (!File.Exists(path))
        {
            return new EmbeddingCache(new Dictionary<string, CacheEntry>());
        }

        using FileStream stream = File.OpenRead(path);
        CacheFile? file = JsonSerializer.Deserialize<CacheFile>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Dictionary<string, CacheEntry> entries = file?.Entries ?? new Dictionary<string, CacheEntry>();
        return new EmbeddingCache(entries);
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, new CacheFile(_entriesById), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }

    public static string ComputeTextHash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public bool TryGet(string synsetId, string textHash, out float[] vector)
    {
        if (_entriesById.TryGetValue(synsetId, out CacheEntry? entry) && entry.TextHash == textHash)
        {
            vector = entry.Vector;
            return true;
        }

        vector = [];
        return false;
    }

    public void Set(string synsetId, string textHash, float[] vector)
    {
        _entriesById[synsetId] = new CacheEntry(textHash, vector);
    }

    private sealed record CacheFile(Dictionary<string, CacheEntry> Entries);

    private sealed record CacheEntry(string TextHash, float[] Vector);
}
