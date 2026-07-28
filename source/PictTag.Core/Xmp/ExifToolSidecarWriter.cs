using System.Diagnostics;
using System.Globalization;
using System.Text;
using SixLabors.ImageSharp;

namespace PictTag.Core.Xmp;

/// <summary>
/// Writes XMP sidecars by shelling out to the real `exiftool` binary. Relies on exiftool's
/// documented ability to create XMP files from scratch and its "RegionInfo" composite tag,
/// which serializes to the same XMP-mwg-rs:Regions structure as <see cref="XmpCoreSidecarWriter"/>
/// but is maintained by exiftool itself rather than hand-built here.
/// </summary>
public class ExifToolSidecarWriter : IXmpSidecarWriter
{
    private const string ExecutableName = "exiftool";

    public static bool IsExifToolAvailable => TryGetVersion() is not null;

    public async Task<string> WriteSidecarAsync(
        string imagePath,
        ImageAnalysisResult result,
        XmpSidecarNamingConvention namingConvention,
        CancellationToken ct = default)
    {
        if (!IsExifToolAvailable)
        {
            throw new ExifToolNotFoundException(
                $"'{ExecutableName}' was not found on PATH. Install it from https://exiftool.org/ or select the XmpCore engine instead.");
        }

        string sidecarPath = SidecarPathResolver.Resolve(imagePath, namingConvention);
        ImageInfo imageInfo = Image.Identify(imagePath);
        bool sidecarExists = File.Exists(sidecarPath);

        if (sidecarExists)
        {
            // dc:Subject is a list tag: "+=" only appends, so re-running against an existing
            // sidecar would accumulate old and new labels together unless cleared first.
            // (RegionInfo doesn't need this - setting it fresh always replaces the whole
            // struct in one shot, verified empirically against the real exiftool binary.)
            (int clearExit, string clearErr) = await RunAsync(["-overwrite_original", "-XMP-dc:Subject=", sidecarPath], ct);
            if (clearExit != 0)
            {
                throw new InvalidOperationException($"exiftool exited with code {clearExit} while clearing existing tags: {clearErr}");
            }
        }

        // -overwrite_original only makes sense (and only works) when the sidecar already
        // exists - passing it while creating a brand-new file makes exiftool expect one to
        // already be there and fail with "File not found".
        // Always include a real, non-empty tag write (not just -charset, and not a bare
        // "clear to empty") - exiftool refuses to create a brand-new file when the only
        // operations given amount to no actual content, which happens whenever there are
        // zero detected entities. Verified empirically against the real binary.
        List<string> args = ["-charset", "utf8", "-XMP-xmp:CreatorTool=PictTag"];
        if (sidecarExists)
        {
            args.Add("-overwrite_original");
        }

        foreach (DetectedEntity entity in result.Entities)
        {
            args.Add($"-XMP-dc:Subject+={entity.Label}");
        }

        if (result.Entities.Count > 0)
        {
            args.Add($"-RegionInfo={BuildRegionInfoStruct(result, imageInfo.Width, imageInfo.Height)}");
        }

        args.Add(sidecarPath);

        (int exitCode, string stdErr) = await RunAsync(args, ct);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"exiftool exited with code {exitCode}: {stdErr}");
        }

        return sidecarPath;
    }

    private static string BuildRegionInfoStruct(ImageAnalysisResult result, int imageWidth, int imageHeight)
    {
        StringBuilder sb = new();
        sb.Append("{AppliedToDimensions={W=").Append(imageWidth)
          .Append(",H=").Append(imageHeight)
          .Append(",Unit=pixel},RegionList=[");

        for (int i = 0; i < result.Entities.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            DetectedEntity entity = result.Entities[i];
            MwgRegionArea area = MwgRegionArea.FromBoundingBox(entity.Box);

            sb.Append("{Area={X=").Append(area.X.ToString("F6", CultureInfo.InvariantCulture))
              .Append(",Y=").Append(area.Y.ToString("F6", CultureInfo.InvariantCulture))
              .Append(",W=").Append(area.Width.ToString("F6", CultureInfo.InvariantCulture))
              .Append(",H=").Append(area.Height.ToString("F6", CultureInfo.InvariantCulture))
              .Append(",Unit=normalized},Name=").Append(EscapeStructValue(entity.Label))
              .Append('}');
        }

        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>
    /// Escapes a value for embedding inside exiftool's "structured information" syntax:
    /// a leading '|' before any '|', ',', '}' or ']' anywhere in the value, and before a
    /// leading '{', '[' or whitespace character (only when it starts the value).
    /// See https://exiftool.sourceforge.net/struct.html.
    /// </summary>
    private static string EscapeStructValue(string value)
    {
        StringBuilder sb = new(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool needsEscape = c is '|' or ',' or '}' or ']'
                || (i == 0 && (c is '{' or '[' || char.IsWhiteSpace(c)));
            if (needsEscape)
            {
                sb.Append('|');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string? TryGetVersion()
    {
        try
        {
            (int exitCode, string _) = RunAsync(["-ver"], CancellationToken.None).GetAwaiter().GetResult();
            return exitCode == 0 ? string.Empty : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(int ExitCode, string StdErr)> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        ProcessStartInfo startInfo = new(ExecutableName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{ExecutableName}'.");

        string stdErr = await process.StandardError.ReadToEndAsync(ct);
        await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return (process.ExitCode, stdErr);
    }
}
