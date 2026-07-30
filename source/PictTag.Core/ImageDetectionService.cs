using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using OllamaSharp;
using PictTag.Core.Orientation;
using PictTag.Core.Taxonomy;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IoPath = System.IO.Path;

namespace PictTag.Core;

public class ImageDetectionService
{
    private static readonly string[] PreferredFontFamilies =
    [
        "Segoe UI", "Arial", "Liberation Sans", "DejaVu Sans", "Verdana",
    ];

    private readonly EntityTaxonomyResolver _taxonomyResolver;
    private readonly ImageOrientationCorrector _orientationCorrector;
    private readonly bool _fixOriginalFileOrientation;

    /// <summary>
    /// Defaults to the shared WordNet-derived taxonomy and its Ollama-backed embedding fallback
    /// (see PictTag.Core.Taxonomy), and the shared ONNX-based orientation classifier (see
    /// PictTag.Core.Orientation) - pass explicit instances to use a different/tuned taxonomy
    /// dataset, a non-default Ollama server, or a different orientation classifier without
    /// touching detection logic. <paramref name="fixOriginalFileOrientation"/> controls whether a
    /// confidently-wrong EXIF Orientation tag gets corrected on the original file itself
    /// (metadata-only, non-destructive) - the image is always used correctly-oriented internally
    /// regardless of this setting.
    /// </summary>
    public ImageDetectionService(
        ITaxonomyProvider? taxonomyProvider = null,
        ITaxonomyEmbeddingIndex? embeddingIndex = null,
        IImageOrientationClassifier? orientationClassifier = null,
        bool fixOriginalFileOrientation = true)
    {
        _taxonomyResolver = new EntityTaxonomyResolver(
            taxonomyProvider ?? WordNetTaxonomyProvider.Shared.Value,
            embeddingIndex ?? OllamaTaxonomyEmbeddingIndex.Shared.Value);
        _orientationCorrector = new ImageOrientationCorrector(orientationClassifier ?? OnnxImageOrientationClassifier.Shared);
        _fixOriginalFileOrientation = fixOriginalFileOrientation;
    }

    private const string DetectionPrompt = """
        Analyze this image and respond with:

        - title: a short, specific caption (a few words).
        - description: 2-4 sentences describing what the image shows.
        - altText: a short (one sentence, well under 250 characters) accessibility caption for
          a screen reader - distinct from description, not just a truncated copy of it.
        - medium: the visual medium of the image itself - Photograph, Screenshot, Painting,
          Drawing, DigitalIllustration, ThreeDRender, or Other.
        - artStyle: only if medium is an art form (Painting, Drawing, DigitalIllustration, or
          ThreeDRender), a short description of the art style (e.g. "impressionism", "anime",
          "pixel art"); otherwise omit it. Do not invent a style for a plain photograph.
        - setting: whether the scene is Indoor, Outdoor, Studio, or Unknown if it cannot be
          determined from the image.
        - scene: zero or more terms describing how the image is framed/composed, from: Headshot,
          HalfLength, FullLength, Profile, RearView, Single, Couple, Two, Group, GeneralView,
          PanoramicView, AerialView, UnderWater, NightScene, Satellite, ExteriorView,
          InteriorView, CloseUp, Action, Performing, Posing, Symbolic, OffBeat, MovieScene.
          Pick every term that genuinely applies (e.g. a photo can be both Group and Posing);
          leave empty if none clearly fit rather than forcing a weak match.
        - composition: your subjective visual impression of the image's composition, not a
          precise measurement. Give:
            - symmetry: Symmetrical, Asymmetrical, RadialSymmetry, or None if not applicable.
            - ruleOfThirdsAdherence: true if the main subject/horizon roughly aligns with the
              rule-of-thirds grid lines or intersections, false otherwise.
            - colorVarianceEstimate: a rough 0.0-1.0 impression of how uniform (near 0.0) vs.
              how colorful/varied (near 1.0) the palette looks.
            - edgeDensityEstimate: a rough 0.0-1.0 impression of how visually simple/sparse
              (near 0.0) vs. busy/detailed (near 1.0) the image looks.
            - notes: an optional short phrase about anything else notable (e.g. "diagonal
              leading lines", "centered subject"); omit if nothing stands out.
          This applies to every image, not just paintings or drawings - photographs have
          composition too.
        - detections: every distinct, salient object in the image (e.g. people, animals,
          vehicles, furniture, notable items) - not the scene or background as a whole. Do
          not merge multiple objects into one entry, and do not describe the sky, lighting,
          or overall composition as if it were an object. For each detected object, give:
            - label: a short, specific, lowercase description (e.g. "golden retriever", not
              just "animal"). Prefer the standard dictionary/common name for the object as it
              would appear in a field guide or catalog (e.g. "golden retriever" over "dog with
              golden fur"; a single standard noun over an invented compound phrase when one
              exists) - this label is looked up against a fixed vocabulary afterward, so
              standard naming matters more than creative phrasing.
            - group: a more general term for what kind of thing this specifically is - broader
              than label, narrower than category (e.g. "golden retriever" -> "dog", "angel" ->
              "religious figure"). Repeat the label if nothing more general genuinely applies.
            - category: the single broad category it belongs to - People, Animals, Vehicles,
              Buildings, Nature, Food, Objects, Text, Art, or Other.
            - its bounding box on a 0-1000 integer grid, where (0,0) is the top-left corner
              and (1000,1000) is the bottom-right corner.
        """;

    public async Task<ImageAnalysisResult> DetectAsync(
        string inputPath,
        string ollamaUrl = "http://localhost:11434",
        CancellationToken ct = default)
    {
        (Image<Rgba32> image, ImageAnalysisResult result) = await DetectCoreAsync(inputPath, ollamaUrl, ct);
        image.Dispose();
        return result;
    }

    public async Task<ImageAnalysisResult> ProcessAndAnnotateAsync(
        string inputPath,
        string outputPath,
        string ollamaUrl = "http://localhost:11434",
        CancellationToken ct = default)
    {
        (Image<Rgba32> image, ImageAnalysisResult result) = await DetectCoreAsync(inputPath, ollamaUrl, ct);
        using (image)
        {
            Font? labelFont = TryGetLabelFont(Math.Max(14f, image.Height / 60f));

            Pen boxPen = Pens.Solid(Color.LimeGreen, 3f);
            SolidBrush labelBrush = new(Color.LimeGreen);

            image.Mutate(ctx => ctx.Paint(canvas =>
            {
                foreach (DetectedEntity entity in result.Entities)
                {
                    RectanglePolygon rect = ToPixelRectangle(entity.Box, image.Width, image.Height);
                    canvas.Draw(boxPen, rect);

                    if (labelFont is not null)
                    {
                        PointF labelPosition = new(rect.Location.X, Math.Max(0, rect.Location.Y - labelFont.Size - 4));
                        RichTextOptions textOptions = new(labelFont) { Origin = labelPosition };
                        canvas.DrawText(textOptions, entity.Raw.Label, labelBrush, pen: null);
                    }
                }
            }));

            Directory.CreateDirectory(IoPath.GetDirectoryName(IoPath.GetFullPath(outputPath))!);
            await image.SaveAsync(outputPath, ct);
        }

        return result;
    }

    /// <summary>
    /// The real work: correct orientation (see PictTag.Core.Orientation), send the corrected
    /// image to the model, resolve taxonomy, and return the corrected <see cref="Image{Rgba32}"/>
    /// alongside the result so <see cref="ProcessAndAnnotateAsync"/> can draw boxes on the exact
    /// same image the model actually analyzed - without reloading the file or re-running the
    /// orientation classifier a second time. The caller owns disposing the returned image.
    /// </summary>
    private async Task<(Image<Rgba32> Image, ImageAnalysisResult Result)> DetectCoreAsync(
        string inputPath, string ollamaUrl, CancellationToken ct)
    {
        OrientationCorrectionResult orientation = await _orientationCorrector.CorrectAsync(inputPath, _fixOriginalFileOrientation, ct);
        Image<Rgba32> image = orientation.Image;

        byte[] imageBytes = await EncodeToJpegBytesAsync(image, ct);

        IChatClient client = new OllamaApiClient(new Uri(ollamaUrl), "gemma4:26b");

        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.User,
            [
                new TextContent(DetectionPrompt),
                new DataContent(imageBytes, "image/jpeg"),
            ]),
        ];

        ChatOptions options = new()
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema<DetectionResponseDto>(
                schemaName: "ImageAnalysis",
                schemaDescription: "Title, description, medium/style, and every distinct object detected in the image."),
            Temperature = 0.2f,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["num_ctx"] = 16384,
            },
        };

        ChatResponse response = await client.GetResponseAsync(messages, options, ct);

        JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        DetectionResponseDto parsed = JsonSerializer.Deserialize<DetectionResponseDto>(response.Text, jsonOptions)
            ?? throw new InvalidOperationException("Model returned no parseable detections.");

        List<DetectedEntity> entities = [];
        foreach (DetectionDto d in parsed.Detections)
        {
            RawDetection raw = new(d.Label, d.Group, d.Category);
            TaxonomyMatch? taxonomy = await _taxonomyResolver.ResolveAsync(raw, ct);
            entities.Add(new DetectedEntity(raw, taxonomy, new BoundingBox(d.YMin, d.XMin, d.YMax, d.XMax)));
        }

        ImageComposition composition = new(
            parsed.Composition.Symmetry,
            parsed.Composition.RuleOfThirdsAdherence,
            parsed.Composition.ColorVarianceEstimate,
            parsed.Composition.EdgeDensityEstimate,
            parsed.Composition.Notes);

        ImageMetadata metadata = new(
            parsed.Title, parsed.Description, parsed.AltText, parsed.Medium, parsed.ArtStyle, parsed.Setting, parsed.Scene, composition);

        ImageAnalysisResult result = new(metadata, entities, image.Width, image.Height);
        return (image, result);
    }

    private static async Task<byte[]> EncodeToJpegBytesAsync(Image<Rgba32> image, CancellationToken ct)
    {
        using MemoryStream stream = new();
        await image.SaveAsync(stream, new JpegEncoder { Quality = 90 }, ct);
        return stream.ToArray();
    }

    private static RectanglePolygon ToPixelRectangle(BoundingBox box, int imageWidth, int imageHeight)
    {
        float x = box.XMin / 1000f * imageWidth;
        float y = box.YMin / 1000f * imageHeight;
        float width = (box.XMax - box.XMin) / 1000f * imageWidth;
        float height = (box.YMax - box.YMin) / 1000f * imageHeight;
        return new RectanglePolygon(x, y, width, height);
    }

    private static Font? TryGetLabelFont(float size)
    {
        foreach (string familyName in PreferredFontFamilies)
        {
            if (SystemFonts.TryGet(familyName, out FontFamily family))
            {
                return family.CreateFont(size, FontStyle.Bold);
            }
        }

        return SystemFonts.Families.Any()
            ? SystemFonts.Families.First().CreateFont(size, FontStyle.Bold)
            : null;
    }

    private record DetectionResponseDto(
        string Title,
        string Description,
        string AltText,
        ImageMedium Medium,
        string? ArtStyle,
        ImageSetting? Setting,
        List<SceneType> Scene,
        CompositionDto Composition,
        List<DetectionDto> Detections);

    private record CompositionDto(
        CompositionSymmetry Symmetry,
        bool RuleOfThirdsAdherence,
        double ColorVarianceEstimate,
        double EdgeDensityEstimate,
        string? Notes);

    private record DetectionDto(
        string Label,
        string Group,
        EntityCategory Category,
        [property: JsonPropertyName("ymin")] int YMin,
        [property: JsonPropertyName("xmin")] int XMin,
        [property: JsonPropertyName("ymax")] int YMax,
        [property: JsonPropertyName("xmax")] int XMax);
}
