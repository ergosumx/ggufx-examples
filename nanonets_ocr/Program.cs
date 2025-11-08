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

namespace NanonetsOcr
{
    internal static class Program
    {
        private static readonly string ExampleRoot = ResolveProjectRoot();
        private static readonly string ExamplesRoot = Path.GetFullPath(Path.Combine(ExampleRoot, ".."));
        private static readonly string InputDirectory = Path.GetFullPath(Path.Combine(ExamplesRoot, "_io", "1024"));
        private static readonly string OutputDirectory = Path.GetFullPath(Path.Combine(ExampleRoot, "io", "output"));
        private static readonly string ModelsDirectory = Path.GetFullPath(Path.Combine(ExampleRoot, "model"));
        private static readonly string ModelPath = Path.Combine(ModelsDirectory, "Nanonets-OCR-s-Q4_K_M.gguf");
        private static readonly string ProjectorPath = Path.Combine(ModelsDirectory, "mmproj-Nanonets-OCR-s.gguf");

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
        <|im_start|>system
        You are a helpful assistant.<|im_end|>
        <|im_start|>user
        Extract the text from the above document as if you were reading it naturally.
        Return the tables in html format.
        Return the equations in LaTeX representation.
        If there is an image in the document and image caption is not present, add a small description of the image inside the <img></img> tag; otherwise, add the image caption inside <img></img>.
        Watermarks should be wrapped in brackets. Ex: <watermark>OFFICIAL COPY</watermark>.
        Page numbers should be wrapped in brackets. Ex: <page_number>14</page_number> or <page_number>9/22</page_number>.
        Prefer using ☐ and ☑ for check boxes.<|im_end|>
        <|im_start|>assistant
        """;

        public static async Task<int> Main(string[] args)
        {
            try
            {
                Console.WriteLine("GGUFx Nanonets OCR sample\n");

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
                ContextLength = 16384,
                PromptBatchSize = 1024,
                PromptMicroBatchSize = 1024,
                TextBatchSize = 1024,
                ImageBatchSize = 1024,
                PredictLength = -1,
                GpuLayers = -1,
                ThreadCount = Math.Max(2, Environment.ProcessorCount / 2),
                NativeVerbosity = 2,
                Temperature = 0.2f,
                MmprojUseGpu = true,
                KvCacheUnified = GgufxTriState.Enabled,
                ResetStateBetweenRequests = true,
                UseJinja = true,
                ChatTemplate = """
                {#- Copyright 2025-present the Unsloth team. All rights reserved. #} {#- Licensed under the Apache License, Version 2.0 (the "License") #} {%- set image_count = namespace(value=0) -%} {%- set video_count = namespace(value=0) -%} {%- set text_count = namespace(value=0) -%} {%- for message in messages -%} {%- if loop.first and message["role"] != "system" -%} {{- "<|im_start|>system\nYou are a helpful assistant.<|im_end|>\n" -}} {%- endif -%} {{- "<|im_start|>" -}} {{- message["role"] -}} {{- "\n" -}} {%- if message["content"] is string -%} {{- message["content"] -}} {{- "<|im_end|>\n" -}} {%- else -%} {#- Check if text field is present #} {%- set text_count.value = 0 -%} {%- for content in message["content"] -%} {%- if content["type"] == "image" or "image" in content or "image_url" in content -%} {%- set image_count.value = image_count.value + 1 -%} {%- if add_vision_id -%} {{- "Picture " -}} {{- image_count.value -}} {{- ": " -}} {%- endif -%} {{- "<|vision_start|><|image_pad|><|vision_end|>" -}} {%- elif content["type"] == "video" or "video" in content -%} {%- set video_count.value = video_count.value + 1 -%} {%- if add_vision_id -%} {{- "Video " -}} {{- video_count.value -}} {{- ": " -}} {%- endif -%} {{- "<|vision_start|><|video_pad|><|vision_end|>" -}} {%- elif "text" in content -%} {{- content["text"]|string -}} {%- if content["text"]|length != 0 -%} {%- set text_count.value = text_count.value + 1 -%} {%- endif -%} {%- endif -%} {%- endfor -%} {#- If text field seen, add a newline #} {%- if text_count.value != 0 -%} {{- "\n" -}} {%- endif -%} {{- "Extract the text from the above document as if you were reading it naturally. Return the tables in html format. Return the equations in LaTeX representation. If there is an image in the document and image caption is not present, add a small description of the image inside the <img></img> tag; otherwise, add the image caption inside <img></img>. Watermarks should be wrapped in brackets. Ex: <watermark>OFFICIAL COPY</watermark>. Page numbers should be wrapped in brackets. Ex: <page_number>14</page_number> or <page_number>9/22</page_number>. Prefer using ☐ and ☑ for check boxes." -}} {{- "<|im_end|>\n" -}} {%- endif -%} {%- endfor -%} {%- if add_generation_prompt -%} {{- "<|im_start|>assistant\n" -}} {%- endif -%} {#- Copyright 2025-present the Unsloth team. All rights reserved. #} {#- Licensed under the Apache License, Version 2.0 (the "License") #}
                """
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
            const string projectFile = "nanonets_ocr.csproj";

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
