// Configuration Testing Tool for GGUFx ASR
// Tests different parameter combinations to find optimal accuracy vs performance

using ErgoX.GgufX.Asr;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace ErgoX.GgufX.Examples.WhisperCommand;

internal class ConfigTester
{
    private readonly string _modelPath;
    private readonly string _commandsPath;
    private readonly int _deviceId;
    private readonly List<TestResult> _results = new();

    public ConfigTester(string modelPath, string commandsPath, int deviceId = -1)
    {
        _modelPath = modelPath;
        _commandsPath = commandsPath;
        _deviceId = deviceId;
    }

    public async Task RunTests()
    {
        Console.WriteLine("=== GGUFx ASR Configuration Testing ===\n");
        Console.WriteLine($"Model: {_modelPath}");
        Console.WriteLine($"Commands: {_commandsPath}");
        Console.WriteLine($"Device: {(_deviceId >= 0 ? _deviceId.ToString() : "default")}\n");

        // Test configurations
        var configs = new[]
        {
            // Baseline (default Whisper settings)
            new TestConfig("Baseline (Default)",
                AudioContextSize: 0, NoContext: false, MaxAudioBufferSeconds: 0, MaxTokens: 1),

            // Performance optimized (current fast settings)
            new TestConfig("Fast (Current)",
                AudioContextSize: 0, NoContext: true, MaxAudioBufferSeconds: 3, MaxTokens: 1),

            // Accuracy optimized
            new TestConfig("Accurate (Full Context)",
                AudioContextSize: 0, NoContext: false, MaxAudioBufferSeconds: 5, MaxTokens: 32),

            // Balanced configurations
            new TestConfig("Balanced (1500 ctx)",
                AudioContextSize: 1500, NoContext: false, MaxAudioBufferSeconds: 3, MaxTokens: 1),

            new TestConfig("Balanced (1000 ctx)",
                AudioContextSize: 1000, NoContext: false, MaxAudioBufferSeconds: 3, MaxTokens: 1),

            new TestConfig("Balanced (768 ctx)",
                AudioContextSize: 768, NoContext: false, MaxAudioBufferSeconds: 3, MaxTokens: 1),

            // Speed optimized variations
            new TestConfig("Fast (768 ctx, no context)",
                AudioContextSize: 768, NoContext: true, MaxAudioBufferSeconds: 2, MaxTokens: 1),

            new TestConfig("Fast (1000 ctx, no context)",
                AudioContextSize: 1000, NoContext: true, MaxAudioBufferSeconds: 2, MaxTokens: 1),
        };

        Console.WriteLine($"Testing {configs.Length} configurations...\n");
        Console.WriteLine("Instructions:");
        Console.WriteLine("- Each test will run for 30 seconds");
        Console.WriteLine("- Say commands clearly: play, pause, stop, next, previous, volume up, volume down");
        Console.WriteLine("- Try to say each command at least once per test");
        Console.WriteLine("- Press Ctrl+C to skip to next configuration\n");

        int testNum = 1;
        foreach (var config in configs)
        {
            Console.WriteLine($"\n{'='*80}");
            Console.WriteLine($"TEST {testNum}/{configs.Length}: {config.Name}");
            Console.WriteLine($"{'='*80}");
            Console.WriteLine($"  AudioContextSize: {config.AudioContextSize} (0=auto)");
            Console.WriteLine($"  NoContext: {config.NoContext}");
            Console.WriteLine($"  MaxAudioBufferSeconds: {config.MaxAudioBufferSeconds} (0=unlimited)");
            Console.WriteLine($"  MaxTokens: {config.MaxTokens}");
            Console.WriteLine();

            await RunSingleTest(config).ConfigureAwait(false);

            testNum++;

            if (testNum <= configs.Length)
            {
                Console.WriteLine("\nWaiting 3 seconds before next test...");
                await Task.Delay(3000).ConfigureAwait(false);
            }
        }

        PrintSummary();
    }

    private async Task RunSingleTest(TestConfig config)
    {
        var results = new ConcurrentBag<CommandResult>();
        var stopwatch = Stopwatch.StartNew();
        bool testRunning = true;

        var options = new GgufxAsrCommandOptions
        {
            ModelPath = _modelPath,
            Mode = GgufxAsrCommandMode.CommandList,
            CommandsFilePath = _commandsPath,
            DeviceId = _deviceId,
            VadThreshold = 0.01f,
            CommandDurationMs = 3000,
            ThreadCount = 4,
            UseGpu = GgufxTriState.Enabled,
            FlashAttention = GgufxTriState.Enabled,
            SuppressLogs = true,  // Suppress for cleaner output

            // Test configuration
            AudioContextSize = config.AudioContextSize,
            NoContext = config.NoContext,
            MaxAudioBufferSeconds = config.MaxAudioBufferSeconds,
            MaxTokens = config.MaxTokens,
            SingleSegment = true,
            EntropyThreshold = 2.4f,
            LogProbThreshold = -1.0f,
            NoSpeechThreshold = 0.6f
        };

        using var session = new GgufxAsrCommandSession(options);

        session.CommandRecognized += (sender, e) =>
        {
            if (e.Result.BestCommand != null)
            {
                var result = new CommandResult
                {
                    Command = e.Result.BestCommand.Command,
                    Probability = e.Result.BestCommand.Probability,
                    ProcessingTimeMs = e.Result.ProcessingTimeMs,
                    Timestamp = DateTime.Now
                };

                results.Add(result);

                var color = result.Probability switch
                {
                    >= 0.8f => ConsoleColor.Green,
                    >= 0.5f => ConsoleColor.Yellow,
                    _ => ConsoleColor.Red
                };

                Console.ForegroundColor = color;
                Console.WriteLine($"  [{result.Timestamp:HH:mm:ss}] {result.Command,-15} " +
                                $"Prob: {result.Probability:F3}  Time: {result.ProcessingTimeMs}ms");
                Console.ResetColor();
            }
        };

        // Cancel handler for this test
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler? handler = null;
        handler = (sender, e) =>
        {
            if (testRunning)
            {
                e.Cancel = true;
                try
                {
#pragma warning disable MA0045 // Cancel is appropriate in event handler
                    cts.Cancel();
#pragma warning restore MA0045
                }
                catch
                {
                    // Ignore disposal errors
                }
                Console.WriteLine("\n  Test stopped by user.");
                if (handler != null)
                {
                    Console.CancelKeyPress -= handler;
                }
            }
        };
        Console.CancelKeyPress += handler;

        try
        {
            session.Start();
            Console.WriteLine("Listening... (30 seconds, or press Ctrl+C to skip)\n");

            await Task.Delay(30000, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // User cancelled
        }
        finally
        {
            testRunning = false;
            session.Stop();
        }

        stopwatch.Stop();

        // Calculate statistics
        if (results.Any())
        {
            var resultsList = results.ToList();
            var avgProb = resultsList.Average(r => r.Probability);
            var avgTime = resultsList.Average(r => r.ProcessingTimeMs);
            var maxProb = resultsList.Max(r => r.Probability);
            var minProb = resultsList.Min(r => r.Probability);
            var highConfidence = resultsList.Count(r => r.Probability >= 0.8f);
            var mediumConfidence = resultsList.Count(r => r.Probability >= 0.5f && r.Probability < 0.8f);
            var lowConfidence = resultsList.Count(r => r.Probability < 0.5f);

            Console.WriteLine($"\n  Results: {resultsList.Count} commands detected");
            Console.WriteLine($"  Avg Probability: {avgProb:F3}  (Max: {maxProb:F3}, Min: {minProb:F3})");
            Console.WriteLine($"  Avg Processing: {avgTime:F0}ms");
            Console.WriteLine($"  Confidence: High={highConfidence}, Medium={mediumConfidence}, Low={lowConfidence}");

            _results.Add(new TestResult
            {
                Config = config,
                CommandCount = resultsList.Count,
                AverageProbability = avgProb,
                AverageProcessingTime = avgTime,
                MaxProbability = maxProb,
                MinProbability = minProb,
                HighConfidenceCount = highConfidence,
                MediumConfidenceCount = mediumConfidence,
                LowConfidenceCount = lowConfidence
            });
        }
        else
        {
            Console.WriteLine("\n  No commands detected in this test.");
        }
    }

    private void PrintSummary()
    {
        Console.WriteLine("\n\n");
        Console.WriteLine($"{'='*100}");
        Console.WriteLine("SUMMARY - Configuration Comparison");
        Console.WriteLine($"{'='*100}\n");

        if (!_results.Any())
        {
            Console.WriteLine("No results to display.");
            return;
        }

        // Sort by average probability (descending)
        var sortedByAccuracy = _results.OrderByDescending(r => r.AverageProbability).ToList();

        Console.WriteLine("RANKED BY ACCURACY (Average Probability):");
        Console.WriteLine(new string('-', 100));
        Console.WriteLine($"{"Rank",-5} {"Configuration",-30} {"Avg Prob",-12} {"High/Med/Low",-15} {"Avg Time",-12} {"Count",-8}");
        Console.WriteLine(new string('-', 100));

        int rank = 1;
        foreach (var result in sortedByAccuracy)
        {
            Console.WriteLine($"{rank,-5} {result.Config.Name,-30} " +
                            $"{result.AverageProbability:F3} ({result.MaxProbability:F2}-{result.MinProbability:F2})  " +
                            $"{result.HighConfidenceCount}/{result.MediumConfidenceCount}/{result.LowConfidenceCount,-15} " +
                            $"{result.AverageProcessingTime:F0}ms        " +
                            $"{result.CommandCount,-8}");
            rank++;
        }

        Console.WriteLine("\n");

        // Sort by processing time (ascending)
        var sortedBySpeed = _results.OrderBy(r => r.AverageProcessingTime).ToList();

        Console.WriteLine("RANKED BY SPEED (Average Processing Time):");
        Console.WriteLine(new string('-', 100));
        Console.WriteLine($"{"Rank",-5} {"Configuration",-30} {"Avg Time",-12} {"Avg Prob",-12} {"High Conf %",-12}");
        Console.WriteLine(new string('-', 100));

        rank = 1;
        foreach (var result in sortedBySpeed)
        {
            var highConfPct = result.CommandCount > 0
                ? (result.HighConfidenceCount * 100.0 / result.CommandCount)
                : 0;

            Console.WriteLine($"{rank,-5} {result.Config.Name,-30} " +
                            $"{result.AverageProcessingTime:F0}ms        " +
                            $"{result.AverageProbability:F3}      " +
                            $"{highConfPct:F1}%");
            rank++;
        }

        Console.WriteLine("\n");
        Console.WriteLine("RECOMMENDATIONS:");
        Console.WriteLine(new string('-', 100));

        var best = sortedByAccuracy.First();
        var fastest = sortedBySpeed.First();

        Console.WriteLine($"🏆 Best Accuracy: {best.Config.Name}");
        Console.WriteLine($"   Avg Probability: {best.AverageProbability:F3}, Processing: {best.AverageProcessingTime:F0}ms");
        Console.WriteLine($"   Settings: AudioCtx={best.Config.AudioContextSize}, NoContext={best.Config.NoContext}, " +
                        $"MaxAudio={best.Config.MaxAudioBufferSeconds}s, MaxTokens={best.Config.MaxTokens}");

        Console.WriteLine();

        Console.WriteLine($"⚡ Fastest: {fastest.Config.Name}");
        Console.WriteLine($"   Processing: {fastest.AverageProcessingTime:F0}ms, Avg Probability: {fastest.AverageProbability:F3}");
        Console.WriteLine($"   Settings: AudioCtx={fastest.Config.AudioContextSize}, NoContext={fastest.Config.NoContext}, " +
                        $"MaxAudio={fastest.Config.MaxAudioBufferSeconds}s, MaxTokens={fastest.Config.MaxTokens}");

        // Find best balanced (high accuracy with reasonable speed)
        var balanced = sortedByAccuracy
            .Where(r => r.AverageProbability >= sortedByAccuracy.Average(x => x.AverageProbability))
            .OrderBy(r => r.AverageProcessingTime)
            .FirstOrDefault();

        if (balanced != null && balanced != best)
        {
            Console.WriteLine();
            Console.WriteLine($"⚖️  Best Balanced: {balanced.Config.Name}");
            Console.WriteLine($"   Avg Probability: {balanced.AverageProbability:F3}, Processing: {balanced.AverageProcessingTime:F0}ms");
            Console.WriteLine($"   Settings: AudioCtx={balanced.Config.AudioContextSize}, NoContext={balanced.Config.NoContext}, " +
                            $"MaxAudio={balanced.Config.MaxAudioBufferSeconds}s, MaxTokens={balanced.Config.MaxTokens}");
        }

        Console.WriteLine($"\n{'='*100}\n");
    }

    private record TestConfig(string Name, int AudioContextSize, bool NoContext, int MaxAudioBufferSeconds, int MaxTokens)
    {
        public int AudioContextSize { get; init; } = AudioContextSize;
        public bool NoContext { get; init; } = NoContext;
        public int MaxAudioBufferSeconds { get; init; } = MaxAudioBufferSeconds;
        public int MaxTokens { get; init; } = MaxTokens;
    }

    private record CommandResult
    {
        public required string Command { get; init; }
        public required float Probability { get; init; }
        public required long ProcessingTimeMs { get; init; }
        public required DateTime Timestamp { get; init; }
    }

    private record TestResult
    {
        public required TestConfig Config { get; init; }
        public required int CommandCount { get; init; }
        public required double AverageProbability { get; init; }
        public required double AverageProcessingTime { get; init; }
        public required float MaxProbability { get; init; }
        public required float MinProbability { get; init; }
        public required int HighConfidenceCount { get; init; }
        public required int MediumConfidenceCount { get; init; }
        public required int LowConfidenceCount { get; init; }
    }
}
