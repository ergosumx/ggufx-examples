using System.Diagnostics;
using System.IO;
using System.Threading;
using ErgoX.GgufX.Asr;

namespace WhisperStream;

internal class ModelValidator
{
    private static readonly string[] ExpectedPhrases =
    [
        "architectural patterns",
        "layer architecture", "layered architecture",
        "presentation business logic",
        "data access layers",
        "model view presenter",
        "event driven architecture", "event-driven architecture",
        "components communicate by events",
        "cqrs",
        "separating write from read", "separating right from read",
        "microkernel architectures", "micronel architectures",
        "core functionality",
        "kernel and extensive through plugins", "kernel and extensible through plugins",
        "eclipse ide",
        "plug-in based architecture", "plugin based architecture",
        "microservices architecture",
        "loosely coupled services", "loosely coupo services",
        "netflix",
        "everything from recommendations",
        "monolithic architecture",
        "modular monolith",
        "clear boundaries",
        "codebase",
        "easier maintenance",
        "system design"
    ];

    private static readonly object ConsoleLock = new();
    private readonly List<string> capturedText = [];
    private readonly Stopwatch stopwatch = new();

    public async Task<ValidationResult> ValidateModel(string modelPath, string vadModelPath, int deviceId, bool isLoopback, int durationSeconds = 65)
    {
        capturedText.Clear();

        lock (ConsoleLock)
        {
            Console.WriteLine($"\n{'='}{new string('=', 70)}");
            Console.WriteLine($"Testing: {Path.GetFileName(modelPath)}");
            Console.WriteLine($"{'='}{new string('=', 70)}");
        }

        try
        {
            using var session = GgufxAsrStreamSession.Create(
                modelPath,
                deviceId: deviceId,
                isLoopback: isLoopback,
                wordLevelTimestamps: true,
                suppressLogs: true);

            session.ResultReceived += OnResultReceived;

            stopwatch.Restart();
            session.Open();

            // Capture audio for one full loop + buffer
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds), CancellationToken.None).ConfigureAwait(false);

            session.Close();
            stopwatch.Stop();

            // Calculate metrics
            var fullTranscript = string.Join(" ", capturedText).ToLowerInvariant();
            var matchedPhrases = ExpectedPhrases.Count(phrase => fullTranscript.Contains(phrase.ToLowerInvariant()));
            var accuracy = (double)matchedPhrases / ExpectedPhrases.Length * 100;

            var result = new ValidationResult
            {
                ModelName = Path.GetFileName(modelPath),
                ModelSize = new FileInfo(modelPath).Length,
                TranscriptLength = fullTranscript.Length,
                MatchedPhrases = matchedPhrases,
                TotalPhrases = ExpectedPhrases.Length,
                Accuracy = accuracy,
                ProcessingTime = stopwatch.Elapsed,
                FullTranscript = fullTranscript
            };

            lock (ConsoleLock)
            {
                Console.WriteLine($"\n--- Results ---");
                Console.WriteLine($"Matched: {matchedPhrases}/{ExpectedPhrases.Length} key phrases ({accuracy:F1}%)");
                Console.WriteLine($"Transcript length: {fullTranscript.Length} chars");
                Console.WriteLine($"Processing time: {stopwatch.Elapsed.TotalSeconds:F1}s");

                // Show sample matched phrases
                Console.WriteLine($"\nMatched phrases:");
                foreach (var phrase in ExpectedPhrases.Where(p => fullTranscript.Contains(p.ToLowerInvariant())).Take(10))
                {
                    Console.WriteLine($"  ✓ {phrase}");
                }

                var missedPhrases = ExpectedPhrases.Where(p => !fullTranscript.Contains(p.ToLowerInvariant())).ToList();
                if (missedPhrases.Count > 0)
                {
                    Console.WriteLine($"\nMissed phrases (showing first 10):");
                    foreach (var phrase in missedPhrases.Take(10))
                    {
                        Console.WriteLine($"  ✗ {phrase}");
                    }
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            lock (ConsoleLock)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }

            return new ValidationResult
            {
                ModelName = Path.GetFileName(modelPath),
                ModelSize = new FileInfo(modelPath).Length,
                Accuracy = 0,
                ProcessingTime = stopwatch.Elapsed,
                Error = ex.Message
            };
        }
    }

    private void OnResultReceived(object? sender, GgufxAsrStreamResultEventArgs result)
    {
        if (result.Kind == GgufxAsrStreamResultKind.Final && !string.IsNullOrWhiteSpace(result.Text))
        {
            var cleanText = result.Text
                .Replace("<|endoftext|>", "")
                .Replace("[_BEG_]", "")
                .Replace("[_TT_", "")
                .Trim();

            if (!string.IsNullOrWhiteSpace(cleanText))
            {
                capturedText.Add(cleanText);
            }
        }
    }

    public static void PrintSummary(List<ValidationResult> results)
    {
        Console.WriteLine($"\n\n{'='}{new string('=', 90)}");
        Console.WriteLine("VALIDATION SUMMARY - ALL MODELS");
        Console.WriteLine($"{'='}{new string('=', 90)}\n");

        var sorted = results.OrderByDescending(r => r.Accuracy).ToList();

        Console.WriteLine($"{"Rank",-6} {"Model",-35} {"Size",-12} {"Accuracy",-12} {"Time",-10}");
        Console.WriteLine(new string('-', 90));

        var rank = 1;
        foreach (var result in sorted)
        {
            var sizeStr = FormatSize(result.ModelSize);
            var accuracyStr = result.Error != null ? "ERROR" : $"{result.Accuracy:F1}%";
            var timeStr = $"{result.ProcessingTime.TotalSeconds:F1}s";

            var marker = rank == 1 ? "🏆" : rank <= 3 ? "⭐" : "  ";
            Console.WriteLine($"{marker} #{rank,-3} {result.ModelName,-35} {sizeStr,-12} {accuracyStr,-12} {timeStr,-10}");

            rank++;
        }

        Console.WriteLine($"\n{'='}{new string('=', 90)}");

        var best = sorted.FirstOrDefault();
        if (best != null && best.Error == null)
        {
            Console.WriteLine($"\n🏆 RECOMMENDED MODEL: {best.ModelName}");
            Console.WriteLine($"   Accuracy: {best.Accuracy:F1}% ({best.MatchedPhrases}/{best.TotalPhrases} phrases)");
            Console.WriteLine($"   Size: {FormatSize(best.ModelSize)}");
            Console.WriteLine($"   Processing: {best.ProcessingTime.TotalSeconds:F1}s");
        }

        Console.WriteLine();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}

internal class ValidationResult
{
    public string ModelName { get; set; } = string.Empty;
    public long ModelSize { get; set; }
    public int TranscriptLength { get; set; }
    public int MatchedPhrases { get; set; }
    public int TotalPhrases { get; set; }
    public double Accuracy { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public string FullTranscript { get; set; } = string.Empty;
    public string? Error { get; set; }
}
