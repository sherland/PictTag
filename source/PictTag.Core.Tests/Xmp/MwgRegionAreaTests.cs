using PictTag.Core.Xmp;

namespace PictTag.Core.Tests.Xmp;

public class MwgRegionAreaTests
{
    [Fact]
    public void FromBoundingBox_FullImage_ProducesCenteredUnitArea()
    {
        BoundingBox box = new(YMin: 0, XMin: 0, YMax: 1000, XMax: 1000);

        MwgRegionArea area = MwgRegionArea.FromBoundingBox(box);

        Assert.Equal(0.5, area.X, precision: 6);
        Assert.Equal(0.5, area.Y, precision: 6);
        Assert.Equal(1.0, area.Width, precision: 6);
        Assert.Equal(1.0, area.Height, precision: 6);
    }

    [Fact]
    public void FromBoundingBox_TopLeftQuadrant_ProducesExpectedCenterAndSize()
    {
        // A box spanning the top-left quarter of the image: x in [0,500], y in [0,500].
        BoundingBox box = new(YMin: 0, XMin: 0, YMax: 500, XMax: 500);

        MwgRegionArea area = MwgRegionArea.FromBoundingBox(box);

        Assert.Equal(0.25, area.X, precision: 6);
        Assert.Equal(0.25, area.Y, precision: 6);
        Assert.Equal(0.5, area.Width, precision: 6);
        Assert.Equal(0.5, area.Height, precision: 6);
    }

    [Fact]
    public void FromBoundingBox_OffCenterBox_ProducesExpectedCenterAndSize()
    {
        // Matches the real "chimney" detection from Phase 1: ymin=897 xmin=906 ymax=1000 xmax=974.
        BoundingBox box = new(YMin: 897, XMin: 906, YMax: 1000, XMax: 974);

        MwgRegionArea area = MwgRegionArea.FromBoundingBox(box);

        Assert.Equal((906 + 974) / 2000.0, area.X, precision: 6);
        Assert.Equal((897 + 1000) / 2000.0, area.Y, precision: 6);
        Assert.Equal((974 - 906) / 1000.0, area.Width, precision: 6);
        Assert.Equal((1000 - 897) / 1000.0, area.Height, precision: 6);
    }
}
