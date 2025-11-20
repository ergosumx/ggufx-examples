using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using ErgoX.GgufX.Onnx;
using ErgoX.TokenX.HuggingFace;

namespace OnnxKvCacheExample;

class Program
{
    static async Task Main(string[] args)
    {
        string encoderPath = @"c:\Users\nilayparikh\.sources\vecrax\ggufx\examples\onnx_kv_cache\model\encoder_model_q4f16.onnx";
        string decoderPath = @"c:\Users\nilayparikh\.sources\vecrax\ggufx\examples\onnx_kv_cache\model\decoder_model_merged_q4f16.onnx";
        string t5Dir = @"C:\Users\nilayparikh\.sources\vecrax\ggufx\examples\onnx_kv_cache\tokenizer";

        Console.WriteLine($"Loading ONNX models from {encoderPath} and {decoderPath}...");

        // Suppress native logs
        GgufxOnnxSession.SetGlobalVerbose(true);

        try
        {
            Console.WriteLine($"Loading Tokenizer from {t5Dir}...");
            using var t5Tokenizer = await AutoTokenizer.LoadAsync(t5Dir, new AutoTokenizerLoadOptions
            {
                ApplyTokenizerDefaults = true
            }, CancellationToken.None).ConfigureAwait(false);

            using var session = new EncoderDecoderOnnxSessionWithKV(encoderPath, decoderPath);
            session.SetVerbose(true);
            Console.WriteLine("Session initialized successfully.");

            // Input Text
            string inputText = "translate English to German: Hi, my name is Nilay, what is your name?";
            Console.WriteLine($"Input: {inputText}");

            // Encode
            var t5Encoding = t5Tokenizer.Tokenizer.Encode(inputText);
            var inputIds = t5Encoding.Ids.Select(x => (int)x).ToArray();

            Console.WriteLine($"Input IDs: {string.Join(", ", inputIds)}");

            Console.WriteLine("Generating...");

            // Generate
            int[] outputTokens = session.Generate(inputIds, 50);

            Console.WriteLine($"Output Tokens: {string.Join(", ", outputTokens)}");

            // Decode
            string outputText = t5Tokenizer.Tokenizer.Decode(outputTokens);
            Console.WriteLine($"Output: {outputText}");

            Console.WriteLine("Done.");
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }
}
