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
    public void BuildSegments_GroupDistinctFromLabel_Returns3LevelPath()
    {
        // The real case reported in digiKam: an "angel" entity that is a kind of "religious
        // figure" should produce a general-before-specific 3-level path, not a single
        // slash-joined leaf.
        string[] segments = HierarchicalTagPath.BuildSegments("Art", "religious figure", "angel");

        Assert.Equal(["Art", "Religious Figure", "Angel"], segments);
    }

    [Fact]
    public void BuildSegments_GroupSameAsLabel_CollapsesTo2LevelPath()
    {
        // The model is told to repeat the label when no more general group genuinely applies -
        // that must not produce a redundant "X > X" tag.
        string[] segments = HierarchicalTagPath.BuildSegments("Objects", "chimney", "Chimney");

        Assert.Equal(["Objects", "Chimney"], segments);
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
