using System.Text.Json;
using PictTag.Core.Xmp;
using XmpCore;

namespace PictTag.Core.Tests.Xmp;

/// <summary>
/// Runs the real detection pipeline against the curated art-style fixtures downloaded by
/// Get-ArtStyleTestImages.ps1. Hits a real Ollama server for ~100 images, so it's opt-in only
/// (set PICTTAG_RUN_LIVE_MODEL_TESTS=1) and always skipped in a default `dotnet test` run -
/// same reasoning as the exiftool-conditional skip, but for a much slower dependency.
/// </summary>
public class ArtStyleDetectionTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ManifestPath = Path.Combine(RepoRoot, "data", "art-styles-manifest.json");

    private static bool LiveModelTestsEnabled =>
        Environment.GetEnvironmentVariable("PICTTAG_RUN_LIVE_MODEL_TESTS") == "1";

    public static TheoryData<string, string, string> Fixtures()
    {
        TheoryData<string, string, string> data = new();
        if (!File.Exists(ManifestPath))
        {
            return data;
        }

        List<ManifestEntry> manifest = JsonSerializer.Deserialize<List<ManifestEntry>>(
            File.ReadAllText(ManifestPath), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];

        foreach (ManifestEntry entry in manifest)
        {
            string styleDir = Path.Combine(RepoRoot, "data", "test-images", "art-styles", entry.Slug);
            if (!Directory.Exists(styleDir))
            {
                continue;
            }

            string[] extensions = [".jpg", ".jpeg", ".png", ".webp"];
            foreach (string imagePath in Directory.EnumerateFiles(styleDir)
                .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => f))
            {
                data.Add(entry.DisplayName, string.Join('|', entry.StyleKeywords), imagePath);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    [Trait("Category", "LiveModel")]
    public async Task DetectAsync_ArtStyleImage_ProducesValidSidecarWithPlausibleMetadata(
        string styleDisplayName, string styleKeywordsJoined, string imagePath)
    {
        Assert.SkipUnless(
            LiveModelTestsEnabled,
            "set PICTTAG_RUN_LIVE_MODEL_TESTS=1 to run against a real Ollama server and the downloaded art-style fixtures");

        string[] styleKeywords = styleKeywordsJoined.Split('|');

        ImageDetectionService service = new();
        ImageAnalysisResult result = await service.DetectAsync(imagePath, ct: TestContext.Current.CancellationToken);

        IXmpSidecarWriter writer = new XmpCoreSidecarWriter();
        string sidecarPath = await writer.WriteSidecarAsync(
            imagePath, result, XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken);

        // The Gemini prompt's "valid XML / adheres to XMP schema" check: parsing must not throw.
        IXmpMeta xmp;
        using (FileStream stream = File.OpenRead(sidecarPath))
        {
            xmp = XmpMetaFactory.Parse(stream);
        }

        Assert.False(string.IsNullOrWhiteSpace(xmp.GetLocalizedText(XmpConstants.NsDC, "title", "", "x-default")?.Value));
        Assert.False(string.IsNullOrWhiteSpace(xmp.GetLocalizedText(XmpConstants.NsDC, "description", "", "x-default")?.Value));

        Assert.NotEqual(ImageMedium.Photograph, result.Metadata.Medium);
        Assert.NotEqual(ImageMedium.Screenshot, result.Metadata.Medium);

        // Composition is an LLM impression, not a measurement - only its shape is verifiable.
        Assert.InRange(result.Metadata.Composition.ColorVarianceEstimate, 0.0, 1.0);
        Assert.InRange(result.Metadata.Composition.EdgeDensityEstimate, 0.0, 1.0);

        Assert.NotNull(result.Metadata.ArtStyle);
        string artStyleLower = result.Metadata.ArtStyle!.ToLowerInvariant();
        bool matched = styleKeywords.Any(kw => artStyleLower.Contains(kw.ToLowerInvariant()));
        Assert.True(
            matched,
            $"Expected ArtStyle '{result.Metadata.ArtStyle}' to contain one of [{string.Join(", ", styleKeywords)}] for style '{styleDisplayName}'.");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PictTag.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    private record ManifestEntry(string Slug, string DisplayName, string SearchQuery, List<string> StyleKeywords);
}
