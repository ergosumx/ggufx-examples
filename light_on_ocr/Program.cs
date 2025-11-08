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

namespace LightOnOcr
{
    internal static class Program
    {
        private static readonly string ExampleRoot = ResolveProjectRoot();
        private static readonly string ExamplesRoot = Path.GetFullPath(Path.Combine(ExampleRoot, ".."));
        private static readonly string InputDirectory = Path.GetFullPath(Path.Combine(ExamplesRoot, "_io", "1024"));
        private static readonly string OutputDirectory = Path.GetFullPath(Path.Combine(ExampleRoot, "io", "output"));
        private static readonly string ModelsDirectory = Path.GetFullPath(Path.Combine(ExampleRoot, "model"));
        private static readonly string ModelPath = Path.Combine(ModelsDirectory, "LightOnOCR-1B-1025-Q6_K.gguf");
        private static readonly string ProjectorPath = Path.Combine(ModelsDirectory, "mmproj-Q8_0.gguf");

        private static readonly string[] SupportedImageExtensions =
        [
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".webp",
            ".tiff"
        ];

        private const string Prompt = "Extract all visible text from the document as plain text, preserving line structure.";

        public static async Task<int> Main(string[] args)
        {
            try
            {
                Console.WriteLine("GGUFx LightOn OCR sample\n");

                var images = EnumerateInputMedia().ToList();
                if (images.Count == 0)
                {
                    Console.WriteLine($"No input files were found in '{InputDirectory}'.");
                    return 0;
                }

                var options = BuildOptions();
                using var session = GgufxMultimodalSession.Create(options);

                Directory.CreateDirectory(OutputDirectory);

                foreach (var image in images)
                {
                    await ProcessDocumentAsync(session, image).ConfigureAwait(false);
                }

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

        private static IEnumerable<string> EnumerateInputMedia()
        {
            if (!Directory.Exists(InputDirectory))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(InputDirectory)
                .Where(file => SupportedImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);
        }

        private static GgufxMultimodalSessionOptions BuildOptions()
        {
            return new GgufxMultimodalSessionOptions
            {
                ModelPath = EnsureFile(ModelPath),
                MmprojPath = EnsureFile(ProjectorPath),
                ContextLength = 4096,
                PromptBatchSize = 256,
                ImageBatchSize = 64,
                PredictLength = -1,
                GpuLayers = -1,
                ThreadCount = Math.Max(2, Environment.ProcessorCount / 2),
                NativeVerbosity = 0,
                Temperature = 0.2f,
                MmprojUseGpu = true,
                UseJinja = false,
            };
        }

        private static string EnsureFile(string path)
        {
            var resolved = Path.GetFullPath(path);
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException($"Required asset was not found: {resolved}", resolved);
            }

            return resolved;
        }

        private static string ResolveProjectRoot()
        {
            var directory = Path.GetFullPath(AppContext.BaseDirectory);
            const string projectFile = "light_on_ocr.csproj";

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
}
