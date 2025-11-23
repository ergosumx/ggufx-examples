// GGUFx ASR Command Recognition Example
// Demonstrates CommandList, AlwaysPrompt, and GeneralPurpose modes

using ErgoX.GgufX.Asr;

namespace ErgoX.GgufX.Examples.WhisperCommand;

internal static class Program
{
    private static GgufxAsrCommandSession? _session;
    private static bool _running = true;

    private static async Task Main(string[] args)
    {
        Console.WriteLine("=== GGUFx ASR Command Recognition Demo ===\n");

        // Parse command line
        var modelPath = GetArgValue(args, "--model") ?? "models/ggml-base.en.bin";
        var mode = GetArgValue(args, "--mode") ?? "list";
        var deviceId = int.Parse(GetArgValue(args, "--device") ?? "-1");
        var commandsPath = GetArgValue(args, "--commands") ?? "commands.txt";

        // Check for test mode
        if (args.Contains("--test"))
        {
            if (!File.Exists(modelPath))
            {
                Console.Error.WriteLine($"Error: Model file not found: {modelPath}");
                return;
            }
            if (!File.Exists(commandsPath))
            {
                Console.Error.WriteLine($"Error: Commands file not found: {commandsPath}");
                return;
            }

            var tester = new ConfigTester(modelPath, commandsPath, deviceId);
            await tester.RunTests().ConfigureAwait(false);
            return;
        }

        if (!File.Exists(modelPath))
        {
            Console.Error.WriteLine($"Error: Model file not found: {modelPath}");
            Console.WriteLine("\nUsage: whisper_command --model <path> [--mode list|prompt|general] [--device <id>] [--test]");
            Console.WriteLine("       --test: Run systematic configuration tests to find optimal settings");
            return;
        }

        Console.WriteLine($"Model: {modelPath}");
        Console.WriteLine($"Mode: {mode}");
        Console.WriteLine($"Device: {(deviceId >= 0 ? deviceId.ToString() : "default")}\n");

        // List available devices
        ListAudioDevices();

        // Create session based on mode
        try
        {
            Console.WriteLine("[DEBUG] Creating session...");

            _session = mode.ToLower() switch
            {
                "list" => CreateCommandListSession(modelPath, deviceId),
                "prompt" => CreateAlwaysPromptSession(modelPath, deviceId),
                "general" => CreateGeneralPurposeSession(modelPath, deviceId),
                _ => throw new ArgumentException($"Invalid mode: {mode}")
            };            // Handle Ctrl+C gracefully
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                _running = false;
                Console.WriteLine("\nStopping...");
            };

            Console.WriteLine("\n[DEBUG] About to call Start()...");

            // Start recognition
            try
            {
                _session.Start();
                Console.WriteLine("[DEBUG] Start() returned successfully!");
                Console.WriteLine("\n--- Session started. Press Ctrl+C to stop ---\n");
            }
            catch (Exception startEx)
            {
                Console.Error.WriteLine($"[ERROR] Start() threw exception: {startEx.GetType().Name}");
                Console.Error.WriteLine($"[ERROR] Message: {startEx.Message}");
                Console.Error.WriteLine($"[ERROR] Stack: {startEx.StackTrace}");
                throw;
            }

            Console.WriteLine("[DEBUG] Entering wait loop...");

            // Wait for Ctrl+C
            while (_running)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }            // Cleanup
            _session.Stop();
            _session.Dispose();
            _session = null;

            Console.WriteLine("Session stopped.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[ERROR] Exception occurred: {ex.GetType().Name}");
            Console.Error.WriteLine($"[ERROR] Message: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.Error.WriteLine($"[ERROR] Inner: {ex.InnerException.Message}");
            }
            Console.Error.WriteLine($"[ERROR] Stack trace:");
            Console.Error.WriteLine(ex.StackTrace);
            return;
        }
    }

    private static GgufxAsrCommandSession CreateCommandListSession(string modelPath, int deviceId)
    {
        Console.WriteLine("Mode: Command List (classify into predefined commands)");
        Console.WriteLine("Available commands: play, pause, stop, next, previous, volume up, volume down");
        Console.WriteLine();

        // Check if Silero VAD model exists
        var vadModelPath = Path.Combine(
            Path.GetDirectoryName(modelPath) ?? "",
            "ggml-silero-v5.1.2.bin");

        if (File.Exists(vadModelPath))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Found Silero VAD model: {Path.GetFileName(vadModelPath)}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠ Silero VAD model not found, using simple energy-based VAD");
            Console.WriteLine($"  For better performance, place ggml-silero-v5.1.2.bin in model directory");
            Console.ResetColor();
            vadModelPath = null;
        }

        var options = new GgufxAsrCommandOptions
        {
            ModelPath = modelPath,
            Mode = GgufxAsrCommandMode.CommandList,
            CommandsFilePath = "commands.txt",
            DeviceId = deviceId,
            VadModelPath = vadModelPath,  // Use Silero VAD if available (not yet implemented in native)
            VadThreshold = 0.01f,  // VERY LOW - almost any sound will trigger (testing)
            CommandDurationMs = 3000,  // 3 seconds for command
            ThreadCount = 4,
            UseGpu = GgufxTriState.Default,
            FlashAttention = GgufxTriState.Default,
            SuppressLogs = false,  // Enable native logging to debug

            // *** ACCURACY TUNING PARAMETERS - ALL CONFIGURABLE ***
            // Larger audio context = better accuracy but slower
            // 0 = default (1500), 768 = fast but less accurate, 1000 = balanced
            AudioContextSize = 0,  // Use full context for best accuracy

            // Max audio buffer to process in seconds
            // 0 = unlimited, 2-5 = typical for commands
            MaxAudioBufferSeconds = 3,  // Process last 3 seconds only

            // No context mode - disable for better accuracy, enable for speed
            NoContext = false,  // Keep context for better accuracy

            // Single segment mode - typically true for commands
            SingleSegment = true,

            // Max tokens per decode - affects how much text is generated
            MaxTokens = 1,  // 1 token for classification (faster)

            // Thresholds for filtering/splitting
            EntropyThreshold = 2.4f,      // Default Whisper value
            LogProbThreshold = -1.0f,     // No filtering
            NoSpeechThreshold = 0.6f      // Default silence threshold
        };

        var session = new GgufxAsrCommandSession(options);
        session.CommandRecognized += OnCommandRecognized;        return session;
    }

    private static GgufxAsrCommandSession CreateAlwaysPromptSession(string modelPath, int deviceId)
    {
        Console.WriteLine("Mode: Always Prompt (require activation phrase)");
        Console.WriteLine("Activation phrase: \"hey assistant\"");
        Console.WriteLine("Say the activation phrase, then your command.");

        var options = new GgufxAsrCommandOptions
        {
            ModelPath = modelPath,
            Mode = GgufxAsrCommandMode.AlwaysPrompt,
            CommandsFilePath = "commands.txt",
            ActivationPhrase = "hey assistant",
            ActivationThreshold = 0.7f,
            DeviceId = deviceId,
            VadThreshold = 0.6f,
            PromptDurationMs = 5000,     // 5 seconds for activation
            CommandDurationMs = 8000,    // 8 seconds for command
            ThreadCount = 4,
            UseGpu = GgufxTriState.Default,
            FlashAttention = GgufxTriState.Default,
            SuppressLogs = false
        };

        var session = new GgufxAsrCommandSession(options);
        session.CommandRecognized += OnCommandRecognized;
        return session;
    }

    private static GgufxAsrCommandSession CreateGeneralPurposeSession(string modelPath, int deviceId)
    {
        Console.WriteLine("Mode: General Purpose (free-form transcription)");
        Console.WriteLine("Speak naturally - all speech will be transcribed.");

        var options = new GgufxAsrCommandOptions
        {
            ModelPath = modelPath,
            Mode = GgufxAsrCommandMode.GeneralPurpose,
            DeviceId = deviceId,
            VadThreshold = 0.6f,
            CommandDurationMs = 10000,   // 10 seconds for general speech
            MaxTokens = 128,              // More tokens for longer transcriptions
            ThreadCount = 4,
            UseGpu = GgufxTriState.Default,
            FlashAttention = GgufxTriState.Default,
            SuppressLogs = false,
            Language = "en"
        };

        var session = new GgufxAsrCommandSession(options);
        session.CommandRecognized += OnCommandRecognized;
        return session;
    }

    private static void OnCommandRecognized(object? sender, GgufxAsrCommandResultEventArgs e)
    {
        var result = e.Result;
        var timestamp = DateTime.Now.ToString("HH:mm:ss");

        Console.WriteLine($"\n[{timestamp}] --- Recognition Result ---");

        if (result.IsActivationPhraseMatch)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Activation phrase matched (confidence: {result.ActivationConfidence:F2})");
            Console.ResetColor();
            Console.WriteLine($"  Transcription: \"{result.Transcription}\"");
        }
        else if (result.BestCommand != null)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"► Command: {result.BestCommand.Command}");
            Console.ResetColor();
            Console.WriteLine($"  Probability: {result.BestCommand.Probability:F2}");

            if (result.Alternatives.Count > 1)
            {
                Console.WriteLine("  Alternatives:");
                foreach (var alt in result.Alternatives.Skip(1).Take(3))
                {
                    Console.WriteLine($"    - {alt.Command} ({alt.Probability:F2})");
                }
            }
        }
        else if (!string.IsNullOrEmpty(result.Transcription))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"► Transcription: \"{result.Transcription}\"");
            Console.ResetColor();
        }

        if (result.ProcessingTimeMs > 0)
        {
            Console.WriteLine($"  Processing time: {result.ProcessingTimeMs} ms");
        }

        Console.WriteLine();
    }

    private static void ListAudioDevices()
    {
        Console.WriteLine("Available audio devices:");
        Console.WriteLine("  [Use --device <id> to select a specific device]");
        Console.WriteLine("  [-1] Default system microphone");
        Console.WriteLine();

        // Note: Device enumeration is handled by native code
        // In production, you might expose a native API to list devices
        Console.WriteLine("For Jabra devices:");
        Console.WriteLine("  - Ensure Jabra Direct is installed");
        Console.WriteLine("  - Check device is connected and recognized by Windows");
        Console.WriteLine("  - Use Windows Sound Settings to verify input device");
        Console.WriteLine();
    }

    private static string? GetArgValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
