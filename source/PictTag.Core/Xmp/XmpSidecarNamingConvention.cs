namespace PictTag.Core.Xmp;

public enum XmpSidecarNamingConvention
{
    /// <summary>Replaces the image extension, e.g. "photo.jpg" -> "photo.xmp" (Adobe/Lightroom convention).</summary>
    ReplaceExtension,

    /// <summary>Appends to the full filename, e.g. "photo.jpg" -> "photo.jpg.xmp" (digiKam/darktable convention).</summary>
    AppendExtension,
}
