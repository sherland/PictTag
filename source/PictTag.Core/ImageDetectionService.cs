using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using OllamaSharp;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
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
              just "animal").
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
        byte[] imageBytes = await File.ReadAllBytesAsync(inputPath, ct);
        string mimeType = GetMimeType(inputPath);

        IChatClient client = new OllamaApiClient(new Uri(ollamaUrl), "gemma4:26b");

        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.User,
            [
                new TextContent(DetectionPrompt),
                new DataContent(imageBytes, mimeType),
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

        List<DetectedEntity> entities = parsed.Detections
            .Select(d => new DetectedEntity(d.Label, d.Category, new BoundingBox(d.YMin, d.XMin, d.YMax, d.XMax)))
            .ToList();

        ImageComposition composition = new(
            parsed.Composition.Symmetry,
            parsed.Composition.RuleOfThirdsAdherence,
            parsed.Composition.ColorVarianceEstimate,
            parsed.Composition.EdgeDensityEstimate,
            parsed.Composition.Notes);

        ImageMetadata metadata = new(
            parsed.Title, parsed.Description, parsed.AltText, parsed.Medium, parsed.ArtStyle, parsed.Setting, parsed.Scene, composition);

        return new ImageAnalysisResult(metadata, entities);
    }

    public async Task<ImageAnalysisResult> ProcessAndAnnotateAsync(
        string inputPath,
        string outputPath,
        string ollamaUrl = "http://localhost:11434",
        CancellationToken ct = default)
    {
        ImageAnalysisResult result = await DetectAsync(inputPath, ollamaUrl, ct);

        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(inputPath, ct);

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
                    canvas.DrawText(textOptions, entity.Label, labelBrush, pen: null);
                }
            }
        }));

        Directory.CreateDirectory(IoPath.GetDirectoryName(IoPath.GetFullPath(outputPath))!);
        await image.SaveAsync(outputPath, ct);

        return result;
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

    private static string GetMimeType(string path) => IoPath.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => "image/jpeg",
    };

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
        EntityCategory Category,
        [property: JsonPropertyName("ymin")] int YMin,
        [property: JsonPropertyName("xmin")] int XMin,
        [property: JsonPropertyName("ymax")] int YMax,
        [property: JsonPropertyName("xmax")] int XMax);
}
