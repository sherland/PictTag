using PictTag.TaxonomyBuilder.Seeding;
using PictTag.TaxonomyBuilder.Taxonomy;
using PictTag.TaxonomyBuilder.WordNet;

namespace PictTag.TaxonomyBuilder.Tests.Taxonomy;

public class TaxonomyGraphBuilderTests
{
    // A tiny hand-built WordNet fragment loosely modeled on the real golden retriever chain
    // (entity -> ... -> animal -> ... -> canine -> dog -> ... -> retriever -> golden retriever),
    // compressed down to just enough levels to exercise the algorithm - not the real 82k-synset
    // database (see WordNetParserTests for real-data coverage).
    private static WordNetDatabase BuildFixtureDatabase(params WordNetSynset[] extra)
    {
        List<WordNetSynset> synsets =
        [
            new("n00001", ["entity"], "root of everything", [], ["n00002", "n00010"]),
            new("n00002", ["animal"], "the category anchor for Animals", ["n00001"], ["n00003"]),
            new("n00003", ["canine"], "dogs and relatives", ["n00002"], ["n00004"]),
            new("n00004", ["dog", "domestic dog"], "a dog", ["n00003"], ["n00005"]),
            new("n00005", ["retriever"], "a retriever", ["n00004"], ["n00006"]),
            new("n00006", ["golden retriever"], "the user's own example", ["n00005"], []),
            new("n00010", ["artifact"], "the category anchor for Objects", ["n00001"], ["n00011"]),
            new("n00011", ["boat"], "a boat, domain-expansion anchor", ["n00010"], ["n00012", "n00013"]),
            new("n00012", ["canoe"], "a canoe", ["n00011"], ["n00014"]),
            new("n00013", ["sailboat"], "a sailboat", ["n00011"], []),
            new("n00014", ["kayak"], "a kayak - two hyponym levels below the boat anchor", ["n00012"], []),
        ];
        synsets.AddRange(extra);
        return new WordNetDatabase(synsets.ToDictionary(s => s.SynsetId), new Dictionary<string, IReadOnlyList<string>>());
    }

    private static TrimConfig FixtureTrimConfig(
        IReadOnlyList<string>? excludeSynsets = null, IReadOnlyList<string>? excludeSubtrees = null, bool stripLatinLemmas = true) => new(
        RootCollapseAnchors: new Dictionary<string, IReadOnlyList<string>>
        {
            ["Animals"] = ["n00002"],
            ["Objects"] = ["n00010"],
        },
        ExcludeSynsets: excludeSynsets ?? [],
        ExcludeSubtrees: excludeSubtrees ?? [],
        StripLatinLemmas: stripLatinLemmas);

    private static Dictionary<string, TaxonomyBuildNode> BuildById(IReadOnlyList<TaxonomyBuildNode> nodes) =>
        nodes.ToDictionary(n => n.Id);

    [Fact]
    public void Build_UpwardWalkFromLeafSeed_StopsAtConfiguredCategoryAnchorNotTrueRoot()
    {
        WordNetDatabase db = BuildFixtureDatabase();
        List<ImageNetLeaf> leaves = [new("n00006", "golden retriever")];

        IReadOnlyList<TaxonomyBuildNode> nodes = TaxonomyGraphBuilder.Build(db, leaves, [], [], FixtureTrimConfig());
        Dictionary<string, TaxonomyBuildNode> byId = BuildById(nodes);

        // entity.n.01 (n00001) is above the Animals anchor and must never appear - this is the
        // whole point of rootCollapseAnchors dropping WordNet's abstract upper ontology.
        Assert.DoesNotContain("n00001", byId.Keys);

        Assert.Equal(["n00002", "n00003", "n00004", "n00005", "n00006"], byId.Keys.OrderBy(k => k));
        Assert.Null(byId["n00002"].PrimaryParentId);
        Assert.Equal("n00002", Walk(byId, "n00006"));
    }

    private static string Walk(Dictionary<string, TaxonomyBuildNode> byId, string leafId)
    {
        string id = leafId;
        while (byId[id].PrimaryParentId is { } parent)
        {
            id = parent;
        }

        return id;
    }

    [Fact]
    public void Build_UsersOwnGoldenRetrieverExample_ProducesTheExactCitedChain()
    {
        WordNetDatabase db = BuildFixtureDatabase();
        List<ImageNetLeaf> leaves = [new("n00006", "golden retriever")];

        IReadOnlyList<TaxonomyBuildNode> nodes = TaxonomyGraphBuilder.Build(db, leaves, [], [], FixtureTrimConfig());
        Dictionary<string, TaxonomyBuildNode> byId = BuildById(nodes);

        List<string> chain = [];
        string? id = "n00006";
        while (id is not null)
        {
            TaxonomyBuildNode node = byId[id];
            chain.Add(node.Name);
            id = node.PrimaryParentId;
        }

        chain.Reverse();
        Assert.Equal(["animal", "canine", "dog", "retriever", "golden retriever"], chain);
    }

    [Fact]
    public void Build_SynsetWithTwoHypernyms_UsesOnlyTheFirstListedOne()
    {
        // "dog" gets a second hypernym here ("domestic_animal", listed second) - mirrors the
        // real WordNet DAG (dog really does have two hypernyms: canine and domestic_animal).
        // Only the first-listed one should ever be followed. Built manually (not via
        // BuildFixtureDatabase's `extra` param) since this replaces rather than adds to n00004.
        Dictionary<string, WordNetSynset> byId = new()
        {
            ["n00001"] = new("n00001", ["entity"], "root", [], ["n00002"]),
            ["n00002"] = new("n00002", ["animal"], "anchor", ["n00001"], ["n00003"]),
            ["n00003"] = new("n00003", ["canine"], "canine", ["n00002"], ["n00004"]),
            ["n00004"] = new("n00004", ["dog"], "a dog with two hypernyms", ["n00003", "n00099"], ["n00005"]),
            ["n00099"] = new("n00099", ["domestic animal"], "second hypernym, must be ignored", ["n00002"], []),
            ["n00005"] = new("n00005", ["retriever"], "a retriever", ["n00004"], []),
        };
        WordNetDatabase db = new(byId, new Dictionary<string, IReadOnlyList<string>>());

        IReadOnlyList<TaxonomyBuildNode> nodes = TaxonomyGraphBuilder.Build(
            db, [new ImageNetLeaf("n00005", "retriever")], [], [], FixtureTrimConfig());
        Dictionary<string, TaxonomyBuildNode> nodesById = BuildById(nodes);

        Assert.Equal("n00003", nodesById["n00004"].PrimaryParentId);
        Assert.DoesNotContain("n00099", nodesById.Keys);
    }

    [Fact]
    public void Build_ExcludedSynset_IsSplicedOutAndChainReconnectsAboveIt()
    {
        WordNetDatabase db = BuildFixtureDatabase();
        List<ImageNetLeaf> leaves = [new("n00006", "golden retriever")];

        // Exclude "hunting dog"-equivalent n00004 ("dog") itself to prove splicing: retriever's
        // chain should skip straight from retriever to canine, not break or dangle.
        TrimConfig trimConfig = FixtureTrimConfig(excludeSynsets: ["n00004"]);
        IReadOnlyList<TaxonomyBuildNode> nodes = TaxonomyGraphBuilder.Build(db, leaves, [], [], trimConfig);
        Dictionary<string, TaxonomyBuildNode> byId = BuildById(nodes);

        Assert.DoesNotContain("n00004", byId.Keys);
        Assert.Equal("n00003", byId["n00005"].PrimaryParentId);
    }

    [Fact]
    public void Build_DomainExpansion_SweepsInHyponymsUpToConfiguredDepthOnly()
    {
        WordNetDatabase db = BuildFixtureDatabase();
        List<DomainExpansion> expansions = [new("n00011", "boat", Depth: 2, Comment: "test")];

        IReadOnlyList<TaxonomyBuildNode> nodes = TaxonomyGraphBuilder.Build(db, [], [], expansions, FixtureTrimConfig());
        Dictionary<string, TaxonomyBuildNode> byId = BuildById(nodes);

        // boat (depth 0), canoe+sailboat (depth 1), kayak sits at depth 2 below canoe (depth 2
        // overall from boat) so it's still within a depth-2 expansion... use depth 1 instead to
        // prove the cutoff excludes canoe's own children.
        Assert.Contains("n00011", byId.Keys);
        Assert.Contains("n00012", byId.Keys);
        Assert.Contains("n00013", byId.Keys);
        Assert.Contains("n00014", byId.Keys); // kayak: boat->canoe->kayak is exactly depth 2
    }

    [Fact]
    public void Build_DomainExpansionDepthOne_DoesNotReachTwoLevelsDown()
    {
        WordNetDatabase db = BuildFixtureDatabase();
        List<DomainExpansion> expansions = [new("n00011", "boat", Depth: 1, Comment: "test")];

        IReadOnlyList<TaxonomyBuildNode> nodes = TaxonomyGraphBuilder.Build(db, [], [], expansions, FixtureTrimConfig());
        Dictionary<string, TaxonomyBuildNode> byId = BuildById(nodes);

        Assert.Contains("n00012", byId.Keys); // canoe: depth 1 below boat
        Assert.DoesNotContain("n00014", byId.Keys); // kayak: depth 2 below boat - out of range
    }

    [Fact]
    public void Build_LatinBinomialLemma_IsStrippedButCommonNameSurvives()
    {
        Dictionary<string, WordNetSynset> byId = new()
        {
            ["n00001"] = new("n00001", ["entity"], "root", [], ["n00002"]),
            ["n00002"] = new("n00002", ["animal"], "anchor", ["n00001"], ["n00050"]),
            ["n00050"] = new("n00050", ["dog", "domestic dog", "Canis familiaris"], "has a Latin binomial lemma", ["n00002"], []),
        };
        WordNetDatabase db = new(byId, new Dictionary<string, IReadOnlyList<string>>());

        IReadOnlyList<TaxonomyBuildNode> nodes = TaxonomyGraphBuilder.Build(
            db, [new ImageNetLeaf("n00050", "dog")], [], [], FixtureTrimConfig(stripLatinLemmas: true));
        TaxonomyBuildNode node = nodes.Single(n => n.Id == "n00050");

        Assert.DoesNotContain("Canis familiaris", node.Lemmas);
        Assert.Contains("dog", node.Lemmas);
        Assert.Contains("domestic dog", node.Lemmas);
    }

    [Fact]
    public void Build_StripLatinLemmasDisabled_KeepsBinomialLemma()
    {
        Dictionary<string, WordNetSynset> byId = new()
        {
            ["n00001"] = new("n00001", ["entity"], "root", [], ["n00002"]),
            ["n00002"] = new("n00002", ["animal"], "anchor", ["n00001"], ["n00050"]),
            ["n00050"] = new("n00050", ["dog", "Canis familiaris"], "has a Latin binomial lemma", ["n00002"], []),
        };
        WordNetDatabase db = new(byId, new Dictionary<string, IReadOnlyList<string>>());

        IReadOnlyList<TaxonomyBuildNode> nodes = TaxonomyGraphBuilder.Build(
            db, [new ImageNetLeaf("n00050", "dog")], [], [], FixtureTrimConfig(stripLatinLemmas: false));
        TaxonomyBuildNode node = nodes.Single(n => n.Id == "n00050");

        Assert.Contains("Canis familiaris", node.Lemmas);
    }

    [Fact]
    public void Build_AllLemmasLookLikeBinomials_KeepsOriginalListRatherThanEmptying()
    {
        Dictionary<string, WordNetSynset> byId = new()
        {
            ["n00001"] = new("n00001", ["entity"], "root", [], ["n00002"]),
            ["n00002"] = new("n00002", ["animal"], "anchor", ["n00001"], ["n00050"]),
            ["n00050"] = new("n00050", ["Canis familiaris"], "only has a Latin binomial lemma", ["n00002"], []),
        };
        WordNetDatabase db = new(byId, new Dictionary<string, IReadOnlyList<string>>());

        IReadOnlyList<TaxonomyBuildNode> nodes = TaxonomyGraphBuilder.Build(
            db, [new ImageNetLeaf("n00050", "misc")], [], [], FixtureTrimConfig(stripLatinLemmas: true));
        TaxonomyBuildNode node = nodes.Single(n => n.Id == "n00050");

        Assert.Equal(["Canis familiaris"], node.Lemmas);
    }

    [Fact]
    public void Build_ManualSeed_IsIncludedEvenWithoutAnImageNetLeaf()
    {
        WordNetDatabase db = BuildFixtureDatabase();
        List<ManualSeed> manualSeeds = [new("golden retriever", "n00006", "test seed")];

        IReadOnlyList<TaxonomyBuildNode> nodes = TaxonomyGraphBuilder.Build(db, [], manualSeeds, [], FixtureTrimConfig());

        Assert.Contains(nodes, n => n.Id == "n00006");
    }

    [Fact]
    public void Build_SharedAncestor_IsNotDuplicatedAcrossTwoSeeds()
    {
        WordNetDatabase db = BuildFixtureDatabase();
        List<ImageNetLeaf> leaves = [new("n00006", "golden retriever"), new("n00005", "retriever")];

        IReadOnlyList<TaxonomyBuildNode> nodes = TaxonomyGraphBuilder.Build(db, leaves, [], [], FixtureTrimConfig());

        Assert.Single(nodes, n => n.Id == "n00004"); // "dog" is an ancestor of both seeds, walked once
    }
}
