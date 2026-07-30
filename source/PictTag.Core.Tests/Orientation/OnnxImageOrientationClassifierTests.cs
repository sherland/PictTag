using PictTag.Core.Orientation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PictTag.Core.Tests.Orientation;

/// <summary>
/// Hits the real ONNX orientation classifier (auto-downloads the ~80MB model on first run if
/// missing), so it's opt-in only (set PICTTAG_RUN_LIVE_MODEL_TESTS=1) - same gating convention as
/// the other slow/network-dependent tests, even though this one doesn't need Ollama specifically.
/// </summary>
public class OnnxImageOrientationClassifierTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static bool LiveModelTestsEnabled => Environment.GetEnvironmentVariable("PICTTAG_RUN_LIVE_MODEL_TESTS") == "1";

    [Fact]
    public async Task ClassifyAsync_RealMisorientedPhoto_DetectsRemainingRotationAfterAutoOrient()
    {
        Assert.SkipUnless(LiveModelTestsEnabled, "set PICTTAG_RUN_LIVE_MODEL_TESTS=1 to run against the real ONNX model");

        // The real photo this whole fix started from: EXIF Orientation "Rotate 90 CW" but the
        // model was never applying it, so the image the AI saw (and boxes were drawn on) was
        // genuinely sideways. AutoOrient alone should now produce a correctly-oriented image, so
        // the classifier verifying it should agree it's already correct - this tests the "EXIF
        // was actually right" path.
        string photoPath = Path.Combine(RepoRoot, "data", "test-images", "2025-02-08 17.56.14.jpg");
        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(photoPath, TestContext.Current.CancellationToken);
        image.Mutate(x => x.AutoOrient());

        OnnxImageOrientationClassifier classifier = new();
        OrientationPrediction prediction = await classifier.ClassifyAsync(image, TestContext.Current.CancellationToken);

        Assert.Equal(OrientationClass.Correct, prediction.PredictedClass);
        Assert.True(prediction.Confidence >= 0.5, $"expected reasonably confident, got {prediction.Confidence}");
    }

    [Fact]
    public async Task ClassifyAsync_SamePhotoRotated90WithoutCorrection_DetectsTheRotation()
    {
        Assert.SkipUnless(LiveModelTestsEnabled, "set PICTTAG_RUN_LIVE_MODEL_TESTS=1 to run against the real ONNX model");

        // Same real photo, but deliberately feed the classifier the *raw* (un-auto-oriented)
        // pixel data - proves the classifier actually detects real rotation, not just always
        // returning "Correct".
        string photoPath = Path.Combine(RepoRoot, "data", "test-images", "2025-02-08 17.56.14.jpg");
        using Image<Rgba32> rawImage = await Image.LoadAsync<Rgba32>(photoPath, TestContext.Current.CancellationToken);
        // Deliberately skip AutoOrient here - this is the raw, sideways pixel data.

        OnnxImageOrientationClassifier classifier = new();
        OrientationPrediction prediction = await classifier.ClassifyAsync(rawImage, TestContext.Current.CancellationToken);

        Assert.NotEqual(OrientationClass.Correct, prediction.PredictedClass);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PictTag.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate PictTag.slnx from the test output directory.");
    }
}
