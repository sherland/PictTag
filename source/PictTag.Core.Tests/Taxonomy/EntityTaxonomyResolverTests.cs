using PictTag.Core.Taxonomy;

namespace PictTag.Core.Tests.Taxonomy;

public class EntityTaxonomyResolverTests
{
    [Fact]
    public async Task ResolveAsync_ExactLabelMatch_ShortCircuitsBeforeTheEmbeddingTier()
    {
        FakeTaxonomyProvider provider = new FakeTaxonomyProvider()
            .AddNode("n1", "animal", null, EntityCategory.Animals)
            .AddNode("n2", "dog", "n1", EntityCategory.Animals);
        FakeTaxonomyEmbeddingIndex embeddingIndex = new(new Dictionary<string, TaxonomyMatch?>());
        EntityTaxonomyResolver resolver = new(provider, embeddingIndex);

        RawDetection raw = new("dog", "animal", EntityCategory.Animals);
        TaxonomyMatch? match = await resolver.ResolveAsync(raw, TestContext.Current.CancellationToken);

        Assert.NotNull(match);
        Assert.Equal(TaxonomyMatchQuality.Exact, match.Quality);
        Assert.Equal(["animal", "dog"], match.Ancestors.Select(a => a.Name));
        Assert.Empty(embeddingIndex.Queries); // exact match found - embedding tier must never be invoked
    }

    [Fact]
    public async Task ResolveAsync_LabelDoesNotMatchButGroupDoes_FallsBackToGroup()
    {
        FakeTaxonomyProvider provider = new FakeTaxonomyProvider()
            .AddNode("n1", "animal", null, EntityCategory.Animals)
            .AddNode("n2", "dog", "n1", EntityCategory.Animals);
        FakeTaxonomyEmbeddingIndex embeddingIndex = new(new Dictionary<string, TaxonomyMatch?>());
        EntityTaxonomyResolver resolver = new(provider, embeddingIndex);

        // "doggo" (label) doesn't literally match any lemma, but "dog" (group) does.
        RawDetection raw = new("doggo", "dog", EntityCategory.Animals);
        TaxonomyMatch? match = await resolver.ResolveAsync(raw, TestContext.Current.CancellationToken);

        Assert.NotNull(match);
        Assert.Equal(TaxonomyMatchQuality.Exact, match.Quality);
        Assert.Equal("dog", match.Ancestors[^1].Name);
        Assert.Empty(embeddingIndex.Queries);
    }

    [Fact]
    public async Task ResolveAsync_HomonymWithCategoryHint_ResolvesToTheMatchingSenseNotJustTheFirstOne()
    {
        // The concrete proof categoryHint disambiguation earns its keep: "crane" the bird vs.
        // "crane" the lifting device - both share the literal lemma "crane".
        FakeTaxonomyProvider provider = new FakeTaxonomyProvider()
            .AddNode("bird-root", "animal", null, EntityCategory.Animals)
            .AddNode("bird-crane", "crane", "bird-root", EntityCategory.Animals)
            .AddNode("object-root", "artifact", null, EntityCategory.Objects)
            .AddNode("machine-crane", "crane", "object-root", EntityCategory.Objects);
        FakeTaxonomyEmbeddingIndex embeddingIndex = new(new Dictionary<string, TaxonomyMatch?>());
        EntityTaxonomyResolver resolver = new(provider, embeddingIndex);

        TaxonomyMatch? animalMatch = await resolver.ResolveAsync(new RawDetection("crane", "bird", EntityCategory.Animals), TestContext.Current.CancellationToken);
        TaxonomyMatch? objectMatch = await resolver.ResolveAsync(new RawDetection("crane", "machine", EntityCategory.Objects), TestContext.Current.CancellationToken);

        Assert.Equal(["animal", "crane"], animalMatch!.Ancestors.Select(a => a.Name));
        Assert.Equal(["artifact", "crane"], objectMatch!.Ancestors.Select(a => a.Name));
    }

    [Fact]
    public async Task ResolveAsync_NoExactMatch_FallsThroughToEmbeddingTier()
    {
        FakeTaxonomyProvider provider = new FakeTaxonomyProvider()
            .AddNode("n1", "artifact", null, EntityCategory.Objects)
            .AddNode("n2", "laptop", "n1", EntityCategory.Objects);
        TaxonomyMatch semanticMatch = new(
            [new TaxonomyNode("n1", "artifact"), new TaxonomyNode("n2", "laptop")],
            TaxonomyMatchQuality.Semantic, 0.81, "laptop");
        FakeTaxonomyEmbeddingIndex embeddingIndex = new(new Dictionary<string, TaxonomyMatch?>
        {
            ["notebook pc"] = semanticMatch,
        });
        EntityTaxonomyResolver resolver = new(provider, embeddingIndex);

        RawDetection raw = new("notebook pc", "computer", EntityCategory.Objects);
        TaxonomyMatch? match = await resolver.ResolveAsync(raw, TestContext.Current.CancellationToken);

        Assert.NotNull(match);
        Assert.Equal(TaxonomyMatchQuality.Semantic, match.Quality);
        Assert.Equal("laptop", match.Ancestors[^1].Name);
        Assert.Equal(["notebook pc"], embeddingIndex.Queries); // embedding tier queried with the raw label
    }

    [Fact]
    public async Task ResolveAsync_LabelSemanticMissesButGroupSemanticHits_FallsBackToGroup()
    {
        // Found empirically: Label "treetops" (too specific/compound to embed close to
        // anything) alongside Group "trees" (which embeds well above threshold against "tree")
        // must still resolve - mirrors the exact-match tier's Label-then-Group fallback.
        FakeTaxonomyProvider provider = new();
        TaxonomyMatch groupMatch = new(
            [new TaxonomyNode("n1", "plant"), new TaxonomyNode("n2", "tree")], TaxonomyMatchQuality.Semantic, 0.70, "tree");
        FakeTaxonomyEmbeddingIndex embeddingIndex = new(new Dictionary<string, TaxonomyMatch?>
        {
            ["treetops"] = null, // below threshold
            ["trees"] = groupMatch,
        });
        EntityTaxonomyResolver resolver = new(provider, embeddingIndex);

        RawDetection raw = new("treetops", "trees", EntityCategory.Nature);
        TaxonomyMatch? match = await resolver.ResolveAsync(raw, TestContext.Current.CancellationToken);

        Assert.NotNull(match);
        Assert.Equal("tree", match.Ancestors[^1].Name);
        Assert.Equal(["treetops", "trees"], embeddingIndex.Queries);
    }

    [Fact]
    public async Task ResolveAsync_LabelEqualsGroupAndSemanticMisses_DoesNotQueryEmbeddingIndexTwice()
    {
        FakeTaxonomyProvider provider = new();
        FakeTaxonomyEmbeddingIndex embeddingIndex = new(new Dictionary<string, TaxonomyMatch?>());
        EntityTaxonomyResolver resolver = new(provider, embeddingIndex);

        RawDetection raw = new("flumph", "flumph", EntityCategory.Objects);
        TaxonomyMatch? match = await resolver.ResolveAsync(raw, TestContext.Current.CancellationToken);

        Assert.Null(match);
        Assert.Equal(["flumph"], embeddingIndex.Queries); // no redundant second call when Label == Group
    }

    [Fact]
    public async Task ResolveAsync_NeitherTierMatches_ReturnsNull()
    {
        FakeTaxonomyProvider provider = new();
        FakeTaxonomyEmbeddingIndex embeddingIndex = new(new Dictionary<string, TaxonomyMatch?>());
        EntityTaxonomyResolver resolver = new(provider, embeddingIndex);

        RawDetection raw = new("flumph", "toy", EntityCategory.Objects);
        TaxonomyMatch? match = await resolver.ResolveAsync(raw, TestContext.Current.CancellationToken);

        Assert.Null(match);
    }
}
