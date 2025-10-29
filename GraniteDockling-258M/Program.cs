using System.Diagnostics;
using System.Text;
using ErgoX.VecraX.GgufX.Abstractions.Model;
using ErgoX.VecraX.GgufX.Backend;
using ErgoX.VecraX.GgufX.Core;
using ErgoX.VecraX.GgufX.Sampling;
using SamplingUtilities = ErgoX.VecraX.GgufX.Sampling.Sampling;
using ErgoX.VecraX.GgufX.Utilities;

namespace ErgoX.VecraX.GgufX.Examples.GraniteDockling
{
    public class Program
    {
        public static int Main(string[] args)
        {
            // Configure Vulkan backend for better GPU acceleration
            BackendConfiguration.Configure(new BackendSelectionOptions(BackendType.Vulkan, BackendSelectionMode.Safe));
            Console.WriteLine("Backend configured: Vulkan");
            Console.WriteLine();

            var cancellationToken = CancellationToken.None;
            var repositoryRoot = LocateRepositoryRoot();
            if (repositoryRoot is null)
            {
                Console.Error.WriteLine("Failed to locate repository root. Ensure the example runs from within the repo.");
                return 1;
            }

            var modelPath = Path.Combine(repositoryRoot, ".models", "granite-docling-258M-GGUF", "granite-docling-258M-Q8_0.gguf");
            var projectorPath = Path.Combine(repositoryRoot, ".models", "granite-docling-258M-GGUF", "mmproj-granite-docling-258M-Q8_0.gguf");
            var inputsDirectory = Path.Combine(repositoryRoot, "examples", "GraniteDockling-258M", "Inputs");
            var outputDirectory = Path.Combine(repositoryRoot, "examples", "GraniteDockling-258M", "Output");

            if (!File.Exists(modelPath))
            {
                Console.Error.WriteLine($"Model file not found: {modelPath}");
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
                return 1;
            }

            Directory.CreateDirectory(outputDirectory);

            var generationConfig = GenerationConfig.Precise with
            {
                MaxTokens = 512,
                Temperature = 0.3f,
                TopP = 0.95f,
                SamplingStrategy = SamplingStrategy.TopP,
                StopSequences = new List<string> { "<|end_of_text|>", "<|start_of_role|>" }
            };

            // Load model with GPU offloading enabled
            var modelOptions = ModelOptions.Default with { GpuLayerCount = 99 };
            using var model = Model.Load(modelPath, modelOptions);

            // Create projector with GPU enabled
            var projectorOptions = new MultimodalProjectorOptions { UseGpu = true };
            using var projector = model.CreateMultimodalProjector(projectorPath, projectorOptions);

            Console.WriteLine($"Loaded model: {modelPath}");
            Console.WriteLine($"Loaded projector: {projectorPath}");
            Console.WriteLine($"Media marker: {projector.MediaMarker}");
            Console.WriteLine($"Supports vision: {projector.SupportsVision}");
            Console.WriteLine($"Supports audio: {projector.SupportsAudio}");
            Console.WriteLine();

            var promptTemplate = BuildPromptTemplate(projector.MediaMarker);
            var contextOptions = new ContextOptions
            {
                ContextLength = 4096,
                BatchSize = 64
            };

            foreach (var imagePath in Directory.EnumerateFiles(inputsDirectory))
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

                    var docling = GenerateContinuation(context, model, generationConfig, cancellationToken);
                    stopwatch.Stop();

                    var outputPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(imagePath) + ".docling.txt");
                    var document = BuildDocument(fileName, docling, stopwatch.Elapsed, context.ProcessedTokenCount, projector);
                    File.WriteAllText(outputPath, document, Encoding.UTF8);

                    Console.WriteLine($"Wrote {outputPath} ({stopwatch.Elapsed.TotalSeconds:F1}s, tokens: {context.ProcessedTokenCount})");
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error processing {fileName}: {ex.Message}");
                    Console.Error.WriteLine(ex.StackTrace);
                    Console.WriteLine();
                    throw;
                }
            }

            return 0;
        }

        private static string? LocateRepositoryRoot()
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

        private static string BuildPromptTemplate(string mediaMarker)
        {
            return $"""
<|start_of_role|>system<|end_of_role|>Convert this page to docling.<|end_of_text|> <|start_of_role|>user<|end_of_role|>Convert this page to docling.
{mediaMarker}<|end_of_text|> <|start_of_role|>assistant<|end_of_role|>
""";
        }

        private static string GenerateContinuation(ModelContext context, Model model, GenerationConfig config, CancellationToken cancellationToken)
        {
            var outputBuilder = new StringBuilder(config.MaxTokens * 4);
            var stopSequenceBuffer = new StringBuilder();
            var random = new Random();

            for (var i = 0; i < config.MaxTokens; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                float[] logits;
                try
                {
                    logits = context.GetLogits();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error getting logits at token {i}: {ex.Message}");
                    Console.Error.WriteLine($"Stack: {ex.StackTrace}");
                    throw;
                }
                if (config.LogitBiases is not null && config.LogitBiases.Count > 0)
                {
                    SamplingUtilities.ApplyBias(logits.AsSpan(), config.LogitBiases);
                }

                if (config.BannedTokens is not null && config.BannedTokens.Count > 0)
                {
                    SamplingUtilities.MaskTokens(logits.AsSpan(), config.BannedTokens);
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

                // Evaluate next token for the next iteration
                context.Evaluate(new Token[] { nextToken }, requestLogitsForLastToken: true, cancellationToken);
            }

            return outputBuilder.ToString();
        }

        private static string BuildDocument(string sourceFileName, string docling, TimeSpan duration, int tokenCount, MultimodalProjector projector)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"<!-- Source: {sourceFileName} -->");
            builder.AppendLine($"<!-- Generated: {DateTimeOffset.UtcNow:O} | Duration: {duration.TotalSeconds:F1}s | Tokens: {tokenCount} -->");
            builder.AppendLine($"<!-- SupportsVision: {projector.SupportsVision} | SupportsAudio: {projector.SupportsAudio} -->");
            builder.AppendLine($"<!-- Format: Docling Structured Output -->");
            builder.AppendLine();
            builder.AppendLine(docling.Trim());
            builder.AppendLine();
            return builder.ToString();
        }
    }
}
