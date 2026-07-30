using PictTag.TaxonomyBuilder.Embedding;
using PictTag.TaxonomyBuilder.Taxonomy;

namespace PictTag.TaxonomyBuilder.Tests.Embedding;

public class NodeEmbedderTests
{
    [Fact]
    public async Task EmbedAllAsync_NoExistingCache_EmbedsEveryNodeOnce()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), $"embedding-cache-{Guid.NewGuid():N}.json");
        try
        {
            List<TaxonomyBuildNode> nodes =
            [
                new("n001", "dog", ["dog"], "a domesticated canine", null),
                new("n002", "cat", ["cat"], "a domesticated feline", "n001"),
            ];
            FakeEmbeddingGenerator generator = new();

            EmbeddingResult result = await NodeEmbedder.EmbedAllAsync(nodes, generator, cachePath, CancellationToken.None);

            Assert.Equal(2, generator.CallCount);
            Assert.Equal(0, result.CacheHits);
            Assert.Equal(2, result.NewlyEmbedded);
            Assert.Equal(2, result.Vectors.Count);
            Assert.True(File.Exists(cachePath));
        }
        finally
        {
            File.Delete(cachePath);
        }
    }

    [Fact]
    public async Task EmbedAllAsync_RerunWithUnchangedNodes_UsesCacheAndNeverCallsGenerator()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), $"embedding-cache-{Guid.NewGuid():N}.json");
        try
        {
            List<TaxonomyBuildNode> nodes =
            [
                new("n001", "dog", ["dog"], "a domesticated canine", null),
                new("n002", "cat", ["cat"], "a domesticated feline", "n001"),
            ];

            FakeEmbeddingGenerator firstRunGenerator = new();
            EmbeddingResult firstResult = await NodeEmbedder.EmbedAllAsync(nodes, firstRunGenerator, cachePath, CancellationToken.None);

            FakeEmbeddingGenerator secondRunGenerator = new();
            EmbeddingResult secondResult = await NodeEmbedder.EmbedAllAsync(nodes, secondRunGenerator, cachePath, CancellationToken.None);

            Assert.Equal(0, secondRunGenerator.CallCount);
            Assert.Equal(2, secondResult.CacheHits);
            Assert.Equal(0, secondResult.NewlyEmbedded);
            Assert.Equal(firstResult.Vectors[0], secondResult.Vectors[0]);
            Assert.Equal(firstResult.Vectors[1], secondResult.Vectors[1]);
        }
        finally
        {
            File.Delete(cachePath);
        }
    }

    [Fact]
    public async Task EmbedAllAsync_OneNodesGlossChanges_OnlyThatNodeIsReEmbedded()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), $"embedding-cache-{Guid.NewGuid():N}.json");
        try
        {
            List<TaxonomyBuildNode> originalNodes =
            [
                new("n001", "dog", ["dog"], "a domesticated canine", null),
                new("n002", "cat", ["cat"], "a domesticated feline", "n001"),
            ];
            await NodeEmbedder.EmbedAllAsync(originalNodes, new FakeEmbeddingGenerator(), cachePath, CancellationToken.None);

            List<TaxonomyBuildNode> changedNodes =
            [
                new("n001", "dog", ["dog"], "a domesticated canine", null), // unchanged
                new("n002", "cat", ["cat"], "a small domesticated feline with retractable claws", "n001"), // gloss changed
            ];
            FakeEmbeddingGenerator secondRunGenerator = new();
            EmbeddingResult result = await NodeEmbedder.EmbedAllAsync(changedNodes, secondRunGenerator, cachePath, CancellationToken.None);

            Assert.Equal(1, secondRunGenerator.CallCount);
            Assert.Equal(1, result.CacheHits);
            Assert.Equal(1, result.NewlyEmbedded);
        }
        finally
        {
            File.Delete(cachePath);
        }
    }

    [Fact]
    public void BuildCanonicalText_HasGloss_CombinesNameAndGloss()
    {
        TaxonomyBuildNode node = new("n001", "dog", ["dog"], "a domesticated canine", null);

        Assert.Equal("dog: a domesticated canine", NodeEmbedder.BuildCanonicalText(node));
    }

    [Fact]
    public void BuildCanonicalText_NoGloss_UsesNameAlone()
    {
        TaxonomyBuildNode node = new("n001", "dog", ["dog"], "", null);

        Assert.Equal("dog", NodeEmbedder.BuildCanonicalText(node));
    }
}
