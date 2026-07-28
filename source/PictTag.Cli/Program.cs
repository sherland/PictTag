using System.CommandLine;
using System.Text.Json;
using PictTag.Core;

Option<string> inputOption = new("--input", "-i")
{
    Description = "Path to the input image.",
    DefaultValueFactory = _ => "../../data/test-images/IMG_0922.JPG",
};

Option<string> outputOption = new("--output", "-o")
{
    Description = "Path to write the annotated output image.",
    DefaultValueFactory = _ => "annotated_sample.jpg",
};

Option<string> ollamaUrlOption = new("--ollama-url", "-u")
{
    Description = "Base URL of the Ollama server.",
    DefaultValueFactory = _ => "http://localhost:11434",
};

RootCommand rootCommand = new("PictTag - detect and annotate objects in a photo using a local Ollama vision model.");
rootCommand.Add(inputOption);
rootCommand.Add(outputOption);
rootCommand.Add(ollamaUrlOption);

rootCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    string inputPath = parseResult.GetValue(inputOption)!;
    string outputPath = parseResult.GetValue(outputOption)!;
    string ollamaUrl = parseResult.GetValue(ollamaUrlOption)!;

    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Error: input file not found: {inputPath}");
        return 1;
    }

    ImageDetectionService service = new();
    ImageAnalysisResult result;

    try
    {
        result = await service.ProcessAndAnnotateAsync(inputPath, outputPath, ollamaUrl, ct);
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"Error: could not reach Ollama at {ollamaUrl}: {ex.Message}");
        return 2;
    }
    catch (JsonException ex)
    {
        Console.Error.WriteLine($"Error: could not parse the model's response as JSON: {ex.Message}");
        return 3;
    }

    foreach (DetectedEntity entity in result.Entities)
    {
        Console.WriteLine($"{entity.Label}: ymin={entity.Box.YMin} xmin={entity.Box.XMin} ymax={entity.Box.YMax} xmax={entity.Box.XMax}");
    }

    Console.WriteLine($"Annotated image saved to {outputPath}");
    return 0;
});

ParseResult parsed = rootCommand.Parse(args);
return await parsed.InvokeAsync();
