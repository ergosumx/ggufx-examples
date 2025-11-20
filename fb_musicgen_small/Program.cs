using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ErgoX.GgufX.Onnx;
using ErgoX.TokenX.HuggingFace;
using System.Threading.Tasks;
using System.Threading;

namespace fb_musicgen_small;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Facebook MusicGen Small Example");

        string rootDir = @"C:\Users\nilayparikh\.sources\vecrax\ggufx\examples\fb_musicgen_small";
        string modelDir = Path.Combine(rootDir, "model");
        string encoderPath = Path.Combine(modelDir, "text_encoder_q4f16.onnx");
        string decoderPath = Path.Combine(modelDir, "decoder_model_merged_q4.onnx");
        string tokenizerDir = Path.Combine(rootDir, "tokenizer");
        string outputDir = Path.Combine(rootDir, "output");

        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        if (!File.Exists(encoderPath) || !File.Exists(decoderPath))
        {
            Console.WriteLine("Error: Model files not found.");
            Console.WriteLine($"Please ensure {encoderPath} and {decoderPath} exist.");
            return;
        }

        try
        {
            // Suppress native logs
            GgufxOnnxSession.SetGlobalVerbose(true);

            Console.WriteLine("Initializing Tokenizer...");
            // MusicGen uses T5 tokenizer for text
            using var tokenizer = await AutoTokenizer.LoadAsync(tokenizerDir, new AutoTokenizerLoadOptions
            {
                ApplyTokenizerDefaults = true
            }, CancellationToken.None).ConfigureAwait(false);

            Console.WriteLine("Initializing GgufxOnnxSession...");
            using var session = new EncoderDecoderOnnxSessionWithKV(encoderPath, decoderPath);
            session.SetVerbose(true); // Enable verbose logging for this session

            string prompt = "A light and cheerful baroque piece with cello and violin.";
            Console.WriteLine($"Prompt: {prompt}");

            // 1. Tokenize Prompt
            var encoding = tokenizer.Tokenizer.Encode(prompt);
            var inputIds = encoding.Ids.Select(x => (int)x).ToArray();
            Console.WriteLine($"Tokenized prompt: {inputIds.Length} tokens");

            // 2. Encode
            Console.WriteLine("Encoding prompt...");
            IntPtr batchEnc = session.CreateBatch(inputIds.Length, 0, 1);
            try
            {
                for (int i = 0; i < inputIds.Length; i++)
                {
                    session.BatchAdd(batchEnc, inputIds[i], i, 0, false);
                }
                session.Encode(batchEnc);
            }
            finally
            {
                session.FreeBatch(batchEnc);
            }

            // 3. Generation Loop (Delay Pattern)
            Console.WriteLine("Starting generation loop...");
            string logPath = Path.Combine(outputDir, "generation_log.txt");
            using StreamWriter logWriter = new StreamWriter(logPath);
            logWriter.WriteLine($"Generation started at {DateTime.Now}");
            logWriter.WriteLine($"Prompt: {prompt}");

            // MusicGen generates 4 codebooks.
            int nCodebooks = 4;
            int vocabSize = 2048; // EnCodec vocabulary size
            List<List<int>> codebooks = new List<List<int>>(nCodebooks);
            for (int i = 0; i < nCodebooks; i++) codebooks.Add(new List<int>());

            // We need to maintain the delay pattern.
            // Codebook k is delayed by k steps.
            // To predict step T for all codebooks, we provide inputs:
            // CB0[T-1], CB1[T-2], CB2[T-3], CB3[T-4]
            // (Assuming we are predicting T).

            // Start with PAD tokens.
            int padToken = 2048; // Assuming 2048 is PAD/BOS for this model (vocab is 2048, so 2048 is out of bounds/special)

            int maxTokens = 256;
            // We need a batch that can hold nCodebooks tokens per step.
            IntPtr batchDec = session.CreateBatch(nCodebooks, 0, 1);

            for (int step = 0; step < maxTokens; step++)
            {
                session.BatchClear(batchDec);

                // Construct input for this step
                for (int c = 0; c < nCodebooks; c++)
                {
                    int delay = c;
                    int inputIdx = step - 1 - delay; // Input is previous token

                    int token = padToken;
                    if (inputIdx >= 0 && inputIdx < codebooks[c].Count)
                    {
                        token = codebooks[c][inputIdx];
                    }

                    // Add to batch.
                    // Use seqId = c to keep histories separate in the KV cache.
                    // The native layer will map these seqIds to the batch dimension of the ONNX model.
                    session.BatchAdd(batchDec, token, step, c, true);
                }

                try
                {
                    session.Eval(batchDec);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Eval failed at step {step}: {ex.Message}");
                    logWriter.WriteLine($"Eval failed at step {step}: {ex.Message}");
                    break;
                }

                // Get Logits
                // The native library now returns logits for all codebooks flattened.
                // Shape: [nCodebooks * vocabSize]
                // Note: We assume vocabSize is 2048. If the model outputs more (e.g. 32000), we need to know.
                // Let's try to read a larger buffer just in case, then parse.
                // But GetLogits takes a size and copies that many floats.
                // If we ask for too few, we get partial. If too many, we might crash if native buffer is small.
                // Let's assume 2048 for now. If it looks wrong (all zeros?), we investigate.

                float[] allLogits = session.GetLogits(vocabSize * nCodebooks);

                // Sample and update codebooks
                for (int c = 0; c < nCodebooks; c++)
                {
                    // Extract logits for codebook c
                    // The logits are likely ordered by codebook: [CB0, CB1, CB2, CB3]
                    int offset = c * vocabSize;

                    // Simple Greedy Sampling
                    int bestToken = 0;
                    float maxLogit = float.MinValue;
                    for (int v = 0; v < vocabSize; v++)
                    {
                        float val = allLogits[offset + v];
                        if (val > maxLogit)
                        {
                            maxLogit = val;
                            bestToken = v;
                        }
                    }

                    // The prediction we just made is for which step?
                    // If we input T-1, we predict T.
                    // But for delayed codebooks, we are predicting T-delay.
                    // Actually, the model outputs predictions for the *next* token in the sequence.
                    // If we input CB1[T-2], we predict CB1[T-1].
                    // So at step `step`, we are generating:
                    // CB0[step]
                    // CB1[step-1]
                    // CB2[step-2]
                    // CB3[step-3]

                    // We only add to codebook if the index is valid (>=0).
                    int predictedIdx = step - c;
                    if (predictedIdx >= 0)
                    {
                        // Ensure we fill sequentially
                        if (codebooks[c].Count == predictedIdx)
                        {
                            codebooks[c].Add(bestToken);
                        }
                    }
                }

                if (step % 10 == 0) Console.Write(".");
            }

            session.FreeBatch(batchDec);
            Console.WriteLine("\nGeneration loop finished.");
            logWriter.WriteLine("Generation loop finished.");

            // Log generated tokens
            logWriter.WriteLine("Generated Codebooks:");
            for (int c = 0; c < nCodebooks; c++)
            {
                logWriter.WriteLine($"CB{c}: {string.Join(", ", codebooks[c])}");
            }

            // 4. Audio Decoding (EnCodec)
            Console.WriteLine("Decoding audio with EnCodec...");
            string encodecPath = Path.Combine(modelDir, "encodec_decode_q4f16.onnx");

            if (File.Exists(encodecPath))
            {
                try
                {
                    // Use OnnxSession (formerly DecoderOnnxSession) for EnCodec
                    using var encodecSession = new OnnxSession(encodecPath);

                    // Prepare input
                    // Find min length
                    int minLen = codebooks.Min(c => c.Count);

                    // Flatten codebooks
                    // The native session expects tokens in a batch.
                    // It will reshape them based on model input rank.
                    // EnCodec expects [batch, 1, n_codebooks, seq_len] (Rank 4)
                    // We need to pass tokens in the order that native code will reshape correctly.
                    // Native code:
                    // input_ids_data[i] = batch->token[i];
                    // input_ids_shape = {1, 1, n_codebooks, seq_len};
                    // So the data should be flattened as:
                    // [batch, 1, n_codebooks, seq_len]
                    // Since batch=1, 1=1, we need [n_codebooks, seq_len]
                    // So we iterate codebooks first, then seq_len.

                    int totalTokens = nCodebooks * minLen;
                    IntPtr batchEncodec = session.CreateBatch(totalTokens, 0, 1);

                    try
                    {
                        int idx = 0;
                        for (int c = 0; c < nCodebooks; c++)
                        {
                            for (int t = 0; t < minLen; t++)
                            {
                                // We add tokens to the batch.
                                // Position/SeqId don't matter for EnCodec as we just use the token array.
                                session.BatchAdd(batchEncodec, codebooks[c][t], idx++, 0, false);
                            }
                        }

                        // Run Eval
                        encodecSession.Eval(batchEncodec);

                        // Get Output (Audio)
                        // We need to estimate the size.
                        // EnCodec downsampling is usually 320.
                        // So output samples approx seq_len * 320.
                        // Let's allocate a bit more to be safe or just use a large buffer.
                        // 256 tokens * 320 = 81920 samples.
                        // Let's ask for 200,000 floats.
                        int estimatedSize = minLen * 640; // Conservative estimate
                        float[] audioData = encodecSession.GetLogits(estimatedSize);

                        // Check if we got data (not all zeros)
                        if (audioData.Length > 0 && audioData.Any(x => x != 0))
                        {
                            // Save to WAV
                            // Assuming 1 channel (mono) and 32kHz
                            string wavPath = Path.Combine(outputDir, "output.wav");
                            WriteWav(wavPath, audioData, 32000, 1);
                            Console.WriteLine($"Saved audio to {wavPath}");
                        }
                        else
                        {
                            Console.WriteLine("EnCodec produced silence or no data.");
                        }
                    }
                    finally
                    {
                        session.FreeBatch(batchEncodec);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"EnCodec error: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }
            else
            {
                Console.WriteLine("EnCodec model not found.");
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }


    static void WriteWav(string filename, float[] samples, int sampleRate, int channels)
    {
#pragma warning disable MA0045
        using var fs = new FileStream(filename, FileMode.Create);
        using var bw = new BinaryWriter(fs);
#pragma warning restore MA0045

        int bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;
        int subChunk2Size = samples.Length * bitsPerSample / 8;

        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + subChunk2Size);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitsPerSample);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(subChunk2Size);

        foreach (var sample in samples)
        {
            short s = (short)(Math.Clamp(sample, -1.0f, 1.0f) * 32767);
            bw.Write(s);
        }
    }
}
