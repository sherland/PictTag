using System.Diagnostics;
using PictTag.Core.Xmp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PictTag.Core.Orientation;

/// <summary>
/// Orchestrates robust orientation correction for one image file: applies the file's own EXIF
/// <c>Orientation</c> tag (which can itself be wrong - a real bug this exists to fix, see
/// docs/ORIENTATION.md), verifies the result with <see cref="IImageOrientationClassifier"/>, and
/// - only when the classifier confidently (≥ <see cref="DefaultConfidenceThreshold"/>) disagrees -
/// applies the additional correction and, unless declined, corrects the *original file's* EXIF
/// tag in place (metadata-only, via exiftool - no pixel re-encoding, nothing lossy).
/// </summary>
public sealed class ImageOrientationCorrector(IImageOrientationClassifier classifier, double confidenceThreshold = ImageOrientationCorrector.DefaultConfidenceThreshold)
{
    /// <summary>
    /// Empirically tuned against the reference model's own 98.82% validation accuracy - see
    /// docs/ORIENTATION.md. A constructor parameter, not a hardcoded constant, so it can be
    /// retuned without a code change as real-world results accumulate.
    /// </summary>
    public const double DefaultConfidenceThreshold = 0.98;

    /// <summary>
    /// Loads <paramref name="imagePath"/>, applies EXIF-based auto-orientation, verifies with the
    /// classifier, and applies/reports any additional correction needed. The caller owns disposing
    /// the returned <see cref="OrientationCorrectionResult.Image"/>.
    /// </summary>
    public async Task<OrientationCorrectionResult> CorrectAsync(string imagePath, bool fixOriginalFile, CancellationToken cancellationToken = default)
    {
        Image<Rgba32> image = await Image.LoadAsync<Rgba32>(imagePath, cancellationToken);

        ushort currentOrientation = ReadOrientation(image);
        image.Mutate(x => x.AutoOrient());

        OrientationPrediction prediction = await classifier.ClassifyAsync(image, cancellationToken);

        bool originalFileFixed = false;
        if (prediction.PredictedClass != OrientationClass.Correct && prediction.Confidence >= confidenceThreshold)
        {
            ApplyAdditionalCorrection(image, prediction.PredictedClass);

            if (fixOriginalFile)
            {
                ushort? correctedOrientation = ExifOrientationMath.TryComposeCorrectedOrientation(currentOrientation, prediction.PredictedClass);
                originalFileFixed = correctedOrientation is not null
                    && await TryWriteOrientationTagAsync(imagePath, correctedOrientation.Value, cancellationToken);
            }
        }

        return new OrientationCorrectionResult(image, image.Width, image.Height, originalFileFixed);
    }

    private static ushort ReadOrientation(Image image) =>
        image.Metadata.ExifProfile is { } exif && exif.TryGetValue(ExifTag.Orientation, out IExifValue<ushort>? value)
            ? value.Value
            : ExifOrientationMode.TopLeft;

    private static void ApplyAdditionalCorrection(Image image, OrientationClass correction)
    {
        RotateMode? mode = correction switch
        {
            OrientationClass.Rotate90Cw => RotateMode.Rotate90,
            OrientationClass.Rotate180 => RotateMode.Rotate180,
            OrientationClass.Rotate90Ccw => RotateMode.Rotate270,
            _ => null,
        };

        if (mode is not null)
        {
            image.Mutate(x => x.Rotate(mode.Value));
        }
    }

    /// <summary>
    /// Metadata-only fix via exiftool (<c>-Orientation#=N</c> forces the numeric value rather
    /// than trying to print-convert a descriptive string, verified empirically) - never touches
    /// pixel data, so there's nothing destructive to undo and no backup is kept. Fails open
    /// (returns false, logs a note) if exiftool isn't installed or the write fails for any
    /// reason - never blocks the rest of detection.
    /// </summary>
    private static async Task<bool> TryWriteOrientationTagAsync(string imagePath, ushort orientation, CancellationToken ct)
    {
        if (!ExifToolSidecarWriter.IsExifToolAvailable)
        {
            Console.WriteLine(
                "Note: 'exiftool' not found on PATH - could not correct the original file's EXIF Orientation "
                + "tag in place. The image is still used correctly-oriented for detection/preview/regions.");
            return false;
        }

        ProcessStartInfo startInfo = new("exiftool")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add($"-Orientation#={orientation}");
        startInfo.ArgumentList.Add("-overwrite_original");
        startInfo.ArgumentList.Add(imagePath);

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start 'exiftool'.");
        string stdErr = await process.StandardError.ReadToEndAsync(ct);
        await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            Console.WriteLine(
                $"Note: exiftool failed to correct the Orientation tag on '{imagePath}' (exit {process.ExitCode}: {stdErr}). "
                + "The image is still used correctly-oriented for detection/preview/regions.");
            return false;
        }

        return true;
    }
}
