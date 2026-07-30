using PictTag.Core.Orientation;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace PictTag.Core.Tests.Orientation;

public class ExifOrientationMathTests
{
    [Theory]
    [InlineData(ExifOrientationMode.TopLeft, OrientationClass.Correct, ExifOrientationMode.TopLeft)]
    [InlineData(ExifOrientationMode.TopLeft, OrientationClass.Rotate90Cw, ExifOrientationMode.RightTop)]
    [InlineData(ExifOrientationMode.TopLeft, OrientationClass.Rotate180, ExifOrientationMode.BottomRight)]
    [InlineData(ExifOrientationMode.TopLeft, OrientationClass.Rotate90Ccw, ExifOrientationMode.LeftBottom)]
    public void TryComposeCorrectedOrientation_TagAlreadyNormal_ComposesDirectly(ushort current, OrientationClass correction, ushort expected)
    {
        Assert.Equal(expected, ExifOrientationMath.TryComposeCorrectedOrientation(current, correction));
    }

    [Fact]
    public void TryComposeCorrectedOrientation_TagSaidRotate90CwButShouldHaveBeenNormal_UndoesToNormal()
    {
        // The concrete real-world case this whole feature exists for: EXIF claims "Rotate 90 CW"
        // is needed, AutoOrient applies that, and the classifier says the *result* still needs
        // 90 CCW more (i.e. the 90 CW correction was wrong and should be undone entirely) - the
        // net effect on the raw pixel data should be "no rotation needed", not a partial fix.
        ushort? result = ExifOrientationMath.TryComposeCorrectedOrientation(ExifOrientationMode.RightTop, OrientationClass.Rotate90Ccw);

        Assert.Equal(ExifOrientationMode.TopLeft, result);
    }

    [Fact]
    public void TryComposeCorrectedOrientation_TagSaidRotate90CwAndNeedsAnother90Cw_ComposesToRotate180()
    {
        ushort? result = ExifOrientationMath.TryComposeCorrectedOrientation(ExifOrientationMode.RightTop, OrientationClass.Rotate90Cw);

        Assert.Equal(ExifOrientationMode.BottomRight, result);
    }

    [Fact]
    public void TryComposeCorrectedOrientation_WrapsAroundPastFullCircle()
    {
        // 270 (LeftBottom) + 180 = 450 -> wraps to 90 (RightTop).
        ushort? result = ExifOrientationMath.TryComposeCorrectedOrientation(ExifOrientationMode.LeftBottom, OrientationClass.Rotate180);

        Assert.Equal(ExifOrientationMode.RightTop, result);
    }

    [Theory]
    [InlineData(ExifOrientationMode.TopRight)]
    [InlineData(ExifOrientationMode.BottomLeft)]
    [InlineData(ExifOrientationMode.LeftTop)]
    [InlineData(ExifOrientationMode.RightBottom)]
    [InlineData(ExifOrientationMode.Unknown)]
    public void TryComposeCorrectedOrientation_MirroredOrUnknownTag_ReturnsNull(ushort current)
    {
        // Mirrored variants (and a missing/unknown tag) aren't safe to compose a rotation on top
        // of generically - callers should skip the original-file fix rather than guess.
        Assert.Null(ExifOrientationMath.TryComposeCorrectedOrientation(current, OrientationClass.Rotate90Cw));
    }
}
