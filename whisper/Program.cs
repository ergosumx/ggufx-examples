using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ErgoX.GgufX;
using ErgoX.GgufX.Asr;

namespace WhisperExample
{
    internal static class Program
    {
        private const string ModelFileName = "ggml-large-v3-turbo-q5_0.bin";
        private const string VadModelFileName = "ggml-silero-v5.1.2.bin";

        private static readonly string[] DemoAudioFiles =
        {
            Path.Combine("examples", "_io", "audio", "wav", "jfk.wav"),
        };

        public static async Task<int> Main()
        {
            try
            {
                Console.WriteLine("GGUFx Whisper quickstart\n");

                var projectRoot = ResolveProjectRoot();
                var repositoryRoot = Path.GetFullPath(Path.Combine(projectRoot, "..", ".."));

                ConfigureRuntime(repositoryRoot);

                var contextOptions = BuildContextOptions(projectRoot);
                using var session = GgufxAsrSession.Create(contextOptions);

                foreach (var relativePath in DemoAudioFiles)
                {
                    var audioPath = EnsureFile(Path.Combine(repositoryRoot, relativePath));
                    await TranscribeAsync(session, audioPath).ConfigureAwait(false);
                }

                return 0;
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or GgufxNativeException or ArgumentException)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static GgufxAsrContextOptions BuildContextOptions(string projectRoot)
        {
            var modelPath = EnsureFile(Path.Combine(projectRoot, "model", ModelFileName));
            var vadModelPath = EnsureFile(Path.Combine(projectRoot, "model", VadModelFileName));

            return new GgufxAsrContextOptions(modelPath)
            {
                Language = "auto",
                ThreadCount = Math.Max(2, Environment.ProcessorCount / 2),
                ProcessorCount = 1,
                ResponseBufferSize = 512 * 1024,
                VadEnable = GgufxTriState.Enabled,
                VadModelPath = vadModelPath,
                DebugMode = GgufxTriState.Disabled,
                PrintProgress = GgufxTriState.Disabled,
            };
        }

        private static async Task TranscribeAsync(GgufxAsrSession session, string audioPath)
        {
            Console.WriteLine($"Transcribing {Path.GetFileName(audioPath)}");

            var decodedAudio = GgufxAsrAudioDecoder.DecodeFile(audioPath);

            var request = new GgufxAsrRequestOptions(decodedAudio.Samples, decodedAudio.SampleRate)
            {
                Language = "auto",
                VadEnable = GgufxTriState.Enabled,
            };

            var stopwatch = Stopwatch.StartNew();
            var transcript = await session.TranscribeAsync(request, segment =>
            {
                Console.WriteLine($"{segment.Index:D3} [{segment.Start:mm\\:ss\\.fff} → {segment.End:mm\\:ss\\.fff}] {segment.Text}");
            }, CancellationToken.None).ConfigureAwait(false);
            stopwatch.Stop();

            Console.WriteLine("\nTranscript:");
            Console.WriteLine(transcript.Text);
            Console.WriteLine($"Elapsed: {stopwatch.Elapsed.TotalSeconds:F2}s\n");
        }

        private static void ConfigureRuntime(string repositoryRoot)
        {
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
                var candidate = Path.Combine(directory, "whisper.csproj");
                if (File.Exists(candidate))
                {
                    return directory;
                }

                directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
            }

            throw new InvalidOperationException("Unable to locate whisper.csproj.");
        }

        private static string EnsureFile(string path)
        {
            var resolved = Path.GetFullPath(path);
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException($"File not found: {resolved}", resolved);
            }

            return resolved;
        }
    }
}
