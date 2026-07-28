using PictTag.Core.Xmp;

namespace PictTag.Core.Tests.Xmp;

public class SidecarPathResolverTests
{
    [Theory]
    [InlineData(@"C:\photos\IMG_0922.JPG", XmpSidecarNamingConvention.ReplaceExtension, @"C:\photos\IMG_0922.xmp")]
    [InlineData(@"C:\photos\IMG_0922.JPG", XmpSidecarNamingConvention.AppendExtension, @"C:\photos\IMG_0922.JPG.xmp")]
    [InlineData("photo.jpg", XmpSidecarNamingConvention.ReplaceExtension, "photo.xmp")]
    [InlineData("photo.jpg", XmpSidecarNamingConvention.AppendExtension, "photo.jpg.xmp")]
    [InlineData("noext", XmpSidecarNamingConvention.ReplaceExtension, "noext.xmp")]
    [InlineData("noext", XmpSidecarNamingConvention.AppendExtension, "noext.xmp")]
    public void Resolve_ProducesExpectedSidecarPath(string imagePath, XmpSidecarNamingConvention convention, string expected)
    {
        string actual = SidecarPathResolver.Resolve(imagePath, convention);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Resolve_InvalidConvention_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SidecarPathResolver.Resolve("photo.jpg", (XmpSidecarNamingConvention)999));
    }
}
