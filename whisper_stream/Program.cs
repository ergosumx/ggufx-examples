using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
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
        string modelPath = Path.GetFullPath(Path.Combine(projectRoot, "..", "whisper", "model", "ggml-tiny.en-q5_1.bin"));
        int deviceId = 5;
        bool isLoopback = true;
        bool wordLevel = true;
        bool suppressLogs = true;

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
            else if (args[i] == "--suppress-native-logs")
            {
                suppressLogs = true;
            }
            else if (args[i] == "--show-native-logs")
            {
                suppressLogs = false;
            }
            else if (args[i] == "--help")
            {
                PrintUsage();
                return 0;
            }
        }

        Console.WriteLine("=== GGUFx ASR Real-Time Streaming ===");
        Console.WriteLine($"Model: {modelPath}");
        Console.WriteLine($"Device ID: {deviceId} (loopback: {isLoopback})");
        Console.WriteLine($"Word-level: {wordLevel}");
        Console.WriteLine($"Suppress native logs: {suppressLogs}");
        Console.WriteLine("Press Ctrl+C to stop...");
        Console.WriteLine("======================================\n");

        ConfigureRuntime(projectRoot);

        // Setup Ctrl+C handler
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += async (sender, e) =>
        {
            e.Cancel = true;
            await cts.CancelAsync().ConfigureAwait(false);
        };

        try
        {
            // Create session
            using var session = GgufxAsrStreamSession.Create(modelPath);

            // Configure
            session.SetDeviceById(deviceId, isLoopback);
            session.SetWordTimestamps(wordLevel);
            session.SuppressLogs(suppressLogs);

            // Subscribe to results
            session.ResultReceived += OnResultReceived;

            // Start streaming
            Console.WriteLine("Listening...\n");
            session.Open();

            try
            {
                // Wait indefinitely until Ctrl+C
                await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // User pressed Ctrl+C - this is normal, fall through to cleanup
            }

            Console.WriteLine("\n\nStopped by user.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void OnResultReceived(object? sender, GgufxAsrStreamResultEventArgs e)
    {
        lock (ConsoleLock)
        {
            switch (e.Kind)
            {
                case GgufxAsrStreamResultKind.Debug:
                    // Suppress debug messages for clean output
                    break;

                case GgufxAsrStreamResultKind.Word:
                    // Print words inline as paragraph
                    Console.Write(e.Text);
                    Console.Out.Flush();
                    break;

                case GgufxAsrStreamResultKind.Partial:
                    // Suppress partial updates
                    break;

                case GgufxAsrStreamResultKind.Final:
                    // Add space after sentence for readability
                    Console.Write(" ");
                    Console.Out.Flush();
                    break;

                case GgufxAsrStreamResultKind.Error:
                    Console.WriteLine($"\n[ERROR] {e.Text}");
                    break;
            }
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: whisper_stream [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  --model PATH          Path to Whisper model");
        Console.WriteLine("  --device-id ID        Audio device ID (default: 5)");
        Console.WriteLine("  --loopback            Use loopback device");
        Console.WriteLine("  --word-level          Enable word-level timestamps");
        Console.WriteLine("  --suppress-native-logs  Disable Whisper native logging (default)");
        Console.WriteLine("  --show-native-logs      Force Whisper native logging");
        Console.WriteLine("  --help                Show this help");
        Console.WriteLine("\nPress Ctrl+C to stop streaming.");
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
