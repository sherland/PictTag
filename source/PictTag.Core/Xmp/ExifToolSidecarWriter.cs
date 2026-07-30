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

    // exiftool only recognizes namespaces it knows about (lr:, digiKam:, Iptc4xmpExt: are
    // all built in) - a genuinely custom namespace like ours needs a -config file that
    // defines it, or every -XMP-pictTag:... write is rejected as "doesn't exist or isn't
    // writable". Verified empirically against the real binary.
    private const string PictTagConfig = """
        %Image::ExifTool::UserDefined::PictTag = (
            GROUPS        => { 0 => 'XMP', 1 => 'XMP-pictTag', 2 => 'Image' },
            NAMESPACE     => { 'pictTag' => 'https://github.com/sherland/PictTag/ns/1.0/' },
            WRITABLE      => 'string',
            Medium            => { },
            Setting           => { },
            Symmetry          => { },
            RuleOfThirds      => { },
            ColorVariance     => { },
            EdgeDensity       => { },
            CompositionNotes  => { },
        );

        %Image::ExifTool::UserDefined = (
            'Image::ExifTool::XMP::Main' => {
                pictTag => {
                    SubDirectory => {
                        TagTable => 'Image::ExifTool::UserDefined::PictTag',
                    },
                },
            },
        );

        1; #end
        """;

    private static readonly Lazy<string> ConfigFilePath = new(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), "PictTag-exiftool.config");
        File.WriteAllText(path, PictTagConfig);
        return path;
    });

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
            // dc:Subject, lr:HierarchicalSubject and digiKam:TagsList are all list tags:
            // "+=" only appends, so re-running against an existing sidecar would accumulate
            // old and new values together unless cleared first. (RegionInfo, dc:Title,
            // dc:Description and the pictTag fields don't need this - setting them fresh
            // always replaces the value in one shot, verified empirically.)
            (int clearExit, string clearErr) = await RunAsync(
                ["-overwrite_original", "-XMP-dc:Subject=", "-XMP-lr:HierarchicalSubject=", "-XMP-digiKam:TagsList=",
                    "-XMP-iptcExt:Genre=", "-XMP-iptcCore:Scene=", sidecarPath], ct);
            if (clearExit != 0)
            {
                throw new InvalidOperationException($"exiftool exited with code {clearExit} while clearing existing tags: {clearErr}");
            }
        }

        ImageMetadata metadata = result.Metadata;

        // -overwrite_original only makes sense (and only works) when the sidecar already
        // exists - passing it while creating a brand-new file makes exiftool expect one to
        // already be there and fail with "File not found".
        // Always include a real, non-empty tag write (not just -charset, and not a bare
        // "clear to empty") - exiftool refuses to create a brand-new file when the only
        // operations given amount to no actual content, which happens whenever there are
        // zero detected entities. Verified empirically against the real binary.
        List<string> args =
        [
            "-config", ConfigFilePath.Value,
            "-charset", "utf8",
            "-XMP-xmp:CreatorTool=PictTag",
            $"-XMP-dc:Title={metadata.Title}",
            $"-XMP-dc:Description={metadata.Description}",
            $"-XMP-iptcCore:AltTextAccessibility={metadata.AltText}",
            $"-XMP-iptcCore:ExtDescrAccessibility={metadata.Description}",
            $"-XMP-pictTag:Medium={metadata.Medium}",
            $"-XMP-pictTag:Symmetry={metadata.Composition.Symmetry}",
            $"-XMP-pictTag:RuleOfThirds={metadata.Composition.RuleOfThirdsAdherence}",
            $"-XMP-pictTag:ColorVariance={metadata.Composition.ColorVarianceEstimate.ToString("F3", CultureInfo.InvariantCulture)}",
            $"-XMP-pictTag:EdgeDensity={metadata.Composition.EdgeDensityEstimate.ToString("F3", CultureInfo.InvariantCulture)}",
        ];

        if (sidecarExists)
        {
            args.Add("-overwrite_original");
        }

        if (metadata.ArtStyle is not null)
        {
            // Iptc4xmpExt:Genre is the real IPTC field for artistic/style genre - ArtStyle
            // lives there instead of a custom pictTag property. Genre is a Bag of CVTerm
            // structs, not plain text (verified empirically - a bare string write fails with
            // "Improperly formed structure"), so only the free-text CvTermName is populated;
            // CvId/CvTermId/CvTermRefinedAbout are left unset since ArtStyle isn't sourced
            // from a real controlled vocabulary with term IDs.
            args.Add($"-XMP-iptcExt:Genre+={{CvTermName={EscapeStructValue(metadata.ArtStyle)}}}");
        }

        if (metadata.Setting is not null)
        {
            args.Add($"-XMP-pictTag:Setting={metadata.Setting}");
        }

        if (metadata.Composition.Notes is not null)
        {
            args.Add($"-XMP-pictTag:CompositionNotes={metadata.Composition.Notes}");
        }

        string? digitalSourceType = IptcDigitalSourceType.ForMedium(metadata.Medium);
        if (digitalSourceType is not null)
        {
            args.Add($"-XMP-iptcExt:DigitalSourceType={digitalSourceType}");
        }

        // Distinct(), not a HashSet, so the model's own ordering is preserved (HashSet
        // iteration order isn't guaranteed) - the Setting-derived code, if any, goes last.
        List<SceneType> scenes = metadata.Scene.Distinct().ToList();
        if (metadata.Setting == ImageSetting.Indoor && !scenes.Contains(SceneType.InteriorView))
        {
            scenes.Add(SceneType.InteriorView);
        }
        else if (metadata.Setting == ImageSetting.Outdoor && !scenes.Contains(SceneType.ExteriorView))
        {
            scenes.Add(SceneType.ExteriorView);
        }

        foreach (SceneType scene in scenes)
        {
            args.Add($"-XMP-iptcCore:Scene+={IptcSceneCode.ForSceneType(scene)}");
        }

        // Medium/ArtStyle/Symmetry are also surfaced as browsable tags (not just pictTag:*
        // properties) so they show up in digiKam's/Lightroom's tag panel like any other tag.
        AppendHierarchicalTagArgs(args, ["Medium", metadata.Medium.ToString()]);
        if (metadata.ArtStyle is not null)
        {
            AppendHierarchicalTagArgs(args, ["ArtStyle", HierarchicalTagPath.TitleCase(metadata.ArtStyle)]);
        }

        AppendHierarchicalTagArgs(args, ["Symmetry", metadata.Composition.Symmetry.ToString()]);

        foreach (DetectedEntity entity in result.Entities)
        {
            AppendHierarchicalTagArgs(args, HierarchicalTagPath.BuildSegments(entity.Category.ToString(), entity.Group, entity.Label));
        }

        if (result.Entities.Count > 0)
        {
            args.Add($"-RegionInfo={BuildRegionInfoStruct(result, imageInfo.Width, imageInfo.Height)}");
            args.Add($"-XMP-iptcExt:ImageRegion={BuildImageRegionStruct(result)}");
        }

        args.Add(sidecarPath);

        (int exitCode, string stdErr) = await RunAsync(args, ct);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"exiftool exited with code {exitCode}: {stdErr}");
        }

        return sidecarPath;
    }

    private static void AppendHierarchicalTagArgs(List<string> args, string[] segments)
    {
        args.Add($"-XMP-dc:Subject+={segments[^1]}");
        args.Add($"-XMP-lr:HierarchicalSubject+={HierarchicalTagPath.Compose('|', segments)}");
        args.Add($"-XMP-digiKam:TagsList+={HierarchicalTagPath.Compose('/', segments)}");
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
    /// Iptc4xmpExt:ImageRegion - IPTC's own, more modern region structure, written alongside
    /// (not instead of) -RegionInfo/mwg-rs:Regions above. Field names/types and the
    /// RegionBoundary top-left-corner coordinate convention were verified empirically against
    /// exiftool's own built-in tag tables and its maintained MWG&lt;-&gt;IPTC region conversion
    /// logic - not guessed. rCtype/rRole (content-type/role sub-structs) are omitted: no real
    /// controlled vocabulary URIs for them were verified, and guessing at IDs would be worse
    /// than leaving them out. Uses a plain "=" (not "+=") like -RegionInfo - the whole array
    /// is replaced in one shot, so no separate clear-pass entry is needed for this field.
    /// </summary>
    private static string BuildImageRegionStruct(ImageAnalysisResult result)
    {
        StringBuilder sb = new();
        sb.Append('[');

        for (int i = 0; i < result.Entities.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            DetectedEntity entity = result.Entities[i];
            IptcRegionBoundary boundary = IptcRegionBoundary.FromBoundingBox(entity.Box);

            sb.Append("{RId=").Append(i + 1)
              .Append(",Name=").Append(EscapeStructValue(entity.Label))
              .Append(",RegionBoundary={RbShape=rectangle,RbUnit=relative")
              .Append(",RbX=").Append(boundary.X.ToString("F6", CultureInfo.InvariantCulture))
              .Append(",RbY=").Append(boundary.Y.ToString("F6", CultureInfo.InvariantCulture))
              .Append(",RbW=").Append(boundary.Width.ToString("F6", CultureInfo.InvariantCulture))
              .Append(",RbH=").Append(boundary.Height.ToString("F6", CultureInfo.InvariantCulture))
              .Append("}}");
        }

        sb.Append(']');
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
