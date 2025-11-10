namespace NeuTtsAirQ4Gguf;

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ErgoX.GgufX;
using ErgoX.GgufX.Tts;

/// <summary>
/// Console sample showcasing NeuTTS Air voice cloning via GGUFx.
/// </summary>
internal static class Program
{
    private const string ProjectFileName = "neutts_air_q4_gguf.csproj";

    private const string DefaultPrompt = """
        Hello there! I am speaking with a cloned voice generated entirely on-device by GGUFx and NeuTTS Air.
        Just a few seconds of reference audio was enough to capture this speaking style.
        """;

    private static readonly string ProjectRoot = ResolveProjectRoot();
    private static readonly string ModelsDirectory = Path.GetFullPath(Path.Combine(ProjectRoot, "model"));
    private static readonly string OutputDirectory = Path.GetFullPath(Path.Combine(ProjectRoot, "io", "output"));
    private static readonly string DefaultSpeakerProfile = Path.GetFullPath(Path.Combine(ProjectRoot, "io", "speaker", "clone.voice.json"));

    /// <summary>
    /// Entry-point executed by the sample.
    /// </summary>
    /// <param name="args">Optional command-line arguments controlling synthesis.</param>
    /// <returns>Zero on success; otherwise a non-zero exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            var ttcModel = ResolveModelPath("neutts-air-Q4-0.gguf", "neutts-air-Q4_0.gguf", "neutts-air-Q4.gguf");
            var vocoderModel = ResolveModelPath("neucodec-Q4_0.gguf", "neucodec-q4_0.gguf", "neucodec.gguf");

            Directory.CreateDirectory(OutputDirectory);

            var contextOptions = BuildContextOptions(ttcModel, vocoderModel, options);
            using var session = GgufxTtsSession.Create(contextOptions);

            var request = new GgufxTtsRequestOptions(options.Prompt)
            {
                SampleRateHint = contextOptions.SampleRate,
                UseGuideTokens = options.EnableGuideTokens ? GgufxTriState.Default : GgufxTriState.Disabled,
            };

            Console.WriteLine("GGUFx NeuTTS Air sample");
            Console.WriteLine($"  TTC model   : {ttcModel}");
            Console.WriteLine($"  Vocoder     : {vocoderModel}");
            Console.WriteLine($"  Speaker     : {(string.IsNullOrWhiteSpace(contextOptions.SpeakerFilePath) ? "none (default voice)" : contextOptions.SpeakerFilePath)}");
            Console.WriteLine($"  Prompt      : {TrimForConsole(options.Prompt)}");
            Console.WriteLine();

            var stopwatch = Stopwatch.StartNew();
            var response = session.Synthesize(request);
            stopwatch.Stop();

            var outputPath = EnsureOutputPath(options.OutputPath);
            await WriteWaveFileAsync(outputPath, response.Samples.AsMemory(), response.SampleRate).ConfigureAwait(false);

            Console.WriteLine($"Generated {response.Samples.Length} samples (~{response.Duration.TotalSeconds:F1}s) at {response.SampleRate} Hz.");
            Console.WriteLine($"Audio code tokens: {response.CodeTokenCount}.");
            Console.WriteLine($"Elapsed: {stopwatch.Elapsed.TotalSeconds:F2}s.");
            Console.WriteLine($"Saved WAV to {outputPath}.");
            Console.WriteLine();
            Console.WriteLine("Consider swapping the speaker JSON with another profile to demonstrate instant voice cloning.");

            return 0;
        }
        catch (GgufxNativeException ex)
        {
            Console.Error.WriteLine($"Native runtime failure ({ex.StatusCode}): {ex.NativeMessage}");
            return 1;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is FileNotFoundException || ex is InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static GgufxTtsContextOptions BuildContextOptions(string ttcModel, string vocoderModel, RunArguments arguments)
    {
        var options = new GgufxTtsContextOptions(ttcModel, vocoderModel)
        {
            SampleRate = 24_000,
            SampleBufferCapacity = 24_000 * 20,
            MaxCodeTokens = 4096,
            UseGuideTokens = arguments.EnableGuideTokens ? GgufxTriState.Enabled : GgufxTriState.Disabled,
        };

        options.TtcRuntime.ThreadCount = Math.Max(2, Environment.ProcessorCount / 2);
        options.VocoderRuntime.ThreadCount = options.TtcRuntime.ThreadCount;
        options.TtcRuntime.ContextLength = 2_048;
        options.TtcRuntime.PredictLength = 1_024;
        options.VocoderRuntime.ContextLength = 1_024;
        options.VocoderRuntime.PredictLength = 512;

        if (!string.IsNullOrWhiteSpace(arguments.SpeakerPath))
        {
            options.SpeakerFilePath = arguments.SpeakerPath;
        }

        return options;
    }

    private static RunArguments ParseArguments(string[] args)
    {
        var prompt = DefaultPrompt;
        string? speakerOverride = null;
        string? outputOverride = null;
        var enableGuideTokens = true;

        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            switch (argument)
            {
                case "--text":
                case "-t":
                    prompt = RequireValue(args, ref i, argument);
                    break;
                case "--speaker":
                case "-s":
                    speakerOverride = RequireValue(args, ref i, argument);
                    break;
                case "--output":
                case "-o":
                    outputOverride = RequireValue(args, ref i, argument);
                    break;
                case "--no-guide":
                    enableGuideTokens = false;
                    break;
                default:
                    prompt = string.Join(' ', args[i..]);
                    i = args.Length;
                    break;
            }
        }

        var speakerPath = ResolveSpeakerPath(speakerOverride);
        var outputPath = outputOverride is null ? null : ResolveAbsolutePath(outputOverride);

        return new RunArguments(prompt, speakerPath, outputPath, enableGuideTokens);
    }

    private static string ResolveModelPath(string primaryFileName, params string[] fallbacks)
    {
        var candidates = new string[1 + fallbacks.Length];
        candidates[0] = primaryFileName;
        if (fallbacks.Length > 0)
        {
            Array.Copy(fallbacks, 0, candidates, 1, fallbacks.Length);
        }

        foreach (var candidate in candidates)
        {
            var resolved = Path.GetFullPath(Path.Combine(ModelsDirectory, candidate));
            if (File.Exists(resolved))
            {
                return resolved;
            }
        }

        throw new FileNotFoundException($"Unable to locate model file. Expected one of: {string.Join(", ", candidates)} in '{ModelsDirectory}'.", primaryFileName);
    }

    private static string? ResolveSpeakerPath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var resolved = ResolveAbsolutePath(overridePath);
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException($"Speaker profile not found: {resolved}", resolved);
            }

            return resolved;
        }

        var environmentOverride = Environment.GetEnvironmentVariable("NEUTTS_SPEAKER_PATH");
        if (!string.IsNullOrWhiteSpace(environmentOverride))
        {
            var resolved = ResolveAbsolutePath(environmentOverride);
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException($"Speaker profile specified via NEUTTS_SPEAKER_PATH was not found: {resolved}", resolved);
            }

            return resolved;
        }

        return File.Exists(DefaultSpeakerProfile) ? DefaultSpeakerProfile : null;
    }

    private static string EnsureOutputPath(string? requestedPath)
    {
        var outputPath = requestedPath ?? Path.Combine(OutputDirectory, $"neuTTS-{DateTime.UtcNow:yyyyMMdd-HHmmss}.wav");
        var directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Unable to determine output directory for '{outputPath}'.");
        }

        Directory.CreateDirectory(directory);
        return outputPath;
    }

    private static string ResolveAbsolutePath(string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(ProjectRoot, path));
    }

    private static string ResolveProjectRoot()
    {
        var directory = Path.GetFullPath(AppContext.BaseDirectory);
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(directory, ProjectFileName);
            if (File.Exists(candidate))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException($"Unable to locate the project root containing '{ProjectFileName}'.");
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Option '{option}' expects a value.");
        }

        index++;
        return args[index];
    }

    private static string TrimForConsole(string value)
    {
        const int maxLength = 96;
        if (value.Length <= maxLength)
        {
            return value.Replace("\r", string.Empty).Replace("\n", " ");
        }

        var flattened = value.Replace("\r", string.Empty).Replace("\n", " ");
        return flattened[..maxLength] + "…";
    }

    private static async Task WriteWaveFileAsync(string outputPath, ReadOnlyMemory<float> samples, int sampleRate)
    {
        var dataSize = checked(samples.Length * sizeof(short));
        var headerBuffer = BuildWaveHeader(sampleRate, dataSize);
        var pcmBuffer = ConvertSamplesToPcm(samples.Span);
        var payload = new byte[headerBuffer.Length + pcmBuffer.Length];

        Buffer.BlockCopy(headerBuffer, 0, payload, 0, headerBuffer.Length);
        if (pcmBuffer.Length > 0)
        {
            Buffer.BlockCopy(pcmBuffer, 0, payload, headerBuffer.Length, pcmBuffer.Length);
        }

        await File.WriteAllBytesAsync(outputPath, payload, CancellationToken.None).ConfigureAwait(false);
    }

    private static byte[] BuildWaveHeader(int sampleRate, int dataSize)
    {
        var fileSize = 36 + dataSize;
        var buffer = new byte[44];
        var span = buffer.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..8], fileSize);
        "WAVE"u8.CopyTo(span[8..12]);
        "fmt "u8.CopyTo(span[12..16]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..20], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..22], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..24], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..28], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..32], sampleRate * sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(span[32..34], (short)sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(span[34..36], 16);
        "data"u8.CopyTo(span[36..40]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..44], dataSize);
        return buffer;
    }

    private static byte[] ConvertSamplesToPcm(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return Array.Empty<byte>();
        }

        var buffer = new byte[samples.Length * sizeof(short)];
        var pcm = MemoryMarshal.Cast<byte, short>(buffer.AsSpan());

        for (var i = 0; i < samples.Length; i++)
        {
            var clamped = Math.Clamp(samples[i], -1.0f, 1.0f);
            pcm[i] = (short)Math.Round(clamped * short.MaxValue, MidpointRounding.AwayFromZero);
        }

        return buffer;
    }

    private sealed record RunArguments(string Prompt, string? SpeakerPath, string? OutputPath, bool EnableGuideTokens);
}
