using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using ErgoX.GgufX.Onnx;
using ErgoX.TokenX.HuggingFace;

namespace FalconsAiTextSummarizationExample;

class Program
{
    static async Task Main(string[] args)
    {
        string baseDir = @"c:\Users\nilayparikh\.sources\vecrax\ggufx\examples\falconsai_text_summarization";
        string encoderPath = System.IO.Path.Combine(baseDir, "model", "encoder_model_q4f16.onnx");
        string decoderPath = System.IO.Path.Combine(baseDir, "model", "decoder_model_merged_q4f16.onnx");
        string tokenizerDir = System.IO.Path.Combine(baseDir, "tokenizer");

        Console.WriteLine($"Loading ONNX models from {encoderPath} and {decoderPath}...");

        // Suppress native logs
        GgufxOnnxSession.SetGlobalVerbose(true);

        try
        {
            Console.WriteLine($"Loading Tokenizer from {tokenizerDir}...");
            using var tokenizer = await AutoTokenizer.LoadAsync(tokenizerDir, new AutoTokenizerLoadOptions
            {
                ApplyTokenizerDefaults = true
            }, CancellationToken.None).ConfigureAwait(false);

            using var session = new EncoderDecoderOnnxSessionWithKV(encoderPath, decoderPath);
            session.SetVerbose(true);
            Console.WriteLine("Session initialized successfully.");

            // Input Text
            string inputText = @"Summerise this in 3-4 lines
-----
KV Cache Update Logic (ggufx_onnx_kv.cpp): Issue: The plugin was using kv_cache->get_k(...) with n_tokens_new (batch size), which returned a view starting at the beginning of the cache. Consequently, the plugin was repeatedly overwriting the first few cells (e.g., P0) of the cache with new tokens, leaving historical positions (e.g., P1+) as zeros. Fix: Modified the update loop to request a view of the full cache (kv_size) and then manually calculate the correct byte offset for each token's cell index (derived from get_cell_index). This ensures that each token is written to its allocated slot in the KV cache. Layer Mapping: Issue: The code was accessing kv_cache->layers[l] directly using the model layer index l. llama.cpp uses an internal mapping (map_layer_ids) which can differ from the model index. Fix: Updated the gather logic to use map_layer_ids to resolve the correct internal layer index before accessing the layer buffer. Sequence Length Calculation: Issue: The max_past_len was not being calculated correctly for the gathered batch. Fix: Implemented get_kv_cache_length to correctly determine the maximum past position for the sequence.
";
            Console.WriteLine($"Input: {inputText}");

            // Encode
            var encoding = tokenizer.Tokenizer.Encode(inputText);
            var inputIds = encoding.Ids.Select(x => (int)x).ToArray();

            Console.WriteLine($"Input IDs: {string.Join(", ", inputIds)}");

            Console.WriteLine("Generating...");

            // Generate
            int[] outputTokens = session.Generate(inputIds, 200);

            Console.WriteLine($"Output Tokens: {string.Join(", ", outputTokens)}");

            // Decode
            string outputText = tokenizer.Tokenizer.Decode(outputTokens);
            Console.WriteLine($"Output: {outputText}");
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
