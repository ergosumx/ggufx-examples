using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using ErgoX.GgufX.Onnx;
using ErgoX.TokenX.HuggingFace;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

class Program
{
    private const int EmbeddingSize = 384; // all-minilm-l12-v2

    static async System.Threading.Tasks.Task Main(string[] args)
    {
        string repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot == null)
        {
            Console.WriteLine("Error: Could not find repository root.");
            return;
        }

        string modelPath = Path.Combine(repoRoot, "examples", "models", "embeddings", "sentence-transformers", "lm12-v2", "model_quantized.onnx");
        // Point to vocab.txt for the new hardcoded C++ tokenizer logic
        var tokenizerPath = Path.Combine(repoRoot, "examples", "models", "embeddings", "sentence-transformers", "tokenizer", "vocab.txt");
        string tokenizerJsonPath = Path.Combine(repoRoot, "examples", "models", "embeddings", "sentence-transformers", "tokenizer", "tokenizer.json");

        string dataDir = Path.Combine(repoRoot, "examples", "embedding_benchmark", "data");
        string accuracyDataPath = Path.Combine(dataDir, "accuracy_test.json");

        if (!File.Exists(modelPath))
        {
            Console.WriteLine($"Model not found: {modelPath}");
            return;
        }
        if (!File.Exists(tokenizerPath))
        {
            Console.WriteLine($"Tokenizer vocab not found: {tokenizerPath}");
            return;
        }

        Console.WriteLine($"Model: {modelPath}");
        Console.WriteLine($"Tokenizer: {tokenizerPath}");

        // Warmup text
        string text = "Hello world! This is a benchmark for embedding generation.";
        int iterations = 1000;

        Console.WriteLine($"\nRunning benchmark with {iterations} iterations...");

        GgufxOnnxSession.SetGlobalVerbose(false);

        bool runAll = args.Length == 0 || !args.Contains("--ggufx-only");

        // 1. HF Tokenizer + Microsoft.ML.OnnxRuntime
        if (runAll)
        {
            BenchmarkHfOnnx(modelPath, tokenizerJsonPath, text, iterations);
        }

        // 2. GGUFx ONNX (Internal Tokenization)
        BenchmarkGgufxInternal(modelPath, tokenizerPath, text, iterations);

        // 3. Simple Accuracy Check
        CheckAccuracy(modelPath, tokenizerJsonPath, tokenizerPath, text);

        // 4. Detailed Accuracy Benchmark (from benchmark_onnx)
        if (File.Exists(accuracyDataPath))
        {
            var jsonContent = await File.ReadAllTextAsync(accuracyDataPath).ConfigureAwait(false);
            var inputs = JsonSerializer.Deserialize<List<string>>(jsonContent) ?? new List<string>();
            CheckAccuracyDetailed(modelPath, tokenizerJsonPath, tokenizerPath, inputs);
        }
        else
        {
            Console.WriteLine($"\nSkipping Detailed Accuracy Check: {accuracyDataPath} not found.");
        }
    }

    static void CheckAccuracyDetailed(string modelPath, string tokenizerJsonPath, string vocabPath, List<string> inputs)
    {
        Console.WriteLine("\n==================================================");
        Console.WriteLine("       DETAILED ACCURACY BENCHMARK");
        Console.WriteLine("==================================================\n");

        try
        {
            Console.WriteLine($"Loaded {inputs.Count} test inputs.");

            // Initialize Engines
            var tokenizer = Tokenizer.FromFile(tokenizerJsonPath);

            // Match C++ options
            using var options = new SessionOptions();
            options.IntraOpNumThreads = Environment.ProcessorCount;
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED;
            options.EnableMemoryPattern = true;
            options.EnableCpuMemArena = true;

            // Enable spinning for lower latency
            options.AddSessionConfigEntry("session.intra_op.allow_spinning", "1");

            Console.WriteLine($"EmbeddingSession: Creating ONNX session for model '{modelPath}'...");
            using var msSession = new InferenceSession(modelPath, options);
            using var ggufxSession = new GgufxEmbeddingSession(modelPath, vocabPath);
            ggufxSession.SetVerbose(true);

            Console.WriteLine("EmbeddingSession: ONNX Input Names:");
            foreach (var key in msSession.InputMetadata.Keys)
            {
                Console.WriteLine($" - {key}");
            }

            double totalSim = 0;
            int lowAccuracyCount = 0;

            Console.WriteLine($"{"Text Preview",-40} | {"Similarity",-10}");
            Console.WriteLine(new string('-', 60));

            // Enable verbose logging to see native tokens
            GgufxOnnxSession.SetGlobalVerbose(false);
            ggufxSession.SetVerbose(true);

            foreach (var rawText in inputs)
            {
                var text = NormalizeText(rawText);

                // MS ONNX
                var encoding = tokenizer.Encode(text);
                var inputIds = encoding.Ids.Select(x => (long)x).ToArray();
                var msEmb = RunOnnxWithOutput(msSession, inputIds, encoding.AttentionMask.Select(x => (long)x).ToArray(), encoding.TypeIds.Select(x => (long)x).ToArray());

                // GGUFx Native
                var ggufxEmb = ggufxSession.EmbedText(text, EmbeddingSize);

                // Compare
                double sim = CosineSimilarity(msEmb, ggufxEmb);
                totalSim += sim;

                string preview = text.Length > 37 ? text.Substring(0, 37) + "..." : text;
                // Replace newlines for clean output
                preview = preview.Replace("\n", " ").Replace("\r", "");

                Console.WriteLine($"{preview,-40} | {sim:F6}");

                if (text == "highly recommended!")
                {
                    Console.WriteLine("   -> Debugging 'highly recommended!'");
                    // 1. Try with UNK for !
                    // highly=3811, recommended=6749, !=999, UNK=100
                    long[] debugIdsUnk = { 101, 3811, 6749, 100, 102 };
                    var debugEmbUnk = RunOnnxWithOutput(msSession, debugIdsUnk, Enumerable.Repeat(1L, 5).ToArray(), Enumerable.Repeat(0L, 5).ToArray());
                    double simUnk = CosineSimilarity(debugEmbUnk, ggufxEmb);
                    Console.WriteLine($"   -> Sim with UNK (! -> 100): {simUnk:F6}");

                    // 3. Try with . (1012) instead of !
                    long[] debugIdsDot = { 101, 3811, 6749, 1012, 102 };
                    var debugEmbDot = RunOnnxWithOutput(msSession, debugIdsDot, Enumerable.Repeat(1L, 5).ToArray(), Enumerable.Repeat(0L, 5).ToArray());
                    double simDot = CosineSimilarity(debugEmbDot, ggufxEmb);
                    Console.WriteLine($"   -> Sim with . (! -> 1012): {simDot:F6}");

                    // 4. Try with ? (1029)
                    long[] debugIdsQ = { 101, 3811, 6749, 1029, 102 };
                    var debugEmbQ = RunOnnxWithOutput(msSession, debugIdsQ, Enumerable.Repeat(1L, 5).ToArray(), Enumerable.Repeat(0L, 5).ToArray());
                    double simQ = CosineSimilarity(debugEmbQ, ggufxEmb);
                    Console.WriteLine($"   -> Sim with ? (! -> 1029): {simQ:F6}");
                }

                if (sim < 0.98)
                {
                    lowAccuracyCount++;
                    Console.WriteLine($"   -> MS Tokens: [{string.Join(", ", encoding.Ids)}]");
                    Console.WriteLine($"   -> MS Tokens (Decoded): {tokenizer.Decode(encoding.Ids.Select(x => (int)x).ToArray())}");
                }
            }

            // Disable verbose logging
            ggufxSession.SetVerbose(false);

            Console.WriteLine(new string('-', 60));
            Console.WriteLine($"Average Similarity: {totalSim / inputs.Count:F6}");
            if (lowAccuracyCount > 0)
            {
                Console.WriteLine($"WARNING: {lowAccuracyCount} inputs had similarity < 0.98");
            }
            else
            {
                Console.WriteLine("SUCCESS: All inputs matched with high similarity.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Detailed Accuracy Check Failed: {ex.Message}");
        }
    }

    static void CheckAccuracy(string modelPath, string tokenizerJsonPath, string vocabPath, string text)
    {
        text = NormalizeText(text);
        Console.WriteLine("\n--- 4. Accuracy Check (Cosine Similarity) ---");
        try
        {
            // MS ONNX (Baseline)
            var tokenizer = Tokenizer.FromFile(tokenizerJsonPath);

            // Match C++ options
            using var options = new SessionOptions();
            options.IntraOpNumThreads = Environment.ProcessorCount;
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED;
            options.EnableMemoryPattern = true;
            options.EnableCpuMemArena = true;

            using var session = new InferenceSession(modelPath, options);
            var encoding = tokenizer.Encode(text);
            var msEmbeddings = RunOnnxWithOutput(session, encoding.Ids.Select(x => (long)x).ToArray(), encoding.AttentionMask.Select(x => (long)x).ToArray(), encoding.TypeIds.Select(x => (long)x).ToArray());

            // GGUFx Native (Internal Tokenization)
            using var ggufxSession = new GgufxEmbeddingSession(modelPath, vocabPath);
            var ggufxEmbeddings = ggufxSession.EmbedText(text, 384);

            // Compare
            double similarity = CosineSimilarity(msEmbeddings, ggufxEmbeddings);
            Console.WriteLine($"Cosine Similarity: {similarity:F6}");

            if (similarity < 0.99)
            {
                Console.WriteLine("WARNING: Similarity is low. This is likely due to tokenization differences between C# Tokenizer and Native Minimal Tokenizer.");
            }
            else
            {
                Console.WriteLine("SUCCESS: Embeddings match.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Accuracy Check Failed: {ex.Message}");
        }
    }

    static float[] RunOnnxWithOutput(InferenceSession session, long[] inputIds, long[] attentionMask, long[] tokenTypeIds)
    {
        var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length });
        var attentionMaskTensor = new DenseTensor<long>(attentionMask, new[] { 1, attentionMask.Length });
        var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, new[] { 1, tokenTypeIds.Length });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
        };

        using var results = session.Run(inputs);
        var output = results.First().AsTensor<float>();

        // Debug: Print first 5 logits of first token
        Console.WriteLine("MS ONNX: first 5 logits of first token:");
        for (int i = 0; i < 5 && i < 384; i++)
        {
            Console.Write($"{output[0, 0, i]:F6} ");
        }
        Console.WriteLine();

        // Mean Pooling
        return MeanPool(output, attentionMask, 384);
    }

    static float[] MeanPool(Tensor<float> lastHiddenState, long[] attentionMask, int hiddenSize)
    {
        int seqLen = attentionMask.Length;
        float[] pooled = new float[hiddenSize];
        int validTokens = 0;

        for (int i = 0; i < seqLen; i++)
        {
            if (attentionMask[i] == 1)
            {
                for (int j = 0; j < hiddenSize; j++)
                {
                    pooled[j] += lastHiddenState[0, i, j];
                }
                validTokens++;
            }
        }

        for (int j = 0; j < hiddenSize; j++)
        {
            pooled[j] /= validTokens;
        }

        return pooled;
    }

    static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }

    static void BenchmarkHfGgufx(string modelPath, string tokenizerPath, string text, int iterations)
    {
        Console.WriteLine("\n--- 2. HF Tokenizer + GGUFx ONNX (External Tokenization) ---");

        try
        {
            var tokenizer = Tokenizer.FromFile(tokenizerPath);
            // Note: GgufxEmbeddingSession constructor requires tokenizer path now for the internal one,
            // but we can pass it even if we don't use it, or use the single-arg constructor if available.
            // The single-arg constructor was preserved in my edit? Let's check.
            // Yes, I kept the single arg constructor.
            using var session = new GgufxEmbeddingSession(modelPath);

            // Warmup
            var encoding = tokenizer.Encode(text);
            RunGgufx(session, encoding.Ids.Select(x => (long)x).ToArray(), encoding.AttentionMask.Select(x => (long)x).ToArray(), encoding.TypeIds.Select(x => (long)x).ToArray());

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var enc = tokenizer.Encode(text);
                RunGgufx(session, enc.Ids.Select(x => (long)x).ToArray(), enc.AttentionMask.Select(x => (long)x).ToArray(), enc.TypeIds.Select(x => (long)x).ToArray());
            }
            sw.Stop();

            Console.WriteLine($"Total Time: {sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"Avg Latency: {sw.ElapsedMilliseconds / (double)iterations} ms");
            Console.WriteLine($"Throughput: {iterations / sw.Elapsed.TotalSeconds:F2} req/sec");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }

    static void RunGgufx(GgufxEmbeddingSession session, long[] inputIds, long[] attentionMask, long[] tokenTypeIds)
    {
        session.Run(inputIds, attentionMask, tokenTypeIds);
    }

    static void BenchmarkHfOnnx(string modelPath, string tokenizerPath, string text, int iterations)
    {
        Console.WriteLine("\n--- 1. HF Tokenizer + Microsoft.ML.OnnxRuntime ---");

        try
        {
            var tokenizer = Tokenizer.FromFile(tokenizerPath);
            using var session = new InferenceSession(modelPath);

            // Warmup
            var encoding = tokenizer.Encode(text);
            RunOnnx(session, encoding.Ids.Select(x => (long)x).ToArray(), encoding.AttentionMask.Select(x => (long)x).ToArray(), encoding.TypeIds.Select(x => (long)x).ToArray());

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var enc = tokenizer.Encode(text);
                RunOnnx(session, enc.Ids.Select(x => (long)x).ToArray(), enc.AttentionMask.Select(x => (long)x).ToArray(), enc.TypeIds.Select(x => (long)x).ToArray());
            }
            sw.Stop();

            Console.WriteLine($"Total Time: {sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"Avg Latency: {sw.ElapsedMilliseconds / (double)iterations} ms");
            Console.WriteLine($"Throughput: {iterations / sw.Elapsed.TotalSeconds:F2} req/sec");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }

    static void RunOnnx(InferenceSession session, long[] inputIds, long[] attentionMask, long[] tokenTypeIds)
    {
        var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length });
        var attentionMaskTensor = new DenseTensor<long>(attentionMask, new[] { 1, attentionMask.Length });
        var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, new[] { 1, tokenTypeIds.Length });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
        };

        using var results = session.Run(inputs);
    }

    static void BenchmarkGgufxInternal(string modelPath, string tokenizerPath, string text, int iterations)
    {
        Console.WriteLine("\n--- 3. GGUFx ONNX (Internal Tokenization) ---");

        try
        {
            // Note: tokenizerPath here should be the folder or file depending on what the C++ expects.
            // The C++ TokenizerImpl expects a file path (vocab.txt or tokenizer.json).
            // In Program.cs I set tokenizerPath to the folder. I need to point to the file.
            // But wait, TokenizerImpl logic (which I didn't read fully but assumed) usually takes a file.
            // I'll pass the tokenizer.json path.

            // Pass the directory path, as the native tokenizer loader expects a directory (to find vocab.txt)
            // or a specific model file (which tokenizer.json is not supported yet).
            using var session = new GgufxEmbeddingSession(modelPath, tokenizerPath);
            session.SetVerbose(true);

            // Warmup
            session.EmbedText(text, 384);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                session.EmbedText(text, 384);
            }
            sw.Stop();

            Console.WriteLine($"Total Time: {sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"Avg Latency: {sw.ElapsedMilliseconds / (double)iterations} ms");
            Console.WriteLine($"Throughput: {iterations / sw.Elapsed.TotalSeconds:F2} req/sec");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }

    static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // 1. Normalize to FormC
        text = text.Normalize(NormalizationForm.FormC);

        // 1.5 Expand contractions
        text = Regex.Replace(text, "n't", " not", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "'m", " am", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "'s", " is", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "'re", " are", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "'ve", " have", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "'ll", " will", RegexOptions.IgnoreCase);

        // 2. Remove emojis and other non-text symbols
        // Whitelist: Keep only Letters, Numbers, Punctuation, and Whitespace
        text = Regex.Replace(text, @"[^\p{L}\p{N}\p{P}\p{Z}]", "");

        // 3. Pre-tokenize punctuation: Add spaces around punctuation
        // This mimics BERT's BasicTokenizer which splits punctuation
        // text = Regex.Replace(text, @"([\p{P}])", " $1 ");

        // 4. Collapse multiple spaces and trim
        text = Regex.Replace(text, @"\s+", " ");

        return text.Trim().ToLowerInvariant();
    }

    static string FindRepoRoot(string startPath)
    {
        DirectoryInfo dir = new DirectoryInfo(startPath);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ggufx.code-workspace")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
