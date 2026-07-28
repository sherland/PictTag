using System.Globalization;
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
            TestData.SampleMetadata(),
            [
                new DetectedEntity("cat", EntityCategory.Animals, new BoundingBox(YMin: 100, XMin: 200, YMax: 300, XMax: 400)),
                new DetectedEntity("sofa", EntityCategory.Objects, new BoundingBox(YMin: 0, XMin: 0, YMax: 1000, XMax: 1000)),
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

        // Medium and Symmetry are always tagged too (in addition to detected entities), so
        // Photograph/Asymmetrical (from TestData.SampleMetadata()) precede cat/sofa here.
        Assert.Equal(4, xmp.CountArrayItems(XmpConstants.NsDC, "subject"));
        Assert.Equal("Photograph", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 1).Value);
        Assert.Equal("Asymmetrical", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 2).Value);
        Assert.Equal("cat", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 3).Value);
        Assert.Equal("sofa", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 4).Value);

        Assert.Equal(4, xmp.CountArrayItems(XmpNamespaces.LightroomHierarchical, "hierarchicalSubject"));
        Assert.Equal("Animals|cat", xmp.GetArrayItem(XmpNamespaces.LightroomHierarchical, "hierarchicalSubject", 3).Value);
        Assert.Equal("Objects/sofa", xmp.GetArrayItem(XmpNamespaces.DigiKam, "TagsList", 4).Value);

        // digiKam only builds its tag hierarchy from TagsList when it's an ordered rdf:Seq -
        // exiftool's own tag table already gets this right natively, verified empirically.
        Assert.True(xmp.GetProperty(XmpNamespaces.DigiKam, "TagsList").Options.IsArrayOrdered);

        Assert.Equal(2, xmp.CountArrayItems(MwgNamespaces.Regions, "Regions/mwg-rs:RegionList"));
        Assert.Equal("cat", xmp.GetStructField(MwgNamespaces.Regions, "Regions/mwg-rs:RegionList[1]", MwgNamespaces.Regions, "Name").Value);
    }

    [Fact]
    public async Task WriteSidecarAsync_WritesTitleDescriptionMediumAndDigitalSourceType()
    {
        Assert.SkipUnless(ExifToolSidecarWriter.IsExifToolAvailable, "exiftool not found on PATH");

        string imagePath = CreateTestImage();
        ImageAnalysisResult result = new(
            new ImageMetadata("A Test Photo", "A description of the photo.", ImageMedium.Photograph, ArtStyle: null, ImageSetting.Indoor, TestData.SampleComposition()),
            []);

        IXmpSidecarWriter writer = new ExifToolSidecarWriter();
        string sidecarPath = await writer.WriteSidecarAsync(
            imagePath, result, XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken);

        IXmpMeta xmp;
        using (FileStream stream = File.OpenRead(sidecarPath))
        {
            xmp = XmpMetaFactory.Parse(stream);
        }

        Assert.Equal("A Test Photo", xmp.GetLocalizedText(XmpConstants.NsDC, "title", "", "x-default").Value);
        Assert.Equal("A description of the photo.", xmp.GetLocalizedText(XmpConstants.NsDC, "description", "", "x-default").Value);
        Assert.Equal("Photograph", xmp.GetPropertyString(XmpNamespaces.PictTag, "Medium"));
        Assert.Equal("Indoor", xmp.GetPropertyString(XmpNamespaces.PictTag, "Setting"));
        Assert.False(xmp.DoesPropertyExist(XmpNamespaces.PictTag, "ArtStyle"), "ArtStyle should be omitted when null");
        Assert.Equal(
            "https://cv.iptc.org/newscodes/digitalsourcetype/digitalCapture",
            xmp.GetPropertyString(XmpNamespaces.IptcExt, "DigitalSourceType"));

        Assert.Equal("Asymmetrical", xmp.GetPropertyString(XmpNamespaces.PictTag, "Symmetry"));
        Assert.Equal("True", xmp.GetPropertyString(XmpNamespaces.PictTag, "RuleOfThirds"));
        Assert.Equal(0.5, double.Parse(xmp.GetPropertyString(XmpNamespaces.PictTag, "ColorVariance"), CultureInfo.InvariantCulture), precision: 3);
        Assert.Equal(0.4, double.Parse(xmp.GetPropertyString(XmpNamespaces.PictTag, "EdgeDensity"), CultureInfo.InvariantCulture), precision: 3);
        Assert.Equal("Test composition note.", xmp.GetPropertyString(XmpNamespaces.PictTag, "CompositionNotes"));
    }

    [Fact]
    public async Task WriteSidecarAsync_PaintingMedium_WritesArtStyleAndOmitsDigitalSourceType()
    {
        Assert.SkipUnless(ExifToolSidecarWriter.IsExifToolAvailable, "exiftool not found on PATH");

        string imagePath = CreateTestImage();
        ImageAnalysisResult result = new(
            TestData.SampleMetadata(medium: ImageMedium.Painting, artStyle: "impressionism"),
            []);

        IXmpSidecarWriter writer = new ExifToolSidecarWriter();
        string sidecarPath = await writer.WriteSidecarAsync(
            imagePath, result, XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken);

        IXmpMeta xmp;
        using (FileStream stream = File.OpenRead(sidecarPath))
        {
            xmp = XmpMetaFactory.Parse(stream);
        }

        Assert.Equal("impressionism", xmp.GetPropertyString(XmpNamespaces.PictTag, "ArtStyle"));
        Assert.False(xmp.DoesPropertyExist(XmpNamespaces.IptcExt, "DigitalSourceType"));

        // Medium/ArtStyle/Symmetry should also be browsable tags, not just pictTag:*
        // properties, so they show up in digiKam's/Lightroom's tag panel like any other tag.
        Assert.Equal(3, xmp.CountArrayItems(XmpConstants.NsDC, "subject"));
        Assert.Equal("Painting", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 1).Value);
        Assert.Equal("impressionism", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 2).Value);
        Assert.Equal("Asymmetrical", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 3).Value);
        Assert.Equal("ArtStyle|impressionism", xmp.GetArrayItem(XmpNamespaces.LightroomHierarchical, "hierarchicalSubject", 2).Value);
        Assert.Equal("ArtStyle/impressionism", xmp.GetArrayItem(XmpNamespaces.DigiKam, "TagsList", 2).Value);
    }

    [Fact]
    public async Task WriteSidecarAsync_LabelWithSpecialCharacters_RoundTripsCorrectly()
    {
        Assert.SkipUnless(ExifToolSidecarWriter.IsExifToolAvailable, "exiftool not found on PATH");

        string imagePath = CreateTestImage();
        const string trickyLabel = "a, tricky} label] with |pipes|";
        ImageAnalysisResult result = new(
            TestData.SampleMetadata(),
            [
                new DetectedEntity(trickyLabel, EntityCategory.Other, new BoundingBox(YMin: 0, XMin: 0, YMax: 500, XMax: 500)),
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
            imagePath,
            new ImageAnalysisResult(TestData.SampleMetadata(), [new DetectedEntity("cat", EntityCategory.Animals, new BoundingBox(0, 0, 500, 500))]),
            XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken);

        string sidecarPath = await writer.WriteSidecarAsync(
            imagePath,
            new ImageAnalysisResult(TestData.SampleMetadata(), [new DetectedEntity("dog", EntityCategory.Animals, new BoundingBox(0, 0, 500, 500))]),
            XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken);

        IXmpMeta xmp;
        using (FileStream stream = File.OpenRead(sidecarPath))
        {
            xmp = XmpMetaFactory.Parse(stream);
        }

        // Medium/Symmetry tags (always written) must not accumulate across the two writes -
        // exactly 3 items (Medium, Symmetry, dog), not 6.
        Assert.Equal("dog", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 3).Value);
        Assert.Equal(3, xmp.CountArrayItems(XmpConstants.NsDC, "subject"));
        Assert.Equal("Animals|dog", xmp.GetArrayItem(XmpNamespaces.LightroomHierarchical, "hierarchicalSubject", 3).Value);
        Assert.Equal(3, xmp.CountArrayItems(XmpNamespaces.LightroomHierarchical, "hierarchicalSubject"));
        Assert.Equal("Animals/dog", xmp.GetArrayItem(XmpNamespaces.DigiKam, "TagsList", 3).Value);
        Assert.Equal(3, xmp.CountArrayItems(XmpNamespaces.DigiKam, "TagsList"));
        Assert.False(File.Exists(sidecarPath + "_original"), "exiftool should not leave an _original backup file behind");
    }

    [Fact]
    public async Task WriteSidecarAsync_NoEntities_StillCreatesSidecarFile()
    {
        Assert.SkipUnless(ExifToolSidecarWriter.IsExifToolAvailable, "exiftool not found on PATH");

        // Regression test: exiftool refuses to create a brand-new file when the only
        // write operations amount to no real content - guarded against by always writing
        // at least the Medium/Symmetry tags and pictTag:* properties, even with zero entities.
        string imagePath = CreateTestImage();
        ImageAnalysisResult result = new(TestData.SampleMetadata(), []);

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
        ImageAnalysisResult result = new(TestData.SampleMetadata(), []);

        IXmpSidecarWriter writer = new ExifToolSidecarWriter();

        await Assert.ThrowsAsync<ExifToolNotFoundException>(() =>
            writer.WriteSidecarAsync(imagePath, result, XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken));
    }
}
