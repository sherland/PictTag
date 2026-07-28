using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using PictTag.Core;
using PictTag.Core.Xmp;

Option<string> inputOption = new("--input", "-i")
{
    Description = "Path or glob pattern for the input image(s), e.g. 'photo.jpg' or 'data/photos/**/*.jpg'.",
    DefaultValueFactory = _ => "../../data/test-images/IMG_0922.JPG",
};

Option<string> outputOption = new("--output", "-o")
{
    Description = "Path to write the annotated output image. If --input matches more than one file, this is treated as a directory instead.",
    DefaultValueFactory = _ => "annotated_sample.jpg",
};

Option<string> ollamaUrlOption = new("--ollama-url", "-u")
{
    Description = "Base URL of the Ollama server.",
    DefaultValueFactory = _ => "http://localhost:11434",
};

Option<bool> xmpOption = new("--xmp")
{
    Description = "Write an XMP sidecar file alongside each input image.",
};

Option<XmpSidecarNamingConvention> xmpNamingOption = new("--xmp-naming")
{
    Description = "Sidecar naming convention: 'replace' (photo.xmp, Adobe-style, default) or 'append' (photo.jpg.xmp, digiKam-style).",
    HelpName = "replace|append",
    DefaultValueFactory = _ => XmpSidecarNamingConvention.ReplaceExtension,
    CustomParser = result =>
    {
        string token = result.Tokens[0].Value;
        return token switch
        {
            "replace" => XmpSidecarNamingConvention.ReplaceExtension,
            "append" => XmpSidecarNamingConvention.AppendExtension,
            _ => Invalid<XmpSidecarNamingConvention>(result, "--xmp-naming", token, "replace", "append"),
        };
    },
};

Option<string> xmpEngineOption = new("--xmp-engine")
{
    Description = "XMP sidecar writer engine: 'xmpcore' (default, pure .NET) or 'exiftool' (shells out to the exiftool binary).",
    DefaultValueFactory = _ => "xmpcore",
};
xmpEngineOption.AcceptOnlyFromAmong("xmpcore", "exiftool");

Option<bool> xmpOverwriteOption = new("--xmp-overwrite")
{
    Description = "Regenerate the XMP sidecar even if one already exists for an input file. Default is to skip files that already have a sidecar.",
};

RootCommand rootCommand = new("PictTag - detect and annotate objects in a photo using a local Ollama vision model.");
rootCommand.Add(inputOption);
rootCommand.Add(outputOption);
rootCommand.Add(ollamaUrlOption);
rootCommand.Add(xmpOption);
rootCommand.Add(xmpNamingOption);
rootCommand.Add(xmpEngineOption);
rootCommand.Add(xmpOverwriteOption);

rootCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    string inputPattern = parseResult.GetValue(inputOption)!;
    string outputPath = parseResult.GetValue(outputOption)!;
    string ollamaUrl = parseResult.GetValue(ollamaUrlOption)!;
    bool writeXmp = parseResult.GetValue(xmpOption);
    XmpSidecarNamingConvention namingConvention = parseResult.GetValue(xmpNamingOption);
    string engine = parseResult.GetValue(xmpEngineOption)!;
    bool xmpOverwrite = parseResult.GetValue(xmpOverwriteOption);

    IReadOnlyList<string> inputFiles = ResolveInputFiles(inputPattern);
    if (inputFiles.Count == 0)
    {
        Console.Error.WriteLine($"Error: no files matched '{inputPattern}'.");
        return 1;
    }

    bool isBatch = inputFiles.Count > 1;
    if (isBatch)
    {
        Directory.CreateDirectory(outputPath);
    }

    ImageDetectionService service = new();
    IXmpSidecarWriter? xmpWriter = writeXmp
        ? (engine == "exiftool" ? new ExifToolSidecarWriter() : new XmpCoreSidecarWriter())
        : null;

    bool anyFailed = false;

    foreach (string inputPath in inputFiles)
    {
        if (isBatch)
        {
            Console.WriteLine($"== {inputPath} ==");
        }

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: input file not found: {inputPath}");
            anyFailed = true;
            continue;
        }

        if (writeXmp && !xmpOverwrite)
        {
            string existingSidecarPath = SidecarPathResolver.Resolve(inputPath, namingConvention);
            if (File.Exists(existingSidecarPath))
            {
                Console.WriteLine($"Skipping (sidecar already exists): {existingSidecarPath}");
                continue;
            }
        }

        string effectiveOutputPath = isBatch ? Path.Combine(outputPath, Path.GetFileName(inputPath)) : outputPath;

        ImageAnalysisResult result;
        try
        {
            result = await service.ProcessAndAnnotateAsync(inputPath, effectiveOutputPath, ollamaUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Error: could not reach Ollama at {ollamaUrl}: {ex.Message}");
            anyFailed = true;
            continue;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Error: could not parse the model's response as JSON: {ex.Message}");
            anyFailed = true;
            continue;
        }

        foreach (DetectedEntity entity in result.Entities)
        {
            Console.WriteLine($"{entity.Label}: ymin={entity.Box.YMin} xmin={entity.Box.XMin} ymax={entity.Box.YMax} xmax={entity.Box.XMax}");
        }

        Console.WriteLine($"Annotated image saved to {effectiveOutputPath}");

        if (xmpWriter is not null)
        {
            try
            {
                string sidecarPath = await xmpWriter.WriteSidecarAsync(inputPath, result, namingConvention, ct);
                Console.WriteLine($"XMP sidecar written to {sidecarPath}");
            }
            catch (ExifToolNotFoundException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                anyFailed = true;
            }
        }
    }

    return anyFailed ? 1 : 0;
});

ParseResult parsed = rootCommand.Parse(args);
return await parsed.InvokeAsync();

static IReadOnlyList<string> ResolveInputFiles(string pattern)
{
    string root = Environment.CurrentDirectory;
    Matcher matcher = new();
    matcher.AddInclude(pattern);
    PatternMatchingResult matchResult = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(root)));
    return matchResult.Files.Select(f => Path.GetFullPath(Path.Combine(root, f.Path))).ToList();
}

static T Invalid<T>(ArgumentResult result, string optionName, string token, params string[] validValues)
{
    result.AddError($"Invalid value '{token}' for {optionName}. Expected one of: {string.Join(", ", validValues)}.");
    return default!;
}
