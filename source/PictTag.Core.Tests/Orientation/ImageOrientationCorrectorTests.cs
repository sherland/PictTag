using PictTag.Core.Orientation;
using PictTag.Core.Xmp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace PictTag.Core.Tests.Orientation;

public class ImageOrientationCorrectorTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("picttag-orientation-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string CreateTestImage(int width, int height, ushort orientation)
    {
        string path = Path.Combine(_tempDir, $"test-{Guid.NewGuid():N}.jpg");
        using Image<Rgba32> image = new(width, height);
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, orientation);
        image.SaveAsJpeg(path);
        return path;
    }

    private static ushort ReadOrientationFromDisk(string path)
    {
        ImageInfo info = Image.Identify(path);
        return info.Metadata.ExifProfile is { } exif && exif.TryGetValue(ExifTag.Orientation, out IExifValue<ushort>? value)
            ? value.Value
            : ExifOrientationMode.TopLeft;
    }

    [Fact]
    public async Task CorrectAsync_ClassifierAgreesImageIsCorrect_DoesNotTouchOriginalFile()
    {
        string path = CreateTestImage(100, 50, ExifOrientationMode.TopLeft);
        FakeImageOrientationClassifier classifier = new(new OrientationPrediction(OrientationClass.Correct, 1.0));
        ImageOrientationCorrector corrector = new(classifier);

        OrientationCorrectionResult result = await corrector.CorrectAsync(path, fixOriginalFile: true, TestContext.Current.CancellationToken);

        Assert.False(result.OriginalFileFixed);
        Assert.Equal(100, result.Width);
        Assert.Equal(50, result.Height);
        result.Image.Dispose();
    }

    [Fact]
    public async Task CorrectAsync_ClassifierConfidentlyDisagrees_SwapsDimensionsForA90DegreeCorrection()
    {
        // 100x50 stored, tag already Normal (no baseline AutoOrient change) - classifier says an
        // additional 90 CW rotation is needed, so the final image should be 50x100.
        string path = CreateTestImage(100, 50, ExifOrientationMode.TopLeft);
        FakeImageOrientationClassifier classifier = new(new OrientationPrediction(OrientationClass.Rotate90Cw, 0.99));
        ImageOrientationCorrector corrector = new(classifier);

        OrientationCorrectionResult result = await corrector.CorrectAsync(path, fixOriginalFile: false, TestContext.Current.CancellationToken);

        Assert.Equal(50, result.Width);
        Assert.Equal(100, result.Height);
        result.Image.Dispose();
    }

    [Fact]
    public async Task CorrectAsync_ClassifierConfidentlyDisagreesAndFixEnabled_RewritesOriginalFileOrientation()
    {
        Assert.SkipUnless(ExifToolSidecarWriter.IsExifToolAvailable, "exiftool not found on PATH");

        string path = CreateTestImage(100, 50, ExifOrientationMode.TopLeft);
        FakeImageOrientationClassifier classifier = new(new OrientationPrediction(OrientationClass.Rotate90Cw, 0.99));
        ImageOrientationCorrector corrector = new(classifier);

        OrientationCorrectionResult result = await corrector.CorrectAsync(path, fixOriginalFile: true, TestContext.Current.CancellationToken);

        Assert.True(result.OriginalFileFixed);
        Assert.Equal(ExifOrientationMode.RightTop, ReadOrientationFromDisk(path)); // Normal + additional 90 CW -> RightTop
        result.Image.Dispose();
    }

    [Fact]
    public async Task CorrectAsync_ClassifierConfidentlyDisagreesButFixDisabled_LeavesOriginalFileUntouched()
    {
        Assert.SkipUnless(ExifToolSidecarWriter.IsExifToolAvailable, "exiftool not found on PATH");

        string path = CreateTestImage(100, 50, ExifOrientationMode.TopLeft);
        FakeImageOrientationClassifier classifier = new(new OrientationPrediction(OrientationClass.Rotate90Cw, 0.99));
        ImageOrientationCorrector corrector = new(classifier);

        OrientationCorrectionResult result = await corrector.CorrectAsync(path, fixOriginalFile: false, TestContext.Current.CancellationToken);

        Assert.False(result.OriginalFileFixed);
        Assert.Equal(ExifOrientationMode.TopLeft, ReadOrientationFromDisk(path)); // untouched
        result.Image.Dispose();
    }

    [Fact]
    public async Task CorrectAsync_ConfidenceBelowThreshold_TrustsAutoOrientAndNeverTouchesFile()
    {
        string path = CreateTestImage(100, 50, ExifOrientationMode.TopLeft);
        FakeImageOrientationClassifier classifier = new(new OrientationPrediction(OrientationClass.Rotate90Cw, 0.5)); // below default 0.98
        ImageOrientationCorrector corrector = new(classifier);

        OrientationCorrectionResult result = await corrector.CorrectAsync(path, fixOriginalFile: true, TestContext.Current.CancellationToken);

        Assert.False(result.OriginalFileFixed);
        Assert.Equal(100, result.Width); // no additional rotation applied - dimensions unchanged
        Assert.Equal(50, result.Height);
        result.Image.Dispose();
    }

    [Fact]
    public async Task CorrectAsync_MirroredOrientationTag_NeverTouchesFileEvenWhenConfident()
    {
        // A mirrored variant (TopRight) - ExifOrientationMath can't safely compose a rotation on
        // top of an unknown mirror, so the file fix must be skipped even at high confidence.
        string path = CreateTestImage(100, 50, ExifOrientationMode.TopRight);
        FakeImageOrientationClassifier classifier = new(new OrientationPrediction(OrientationClass.Rotate90Cw, 0.99));
        ImageOrientationCorrector corrector = new(classifier);

        OrientationCorrectionResult result = await corrector.CorrectAsync(path, fixOriginalFile: true, TestContext.Current.CancellationToken);

        Assert.False(result.OriginalFileFixed);
        result.Image.Dispose();
    }
}
