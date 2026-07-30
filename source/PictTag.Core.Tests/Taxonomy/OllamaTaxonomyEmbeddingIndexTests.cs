using Microsoft.Extensions.AI;
using OllamaSharp;
using PictTag.Core.Taxonomy;

namespace PictTag.Core.Tests.Taxonomy;

/// <summary>
/// Hits a real local Ollama server running nomic-embed-text, so it's opt-in only (set
/// PICTTAG_RUN_LIVE_MODEL_TESTS=1) - same gating convention as ArtStyleDetectionTests, but for
/// the embedding model rather than the vision model.
/// </summary>
public class OllamaTaxonomyEmbeddingIndexTests
{
    private static bool LiveModelTestsEnabled => Environment.GetEnvironmentVariable("PICTTAG_RUN_LIVE_MODEL_TESTS") == "1";

    [Theory]
    [Trait("Category", "LiveModel")]
    // "notebook pc" lands on "notebook" (lemma "notebook computer"), not "laptop" - a distinct
    // sibling WordNet synset, not a synonym - so "computer" is the safe shared-ancestor
    // assertion rather than over-specifying which sibling leaf the embedding model picks.
    [InlineData("notebook pc", "computer")]
    [InlineData("wooden boat", "boat")]
    public async Task FindNearestAsync_RealParaphrase_ResolvesToASensibleNode(string freeText, string expectedAncestorSubstring)
    {
        Assert.SkipUnless(LiveModelTestsEnabled, "set PICTTAG_RUN_LIVE_MODEL_TESTS=1 to run against a real Ollama server");

        IEmbeddingGenerator<string, Embedding<float>> generator = new OllamaApiClient(new Uri("http://localhost:11434"), "nomic-embed-text");
        OllamaTaxonomyEmbeddingIndex index = new(WordNetTaxonomyProvider.Shared.Value, generator);

        TaxonomyMatch? match = await index.FindNearestAsync(freeText, categoryHint: null, TestContext.Current.CancellationToken);

        Assert.NotNull(match);
        Assert.Equal(TaxonomyMatchQuality.Semantic, match.Quality);
        Assert.Contains(match.Ancestors, a => a.Name.Contains(expectedAncestorSubstring, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "LiveModel")]
    public async Task FindNearestAsync_NonsenseWord_ReturnsNullBelowSimilarityThreshold()
    {
        Assert.SkipUnless(LiveModelTestsEnabled, "set PICTTAG_RUN_LIVE_MODEL_TESTS=1 to run against a real Ollama server");

        IEmbeddingGenerator<string, Embedding<float>> generator = new OllamaApiClient(new Uri("http://localhost:11434"), "nomic-embed-text");
        OllamaTaxonomyEmbeddingIndex index = new(WordNetTaxonomyProvider.Shared.Value, generator);

        // "macbook" was empirically observed (during design validation) to score only ~0.60
        // against everything, briefly confusable with "macaque" - well below the 0.75 default
        // threshold. This is exactly the case the threshold exists to catch.
        TaxonomyMatch? match = await index.FindNearestAsync("macbook", categoryHint: null, TestContext.Current.CancellationToken);

        Assert.Null(match);
    }
}
