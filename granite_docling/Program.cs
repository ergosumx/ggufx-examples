using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ErgoX.VecraX.GgufX.Multimodal;

namespace GraniteDocling
{
    internal static class Program
    {
        private static readonly string ProjectRoot = @"C:\Users\nilayparikh\.sources\vecrax\ggufx\examples\granite_docling";
        private static readonly string InputDirectory = @"C:\Users\nilayparikh\.sources\vecrax\ggufx\examples\_io\original";
        private static readonly string OutputDirectory = Path.Combine(ProjectRoot, "io", "output");
        private static readonly string ModelsDirectory = Path.Combine(ProjectRoot, "models");
        private static readonly string ModelPath = Path.Combine(ModelsDirectory, "granite-docling-258M-Q8_0.gguf");
        private static readonly string ProjectorPath = Path.Combine(ModelsDirectory, "mmproj-granite-docling-258M-Q8_0.gguf");

        private static readonly string[] SupportedImageExtensions =
        [
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".webp",
            ".tiff"
        ];

        private const string Prompt =
            "Convert the document into GitHub-flavoured Markdown while preserving tables, headings, and lists.";

        public static async Task<int> Main(string[] args)
        {
            try
            {
                Console.WriteLine("GGUFx Granite Docling sample\n");

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
                Console.Error.WriteLine($"Native runtime failed ({ex.StatusCode}): {ex.NativeMessage}");
                return 1;
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine(ex.Message);
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
            var options = new GgufxMultimodalSessionOptions
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
                ResponseBufferSize = 1024 * 1024 * 8
            };

            return options;
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
    }
}
