namespace PictTag.Core.Tests.Xmp;

internal static class TestData
{
    public static ImageMetadata SampleMetadata(
        ImageMedium medium = ImageMedium.Photograph,
        string? artStyle = null,
        ImageSetting? setting = ImageSetting.Outdoor,
        List<SceneType>? scene = null) =>
        new("Test Title", "Test description.", "Test alt text.", medium, artStyle, setting, scene ?? [SceneType.GeneralView], SampleComposition());

    public static ImageComposition SampleComposition() =>
        new(CompositionSymmetry.Asymmetrical, RuleOfThirdsAdherence: true, ColorVarianceEstimate: 0.5, EdgeDensityEstimate: 0.4, Notes: "Test composition note.");
}
