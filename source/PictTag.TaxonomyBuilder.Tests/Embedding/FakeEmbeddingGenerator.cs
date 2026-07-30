using Microsoft.Extensions.AI;

namespace PictTag.TaxonomyBuilder.Tests.Embedding;

/// <summary>
/// A deterministic, in-memory stand-in for the real Ollama-backed generator - returns a fixed
/// 4-dimensional vector derived from the input text's length/hash, and counts calls so tests can
/// assert the embedding cache actually avoided a call rather than just returning plausible data.
/// </summary>
public sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>
{
    public int CallCount { get; private set; }

    public Task<GeneratedEmbeddings<Microsoft.Extensions.AI.Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        List<Microsoft.Extensions.AI.Embedding<float>> embeddings = [];
        foreach (string value in values)
        {
            CallCount++;
            embeddings.Add(new Microsoft.Extensions.AI.Embedding<float>(DeterministicVector(value)));
        }

        return Task.FromResult(new GeneratedEmbeddings<Microsoft.Extensions.AI.Embedding<float>>(embeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private static float[] DeterministicVector(string text)
    {
        int hash = text.GetHashCode();
        return [hash % 100 / 100f, text.Length, 1f, 0f];
    }
}
