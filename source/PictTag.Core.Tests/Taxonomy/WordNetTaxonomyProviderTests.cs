using PictTag.Core.Taxonomy;

namespace PictTag.Core.Tests.Taxonomy;

/// <summary>
/// Smoke tests against the REAL embedded taxonomy.json (not a fake) for a handful of fixed,
/// known terms - deterministic and fast since these are exact-lemma lookups with fixed string
/// inputs, not live LLM output, so no live Ollama/PICTTAG_RUN_LIVE_MODEL_TESTS gate is needed.
/// </summary>
public class WordNetTaxonomyProviderTests
{
    private static readonly WordNetTaxonomyProvider Provider = WordNetTaxonomyProvider.Shared.Value;

    [Fact]
    public void TryExactMatch_GoldenRetriever_MatchesTheUsersOwnCitedExampleChain()
    {
        bool found = Provider.TryExactMatch("golden retriever", EntityCategory.Animals, out TaxonomyMatch match);

        Assert.True(found);
        Assert.Equal(TaxonomyMatchQuality.Exact, match.Quality);
        Assert.Equal("golden retriever", match.Ancestors[^1].Name);
        Assert.Equal("animal", match.Ancestors[0].Name);
        Assert.Contains("dog", match.Ancestors.Select(a => a.Name));
        Assert.Contains("retriever", match.Ancestors.Select(a => a.Name));
    }

    [Fact]
    public void TryExactMatch_Dog_ResolvesUnderTheAnimalsCategoryRoot()
    {
        bool found = Provider.TryExactMatch("dog", EntityCategory.Animals, out TaxonomyMatch match);

        Assert.True(found);
        Assert.Equal("animal", match.Ancestors[0].Name);
        Assert.Equal("dog", match.Ancestors[^1].Name);
    }

    [Fact]
    public void TryExactMatch_CraneWithAnimalsHint_ResolvesToTheBirdSense()
    {
        bool found = Provider.TryExactMatch("crane", EntityCategory.Animals, out TaxonomyMatch match);

        Assert.True(found);
        Assert.Equal(EntityCategory.Animals, Provider.GetCategory(match.Ancestors[^1].SynsetId));
    }

    [Fact]
    public void TryExactMatch_CraneWithObjectsHint_ResolvesToTheMachineSense()
    {
        bool found = Provider.TryExactMatch("crane", EntityCategory.Objects, out TaxonomyMatch match);

        Assert.True(found);
        Assert.Equal(EntityCategory.Objects, Provider.GetCategory(match.Ancestors[^1].SynsetId));
    }

    [Fact]
    public void TryExactMatch_Kayak_IsPresentViaTheBoatDomainExpansion()
    {
        // Confirms the depth-3 boat expansion (bumped from 2 specifically because kayak sits 3
        // levels below "boat") actually landed kayak as a real node, not just an embedding match.
        bool found = Provider.TryExactMatch("kayak", EntityCategory.Vehicles, out TaxonomyMatch match);

        Assert.True(found);
        Assert.Equal("kayak", match.Ancestors[^1].Name);
        Assert.Contains("boat", match.Ancestors.Select(a => a.Name));
    }

    [Fact]
    public void TryExactMatch_UnknownWord_ReturnsFalse()
    {
        bool found = Provider.TryExactMatch("flumph", EntityCategory.Other, out _);

        Assert.False(found);
    }

    [Fact]
    public void GetAncestorChain_UnknownSynsetId_ReturnsEmpty()
    {
        Assert.Empty(Provider.GetAncestorChain("n99999999"));
    }
}
