namespace TinyLlamaChat
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using ErgoX.GgufX.Text;

    internal static class Program
    {
        private const string ModelFileName = "tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf";

        private static async Task Main()
        {
            try
            {
                var modelPath = LocateModel();
                Console.WriteLine($"Using model: {modelPath}");

                await RunSummarisationDemoAsync(modelPath).ConfigureAwait(false);
                await RunDebateDemoAsync(modelPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("An error occurred while running the demo: ");
                Console.WriteLine(ex);
                Console.ResetColor();
                throw;
            }
        }

        private static async Task RunSummarisationDemoAsync(string modelPath)
        {
            Console.WriteLine();
            Console.WriteLine("=== Summarisation Demo (Streaming Tokens) ===");

            using var chat = GgufxTextChatEndpoint.Create(
                CreateContextOptions(modelPath),
                new GgufxTextChatOptions
                {
                    SystemPrompt = "You concisely summarise documents in two sentences or fewer.",
                });

            var document = "GGUFx is an enterprise-focused thin C++ runtime based on GGML & inspired by llama.cpp that exposes a slim managed API for deterministic model hosting. " +
                           "It maintains compatibility with the GGUF model ecosystem while layering in curated interop helpers and modern .NET abstractions.";

            Console.WriteLine("Original text: ");
            Console.WriteLine(document);
            Console.WriteLine();
            Console.Write("Summary (streaming): ");

            var result = await chat.SendAsync(
                $"Summarise the following text in at most two sentences:\n\n{document}",
                token =>
                {
                    if (string.IsNullOrEmpty(token))
                    {
                        return;
                    }

                    Console.Write(token);
                },
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("Final summary:");
            Console.WriteLine(result.Content);
        }

        private static async Task RunDebateDemoAsync(string modelPath)
        {
            Console.WriteLine();
            Console.WriteLine("=== Debate Demo (10 Exchanges) ===");

            using var proChat = GgufxTextChatEndpoint.Create(
                CreateContextOptions(modelPath),
                new GgufxTextChatOptions
                {
                    SystemPrompt = "You are an advocate for beneficial AI. You argue that helpful AI systems advance human progress and improve quality of life.",
                });

            using var conChat = GgufxTextChatEndpoint.Create(
                CreateContextOptions(modelPath),
                new GgufxTextChatOptions
                {
                    SystemPrompt = "You are a cautious ethicist who highlights the risks of AI. You emphasise unintended consequences and advocate for restraint.",
                });

            const int rounds = 10;
            var topic = "Advanced AI will ultimately benefit humanity.";
            string? proLast = null;
            string? conLast = null;

            for (var round = 0; round < rounds; round++)
            {
                var proPrompt = round == 0
                    ? $"State your opening argument supporting the idea that {topic}"
                    : $"Your opponent responded with: \"{conLast}\". Provide a rebuttal that reinforces why {topic}";

                var proTurn = await proChat.SendAsync(proPrompt, tokenCallback: null, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                proLast = proTurn.Content;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Round {round + 1} | Pro-AI] {proLast}");
                Console.ResetColor();

                var conPrompt = $"Your opponent argued: \"{proLast}\". Respond by highlighting the risks and potential harms that AI could pose to society.";
                var conTurn = await conChat.SendAsync(conPrompt, tokenCallback: null, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                conLast = conTurn.Content;

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Round {round + 1} | Caution] {conLast}");
                Console.ResetColor();
            }
        }

        private static GgufxTextContextOptions CreateContextOptions(string modelPath)
        {
            var options = new GgufxTextContextOptions(modelPath)
            {
                UseJinjaTemplate = false,
                ResponseBufferSize = 2 * 1024 * 1024,
            };

            options.Runtime.ThreadCount = Math.Max(2, Environment.ProcessorCount / 2);
            options.Runtime.GpuLayers = 0;
            options.Runtime.ContextLength = 2048;
            options.Runtime.PredictLength = 256;
            options.Decode.PromptBatchSize = 128;
            options.Decode.ResponseBatchSize = 64;

            options.Sampling.Seed = 42;
            options.Sampling.Temperature = 0.8f;
            options.Sampling.TopK = 40;
            options.Sampling.TopP = 0.95f;
            options.Sampling.RepeatPenalty = 1.05f;
            options.Sampling.PenaltyLastN = 64;

            return options;
        }

        private static string LocateModel()
        {
            var root = LocateSolutionRoot();
            var modelPath = Path.Combine(root, "examples", "tiny_llama_chat", "model", ModelFileName);

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException(
                    $"The sample expects the model file to be present at '{modelPath}'. " +
                    "Copy the TinyLLaMA GGUF model into this location before running the demo.",
                    modelPath);
            }

            return modelPath;
        }

        private static string LocateSolutionRoot()
        {
            var directory = Path.GetFullPath(AppContext.BaseDirectory);
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "ErgoX.GgufX.sln")))
                {
                    return directory;
                }

                var parent = Directory.GetParent(directory);
                if (parent is null)
                {
                    break;
                }

                directory = parent.FullName;
            }

            throw new InvalidOperationException("Unable to locate the solution root from the current working directory.");
        }
    }
}
