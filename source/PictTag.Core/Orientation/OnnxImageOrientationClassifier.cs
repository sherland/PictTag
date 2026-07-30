using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PictTag.Core.Orientation;

/// <summary>
/// Classifies image orientation using a dedicated, purpose-built EfficientNetV2-S model
/// (DuarteBarbosa/deep-image-orientation-detection, MIT licensed, 98.82% validation accuracy -
/// see docs/ORIENTATION.md) rather than a vision-language model - this gives a real, meaningful
/// softmax confidence to gate an automatic correction on, unlike an LLM's self-reported
/// confidence. Tries the DirectML execution provider first (GPU acceleration across
/// NVIDIA/AMD/Intel on Windows, no separate CUDA toolkit needed), falling back to CPU on any
/// failure - covers "no GPU", "non-Windows OS", and "driver issue" with one code path.
/// </summary>
public sealed class OnnxImageOrientationClassifier : IImageOrientationClassifier, IDisposable
{
    private const int InputSize = 384; // EfficientNetV2-S pretraining resolution - verified against the model's own config.py
    private const int ResizeSize = InputSize + 32; // 416 - verified against the model's own predict_onnx.py transform pipeline
    private const int CropOffset = (ResizeSize - InputSize) / 2;

    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    private const string ModelFileName = "orientation_model_v2_0.9882.onnx";
    private const string ExpectedSha256 = "cffe911c1dff47fbfbbd90110aaab9c07134645c460d35b3ae8832079bea91ba";
    private static readonly Uri ModelDownloadUri = new(
        "https://huggingface.co/DuarteBarbosa/deep-image-orientation-detection/resolve/main/orientation_model_v2_0.9882.onnx");

    public static readonly OnnxImageOrientationClassifier Shared = new();

    private readonly string _modelPath;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private InferenceSession? _session;

    public OnnxImageOrientationClassifier(string? modelPath = null)
    {
        _modelPath = modelPath ?? ResolveDefaultModelPath();
    }

    public async Task<OrientationPrediction> ClassifyAsync(Image<Rgba32> image, CancellationToken cancellationToken = default)
    {
        InferenceSession session = await EnsureSessionAsync(cancellationToken);

        DenseTensor<float> input = Preprocess(image);
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = session.Run(
            [NamedOnnxValue.CreateFromTensor(session.InputNames[0], input)]);

        float[] logits = outputs.First().AsTensor<float>().ToArray();
        float[] probabilities = Softmax(logits);

        int predictedIndex = 0;
        for (int i = 1; i < probabilities.Length; i++)
        {
            if (probabilities[i] > probabilities[predictedIndex])
            {
                predictedIndex = i;
            }
        }

        return new OrientationPrediction((OrientationClass)predictedIndex, probabilities[predictedIndex]);
    }

    private async Task<InferenceSession> EnsureSessionAsync(CancellationToken ct)
    {
        if (_session is not null)
        {
            return _session;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (_session is not null)
            {
                return _session;
            }

            await EnsureModelDownloadedAsync(_modelPath, ct);
            _session = CreateSession(_modelPath);
            return _session;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static InferenceSession CreateSession(string modelPath)
    {
        try
        {
            SessionOptions dmlOptions = new() { EnableMemoryPattern = false, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            dmlOptions.AppendExecutionProvider_DML(0);
            InferenceSession session = new(modelPath, dmlOptions);
            Console.WriteLine("Orientation classifier: using DirectML (GPU) execution provider.");
            return session;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Orientation classifier: DirectML unavailable ({ex.GetType().Name}: {ex.Message}) - falling back to CPU.");
            SessionOptions cpuOptions = new() { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            return new InferenceSession(modelPath, cpuOptions);
        }
    }

    /// <summary>
    /// Resize to (InputSize+32) x (InputSize+32) [non-aspect-preserving stretch, matching
    /// torchvision's <c>transforms.Resize((h,w))</c> tuple form exactly] then center-crop to
    /// InputSize x InputSize, then normalize with ImageNet mean/std - verified against the
    /// model's own reference predict_onnx.py, not guessed.
    /// </summary>
    private static DenseTensor<float> Preprocess(Image<Rgba32> image)
    {
        using Image<Rgba32> resized = image.Clone(x => x.Resize(new ResizeOptions
        {
            Size = new Size(ResizeSize, ResizeSize),
            Mode = ResizeMode.Stretch,
        }));

        using Image<Rgba32> cropped = resized.Clone(x => x.Crop(new Rectangle(CropOffset, CropOffset, InputSize, InputSize)));

        DenseTensor<float> tensor = new([1, 3, InputSize, InputSize]);
        cropped.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 pixel = row[x];
                    tensor[0, 0, y, x] = (pixel.R / 255f - Mean[0]) / Std[0];
                    tensor[0, 1, y, x] = (pixel.G / 255f - Mean[1]) / Std[1];
                    tensor[0, 2, y, x] = (pixel.B / 255f - Mean[2]) / Std[2];
                }
            }
        });

        return tensor;
    }

    private static float[] Softmax(IReadOnlyList<float> logits)
    {
        float max = logits[0];
        for (int i = 1; i < logits.Count; i++)
        {
            max = Math.Max(max, logits[i]);
        }

        float[] exps = new float[logits.Count];
        float sum = 0f;
        for (int i = 0; i < logits.Count; i++)
        {
            exps[i] = MathF.Exp(logits[i] - max);
            sum += exps[i];
        }

        for (int i = 0; i < exps.Length; i++)
        {
            exps[i] /= sum;
        }

        return exps;
    }

    private static async Task EnsureModelDownloadedAsync(string modelPath, CancellationToken ct)
    {
        if (File.Exists(modelPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(modelPath))!);
        Console.WriteLine($"Downloading orientation classifier model to '{modelPath}' (~80MB, one-time)...");

        using HttpClient client = new();
        byte[] bytes = await client.GetByteArrayAsync(ModelDownloadUri, ct);
        await File.WriteAllBytesAsync(modelPath, bytes, ct);

        string actualSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (actualSha256 != ExpectedSha256)
        {
            Console.WriteLine(
                $"Warning: downloaded orientation model checksum mismatch (expected {ExpectedSha256}, got {actualSha256}). "
                + "If this is an intentional upstream update, update ExpectedSha256 and Get-OrientationModel.ps1.");
        }
    }

    private static string ResolveDefaultModelPath()
    {
        string baseDir = FindRepoRoot(AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
        return Path.Combine(baseDir, "data", "models", "orientation", ModelFileName);
    }

    private static string? FindRepoRoot(string startDir)
    {
        DirectoryInfo? dir = new(startDir);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PictTag.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    public void Dispose() => _session?.Dispose();
}
