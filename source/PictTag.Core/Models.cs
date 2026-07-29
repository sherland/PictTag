namespace PictTag.Core;

public record BoundingBox(int YMin, int XMin, int YMax, int XMax);

public record DetectedEntity(string Label, EntityCategory Category, BoundingBox Box);

public record ImageMetadata(string Title, string Description, string AltText, ImageMedium Medium, string? ArtStyle, ImageSetting? Setting, List<SceneType> Scene, ImageComposition Composition);

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

/// <summary>Mirrors the IPTC Scene-NewsCodes controlled vocabulary (https://cv.iptc.org/newscodes/scene).</summary>
public enum SceneType
{
    Headshot,
    HalfLength,
    FullLength,
    Profile,
    RearView,
    Single,
    Couple,
    Two,
    Group,
    GeneralView,
    PanoramicView,
    AerialView,
    UnderWater,
    NightScene,
    Satellite,
    ExteriorView,
    InteriorView,
    CloseUp,
    Action,
    Performing,
    Posing,
    Symbolic,
    OffBeat,
    MovieScene,
}
