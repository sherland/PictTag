using System.Globalization;
using PictTag.Core.Xmp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using XmpCore;

namespace PictTag.Core.Tests.Xmp;

public class XmpCoreSidecarWriterTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("picttag-xmpcore-tests-").FullName;

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
        string imagePath = CreateTestImage(width: 400, height: 300);
        ImageAnalysisResult result = new(
        [
            new DetectedEntity("cat", new BoundingBox(YMin: 100, XMin: 200, YMax: 300, XMax: 400)),
            new DetectedEntity("sofa", new BoundingBox(YMin: 0, XMin: 0, YMax: 1000, XMax: 1000)),
        ]);

        IXmpSidecarWriter writer = new XmpCoreSidecarWriter();
        string sidecarPath = await writer.WriteSidecarAsync(imagePath, result, XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken);

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

        Assert.Equal("400", xmp.GetStructField(MwgNamespaces.Regions, "Regions/mwg-rs:AppliedToDimensions", MwgNamespaces.StDimensions, "w").Value);
        Assert.Equal("300", xmp.GetStructField(MwgNamespaces.Regions, "Regions/mwg-rs:AppliedToDimensions", MwgNamespaces.StDimensions, "h").Value);

        Assert.Equal(2, xmp.CountArrayItems(MwgNamespaces.Regions, "Regions/mwg-rs:RegionList"));

        IXmpProperty catName = xmp.GetStructField(MwgNamespaces.Regions, "Regions/mwg-rs:RegionList[1]", MwgNamespaces.Regions, "Name");
        Assert.Equal("cat", catName.Value);

        // cat box: ymin=100 xmin=200 ymax=300 xmax=400 -> cx=0.3, cy=0.2, w=0.2, h=0.2
        IXmpProperty catAreaX = xmp.GetStructField(MwgNamespaces.Regions, "Regions/mwg-rs:RegionList[1]/mwg-rs:Area", MwgNamespaces.StArea, "x");
        IXmpProperty catAreaY = xmp.GetStructField(MwgNamespaces.Regions, "Regions/mwg-rs:RegionList[1]/mwg-rs:Area", MwgNamespaces.StArea, "y");
        Assert.Equal(0.3, double.Parse(catAreaX.Value, CultureInfo.InvariantCulture), precision: 6);
        Assert.Equal(0.2, double.Parse(catAreaY.Value, CultureInfo.InvariantCulture), precision: 6);
    }

    [Fact]
    public async Task WriteSidecarAsync_NoEntities_WritesEmptySubjectAndNoRegions()
    {
        string imagePath = CreateTestImage();
        ImageAnalysisResult result = new([]);

        IXmpSidecarWriter writer = new XmpCoreSidecarWriter();
        string sidecarPath = await writer.WriteSidecarAsync(imagePath, result, XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken);

        IXmpMeta xmp;
        using (FileStream stream = File.OpenRead(sidecarPath))
        {
            xmp = XmpMetaFactory.Parse(stream);
        }

        Assert.False(xmp.DoesPropertyExist(MwgNamespaces.Regions, "Regions"));
    }

    [Theory]
    [InlineData(XmpSidecarNamingConvention.ReplaceExtension)]
    [InlineData(XmpSidecarNamingConvention.AppendExtension)]
    public async Task WriteSidecarAsync_UsesRequestedNamingConvention(XmpSidecarNamingConvention convention)
    {
        string imagePath = CreateTestImage();
        ImageAnalysisResult result = new([]);

        IXmpSidecarWriter writer = new XmpCoreSidecarWriter();
        string sidecarPath = await writer.WriteSidecarAsync(imagePath, result, convention, TestContext.Current.CancellationToken);

        Assert.Equal(SidecarPathResolver.Resolve(imagePath, convention), sidecarPath);
    }
}
