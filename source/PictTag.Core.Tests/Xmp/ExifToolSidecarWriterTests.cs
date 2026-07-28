using PictTag.Core.Xmp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using XmpCore;

namespace PictTag.Core.Tests.Xmp;

public class ExifToolSidecarWriterTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("picttag-exiftool-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string CreateTestImage(int width = 400, int height = 300)
    {
        string path = Path.Combine(_tempDir, "test.jpg");
        using Image<Rgba32> image = new(width, height);
        image.SaveAsJpeg(path);
        return path;
    }

    [Fact]
    public async Task WriteSidecarAsync_WritesSubjectKeywordsAndRegions()
    {
        Assert.SkipUnless(ExifToolSidecarWriter.IsExifToolAvailable, "exiftool not found on PATH");

        string imagePath = CreateTestImage(width: 400, height: 300);
        ImageAnalysisResult result = new(
        [
            new DetectedEntity("cat", new BoundingBox(YMin: 100, XMin: 200, YMax: 300, XMax: 400)),
            new DetectedEntity("sofa", new BoundingBox(YMin: 0, XMin: 0, YMax: 1000, XMax: 1000)),
        ]);

        IXmpSidecarWriter writer = new ExifToolSidecarWriter();
        string sidecarPath = await writer.WriteSidecarAsync(
            imagePath, result, XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken);

        Assert.Equal(Path.ChangeExtension(imagePath, ".xmp"), sidecarPath);
        Assert.True(File.Exists(sidecarPath));

        IXmpMeta xmp;
        using (FileStream stream = File.OpenRead(sidecarPath))
        {
            xmp = XmpMetaFactory.Parse(stream);
        }

        Assert.Equal(2, xmp.CountArrayItems(XmpConstants.NsDC, "subject"));
        Assert.Equal("cat", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 1).Value);
        Assert.Equal("sofa", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 2).Value);

        Assert.Equal(2, xmp.CountArrayItems(MwgNamespaces.Regions, "Regions/mwg-rs:RegionList"));
        Assert.Equal("cat", xmp.GetStructField(MwgNamespaces.Regions, "Regions/mwg-rs:RegionList[1]", MwgNamespaces.Regions, "Name").Value);
    }

    [Fact]
    public async Task WriteSidecarAsync_LabelWithSpecialCharacters_RoundTripsCorrectly()
    {
        Assert.SkipUnless(ExifToolSidecarWriter.IsExifToolAvailable, "exiftool not found on PATH");

        string imagePath = CreateTestImage();
        const string trickyLabel = "a, tricky} label] with |pipes|";
        ImageAnalysisResult result = new(
        [
            new DetectedEntity(trickyLabel, new BoundingBox(YMin: 0, XMin: 0, YMax: 500, XMax: 500)),
        ]);

        IXmpSidecarWriter writer = new ExifToolSidecarWriter();
        string sidecarPath = await writer.WriteSidecarAsync(
            imagePath, result, XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken);

        IXmpMeta xmp;
        using (FileStream stream = File.OpenRead(sidecarPath))
        {
            xmp = XmpMetaFactory.Parse(stream);
        }

        Assert.Equal(trickyLabel, xmp.GetStructField(MwgNamespaces.Regions, "Regions/mwg-rs:RegionList[1]", MwgNamespaces.Regions, "Name").Value);
    }

    [Fact]
    public async Task WriteSidecarAsync_SidecarAlreadyExists_OverwritesWithoutLeavingBackupFile()
    {
        Assert.SkipUnless(ExifToolSidecarWriter.IsExifToolAvailable, "exiftool not found on PATH");

        string imagePath = CreateTestImage();
        IXmpSidecarWriter writer = new ExifToolSidecarWriter();

        await writer.WriteSidecarAsync(
            imagePath, new ImageAnalysisResult([new DetectedEntity("cat", new BoundingBox(0, 0, 500, 500))]),
            XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken);

        string sidecarPath = await writer.WriteSidecarAsync(
            imagePath, new ImageAnalysisResult([new DetectedEntity("dog", new BoundingBox(0, 0, 500, 500))]),
            XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken);

        IXmpMeta xmp;
        using (FileStream stream = File.OpenRead(sidecarPath))
        {
            xmp = XmpMetaFactory.Parse(stream);
        }

        Assert.Equal("dog", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 1).Value);
        Assert.False(File.Exists(sidecarPath + "_original"), "exiftool should not leave an _original backup file behind");
    }

    [Fact]
    public async Task WriteSidecarAsync_NoEntities_StillCreatesSidecarFile()
    {
        Assert.SkipUnless(ExifToolSidecarWriter.IsExifToolAvailable, "exiftool not found on PATH");

        // Regression test: exiftool refuses to create a brand-new file when the only
        // write operations amount to no real content, which is exactly what happens when
        // there are zero detected entities (no -XMP-dc:Subject+= or -RegionInfo= args at all).
        string imagePath = CreateTestImage();
        ImageAnalysisResult result = new([]);

        IXmpSidecarWriter writer = new ExifToolSidecarWriter();
        string sidecarPath = await writer.WriteSidecarAsync(
            imagePath, result, XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(sidecarPath));

        IXmpMeta xmp;
        using (FileStream stream = File.OpenRead(sidecarPath))
        {
            xmp = XmpMetaFactory.Parse(stream);
        }

        Assert.False(xmp.DoesPropertyExist(MwgNamespaces.Regions, "Regions"));
    }

    [Fact]
    public async Task WriteSidecarAsync_ExifToolNotFound_ThrowsExifToolNotFoundException()
    {
        Assert.SkipWhen(ExifToolSidecarWriter.IsExifToolAvailable, "this test only applies when exiftool is absent");

        string imagePath = CreateTestImage();
        ImageAnalysisResult result = new([]);

        IXmpSidecarWriter writer = new ExifToolSidecarWriter();

        await Assert.ThrowsAsync<ExifToolNotFoundException>(() =>
            writer.WriteSidecarAsync(imagePath, result, XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken));
    }
}
