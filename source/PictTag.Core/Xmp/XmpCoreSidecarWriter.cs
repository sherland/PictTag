using System.Globalization;
using SixLabors.ImageSharp;
using XmpCore;
using XmpCore.Options;

namespace PictTag.Core.Xmp;

/// <summary>Writes XMP sidecars using XmpCore, a pure-managed port of Adobe's XMP Toolkit.</summary>
public class XmpCoreSidecarWriter : IXmpSidecarWriter
{
    public Task<string> WriteSidecarAsync(
        string imagePath,
        ImageAnalysisResult result,
        XmpSidecarNamingConvention namingConvention,
        CancellationToken ct = default)
    {
        IXmpSchemaRegistry registry = XmpMetaFactory.SchemaRegistry;
        registry.RegisterNamespace(MwgNamespaces.Regions, "mwg-rs");
        registry.RegisterNamespace(MwgNamespaces.StArea, "stArea");
        registry.RegisterNamespace(MwgNamespaces.StDimensions, "stDim");
        registry.RegisterNamespace(XmpNamespaces.LightroomHierarchical, "lr");
        registry.RegisterNamespace(XmpNamespaces.DigiKam, "digiKam");
        registry.RegisterNamespace(XmpNamespaces.IptcExt, "Iptc4xmpExt");
        registry.RegisterNamespace(XmpNamespaces.PictTag, "pictTag");

        ImageInfo imageInfo = Image.Identify(imagePath);

        IXmpMeta xmp = XmpMetaFactory.Create();
        xmp.SetProperty(XmpConstants.NsXmp, "CreatorTool", "PictTag");

        ImageMetadata metadata = result.Metadata;
        xmp.SetLocalizedText(XmpConstants.NsDC, "title", "", "x-default", metadata.Title, null);
        xmp.SetLocalizedText(XmpConstants.NsDC, "description", "", "x-default", metadata.Description, null);

        xmp.SetProperty(XmpNamespaces.PictTag, "Medium", metadata.Medium.ToString());
        if (metadata.ArtStyle is not null)
        {
            xmp.SetProperty(XmpNamespaces.PictTag, "ArtStyle", metadata.ArtStyle);
        }

        if (metadata.Setting is not null)
        {
            xmp.SetProperty(XmpNamespaces.PictTag, "Setting", metadata.Setting.ToString());
        }

        string? digitalSourceType = IptcDigitalSourceType.ForMedium(metadata.Medium);
        if (digitalSourceType is not null)
        {
            xmp.SetProperty(XmpNamespaces.IptcExt, "DigitalSourceType", digitalSourceType);
        }

        ImageComposition composition = metadata.Composition;
        xmp.SetProperty(XmpNamespaces.PictTag, "Symmetry", composition.Symmetry.ToString());
        xmp.SetProperty(XmpNamespaces.PictTag, "RuleOfThirds", composition.RuleOfThirdsAdherence.ToString());
        xmp.SetProperty(XmpNamespaces.PictTag, "ColorVariance", composition.ColorVarianceEstimate.ToString("F3", CultureInfo.InvariantCulture));
        xmp.SetProperty(XmpNamespaces.PictTag, "EdgeDensity", composition.EdgeDensityEstimate.ToString("F3", CultureInfo.InvariantCulture));
        if (composition.Notes is not null)
        {
            xmp.SetProperty(XmpNamespaces.PictTag, "CompositionNotes", composition.Notes);
        }

        // Medium/ArtStyle/Symmetry are also surfaced as browsable tags (not just pictTag:*
        // properties) so they show up in digiKam's/Lightroom's tag panel like any other tag.
        AppendHierarchicalTag(xmp, "Medium", metadata.Medium.ToString());
        if (metadata.ArtStyle is not null)
        {
            AppendHierarchicalTag(xmp, "ArtStyle", metadata.ArtStyle);
        }

        AppendHierarchicalTag(xmp, "Symmetry", composition.Symmetry.ToString());

        foreach (DetectedEntity entity in result.Entities)
        {
            AppendHierarchicalTag(xmp, entity.Category.ToString(), entity.Label);
        }

        if (result.Entities.Count > 0)
        {
            WriteRegions(xmp, result, imageInfo.Width, imageInfo.Height);
        }

        string sidecarPath = SidecarPathResolver.Resolve(imagePath, namingConvention);
        using (FileStream stream = File.Create(sidecarPath))
        {
            XmpMetaFactory.Serialize(xmp, stream);
        }

        return Task.FromResult(sidecarPath);
    }

    private static void AppendHierarchicalTag(IXmpMeta xmp, string category, string leaf)
    {
        xmp.AppendArrayItem(XmpConstants.NsDC, "subject", new PropertyOptions { IsArray = true }, leaf, null);
        xmp.AppendArrayItem(XmpNamespaces.LightroomHierarchical, "hierarchicalSubject", new PropertyOptions { IsArray = true },
            HierarchicalTagPath.Compose(category, leaf, '|'), null);
        // Per the exiv2 digiKam namespace reference, TagsList is XmpSeq (an ordered rdf:Seq),
        // unlike dc:subject/lr:hierarchicalSubject which are XmpBag - digiKam only builds its
        // tag tree from this field correctly when it's serialized as Seq, not Bag.
        xmp.AppendArrayItem(XmpNamespaces.DigiKam, "TagsList", new PropertyOptions { IsArray = true, IsArrayOrdered = true },
            HierarchicalTagPath.Compose(category, leaf, '/'), null);
    }

    private static void WriteRegions(IXmpMeta xmp, ImageAnalysisResult result, int imageWidth, int imageHeight)
    {
        const string mwgNs = MwgNamespaces.Regions;

        xmp.SetProperty(mwgNs, "Regions", null, new PropertyOptions { IsStruct = true });

        string appliedToPath = "Regions" + XmpPathFactory.ComposeStructFieldPath(mwgNs, "AppliedToDimensions");
        xmp.SetProperty(mwgNs, appliedToPath, null, new PropertyOptions { IsStruct = true });
        xmp.SetProperty(mwgNs, appliedToPath + XmpPathFactory.ComposeStructFieldPath(MwgNamespaces.StDimensions, "w"), imageWidth.ToString());
        xmp.SetProperty(mwgNs, appliedToPath + XmpPathFactory.ComposeStructFieldPath(MwgNamespaces.StDimensions, "h"), imageHeight.ToString());
        xmp.SetProperty(mwgNs, appliedToPath + XmpPathFactory.ComposeStructFieldPath(MwgNamespaces.StDimensions, "unit"), "pixel");

        string regionListPath = "Regions" + XmpPathFactory.ComposeStructFieldPath(mwgNs, "RegionList");
        xmp.SetProperty(mwgNs, regionListPath, null, new PropertyOptions { IsArray = true });

        int index = 1;
        foreach (DetectedEntity entity in result.Entities)
        {
            string itemPath = regionListPath + XmpPathFactory.ComposeArrayItemPath("", index);
            xmp.SetProperty(mwgNs, itemPath, null, new PropertyOptions { IsStruct = true });
            xmp.SetProperty(mwgNs, itemPath + XmpPathFactory.ComposeStructFieldPath(mwgNs, "Name"), entity.Label);

            MwgRegionArea area = MwgRegionArea.FromBoundingBox(entity.Box);
            string areaPath = itemPath + XmpPathFactory.ComposeStructFieldPath(mwgNs, "Area");
            xmp.SetProperty(mwgNs, areaPath, null, new PropertyOptions { IsStruct = true });
            xmp.SetProperty(mwgNs, areaPath + XmpPathFactory.ComposeStructFieldPath(MwgNamespaces.StArea, "x"), area.X.ToString("F6", CultureInfo.InvariantCulture));
            xmp.SetProperty(mwgNs, areaPath + XmpPathFactory.ComposeStructFieldPath(MwgNamespaces.StArea, "y"), area.Y.ToString("F6", CultureInfo.InvariantCulture));
            xmp.SetProperty(mwgNs, areaPath + XmpPathFactory.ComposeStructFieldPath(MwgNamespaces.StArea, "w"), area.Width.ToString("F6", CultureInfo.InvariantCulture));
            xmp.SetProperty(mwgNs, areaPath + XmpPathFactory.ComposeStructFieldPath(MwgNamespaces.StArea, "h"), area.Height.ToString("F6", CultureInfo.InvariantCulture));
            xmp.SetProperty(mwgNs, areaPath + XmpPathFactory.ComposeStructFieldPath(MwgNamespaces.StArea, "unit"), "normalized");

            index++;
        }
    }
}
