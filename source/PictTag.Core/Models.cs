namespace PictTag.Core;

public record BoundingBox(int YMin, int XMin, int YMax, int XMax);

public record DetectedEntity(RawDetection Raw, TaxonomyMatch? Taxonomy, BoundingBox Box);

/// <summary>What the model literally returned for one detection, unresolved - preserved verbatim.</summary>
public record RawDetection(string Label, string Group, EntityCategory Category);

/// <summary>One node in a resolved WordNet ancestor chain (see <see cref="PictTag.Core.Taxonomy.ITaxonomyProvider"/>).</summary>
public record TaxonomyNode(string SynsetId, string Name);

public enum TaxonomyMatchQuality
{
    /// <summary>The raw label/group matched a WordNet lemma directly (normalized, case-insensitive).</summary>
    Exact,

    /// <summary>No lemma matched; resolved via embedding nearest-neighbor similarity instead.</summary>
    Semantic,

    /// <summary>Nothing matched with sufficient confidence.</summary>
    Unresolved,
}

/// <summary>
/// The result of resolving a <see cref="RawDetection"/> against the WordNet-derived taxonomy.
/// <see cref="Ancestors"/> is always root-to-leaf and never empty for a non-null match.
/// </summary>
public record TaxonomyMatch(
    IReadOnlyList<TaxonomyNode> Ancestors,
    TaxonomyMatchQuality Quality,
    double Confidence,
    string MatchedLemma);

public record ImageMetadata(string Title, string Description, string AltText, ImageMedium Medium, string? ArtStyle, ImageSetting? Setting, List<SceneType> Scene, ImageComposition Composition);

public record ImageComposition(CompositionSymmetry Symmetry, bool RuleOfThirdsAdherence, double ColorVarianceEstimate, double EdgeDensityEstimate, string? Notes);

/// <summary>
/// <see cref="ImageWidth"/>/<see cref="ImageHeight"/> are the image's final, orientation-corrected
/// dimensions (see <see cref="PictTag.Core.Orientation.ImageOrientationCorrector"/>) - the
/// dimensions a viewer actually displays, not necessarily the raw encoded pixel dimensions (which
/// differ for any 90/270-degree-rotated photo). XMP writers use these directly rather than
/// re-deriving dimensions from the file themselves.
/// </summary>
public record ImageAnalysisResult(ImageMetadata Metadata, List<DetectedEntity> Entities, int ImageWidth, int ImageHeight);

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
