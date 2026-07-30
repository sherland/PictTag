namespace PictTag.Core.Orientation;

/// <summary>
/// The four rotation states an <see cref="IImageOrientationClassifier"/> distinguishes - matches
/// the reference model's own class order exactly (see docs/ORIENTATION.md): the *corrective*
/// rotation an image needs to become upright, not a raw sensor/EXIF value.
/// </summary>
public enum OrientationClass
{
    Correct,
    Rotate90Cw,
    Rotate180,
    Rotate90Ccw,
}

/// <summary>A classifier's verdict for one image: which correction it thinks is needed, and how confident it is.</summary>
public record OrientationPrediction(OrientationClass PredictedClass, double Confidence);

/// <summary>
/// The outcome of <see cref="ImageOrientationCorrector"/> running on one image: the final,
/// upright <see cref="Image"/> (and its dimensions) to use for everything downstream - detection,
/// the annotated preview, and XMP region math - plus whether the original file's own EXIF
/// <c>Orientation</c> tag was corrected in place.
/// </summary>
public sealed record OrientationCorrectionResult(
    SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> Image,
    int Width,
    int Height,
    bool OriginalFileFixed);
