using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace PictTag.Core.Orientation;

/// <summary>
/// Pure arithmetic composing "what the EXIF Orientation tag currently claims" with "what
/// additional correction the classifier found necessary after that was already applied via
/// AutoOrient" into the single Orientation value that correctly describes the original raw pixel
/// data - kept separate from <see cref="ImageOrientationCorrector"/> specifically so it's easy to
/// unit test with plain integers, no image/classifier fakes required.
/// </summary>
internal static class ExifOrientationMath
{
    /// <summary>
    /// Composes <paramref name="currentOrientation"/> with <paramref name="additionalCorrection"/>
    /// into the corrected Orientation value. Returns null if <paramref name="currentOrientation"/>
    /// is one of the 4 mirrored variants (2/4/5/7 - rare in practice) rather than a pure rotation,
    /// since composing a further rotation on top of an unknown mirror isn't safe to do generically -
    /// callers should skip the original-file fix (not attempt one) in that case.
    /// </summary>
    public static ushort? TryComposeCorrectedOrientation(ushort currentOrientation, OrientationClass additionalCorrection)
    {
        int? currentDegrees = DegreesForOrientation(currentOrientation);
        if (currentDegrees is null)
        {
            return null;
        }

        int totalDegrees = (currentDegrees.Value + DegreesForCorrection(additionalCorrection)) % 360;
        return OrientationForDegrees(totalDegrees);
    }

    // Degrees of clockwise rotation needed to correctly display the raw pixel data described by
    // this Orientation value - the 4 mirrored variants (TopRight/BottomLeft/LeftTop/RightBottom)
    // are deliberately not covered; see TryComposeCorrectedOrientation.
    private static int? DegreesForOrientation(ushort orientation) => orientation switch
    {
        ExifOrientationMode.TopLeft => 0,
        ExifOrientationMode.RightTop => 90,
        ExifOrientationMode.BottomRight => 180,
        ExifOrientationMode.LeftBottom => 270,
        _ => null,
    };

    private static ushort OrientationForDegrees(int degrees) => degrees switch
    {
        0 => ExifOrientationMode.TopLeft,
        90 => ExifOrientationMode.RightTop,
        180 => ExifOrientationMode.BottomRight,
        270 => ExifOrientationMode.LeftBottom,
        _ => throw new ArgumentOutOfRangeException(nameof(degrees), degrees, "Must be 0, 90, 180, or 270."),
    };

    // Additional clockwise rotation the classifier says is needed, on top of whatever AutoOrient
    // already applied using the current (possibly wrong) Orientation tag.
    private static int DegreesForCorrection(OrientationClass correction) => correction switch
    {
        OrientationClass.Correct => 0,
        OrientationClass.Rotate90Cw => 90,
        OrientationClass.Rotate180 => 180,
        OrientationClass.Rotate90Ccw => 270,
        _ => throw new ArgumentOutOfRangeException(nameof(correction)),
    };
}
