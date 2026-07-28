namespace PictTag.Core.Xmp;

public interface IXmpSidecarWriter
{
    /// <summary>Writes an XMP sidecar for <paramref name="imagePath"/> and returns the path written.</summary>
    Task<string> WriteSidecarAsync(
        string imagePath,
        ImageAnalysisResult result,
        XmpSidecarNamingConvention namingConvention,
        CancellationToken ct = default);
}
