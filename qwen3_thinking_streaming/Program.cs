namespace Qwen3ThinkingStreaming
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ErgoX.GgufX.Text;

    internal static class Program
    {
        private const string ModelFileName = "Qwen3-4B-Thinking-2507-Q4_K_M.gguf";

        private static async Task Main()
        {
            try
            {
                var modelPath = LocateModel();
                Console.WriteLine($"Using model: {modelPath}");

                await RunThinkingDemoAsync(modelPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("An error occurred while running the demo:");
                Console.WriteLine(ex);
                Console.ResetColor();
                throw;
            }
        }

        private static async Task RunThinkingDemoAsync(string modelPath)
        {
            using var chat = GgufxTextChatEndpoint.Create(
                CreateContextOptions(modelPath),
                new GgufxTextChatOptions
                {
                    SystemPrompt = "You reason step by step in <think> blocks before replying with a clear final answer.",
                    StripReasoningFromHistory = false,
                });

            const string prompt = "You are helping a product team prioritise bug fixes. Evaluate the list of three bugs, determine which one should be fixed first, and explain the reasoning.";

            Console.WriteLine();
            Console.WriteLine("Prompt:");
            Console.WriteLine(prompt);
            Console.WriteLine();
            Console.WriteLine("Streaming response:");

            var pending = new StringBuilder();
            var inThinking = false;
            var thinkingHeaderPrinted = false;
            var answerHeaderPrinted = false;
            var openTag = "<think>";
            var closeTag = "</think>";

            void FlushThinking(string segment)
            {
                if (segment.Length == 0)
                {
                    return;
                }

                if (!thinkingHeaderPrinted)
                {
                    Console.WriteLine("[thinking]");
                    thinkingHeaderPrinted = true;
                }

                WriteColored(segment, ConsoleColor.DarkYellow);
            }

            void FlushAnswer(string segment)
            {
                if (segment.Length == 0)
                {
                    return;
                }

                if (!answerHeaderPrinted)
                {
                    Console.WriteLine();
                    Console.WriteLine("[assistant]");
                    answerHeaderPrinted = true;
                }

                WriteColored(segment, ConsoleColor.Cyan);
            }

            void ProcessBuffer()
            {
                while (true)
                {
                    var snapshot = pending.ToString();
                    if (snapshot.Length == 0)
                    {
                        pending.Clear();
                        return;
                    }

                    if (inThinking)
                    {
                        var closingIndex = snapshot.IndexOf(closeTag, StringComparison.Ordinal);
                        if (closingIndex >= 0)
                        {
                            var chunk = snapshot[..closingIndex];
                            FlushThinking(chunk);
                            pending.Clear();
                            pending.Append(snapshot[(closingIndex + closeTag.Length)..]);
                            inThinking = false;
                            continue;
                        }

                        var safeLength = Math.Max(0, snapshot.Length - (closeTag.Length - 1));
                        if (safeLength > 0)
                        {
                            var chunk = snapshot[..safeLength];
                            FlushThinking(chunk);
                            pending.Clear();
                            pending.Append(snapshot[safeLength..]);
                        }

                        return;
                    }

                    var openIndex = snapshot.IndexOf(openTag, StringComparison.Ordinal);
                    if (openIndex >= 0)
                    {
                        var chunk = snapshot[..openIndex];
                        FlushAnswer(chunk);
                        pending.Clear();
                        pending.Append(snapshot[(openIndex + openTag.Length)..]);
                        inThinking = true;
                        continue;
                    }

                    var safeAnswerLength = Math.Max(0, snapshot.Length - (openTag.Length - 1));
                    if (safeAnswerLength > 0)
                    {
                        var chunk = snapshot[..safeAnswerLength];
                        FlushAnswer(chunk);
                        pending.Clear();
                        pending.Append(snapshot[safeAnswerLength..]);
                    }

                    return;
                }
            }

            var result = await chat.SendAsync(
                prompt,
                token =>
                {
                    if (string.IsNullOrEmpty(token))
                    {
                        return;
                    }

                    lock (pending)
                    {
                        pending.Append(token);
                        ProcessBuffer();
                    }
                },
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            lock (pending)
            {
                ProcessBuffer();
                if (pending.Length > 0)
                {
                    var remainder = pending.ToString();
                    pending.Clear();
                    if (inThinking)
                    {
                        FlushThinking(remainder);
                    }
                    else
                    {
                        FlushAnswer(remainder);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("Final assistant reply (reasoning removed):");
            Console.WriteLine(result.Content);
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
            options.Runtime.ContextLength = 4096;
            options.Runtime.PredictLength = 512;
            options.Decode.PromptBatchSize = 128;
            options.Decode.ResponseBatchSize = 64;

            options.Sampling.Seed = 1234;
            options.Sampling.Temperature = 0.7f;
            options.Sampling.TopK = 40;
            options.Sampling.TopP = 0.9f;
            options.Sampling.RepeatPenalty = 1.05f;
            options.Sampling.PenaltyLastN = 128;

            return options;
        }

        private static string LocateModel()
        {
            var root = LocateSolutionRoot();
            var modelPath = Path.Combine(root, "examples", "qwen3_thinking_streaming", "model", ModelFileName);

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException(
                    $"The sample expects the model file to be present at '{modelPath}'. " +
                    "Copy the Qwen3 thinking GGUF model into this location before running the demo.",
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

        private static void WriteColored(string value, ConsoleColor color)
        {
            if (value.Length == 0)
            {
                return;
            }

            var original = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(value);
            Console.ForegroundColor = original;
        }
    }
}
