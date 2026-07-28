namespace PictTag.Core.Xmp;

/// <summary>
/// Maps <see cref="ImageMedium"/> to the real IPTC 2025 DigitalSourceType controlled
/// vocabulary (https://cv.iptc.org/newscodes/digitalsourcetype), which only covers
/// capture provenance (real photograph vs screen capture vs AI-generated, etc.) - there
/// is no IPTC code for painting/drawing/digital-art, so those map to null.
/// </summary>
internal static class IptcDigitalSourceType
{
    public static string? ForMedium(ImageMedium medium) => medium switch
    {
        ImageMedium.Photograph => "https://cv.iptc.org/newscodes/digitalsourcetype/digitalCapture",
        ImageMedium.Screenshot => "https://cv.iptc.org/newscodes/digitalsourcetype/screenCapture",
        _ => null,
    };
}
