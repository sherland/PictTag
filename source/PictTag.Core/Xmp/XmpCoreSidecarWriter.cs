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

        ImageInfo imageInfo = Image.Identify(imagePath);

        IXmpMeta xmp = XmpMetaFactory.Create();
        xmp.SetProperty(XmpConstants.NsXmp, "CreatorTool", "PictTag");

        foreach (DetectedEntity entity in result.Entities)
        {
            xmp.AppendArrayItem(XmpConstants.NsDC, "subject", new PropertyOptions { IsArray = true }, entity.Label, null);
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
