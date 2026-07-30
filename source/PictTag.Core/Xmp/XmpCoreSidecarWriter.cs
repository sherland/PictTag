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
        registry.RegisterNamespace(XmpNamespaces.IptcCore, "Iptc4xmpCore");
        registry.RegisterNamespace(XmpNamespaces.PictTag, "pictTag");

        ImageInfo imageInfo = Image.Identify(imagePath);

        IXmpMeta xmp = XmpMetaFactory.Create();
        xmp.SetProperty(XmpConstants.NsXmp, "CreatorTool", "PictTag");

        ImageMetadata metadata = result.Metadata;
        xmp.SetLocalizedText(XmpConstants.NsDC, "title", "", "x-default", metadata.Title, null);
        xmp.SetLocalizedText(XmpConstants.NsDC, "description", "", "x-default", metadata.Description, null);
        xmp.SetLocalizedText(XmpNamespaces.IptcCore, "AltTextAccessibility", "", "x-default", metadata.AltText, null);
        xmp.SetLocalizedText(XmpNamespaces.IptcCore, "ExtDescrAccessibility", "", "x-default", metadata.Description, null);

        xmp.SetProperty(XmpNamespaces.PictTag, "Medium", metadata.Medium.ToString());
        if (metadata.ArtStyle is not null)
        {
            // Iptc4xmpExt:Genre is the real IPTC field for artistic/style genre - ArtStyle
            // lives there instead of a custom pictTag property. Genre is a Bag of CVTerm
            // structs, not plain text (verified empirically against exiftool - a bare string
            // write fails with "Improperly formed structure"), so only the free-text
            // CvTermName is populated; CvId/CvTermId/CvTermRefinedAbout are left unset since
            // ArtStyle isn't sourced from a real controlled vocabulary with term IDs.
            xmp.AppendArrayItem(XmpNamespaces.IptcExt, "Genre", new PropertyOptions { IsArray = true }, null, new PropertyOptions { IsStruct = true });
            string genreItemPath = "Genre" + XmpPathFactory.ComposeArrayItemPath("", 1);
            xmp.SetLocalizedText(
                XmpNamespaces.IptcExt, genreItemPath + XmpPathFactory.ComposeStructFieldPath(XmpNamespaces.IptcExt, "CvTermName"),
                "", "x-default", metadata.ArtStyle, null);
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

        WriteScene(xmp, metadata);

        ImageComposition composition = metadata.Composition;
        xmp.SetProperty(XmpNamespaces.PictTag, "Symmetry", composition.Symmetry.ToString());
        xmp.SetProperty(XmpNamespaces.PictTag, "RuleOfThirds", composition.RuleOfThirdsAdherence.ToString());
        xmp.SetProperty(XmpNamespaces.PictTag, "ColorVariance", composition.ColorVarianceEstimate.ToString("F3", CultureInfo.InvariantCulture));
        xmp.SetProperty(XmpNamespaces.PictTag, "EdgeDensity", composition.EdgeDensityEstimate.ToString("F3", CultureInfo.InvariantCulture));
        if (composition.Notes is not null)
        {
            xmp.SetProperty(XmpNamespaces.PictTag, "CompositionNotes", composition.Notes);
        }

        // Medium/ArtStyle/Symmetry are also surfaced as browsable tags (not just pictTag:*/
        // Genre properties) so they show up in digiKam's/Lightroom's tag panel like any other tag.
        AppendHierarchicalTag(xmp, ["Medium", metadata.Medium.ToString()]);
        if (metadata.ArtStyle is not null)
        {
            AppendHierarchicalTag(xmp, ["ArtStyle", HierarchicalTagPath.TitleCase(metadata.ArtStyle)]);
        }

        AppendHierarchicalTag(xmp, ["Symmetry", composition.Symmetry.ToString()]);

        foreach (DetectedEntity entity in result.Entities)
        {
            AppendHierarchicalTag(xmp, HierarchicalTagPath.BuildSegments(entity.Category.ToString(), entity.Group, entity.Label));
        }

        if (result.Entities.Count > 0)
        {
            WriteRegions(xmp, result, imageInfo.Width, imageInfo.Height);
            WriteImageRegions(xmp, result);
        }

        string sidecarPath = SidecarPathResolver.Resolve(imagePath, namingConvention);
        using (FileStream stream = File.Create(sidecarPath))
        {
            XmpMetaFactory.Serialize(xmp, stream);
        }

        return Task.FromResult(sidecarPath);
    }

    private static void WriteScene(IXmpMeta xmp, ImageMetadata metadata)
    {
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
            xmp.AppendArrayItem(XmpNamespaces.IptcCore, "Scene", new PropertyOptions { IsArray = true }, IptcSceneCode.ForSceneType(scene), null);
        }
    }

    private static void AppendHierarchicalTag(IXmpMeta xmp, string[] segments)
    {
        xmp.AppendArrayItem(XmpConstants.NsDC, "subject", new PropertyOptions { IsArray = true }, segments[^1], null);
        xmp.AppendArrayItem(XmpNamespaces.LightroomHierarchical, "hierarchicalSubject", new PropertyOptions { IsArray = true },
            HierarchicalTagPath.Compose('|', segments), null);
        // Per the exiv2 digiKam namespace reference, TagsList is XmpSeq (an ordered rdf:Seq),
        // unlike dc:subject/lr:hierarchicalSubject which are XmpBag - digiKam only builds its
        // tag tree from this field correctly when it's serialized as Seq, not Bag.
        xmp.AppendArrayItem(XmpNamespaces.DigiKam, "TagsList", new PropertyOptions { IsArray = true, IsArrayOrdered = true },
            HierarchicalTagPath.Compose('/', segments), null);
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

    /// <summary>
    /// Iptc4xmpExt:ImageRegion - IPTC's own, more modern region structure, written alongside
    /// (not instead of) mwg-rs:Regions above. Field names/types and the RegionBoundary
    /// top-left-corner coordinate convention were verified empirically against exiftool's own
    /// built-in tag tables and its maintained MWG&lt;-&gt;IPTC region conversion logic - not
    /// guessed. rCtype/rRole (content-type/role sub-structs) are omitted: no real controlled
    /// vocabulary URIs for them were verified, and guessing at IDs would be worse than leaving
    /// them out.
    /// </summary>
    private static void WriteImageRegions(IXmpMeta xmp, ImageAnalysisResult result)
    {
        const string ns = XmpNamespaces.IptcExt;
        xmp.SetProperty(ns, "ImageRegion", null, new PropertyOptions { IsArray = true });

        int index = 1;
        foreach (DetectedEntity entity in result.Entities)
        {
            string itemPath = "ImageRegion" + XmpPathFactory.ComposeArrayItemPath("", index);
            xmp.SetProperty(ns, itemPath, null, new PropertyOptions { IsStruct = true });
            xmp.SetProperty(ns, itemPath + XmpPathFactory.ComposeStructFieldPath(ns, "rId"), index.ToString(CultureInfo.InvariantCulture));
            xmp.SetLocalizedText(ns, itemPath + XmpPathFactory.ComposeStructFieldPath(ns, "Name"), "", "x-default", entity.Label, null);

            IptcRegionBoundary boundary = IptcRegionBoundary.FromBoundingBox(entity.Box);
            string boundaryPath = itemPath + XmpPathFactory.ComposeStructFieldPath(ns, "RegionBoundary");
            xmp.SetProperty(ns, boundaryPath, null, new PropertyOptions { IsStruct = true });
            xmp.SetProperty(ns, boundaryPath + XmpPathFactory.ComposeStructFieldPath(ns, "rbShape"), "rectangle");
            xmp.SetProperty(ns, boundaryPath + XmpPathFactory.ComposeStructFieldPath(ns, "rbUnit"), "relative");
            xmp.SetProperty(ns, boundaryPath + XmpPathFactory.ComposeStructFieldPath(ns, "rbX"), boundary.X.ToString("F6", CultureInfo.InvariantCulture));
            xmp.SetProperty(ns, boundaryPath + XmpPathFactory.ComposeStructFieldPath(ns, "rbY"), boundary.Y.ToString("F6", CultureInfo.InvariantCulture));
            xmp.SetProperty(ns, boundaryPath + XmpPathFactory.ComposeStructFieldPath(ns, "rbW"), boundary.Width.ToString("F6", CultureInfo.InvariantCulture));
            xmp.SetProperty(ns, boundaryPath + XmpPathFactory.ComposeStructFieldPath(ns, "rbH"), boundary.Height.ToString("F6", CultureInfo.InvariantCulture));

            index++;
        }
    }
}
