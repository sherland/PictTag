using PictTag.Core.Orientation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PictTag.Core.Tests.Orientation;

/// <summary>A canned stand-in for <see cref="OnnxImageOrientationClassifier"/> - no ONNX model needed.</summary>
public sealed class FakeImageOrientationClassifier(OrientationPrediction prediction) : IImageOrientationClassifier
{
    public int CallCount { get; private set; }

    public Task<OrientationPrediction> ClassifyAsync(Image<Rgba32> image, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(prediction);
    }
}
