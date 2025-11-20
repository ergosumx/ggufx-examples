using System;
using System.IO;
using System.Text;
using ErgoX.GgufX.Tts;

namespace OuteTtsExample
{
    using System.Threading.Tasks;

    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("GGUFx OuteTTS Example");

            // Paths to models
            // Assuming the executable is run from the project output directory,
            // and models are in examples/oute_tts/model/
            // We might need to adjust this based on where the user runs it from.
            // For now, let's assume relative paths from the repo root or absolute paths if provided.

            string ttcPath = Path.Combine("examples", "oute_tts", "model", "OuteTTS-0.2-500M-Q5_K_M.gguf");
            string vocoderPath = Path.Combine("examples", "oute_tts", "model", "wavtokenizer-large-75-ggml-f16.gguf");

            // Default output path
            string outputDir = Path.Combine("examples", "oute_tts", "output");
            string outputPath = Path.Combine(outputDir, "output.wav");

            string text = "Hello, this is a test of the Oute TTS system running on GGUFx.";

            if (args.Length > 0) ttcPath = args[0];
            if (args.Length > 1) vocoderPath = args[1];
            if (args.Length > 2) text = args[2];
            if (args.Length > 3) outputPath = args[3];

            // Ensure output directory exists
            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(ttcPath))
            {
                // Try looking in current directory or parent directories
                if (File.Exists(Path.Combine("model", "OuteTTS-0.2-500M-Q5_K_M.gguf")))
                {
                     ttcPath = Path.Combine("model", "OuteTTS-0.2-500M-Q5_K_M.gguf");
                     vocoderPath = Path.Combine("model", "wavtokenizer-large-75-ggml-f16.gguf");
                }
                else
                {
                    Console.WriteLine($"Error: Model file not found at {ttcPath}");
                    Console.WriteLine("Usage: OuteTtsExample [ttc_model_path] [vocoder_model_path] [text] [output_path]");
                    return;
                }
            }

            Console.WriteLine($"TTC Model: {ttcPath}");
            Console.WriteLine($"Vocoder: {vocoderPath}");
            Console.WriteLine($"Text: {text}");

            try
            {
                var options = new GgufxTtsContextOptions(ttcPath, vocoderPath);

                // Optional: Configure runtime options if needed
                // options.TtcRuntime.ThreadCount = 4;
                // options.VocoderRuntime.ThreadCount = 4;

                using var session = GgufxTtsSession.Create(options);

                var request = new GgufxTtsRequestOptions(text);

                Console.WriteLine("Synthesizing...");
                var response = session.Synthesize(request);

                Console.WriteLine($"Synthesis complete.");
                Console.WriteLine($"Samples: {response.Samples.Length}");
                Console.WriteLine($"Sample Rate: {response.SampleRate}");
                Console.WriteLine($"Duration: {response.Duration}");

                await WriteWavFileAsync(outputPath, response.Samples, response.SampleRate).ConfigureAwait(false);
                Console.WriteLine($"Saved to {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        static async Task WriteWavFileAsync(string filePath, float[] samples, int sampleRate)
        {
            var stream = new FileStream(filePath, FileMode.Create);
            await using (stream.ConfigureAwait(false))
            {
                using var writer = new BinaryWriter(stream);

                int channels = 1;
                int bitsPerSample = 32; // Float
                int byteRate = sampleRate * channels * (bitsPerSample / 8);
                int blockAlign = channels * (bitsPerSample / 8);
                int subChunk2Size = samples.Length * channels * (bitsPerSample / 8);
                int chunkSize = 36 + subChunk2Size;

                // RIFF Header
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(chunkSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));

                // fmt chunk
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16); // Subchunk1Size (16 for PCM)
                writer.Write((short)3); // AudioFormat (3 for IEEE Float)
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write((short)blockAlign);
                writer.Write((short)bitsPerSample);

                // data chunk
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(subChunk2Size);

                foreach (var sample in samples)
                {
                    writer.Write(sample);
                }
            }
        }
    }
}
