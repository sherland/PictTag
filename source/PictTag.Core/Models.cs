namespace PictTag.Core;

public record BoundingBox(int YMin, int XMin, int YMax, int XMax);

public record DetectedEntity(string Label, EntityCategory Category, BoundingBox Box);

public record ImageMetadata(string Title, string Description, ImageMedium Medium, string? ArtStyle, ImageSetting? Setting, ImageComposition Composition);

public record ImageComposition(CompositionSymmetry Symmetry, bool RuleOfThirdsAdherence, double ColorVarianceEstimate, double EdgeDensityEstimate, string? Notes);

public record ImageAnalysisResult(ImageMetadata Metadata, List<DetectedEntity> Entities);

public enum EntityCategory
{
    People,
    Animals,
    Vehicles,
    Buildings,
    Nature,
    Food,
    Objects,
    Text,
    Art,
    Other,
}

public enum ImageMedium
{
    Photograph,
    Screenshot,
    Painting,
    Drawing,
    DigitalIllustration,
    ThreeDRender,
    Other,
}

public enum ImageSetting
{
    Indoor,
    Outdoor,
    Studio,
    Unknown,
}

public enum CompositionSymmetry
{
    Symmetrical,
    Asymmetrical,
    RadialSymmetry,
    None,
}
