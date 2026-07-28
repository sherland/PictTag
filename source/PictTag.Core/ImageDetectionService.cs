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
        Detect every distinct, salient object in this image (e.g. people, animals, vehicles,
        furniture, notable items) - not the scene or background as a whole. Do not merge
        multiple objects into one entry, and do not describe the sky, lighting, or overall
        composition as if it were an object. For each detected object, give a short lowercase
        label and its bounding box on a 0-1000 integer grid, where (0,0) is the top-left
        corner and (1000,1000) is the bottom-right corner.
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
                schemaName: "ObjectDetections",
                schemaDescription: "Bounding boxes for every distinct object detected in the image."),
            Temperature = 0.2f,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["num_ctx"] = 16384,
            },
        };

        ChatResponse response = await client.GetResponseAsync(messages, options, ct);

        JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
        DetectionResponseDto parsed = JsonSerializer.Deserialize<DetectionResponseDto>(response.Text, jsonOptions)
            ?? throw new InvalidOperationException("Model returned no parseable detections.");

        List<DetectedEntity> entities = parsed.Detections
            .Select(d => new DetectedEntity(d.Label, new BoundingBox(d.YMin, d.XMin, d.YMax, d.XMax)))
            .ToList();

        return new ImageAnalysisResult(entities);
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

    private record DetectionResponseDto(List<DetectionDto> Detections);

    private record DetectionDto(
        string Label,
        [property: JsonPropertyName("ymin")] int YMin,
        [property: JsonPropertyName("xmin")] int XMin,
        [property: JsonPropertyName("ymax")] int YMax,
        [property: JsonPropertyName("xmax")] int XMax);
}
