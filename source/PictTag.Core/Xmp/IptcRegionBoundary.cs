namespace PictTag.Core.Xmp;

/// <summary>
/// A rectangular region boundary in IPTC ImageRegion form: (X, Y) is the TOP-LEFT corner of
/// the region (unlike <see cref="MwgRegionArea"/>'s center-based X/Y), and all values are
/// normalized 0-1 relative to the image's pixel dimensions. Confirmed against exiftool's own
/// maintained MWG&lt;-&gt;IPTC region conversion logic (config_files/convert_regions.config),
/// which subtracts half the width/height to go from MWG's center point to IPTC's corner.
/// </summary>
public record IptcRegionBoundary(double X, double Y, double Width, double Height)
{
    public static IptcRegionBoundary FromBoundingBox(BoundingBox box) => new(
        X: box.XMin / 1000.0,
        Y: box.YMin / 1000.0,
        Width: (box.XMax - box.XMin) / 1000.0,
        Height: (box.YMax - box.YMin) / 1000.0);
}
