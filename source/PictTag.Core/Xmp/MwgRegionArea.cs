namespace PictTag.Core.Xmp;

/// <summary>
/// A region area in MWG (Metadata Working Group) normalized form: (X, Y) is the CENTER
/// of the region, not the top-left corner, and all values are normalized 0-1 relative to
/// the image's pixel dimensions.
/// </summary>
public record MwgRegionArea(double X, double Y, double Width, double Height)
{
    public static MwgRegionArea FromBoundingBox(BoundingBox box) => new(
        X: (box.XMin + box.XMax) / 2000.0,
        Y: (box.YMin + box.YMax) / 2000.0,
        Width: (box.XMax - box.XMin) / 1000.0,
        Height: (box.YMax - box.YMin) / 1000.0);
}
