using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PictTag.Core.Orientation;

/// <summary>
/// Verifies whether an image is actually upright - a narrow, purpose-built check independent of
/// (and more trustworthy than) an EXIF <c>Orientation</c> tag, which can itself be wrong. See
/// docs/ORIENTATION.md for why this uses a dedicated small classifier rather than asking a
/// vision-language model: an LLM's self-reported confidence isn't reliably calibrated the way a
/// trained classifier's softmax output is, and this needs a real, meaningful confidence score to
/// gate an automatic correction on.
/// </summary>
public interface IImageOrientationClassifier
{
    /// <summary>
    /// Classifies <paramref name="image"/> as-is - the caller is responsible for having already
    /// applied any EXIF-based auto-orientation first, since that's what the reference model was
    /// trained/evaluated against.
    /// </summary>
    Task<OrientationPrediction> ClassifyAsync(Image<Rgba32> image, CancellationToken cancellationToken = default);
}
