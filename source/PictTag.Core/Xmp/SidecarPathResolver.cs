namespace PictTag.Core.Xmp;

public static class SidecarPathResolver
{
    public static string Resolve(string imagePath, XmpSidecarNamingConvention convention) => convention switch
    {
        XmpSidecarNamingConvention.ReplaceExtension => Path.ChangeExtension(imagePath, ".xmp"),
        XmpSidecarNamingConvention.AppendExtension => imagePath + ".xmp",
        _ => throw new ArgumentOutOfRangeException(nameof(convention), convention, null),
    };
}
