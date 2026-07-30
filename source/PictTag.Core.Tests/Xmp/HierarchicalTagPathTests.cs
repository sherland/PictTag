using PictTag.Core.Xmp;

namespace PictTag.Core.Tests.Xmp;

public class HierarchicalTagPathTests
{
    [Fact]
    public void TitleCase_LowercasePhrase_CapitalizesEachWord()
    {
        Assert.Equal("Religious Figure", HierarchicalTagPath.TitleCase("religious figure"));
    }

    [Fact]
    public void BuildSegments_AllDistinct_ReturnsEveryLevel()
    {
        // The real case reported in digiKam: an "angel" entity that is a kind of "religious
        // figure" should produce a general-before-specific 3-level path, not a single
        // slash-joined leaf.
        string[] segments = HierarchicalTagPath.BuildSegments(["Art", "Religious Figure", "Angel"]);

        Assert.Equal(["Art", "Religious Figure", "Angel"], segments);
    }

    [Fact]
    public void BuildSegments_LastTwoEqual_CollapsesThoseTwoIntoOne()
    {
        // BuildSegments compares already-prepared segments (title-casing is the caller's
        // responsibility, e.g. BuildEntitySegments) - differing only by case still collapses,
        // since the model repeating a label as its group shouldn't produce a redundant "X > X"
        // tag regardless of casing differences between the two raw strings.
        string[] segments = HierarchicalTagPath.BuildSegments(["Objects", "chimney", "Chimney"]);

        Assert.Equal(["Objects", "chimney"], segments);
    }

    [Fact]
    public void BuildSegments_SevenLevelChain_KeepsEveryDistinctLevel()
    {
        // The user's own cited WordNet example: n02099601's real ancestor chain (verified
        // against the actual committed data/wordnet/raw/data.noun).
        string[] segments = HierarchicalTagPath.BuildSegments(
            ["Animals", "Chordate", "Vertebrate", "Mammal", "Carnivore", "Canine", "Dog", "Retriever", "Golden Retriever"]);

        Assert.Equal(
            ["Animals", "Chordate", "Vertebrate", "Mammal", "Carnivore", "Canine", "Dog", "Retriever", "Golden Retriever"],
            segments);
    }

    [Fact]
    public void BuildSegments_DuplicateInTheMiddleOfALongerChain_CollapsesOnlyThatAdjacentPair()
    {
        // Generalizes the group==label collapse rule across a chain of any length - a resolved
        // taxonomy chain could just as easily repeat a name mid-chain as at the very end.
        string[] segments = HierarchicalTagPath.BuildSegments(["Animals", "Bird", "Bird", "Crane"]);

        Assert.Equal(["Animals", "Bird", "Crane"], segments);
    }

    [Fact]
    public void BuildSegments_SingleSegment_ReturnsItUnchanged()
    {
        string[] segments = HierarchicalTagPath.BuildSegments(["Other"]);

        Assert.Equal(["Other"], segments);
    }

    [Fact]
    public void BuildEntitySegments_ResolvedTaxonomy_UsesTheAncestorChainNotTheRawFields()
    {
        RawDetection raw = new("golden retriever", "dog", EntityCategory.Animals);
        TaxonomyMatch taxonomy = new(
            [new TaxonomyNode("n1", "animal"), new TaxonomyNode("n2", "dog"), new TaxonomyNode("n3", "golden retriever")],
            TaxonomyMatchQuality.Exact, 1.0, "golden retriever");
        DetectedEntity entity = new(raw, taxonomy, new BoundingBox(0, 0, 100, 100));

        string[] segments = HierarchicalTagPath.BuildEntitySegments(entity);

        Assert.Equal(["PictTag", "Animal", "Dog", "Golden Retriever"], segments);
    }

    [Fact]
    public void BuildEntitySegments_UnresolvedTaxonomy_FallsBackToRawCategoryGroupLabel()
    {
        // This is the entity's only fallback shape (not a legacy-compatibility path) - it must
        // keep working exactly as before for whatever fraction of detections the resolver can't
        // confidently place.
        RawDetection raw = new("chimney", "chimney", EntityCategory.Buildings);
        DetectedEntity entity = new(raw, Taxonomy: null, new BoundingBox(0, 0, 100, 100));

        string[] segments = HierarchicalTagPath.BuildEntitySegments(entity);

        Assert.Equal(["PictTag", "Buildings", "Chimney"], segments); // group==label collapses, same as always
    }

    [Fact]
    public void BuildEntitySegments_UnresolvedTaxonomyWithDistinctGroup_KeepsAllThreeRawLevels()
    {
        RawDetection raw = new("angel", "religious figure", EntityCategory.Art);
        DetectedEntity entity = new(raw, Taxonomy: null, new BoundingBox(0, 0, 100, 100));

        string[] segments = HierarchicalTagPath.BuildEntitySegments(entity);

        Assert.Equal(["PictTag", "Art", "Religious Figure", "Angel"], segments);
    }

    [Fact]
    public void Compose_LabelContainsSlash_SlashReplacedSoDigiKamDoesNotSplitTooDeep()
    {
        // Real case observed in digiKam: a model-generated label like "angel/religious figure"
        // written verbatim into TagsList's "Art/angel/religious figure" made digiKam split it
        // into three levels (Art > angel > religious figure) instead of one leaf under Art.
        string composed = HierarchicalTagPath.Compose('/', ["Art", "angel/religious figure"]);

        Assert.Equal(1, composed.Count(c => c == '/'));
        Assert.Equal("Art/angel-religious figure", composed);
    }

    [Fact]
    public void Compose_LabelContainsPipe_PipeReplacedSoLightroomDoesNotSplitTooDeep()
    {
        string composed = HierarchicalTagPath.Compose('|', ["Art", "before|after"]);

        Assert.Equal(1, composed.Count(c => c == '|'));
        Assert.Equal("Art|before-after", composed);
    }

    [Fact]
    public void Compose_LabelContainsTheOtherProperitysSeparator_IsAlsoSanitized()
    {
        // A label safe for this call's separator can still break the *other* hierarchical
        // property, since both '|' and '/' are composed from the same segments.
        string composedWithSlashSeparator = HierarchicalTagPath.Compose('/', ["Art", "before|after"]);
        string composedWithPipeSeparator = HierarchicalTagPath.Compose('|', ["Art", "angel/religious figure"]);

        Assert.Equal("Art/before-after", composedWithSlashSeparator);
        Assert.Equal("Art|angel-religious figure", composedWithPipeSeparator);
    }

    [Fact]
    public void Compose_ThreeSegments_JoinsAllOfThem()
    {
        string composed = HierarchicalTagPath.Compose('/', ["Art", "Religious Figure", "Angel"]);

        Assert.Equal("Art/Religious Figure/Angel", composed);
    }
}
