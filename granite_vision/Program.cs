namespace GraniteVision;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ErgoX.GgufX;
using ErgoX.GgufX.Multimodal;

/// <summary>
/// Entry point for the Granite Vision multimodal console sample.
/// </summary>
internal static class Program
{
    private static readonly string ProjectRoot = ResolveProjectRoot();
    private static readonly string ExamplesRoot = Path.GetFullPath(Path.Combine(ProjectRoot, ".."));
    private static readonly IReadOnlyList<string> CandidateInputDirectories =
    [
        Path.GetFullPath(Path.Combine(ExamplesRoot, "_io", "1024")),
        Path.GetFullPath(Path.Combine(ExamplesRoot, "_io", "original"))
    ];
    private static readonly string OutputDirectory = Path.GetFullPath(Path.Combine(ProjectRoot, "io", "output"));
    private static readonly string ModelsDirectory = Path.GetFullPath(Path.Combine(ProjectRoot, "model"));
    private static readonly string ModelPath = Path.Combine(ModelsDirectory, "granite-vision-3.3-2b-Q8_0.gguf");
    private static readonly string ProjectorPath = Path.Combine(ModelsDirectory, "mmproj-model-f16.gguf");

    private static readonly string[] SupportedImageExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".webp",
        ".tiff"
    ];

    private const string Prompt = """
        Summerise the table
        """;

    /// <summary>
    /// Executes the Granite Vision sample using the bundled GGUF models.
    /// </summary>
    /// <returns>Zero on success; otherwise a non-zero exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            Console.WriteLine("GGUFx Granite Vision sample\n");

            var options = await BuildOptionsAsync().ConfigureAwait(false);
            using var session = GgufxMultimodalSession.Create(options);

            Directory.CreateDirectory(OutputDirectory);

            await ProcessDocumentAsync(session, @"C:\Users\nilayparikh\.sources\vecrax\ggufx\examples\_io\table\Screenshot 2025-10-31 224821.png").ConfigureAwait(false);

            Console.WriteLine("\nProcessing complete.");
            return 0;
        }
        catch (GgufxNativeException ex)
        {
            await Console.Error.WriteLineAsync($"Native runtime failed ({ex.StatusCode}): {ex.NativeMessage}").ConfigureAwait(false);
            return 1;
        }
        catch (FileNotFoundException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>
    /// Generates structured documentation for a single image by invoking the Granite Vision model.
    /// </summary>
    /// <param name="session">Active multimodal session.</param>
    /// <param name="mediaPath">Path to the image to convert.</param>
    private static async Task ProcessDocumentAsync(GgufxMultimodalSession session, string mediaPath)
    {
        Console.WriteLine($"Processing {Path.GetFileName(mediaPath)}");

        var request = new GgufxMultimodalRequest(
            Prompt,
            new[] { GgufxMultimodalMedia.FromImage(mediaPath) },
            clearHistory: true);

        var stopwatch = Stopwatch.StartNew();
        var response = await session.ProcessAsync(request, CancellationToken.None).ConfigureAwait(false);
        stopwatch.Stop();

        var outputFile = Path.Combine(
            OutputDirectory,
            Path.GetFileNameWithoutExtension(mediaPath) + ".md");

        await File.WriteAllTextAsync(outputFile, response, Encoding.UTF8, CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine($"  → Saved to {outputFile} ({stopwatch.Elapsed.TotalSeconds:F1}s)");
    }

    /// <summary>
    /// Resolves the single image to process based on command-line arguments or default directories.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Immutable collection containing one image path when available.</returns>
    private static IReadOnlyList<string> ResolveInputMedia(IReadOnlyList<string> args)
    {
        if (args.Count > 0)
        {
            var imagePath = EnsureFile(args[0]);

            if (args.Count > 1)
            {
                Console.WriteLine("Warning: multiple paths supplied. Only the first will be processed.");
            }

            return new[] { imagePath };
        }

        foreach (var directory in CandidateInputDirectories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            var files = Directory.EnumerateFiles(directory)
                .Where(file => SupportedImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .Take(1)
                .ToList();

            if (files.Count > 0)
            {
                Console.WriteLine($"Using '{directory}' as input source.");
                return files;
            }
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Builds session options aligned with Granite Vision prompt and batching defaults.
    /// </summary>
    /// <returns>Task producing a configured <see cref="GgufxMultimodalSessionOptions"/> instance.</returns>
    private static Task<GgufxMultimodalSessionOptions> BuildOptionsAsync()
    {
        var options = new GgufxMultimodalSessionOptions
        {
            ModelPath = EnsureFile(ModelPath),
            MmprojPath = EnsureFile(ProjectorPath),
            UseJinja = false,
            ContextLength = 16384,
            PromptBatchSize = 512,
            PromptMicroBatchSize = 512,
            ImageBatchSize = 256,
            TextBatchSize = 1024,
            PredictLength = -1,
            GpuLayers = -1,
            ThreadCount = Math.Max(2, Environment.ProcessorCount / 2),
            NativeVerbosity = 0,
            Temperature = 0.3f,
            MmprojUseGpu = true,
            ResetStateBetweenRequests = true,
            ResponseBufferSize = 8 * 1024 * 1024
        };

        return Task.FromResult(options);
    }

    /// <summary>
    /// Validates that a file exists and returns the absolute path.
    /// </summary>
    /// <param name="path">Relative or absolute file path.</param>
    /// <returns>Absolute path to the file.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file cannot be found.</exception>
    private static string EnsureFile(string path)
    {
        var resolved = Path.GetFullPath(path);
        if (!File.Exists(resolved))
        {
            throw new FileNotFoundException($"Required asset was not found: {resolved}", resolved);
        }

        return resolved;
    }

    /// <summary>
    /// Discovers the project directory containing the Granite Vision csproj file.
    /// </summary>
    /// <returns>Absolute path to the project root.</returns>
    private static string ResolveProjectRoot()
    {
        var directory = Path.GetFullPath(AppContext.BaseDirectory);
        const string projectFile = "granite_vision.csproj";

        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(directory, projectFile);
            if (File.Exists(candidate))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException($"Unable to locate the project root containing '{projectFile}'.");
    }
}
