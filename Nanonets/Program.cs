using System.Diagnostics;
using System.Text;
using ErgoX.VecraX.GgufX.Abstractions.Model;
using ErgoX.VecraX.GgufX.Backend;
using ErgoX.VecraX.GgufX.Core;
using ErgoX.VecraX.GgufX.Native;
using ErgoX.VecraX.GgufX.Sampling;
using ErgoX.VecraX.GgufX.Utilities;

// Configure Vulkan backend for better GPU performance
BackendConfiguration.Configure(new BackendSelectionOptions(BackendType.Vulkan, BackendSelectionMode.Safe));

var cancellationToken = CancellationToken.None;
var repositoryRoot = LocateRepositoryRoot();
if (repositoryRoot is null)
{
    Console.Error.WriteLine("Failed to locate repository root. Ensure the example runs from within the repo.");
    return 1;
}

var modelPath = Path.Combine(repositoryRoot, ".models", "Nanonets-OCR-s-GGUF", "Nanonets-OCR-s-UD-Q6_K_XL.gguf");
var projectorPath = Path.Combine(repositoryRoot, ".models", "Nanonets-OCR-s-GGUF", "mmproj-F16.gguf");
var inputsDirectory = Path.Combine(repositoryRoot, "examples", "Nanonets", "Inputs");
var outputDirectory = Path.Combine(repositoryRoot, "examples", "Nanonets", "Output");

if (!File.Exists(modelPath))
{
    Console.Error.WriteLine($"Model file not found: {modelPath}");
    Console.Error.WriteLine("Please ensure the Nanonets OCR model is downloaded to .models/Nanonets-OCR-s-GGUF/");
    return 1;
}

if (!File.Exists(projectorPath))
{
    Console.Error.WriteLine($"Projector file not found: {projectorPath}");
    return 1;
}

if (!Directory.Exists(inputsDirectory))
{
    Console.Error.WriteLine($"Inputs directory not found: {inputsDirectory}");
    Console.Error.WriteLine("Please add images to examples/Nanonets/Inputs/");
    return 1;
}

Directory.CreateDirectory(outputDirectory);

var generationConfig = GenerationConfig.Precise with
{
    MaxTokens = 8192,
    Temperature = 0.2f,
    TopP = 0.95f,
    SamplingStrategy = SamplingStrategy.TopP,
    StopSequences = new List<string> { "<|im_end|>", "<|end_of_text|>" }
};

// WORKAROUND: Disable Vulkan to avoid llama.cpp bug with multimodal+Vulkan
// Issue: llama_get_logits_ith returns null after mtmd_helper_eval_chunks with Vulkan compute backend
// Setting this BEFORE any llama.cpp calls prevents Vulkan backend initialization
Environment.SetEnvironmentVariable("GGML_VK_VISIBLE_DEVICES", "-1");

var modelOptions = new ModelOptions
{
    GpuLayerCount = 0,  // CPU-only mode
    UseMemoryLock = false
};

Console.WriteLine("Loading Nanonets OCR model (CPU mode - Vulkan disabled)...");
using var model = Model.Load(modelPath, modelOptions);

var projectorOptions = new MultimodalProjectorOptions
{
    Verbosity = NativeLogLevel.Debug  // Enable debug logging to see native mtmd details
};
using var projector = model.CreateMultimodalProjector(projectorPath, projectorOptions);

Console.WriteLine($"Loaded model: {modelPath}");
Console.WriteLine($"Loaded projector: {projectorPath}");
Console.WriteLine($"Media marker: {projector.MediaMarker}");
Console.WriteLine($"Supports vision: {projector.SupportsVision}");
Console.WriteLine($"Supports audio: {projector.SupportsAudio}");
Console.WriteLine();

var promptTemplate = BuildPromptTemplate(projector.MediaMarker);
var contextOptions = ContextOptions.Default with { ContextLength = 2048, BatchSize = 128 };

var imageFiles = Directory.EnumerateFiles(inputsDirectory).ToList();
if (imageFiles.Count == 0)
{
    Console.WriteLine("No images found in Inputs directory.");
    Console.WriteLine("Please add document images to: " + inputsDirectory);
    return 0;
}

Console.WriteLine($"Found {imageFiles.Count} image(s) to process.");
Console.WriteLine();

foreach (var imagePath in imageFiles)
{
    var fileName = Path.GetFileName(imagePath);
    Console.WriteLine($"Processing {fileName}...");

    try
    {
        var stopwatch = Stopwatch.StartNew();
        using var context = model.CreateContext(contextOptions);
        Console.WriteLine($"  Context created");

        using var bitmap = projector.LoadBitmapFromFile(imagePath);
        Console.WriteLine($"  Bitmap loaded: {bitmap.Width}x{bitmap.Height}");

        // Validate image size - native library has proven limits
        if (bitmap.Width > 512 || bitmap.Height > 512)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  ⚠️  WARNING: Image size {bitmap.Width}x{bitmap.Height} exceeds recommended maximum of 512x512.");
            Console.WriteLine($"  ⚠️  This may cause crashes during image encoding. Consider resizing the image.");
            Console.ResetColor();
        }

        using var prompt = projector.Tokenize(promptTemplate, new[] { bitmap }, addSpecialTokens: true, parseSpecialTokens: true, cancellationToken);
        Console.WriteLine($"  Tokenized: {prompt.TokenCount} tokens, {prompt.PositionCount} positions");

        context.EvaluateMultimodalPrompt(projector, prompt, requestLogitsForLastChunk: true, cancellationToken);
        Console.WriteLine($"  Prefill complete: {context.ProcessedTokenCount} tokens processed");

        var ocrText = GenerateContinuation(context, model, generationConfig, cancellationToken);
        stopwatch.Stop();

        var outputPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(imagePath) + ".txt");
        var document = BuildDocument(fileName, ocrText, stopwatch.Elapsed, context.ProcessedTokenCount, projector);
        File.WriteAllText(outputPath, document, Encoding.UTF8);

        Console.WriteLine($"✓ Wrote {outputPath} ({stopwatch.Elapsed.TotalSeconds:F1}s, {context.ProcessedTokenCount} tokens)");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"✗ Error processing {fileName}: {ex.Message}");
        Console.Error.WriteLine(ex.StackTrace);
        Console.WriteLine();
        throw;
    }
}

Console.WriteLine("All images processed successfully!");
return 0;

static string? LocateRepositoryRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "ErgoX.VecraX.GgufX.sln")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return null;
}

static string BuildPromptTemplate(string mediaMarker)
{
    return $"""
<|im_start|>system
You are an expert OCR assistant. Extract all visible text from the document image accurately, preserving the original layout and formatting as much as possible.
<|im_end|>
<|im_start|>user
Extract the text from this document image.
{mediaMarker}
<|im_end|>
<|im_start|>assistant
""";
}

static string GenerateContinuation(ModelContext context, Model model, GenerationConfig config, CancellationToken cancellationToken)
{
    var outputBuilder = new StringBuilder(config.MaxTokens * 4);
    var stopSequenceBuffer = new StringBuilder();
    var random = new Random();

    Console.WriteLine($"  Starting generation (max {config.MaxTokens} tokens)...");

    for (var i = 0; i < config.MaxTokens; i++)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var logits = context.GetLogits();
        if (config.LogitBiases is not null && config.LogitBiases.Count > 0)
        {
            Sampling.ApplyBias(logits, config.LogitBiases);
        }

        if (config.BannedTokens is not null && config.BannedTokens.Count > 0)
        {
            Sampling.MaskTokens(logits, config.BannedTokens);
        }

        var nextToken = config.SamplingStrategy switch
        {
            SamplingStrategy.Greedy => SamplingAlgorithms.SampleGreedy(logits),
            SamplingStrategy.TopK => SamplingAlgorithms.SampleTopK(logits, config.TopK, config.Temperature, random),
            SamplingStrategy.TopP => SamplingAlgorithms.SampleTopP(logits, config.TopP, config.Temperature, random),
            SamplingStrategy.MinP => SamplingAlgorithms.SampleMinP(logits, config.MinP, config.Temperature, random),
            SamplingStrategy.Temperature => SamplingAlgorithms.SampleTopP(logits, 1.0f, config.Temperature, random),
            _ => SamplingAlgorithms.SampleGreedy(logits)
        };

        if (config.StopAtEndOfGeneration && model.IsEndOfGeneration(nextToken))
        {
            Console.WriteLine($"  Generation stopped at EOS (token {i})");
            break;
        }

        var piece = model.Detokenize(new Token[] { nextToken }, removeSpecial: false);
        outputBuilder.Append(piece);
        stopSequenceBuffer.Append(piece);

        if (config.StopSequences is not null)
        {
            var currentText = stopSequenceBuffer.ToString();
            var shouldStop = false;
            foreach (var stopSeq in config.StopSequences)
            {
                if (string.IsNullOrEmpty(stopSeq))
                {
                    continue;
                }

                if (currentText.Contains(stopSeq, StringComparison.Ordinal))
                {
                    var outputText = outputBuilder.ToString();
                    var stopIndex = outputText.LastIndexOf(stopSeq, StringComparison.Ordinal);
                    if (stopIndex >= 0)
                    {
                        outputBuilder.Length = stopIndex;
                    }

                    Console.WriteLine($"  Generation stopped at stop sequence (token {i})");
                    shouldStop = true;
                    break;
                }
            }

            if (shouldStop)
            {
                break;
            }

            if (stopSequenceBuffer.Length > 100)
            {
                stopSequenceBuffer.Remove(0, stopSequenceBuffer.Length - 100);
            }
        }

        context.Evaluate(new Token[] { nextToken }, requestLogitsForLastToken: true, cancellationToken);

        if ((i + 1) % 50 == 0)
        {
            Console.WriteLine($"  Generated {i + 1} tokens...");
        }
    }

    Console.WriteLine($"  Generation complete: {outputBuilder.Length} characters");
    return outputBuilder.ToString();
}

static string BuildDocument(string sourceFileName, string ocrText, TimeSpan duration, int tokenCount, MultimodalProjector projector)
{
    var builder = new StringBuilder();
    builder.AppendLine($"<!-- Source: {sourceFileName} -->");
    builder.AppendLine($"<!-- Generated: {DateTimeOffset.UtcNow:O} | Duration: {duration.TotalSeconds:F1}s | Tokens: {tokenCount} -->");
    builder.AppendLine($"<!-- Model: Nanonets OCR | Vision: {projector.SupportsVision} | Audio: {projector.SupportsAudio} -->");
    builder.AppendLine();
    builder.AppendLine("# OCR Output");
    builder.AppendLine();
    builder.AppendLine(ocrText.Trim());
    builder.AppendLine();
    return builder.ToString();
}
