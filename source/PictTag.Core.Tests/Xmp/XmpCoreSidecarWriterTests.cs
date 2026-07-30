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
            TestData.SampleMetadata(),
            [
                // group "Cat" title-cases equal to label "cat" -> collapses to a 2-level tag.
                new DetectedEntity("cat", "Cat", EntityCategory.Animals, new BoundingBox(YMin: 100, XMin: 200, YMax: 300, XMax: 400)),
                // group "furniture" is distinct from label "sofa" -> stays a 3-level tag.
                new DetectedEntity("sofa", "furniture", EntityCategory.Objects, new BoundingBox(YMin: 0, XMin: 0, YMax: 1000, XMax: 1000)),
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

        // Medium and Symmetry are always tagged too (in addition to detected entities), so
        // Photograph/Asymmetrical (from TestData.SampleMetadata()) precede cat/sofa here.
        Assert.Equal(4, xmp.CountArrayItems(XmpConstants.NsDC, "subject"));
        Assert.Equal("Photograph", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 1).Value);
        Assert.Equal("Asymmetrical", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 2).Value);
        Assert.Equal("Cat", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 3).Value);
        Assert.Equal("Sofa", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 4).Value);

        Assert.Equal(4, xmp.CountArrayItems(XmpNamespaces.LightroomHierarchical, "hierarchicalSubject"));
        Assert.Equal("Medium|Photograph", xmp.GetArrayItem(XmpNamespaces.LightroomHierarchical, "hierarchicalSubject", 1).Value);
        Assert.Equal("Symmetry|Asymmetrical", xmp.GetArrayItem(XmpNamespaces.LightroomHierarchical, "hierarchicalSubject", 2).Value);
        Assert.Equal("Animals|Cat", xmp.GetArrayItem(XmpNamespaces.LightroomHierarchical, "hierarchicalSubject", 3).Value);
        Assert.Equal("Objects|Furniture|Sofa", xmp.GetArrayItem(XmpNamespaces.LightroomHierarchical, "hierarchicalSubject", 4).Value);

        Assert.Equal(4, xmp.CountArrayItems(XmpNamespaces.DigiKam, "TagsList"));
        Assert.Equal("Medium/Photograph", xmp.GetArrayItem(XmpNamespaces.DigiKam, "TagsList", 1).Value);
        Assert.Equal("Symmetry/Asymmetrical", xmp.GetArrayItem(XmpNamespaces.DigiKam, "TagsList", 2).Value);
        Assert.Equal("Animals/Cat", xmp.GetArrayItem(XmpNamespaces.DigiKam, "TagsList", 3).Value);
        Assert.Equal("Objects/Furniture/Sofa", xmp.GetArrayItem(XmpNamespaces.DigiKam, "TagsList", 4).Value);

        // Per the exiv2 digiKam namespace reference, TagsList must be an ordered rdf:Seq (unlike
        // dc:subject/lr:hierarchicalSubject, which are rdf:Bag) - digiKam only builds its tag
        // hierarchy from this field when it's a Seq, silently flattening it if it's a Bag instead.
        Assert.True(xmp.GetProperty(XmpNamespaces.DigiKam, "TagsList").Options.IsArrayOrdered);

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

        // Iptc4xmpExt:ImageRegion is written alongside mwg-rs:Regions, with the *top-left*
        // corner (unlike MWG's center point) - verified empirically against exiftool's own
        // maintained MWG<->IPTC region conversion logic. cat box -> rbX=0.2, rbY=0.1.
        Assert.Equal(2, xmp.CountArrayItems(XmpNamespaces.IptcExt, "ImageRegion"));
        Assert.Equal("cat", xmp.GetLocalizedText(XmpNamespaces.IptcExt, "ImageRegion[1]/Iptc4xmpExt:Name", "", "x-default").Value);
        Assert.Equal("1", xmp.GetStructField(XmpNamespaces.IptcExt, "ImageRegion[1]", XmpNamespaces.IptcExt, "rId").Value);
        Assert.Equal("rectangle", xmp.GetStructField(XmpNamespaces.IptcExt, "ImageRegion[1]/Iptc4xmpExt:RegionBoundary", XmpNamespaces.IptcExt, "rbShape").Value);
        Assert.Equal("relative", xmp.GetStructField(XmpNamespaces.IptcExt, "ImageRegion[1]/Iptc4xmpExt:RegionBoundary", XmpNamespaces.IptcExt, "rbUnit").Value);
        Assert.Equal(0.2, double.Parse(xmp.GetStructField(XmpNamespaces.IptcExt, "ImageRegion[1]/Iptc4xmpExt:RegionBoundary", XmpNamespaces.IptcExt, "rbX").Value, CultureInfo.InvariantCulture), precision: 6);
        Assert.Equal(0.1, double.Parse(xmp.GetStructField(XmpNamespaces.IptcExt, "ImageRegion[1]/Iptc4xmpExt:RegionBoundary", XmpNamespaces.IptcExt, "rbY").Value, CultureInfo.InvariantCulture), precision: 6);
        Assert.Equal(0.2, double.Parse(xmp.GetStructField(XmpNamespaces.IptcExt, "ImageRegion[1]/Iptc4xmpExt:RegionBoundary", XmpNamespaces.IptcExt, "rbW").Value, CultureInfo.InvariantCulture), precision: 6);
        Assert.Equal(0.2, double.Parse(xmp.GetStructField(XmpNamespaces.IptcExt, "ImageRegion[1]/Iptc4xmpExt:RegionBoundary", XmpNamespaces.IptcExt, "rbH").Value, CultureInfo.InvariantCulture), precision: 6);
    }

    [Fact]
    public async Task WriteSidecarAsync_WritesTitleDescriptionMediumAndDigitalSourceType()
    {
        string imagePath = CreateTestImage();
        ImageAnalysisResult result = new(
            new ImageMetadata("A Test Photo", "A description of the photo.", "A test photo.", ImageMedium.Photograph, ArtStyle: null, ImageSetting.Indoor, [SceneType.CloseUp], TestData.SampleComposition()),
            []);

        IXmpSidecarWriter writer = new XmpCoreSidecarWriter();
        string sidecarPath = await writer.WriteSidecarAsync(imagePath, result, XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken);

        IXmpMeta xmp;
        using (FileStream stream = File.OpenRead(sidecarPath))
        {
            xmp = XmpMetaFactory.Parse(stream);
        }

        Assert.Equal("A Test Photo", xmp.GetLocalizedText(XmpConstants.NsDC, "title", "", "x-default").Value);
        Assert.Equal("A description of the photo.", xmp.GetLocalizedText(XmpConstants.NsDC, "description", "", "x-default").Value);
        Assert.Equal("A test photo.", xmp.GetLocalizedText(XmpNamespaces.IptcCore, "AltTextAccessibility", "", "x-default").Value);
        Assert.Equal("A description of the photo.", xmp.GetLocalizedText(XmpNamespaces.IptcCore, "ExtDescrAccessibility", "", "x-default").Value);
        Assert.Equal("Photograph", xmp.GetPropertyString(XmpNamespaces.PictTag, "Medium"));
        Assert.Equal("Indoor", xmp.GetPropertyString(XmpNamespaces.PictTag, "Setting"));
        Assert.False(xmp.DoesPropertyExist(XmpNamespaces.PictTag, "ArtStyle"), "ArtStyle no longer lives under pictTag - it moved to Iptc4xmpExt:Genre");
        Assert.False(xmp.DoesPropertyExist(XmpNamespaces.IptcExt, "Genre"), "Genre should be omitted when ArtStyle is null");
        Assert.Equal(
            "https://cv.iptc.org/newscodes/digitalsourcetype/digitalCapture",
            xmp.GetPropertyString(XmpNamespaces.IptcExt, "DigitalSourceType"));

        // CloseUp (model pick) + InteriorView (derived from Setting=Indoor).
        Assert.Equal(2, xmp.CountArrayItems(XmpNamespaces.IptcCore, "Scene"));
        Assert.Equal(IptcSceneCode.ForSceneType(SceneType.CloseUp), xmp.GetArrayItem(XmpNamespaces.IptcCore, "Scene", 1).Value);
        Assert.Equal(IptcSceneCode.ForSceneType(SceneType.InteriorView), xmp.GetArrayItem(XmpNamespaces.IptcCore, "Scene", 2).Value);

        Assert.Equal("Asymmetrical", xmp.GetPropertyString(XmpNamespaces.PictTag, "Symmetry"));
        Assert.Equal("True", xmp.GetPropertyString(XmpNamespaces.PictTag, "RuleOfThirds"));
        Assert.Equal(0.5, double.Parse(xmp.GetPropertyString(XmpNamespaces.PictTag, "ColorVariance"), CultureInfo.InvariantCulture), precision: 3);
        Assert.Equal(0.4, double.Parse(xmp.GetPropertyString(XmpNamespaces.PictTag, "EdgeDensity"), CultureInfo.InvariantCulture), precision: 3);
        Assert.Equal("Test composition note.", xmp.GetPropertyString(XmpNamespaces.PictTag, "CompositionNotes"));
    }

    [Fact]
    public async Task WriteSidecarAsync_PaintingMedium_WritesArtStyleAndOmitsDigitalSourceType()
    {
        string imagePath = CreateTestImage();
        ImageAnalysisResult result = new(
            TestData.SampleMetadata(medium: ImageMedium.Painting, artStyle: "impressionism"),
            []);

        IXmpSidecarWriter writer = new XmpCoreSidecarWriter();
        string sidecarPath = await writer.WriteSidecarAsync(imagePath, result, XmpSidecarNamingConvention.ReplaceExtension, TestContext.Current.CancellationToken);

        IXmpMeta xmp;
        using (FileStream stream = File.OpenRead(sidecarPath))
        {
            xmp = XmpMetaFactory.Parse(stream);
        }

        Assert.False(xmp.DoesPropertyExist(XmpNamespaces.PictTag, "ArtStyle"), "ArtStyle no longer lives under pictTag - it moved to Iptc4xmpExt:Genre");
        Assert.Equal(1, xmp.CountArrayItems(XmpNamespaces.IptcExt, "Genre"));
        Assert.Equal(
            "impressionism",
            xmp.GetLocalizedText(XmpNamespaces.IptcExt, "Genre[1]/Iptc4xmpExt:CvTermName", "", "x-default").Value);
        // No IPTC DigitalSourceType code covers paintings, so it should be left unset.
        Assert.False(xmp.DoesPropertyExist(XmpNamespaces.IptcExt, "DigitalSourceType"));

        // Medium/ArtStyle/Symmetry should also be browsable tags, not just pictTag:*
        // properties, so they show up in digiKam's/Lightroom's tag panel like any other tag.
        Assert.Equal(3, xmp.CountArrayItems(XmpConstants.NsDC, "subject"));
        Assert.Equal("Painting", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 1).Value);
        Assert.Equal("Impressionism", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 2).Value);
        Assert.Equal("Asymmetrical", xmp.GetArrayItem(XmpConstants.NsDC, "subject", 3).Value);
        Assert.Equal("ArtStyle|Impressionism", xmp.GetArrayItem(XmpNamespaces.LightroomHierarchical, "hierarchicalSubject", 2).Value);
        Assert.Equal("ArtStyle/Impressionism", xmp.GetArrayItem(XmpNamespaces.DigiKam, "TagsList", 2).Value);
    }

    [Fact]
    public async Task WriteSidecarAsync_NoEntities_WritesNoRegions()
    {
        string imagePath = CreateTestImage();
        ImageAnalysisResult result = new(TestData.SampleMetadata(), []);

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
        ImageAnalysisResult result = new(TestData.SampleMetadata(), []);

        IXmpSidecarWriter writer = new XmpCoreSidecarWriter();
        string sidecarPath = await writer.WriteSidecarAsync(imagePath, result, convention, TestContext.Current.CancellationToken);

        Assert.Equal(SidecarPathResolver.Resolve(imagePath, convention), sidecarPath);
    }
}
