using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using ErgoX.GgufX;
using ErgoX.GgufX.Asr;

namespace WhisperStream;

internal static class Program
{
    private static readonly object ConsoleLock = new();

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sample prints friendly errors instead of crashing due to environment issues.")]
    public static async Task<int> Main(string[] args)
    {
        var projectRoot = ResolveProjectRoot();
        // Focus on recommended models: large-v3-turbo and small
        string modelPath = Path.GetFullPath(Path.Combine(projectRoot, "..", "whisper", "model", "ggml-tiny.en-q5_1.bin"));
        var defaultVadModelPath = Path.GetFullPath(Path.Combine(projectRoot, "..", "whisper", "model", "ggml-silero-v5.1.2.bin"));
        string? vadModelPath = null;
        string? playbackFile = null;

        int deviceId = 5;
        bool isLoopback = true;
        bool wordLevel = false;  // Default to segment-level for accuracy validation
        bool suppressLogs = true;
        bool validateMode = false;
        bool enableVad = false;
        bool listDevices = false;

        // Parse args
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--model" && i + 1 < args.Length)
            {
                modelPath = Path.GetFullPath(args[++i]);
            }
            else if (args[i] == "--device-id" && i + 1 < args.Length)
            {
                deviceId = int.Parse(args[++i]);
            }
            else if (args[i] == "--loopback")
            {
                isLoopback = true;
            }
            else if (args[i] == "--word-level")
            {
                wordLevel = true;
            }
            else if (args[i] == "--segment-level")
            {
                wordLevel = false;
            }
            else if (args[i] == "--suppress-native-logs")
            {
                suppressLogs = true;
            }
            else if (args[i] == "--show-native-logs")
            {
                suppressLogs = false;
            }
            else if (args[i] == "--validate")
            {
                validateMode = true;
            }
            else if (args[i] == "--list-devices")
            {
                listDevices = true;
            }
            else if (args[i] == "--use-vad")
            {
                enableVad = true;
                if (File.Exists(defaultVadModelPath))
                {
                    vadModelPath = defaultVadModelPath;
                }
                else
                {
                    Console.WriteLine($"Warning: VAD model not found at {defaultVadModelPath}. Falling back to energy detector.");
                }
            }
            else if (args[i] == "--file" && i + 1 < args.Length)
            {
                playbackFile = Path.GetFullPath(args[++i]);
            }
            else if (args[i] == "--help")
            {
                PrintUsage();
                return 0;
            }
        }

        if (listDevices)
        {
            return ListDevices(projectRoot);
        }

        // Run validation mode if requested
        if (validateMode)
        {
            return await RunValidation(projectRoot, deviceId, isLoopback).ConfigureAwait(false);
        }

        modelPath = EnsureFileExists(modelPath);
        var resolvedPlaybackFile = string.IsNullOrWhiteSpace(playbackFile) ? null : EnsureFileExists(playbackFile);
        var sourceDescription = resolvedPlaybackFile is null
            ? $"Device ID: {deviceId} (loopback: {isLoopback})"
            : $"File: {resolvedPlaybackFile}";

        Console.WriteLine("=== GGUFx ASR Real-Time Streaming ===");
        Console.WriteLine($"Model: {modelPath}");
        Console.WriteLine($"VAD: {(enableVad ? $"Enabled ({vadModelPath ?? "energy detector"})" : "Disabled (energy detector)")}");
        Console.WriteLine($"Source: {sourceDescription}");
        Console.WriteLine($"Streaming mode: {(wordLevel ? "Word-level" : "Segment-level")}");
        Console.WriteLine($"Suppress native logs: {suppressLogs}");
        Console.WriteLine("Press Ctrl+C to stop...");
        Console.WriteLine("======================================\n");

        ConfigureRuntime(projectRoot);

        // Setup Ctrl+C handler (FIX: properly handle cancellation)
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler? cancelHandler = null;
        cancelHandler = async (_, e) =>
        {
            e.Cancel = true;
            if (!cts.IsCancellationRequested)
            {
                await cts.CancelAsync().ConfigureAwait(false);
            }
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            // Create session with all configuration
            using var session = GgufxAsrStreamSession.Create(
                modelPath,
                deviceId: deviceId,
                isLoopback: isLoopback,
                wordLevelTimestamps: wordLevel,
                suppressLogs: suppressLogs);

            // Subscribe to results
            session.ResultReceived += OnResultReceived;

            // Start streaming
            Console.WriteLine(resolvedPlaybackFile is null ? "Listening...\n" : $"Streaming from {resolvedPlaybackFile}\n");
            session.Open();

            if (resolvedPlaybackFile is null)
            {
                try
                {
                    // Wait indefinitely until Ctrl+C
                    await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // User pressed Ctrl+C - this is normal, fall through to cleanup
                }

                // Explicitly close session before disposing
                Console.WriteLine("\n\nStopping stream...");
                session.Close();
                Console.WriteLine("Stream stopped.");
                return 0;
            }

            await PlayFileAsync(session, resolvedPlaybackFile!, cts.Token).ConfigureAwait(false);
            Console.WriteLine("\nWaiting for final results...");
            await Task.Delay(TimeSpan.FromSeconds(1.5), cts.Token).ConfigureAwait(false);
            session.Close();
            Console.WriteLine("Playback complete.");
            return 0;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.WriteLine("\nPlayback canceled. Cleaning up...");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            if (cancelHandler is not null)
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }
    }

    private static void OnResultReceived(object? sender, GgufxAsrStreamResultEventArgs e)
    {
        lock (ConsoleLock)
        {
            switch (e.Kind)
            {
                case GgufxAsrStreamResultKind.Debug:
                    // Show debug messages for diagnostics
                    Console.WriteLine($"[DEBUG] {e.Text}");
                    break;

                case GgufxAsrStreamResultKind.Word:
                    // Word-level streaming (if enabled)
                    Console.Write($"{e.Text} ");
                    break;

                case GgufxAsrStreamResultKind.Segment:
                    // Segment-level output (primary mode for accuracy)
                    Console.WriteLine($"[SEGMENT] {e.Text} [{e.StartMs}ms - {e.EndMs}ms] (confidence: {e.Confidence:F2})");
                    break;

                case GgufxAsrStreamResultKind.Partial:
                    Console.WriteLine($"[PARTIAL] {e.Text}");
                    break;

                case GgufxAsrStreamResultKind.Final:
                    // Full accumulated text (highest accuracy)
                    Console.WriteLine($"[FULL] {e.Text} [{e.StartMs}ms - {e.EndMs}ms] (confidence: {e.Confidence:F2})");
                    Console.WriteLine();  // Blank line after full text
                    break;

                case GgufxAsrStreamResultKind.Error:
                    Console.WriteLine($"[ERROR] {e.Text}");
                    break;
            }
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: whisper_stream [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  --model PATH              Path to Whisper model (default: ggml-large-v3-turbo-q5_0.bin)");
        Console.WriteLine("  --device-id ID            Audio device ID (default: 5)");
        Console.WriteLine("  --loopback                Use loopback device");
        Console.WriteLine("  --file PATH               Stream from a WAV/MP3 file via external source");
        Console.WriteLine("  --word-level              Enable word-level timestamps (default: segment-level)");
        Console.WriteLine("  --segment-level           Use segment-level output (default)");
        Console.WriteLine("  --use-vad                 Enable energy-based VAD (Silero optional)");
        Console.WriteLine("  --suppress-native-logs    Disable Whisper native logging (default)");
        Console.WriteLine("  --show-native-logs        Force Whisper native logging");
        Console.WriteLine("  --validate                Run validation against all available models");
        Console.WriteLine("  --list-devices            List available audio capture/loopback devices and exit");
        Console.WriteLine("  --help                    Show this help");
        Console.WriteLine("\nPress Ctrl+C to stop streaming.");
    }

    private static async Task<int> RunValidation(string projectRoot, int deviceId, bool isLoopback)
    {
        var modelDir = Path.GetFullPath(Path.Combine(projectRoot, "..", "whisper", "model"));
        var vadModelPath = Path.Combine(modelDir, "ggml-silero-v5.1.2.bin");

        if (!File.Exists(vadModelPath))
        {
            Console.WriteLine($"ERROR: VAD model not found at {vadModelPath}");
            return 1;
        }

        // Find all Whisper models (exclude VAD model)
        var modelFiles = Directory.GetFiles(modelDir, "ggml-*.bin")
            .Where(f => !f.Contains("silero"))
            .OrderBy(f => new FileInfo(f).Length) // Test smallest to largest
            .ToList();

        if (modelFiles.Count == 0)
        {
            Console.WriteLine("ERROR: No Whisper models found");
            return 1;
        }

        Console.WriteLine($"Found {modelFiles.Count} models to validate:");
        foreach (var model in modelFiles)
        {
            var size = new FileInfo(model).Length;
            Console.WriteLine($"  - {Path.GetFileName(model)} ({size / (1024.0 * 1024):F1} MB)");
        }

        Console.WriteLine($"\nStarting validation (each model will run for ~65 seconds)...");
        Console.WriteLine("Make sure the YouTube video is looping on Device ID {0}!\n", deviceId);

        ConfigureRuntime(projectRoot);

        var validator = new ModelValidator();
        var results = new List<ValidationResult>();

        foreach (var modelPath in modelFiles)
        {
            var result = await validator.ValidateModel(modelPath, vadModelPath, deviceId, isLoopback, durationSeconds: 65).ConfigureAwait(false);
            results.Add(result);

            // Small delay between tests
            await Task.Delay(2000, CancellationToken.None).ConfigureAwait(false);
        }

        ModelValidator.PrintSummary(results);

        return 0;
    }

    private static void ConfigureRuntime(string projectRoot)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", ".."));
        var runtimeDirectory = Path.Combine(repositoryRoot, "src", "ErgoX.GgufX", "runtimes", "win-x64");

        if (!Directory.Exists(runtimeDirectory))
        {
            throw new DirectoryNotFoundException($"Runtime directory not found: {runtimeDirectory}");
        }

        GgufxRuntime.Configure(new GgufxRuntimeOptions
        {
            NativeRootDirectory = runtimeDirectory,
        });
    }

    private static int ListDevices(string projectRoot)
    {
        ConfigureRuntime(projectRoot);

        IReadOnlyList<GgufxAsrAudioDevice> devices;
        try
        {
            devices = GgufxAsrRuntime.ListAudioDevices();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to enumerate devices: {ex.Message}");
            return 1;
        }

        if (devices.Count == 0)
        {
            Console.WriteLine("No audio devices were reported by the GGUFx runtime.");
            return 0;
        }

        Console.WriteLine("Available audio devices:\n");
        Console.WriteLine($"{"ID",-4} {"Role",-8} {"Rate",-12} {"Ch",-4} Name");
        Console.WriteLine(new string('-', 64));

        foreach (var device in devices.OrderBy(d => d.Id))
        {
            var rate = device.SampleRate > 0 ? $"{device.SampleRate} Hz" : "n/a";
            var channels = device.ChannelCount > 0 ? device.ChannelCount.ToString() : "?";
            Console.WriteLine($"{device.Id,-4} {device.Role,-8} {rate,-12} {channels,-4} {device.Name}");
        }

        Console.WriteLine($"\nTotal devices: {devices.Count}");
        return 0;
    }

    private static string EnsureFileExists(string path)
    {
        var resolved = Path.GetFullPath(path);
        if (!File.Exists(resolved))
        {
            throw new FileNotFoundException($"File not found: {resolved}", resolved);
        }

        return resolved;
    }

    private static async Task PlayFileAsync(GgufxAsrStreamSession session, string audioPath, CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false); // Suppress unused parameter warning
        throw new NotSupportedException("Sample feeding requires external source API which is not yet implemented.");
    }

    private static string ResolveProjectRoot()
    {
        var directory = Path.GetFullPath(AppContext.BaseDirectory);

        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(directory, "whisper_stream.csproj");
            if (File.Exists(candidate))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory) ?? string.Empty;
        }

        throw new InvalidOperationException("Unable to locate whisper_stream.csproj.");
    }
}
