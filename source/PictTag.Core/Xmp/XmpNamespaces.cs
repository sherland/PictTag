namespace PictTag.Core.Xmp;

internal static class XmpNamespaces
{
    /// <summary>Lightroom's hierarchical-subject namespace. Values are '|'-separated paths.</summary>
    public const string LightroomHierarchical = "http://ns.adobe.com/lightroom/1.0/";

    /// <summary>digiKam's tag-list namespace. Values are '/'-separated paths.</summary>
    public const string DigiKam = "http://www.digikam.org/ns/1.0/";

    /// <summary>IPTC Extension namespace, home of DigitalSourceType.</summary>
    public const string IptcExt = "http://iptc.org/std/Iptc4xmpExt/2008-02-29/";

    /// <summary>PictTag's own namespace for fields no XMP standard covers (medium, art style, setting).</summary>
    public const string PictTag = "https://github.com/sherland/PictTag/ns/1.0/";
}
