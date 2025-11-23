using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using VecraX.LragX;

namespace LragTest;

public class CorpusItem
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

public class QueryItem
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

class Program
{
    static async Task Main(string[] args)
    {
        string datasetPath = @"D:\beir_assessment\scifact";
        if (args.Length > 0) datasetPath = args[0];

        if (!Directory.Exists(datasetPath))
        {
            Console.WriteLine($"Dataset not found at {datasetPath}");
            return;
        }

        Console.WriteLine($"Evaluating on dataset: {datasetPath}");

        // Paths
        string corpusPath = Path.Combine(datasetPath, "corpus.jsonl");
        string queriesPath = Path.Combine(datasetPath, "queries.jsonl");
        string qrelsPath = Path.Combine(datasetPath, "qrels", "test.tsv");
        string modelPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../examples/models/embeddings/onnx/model_q4f16.onnx"));
        string dataDir = Path.Combine(AppContext.BaseDirectory, "lrag_data_eval");

        // Load Corpus Sample for Calibration
        Console.WriteLine("Loading corpus sample for calibration...");
        var sampleDocs = new List<(uint Id, string Text)>();
        int sampleSize = 100;
        uint tempId = 1;
        foreach (var line in File.ReadLines(corpusPath).Take(sampleSize))
        {
            var item = JsonSerializer.Deserialize<CorpusItem>(line);
            if (item != null)
            {
                sampleDocs.Add((tempId++, $"{item.Title} {item.Text}"));
            }
        }

        // Check for existing data
        string mapPath = Path.Combine(dataDir, "map.json");
        bool skipIngestion = false;
        int bestParallelism = 1;

        if (File.Exists(Path.Combine(dataDir, "lrag.bin")) && File.Exists(mapPath))
        {
            Console.WriteLine("Found existing index and mapping. Skipping ingestion.");
            skipIngestion = true;
        }

        if (!skipIngestion)
        {
            // Calibration
            bestParallelism = Calibrate(sampleDocs, modelPath, dataDir);
            Console.WriteLine($"Best parallelism: {bestParallelism}");

            // Clean previous data
            if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true);
            Directory.CreateDirectory(dataDir);
        }

        // Mappings
        var docIdMap = new ConcurrentDictionary<string, uint>(); // External -> Internal
        var docIdReverseMap = new ConcurrentDictionary<uint, string>(); // Internal -> External
        int nextDocId = 1;

        // Initialize Engine
        Console.WriteLine($"Initializing LragEngine with 1 IntraOp thread...");
        using var engine = new LragEngine(modelPath, dataDir, 1);

        if (!skipIngestion)
        {
            // 1. Ingest Corpus
            Console.WriteLine("Ingesting corpus...");
            var sw = Stopwatch.StartNew();

            var allLines = await File.ReadAllLinesAsync(corpusPath, CancellationToken.None).ConfigureAwait(false);
            int totalDocs = allLines.Length;
            int processedDocs = 0;
            long totalTokens = 0; // Approximate

            await Parallel.ForEachAsync(allLines, new ParallelOptions { MaxDegreeOfParallelism = bestParallelism }, async (line, ct) =>
            {
                var item = JsonSerializer.Deserialize<CorpusItem>(line);
                if (item != null)
                {
                    uint id = (uint)Interlocked.Increment(ref nextDocId) - 1;
                    docIdMap[item.Id] = id;
                    docIdReverseMap[id] = item.Id;

                    string fullText = $"{item.Title} {item.Text}";
                    engine.Ingest(id, fullText);

                    Interlocked.Increment(ref processedDocs);
                    Interlocked.Add(ref totalTokens, fullText.Length / 4); // Rough estimate

                    if (processedDocs % 100 == 0)
                    {
                        double elapsed = sw.Elapsed.TotalSeconds;
                        double docsPerSec = processedDocs / elapsed;
                        double tokensPerSec = totalTokens / elapsed;
                        Console.Write($"\rIngested {processedDocs}/{totalDocs} ({docsPerSec:F1} docs/s, {tokensPerSec:F1} tok/s)   ");
                    }
                }
                await Task.CompletedTask.ConfigureAwait(false);
            }).ConfigureAwait(false);

            sw.Stop();
            Console.WriteLine($"\nIngested {processedDocs} documents in {sw.Elapsed.TotalSeconds:F2}s ({processedDocs / sw.Elapsed.TotalSeconds:F2} docs/s)");

            // Save Mapping
            Console.WriteLine("Saving mapping...");
            File.WriteAllText(mapPath, JsonSerializer.Serialize(docIdMap));
        }
        else
        {
            // Load Mapping
            Console.WriteLine("Loading mapping...");
            var loadedMap = JsonSerializer.Deserialize<Dictionary<string, uint>>(File.ReadAllText(mapPath));
            if (loadedMap != null)
            {
                foreach (var kv in loadedMap)
                {
                    docIdMap[kv.Key] = kv.Value;
                    docIdReverseMap[kv.Value] = kv.Key;
                }
            }
        }

        // 2. Load Queries
        Console.WriteLine("Loading queries...");
        var queries = new Dictionary<string, string>();
        foreach (var line in File.ReadLines(queriesPath))
        {
            var item = JsonSerializer.Deserialize<QueryItem>(line);
            if (item != null) queries[item.Id] = item.Text;
        }

        // 3. Load Qrels
        Console.WriteLine("Loading qrels...");
        var qrels = new Dictionary<string, Dictionary<string, int>>();
        bool firstLine = true;
        foreach (var line in File.ReadLines(qrelsPath))
        {
            if (firstLine) { firstLine = false; continue; } // Skip header
            var parts = line.Split('\t');
            if (parts.Length >= 3)
            {
                string qid = parts[0];
                string docid = parts[1];
                int score = int.Parse(parts[2]);

                if (!qrels.ContainsKey(qid)) qrels[qid] = new Dictionary<string, int>();
                qrels[qid][docid] = score;
            }
        }

        // 4. Evaluate
        Console.WriteLine("Starting evaluation...");
        double totalRecall100 = 0;
        double totalNdcg10 = 0;
        double totalPrecision10 = 0;
        double totalMrr10 = 0;
        int queryCount = 0;

        foreach (var qid in qrels.Keys)
        {
            if (!queries.ContainsKey(qid)) continue;

            string queryText = queries[qid];
            var relevantDocs = qrels[qid];

            // Search
            var results = engine.Search(queryText, 100);

            // Calculate Metrics
            // Recall@100
            int relevantRetrieved = 0;
            foreach (var result in results)
            {
                if (docIdReverseMap.TryGetValue(result.Id, out string? extId) && relevantDocs.ContainsKey(extId))
                {
                    relevantRetrieved++;
                }
            }
            double recall = relevantDocs.Count > 0 ? (double)relevantRetrieved / relevantDocs.Count : 0;

            // Precision@10
            int relevantTop10 = 0;
            for (int i = 0; i < Math.Min(results.Length, 10); i++)
            {
                if (docIdReverseMap.TryGetValue(results[i].Id, out string? extId) && relevantDocs.ContainsKey(extId))
                {
                    relevantTop10++;
                }
            }
            double precision = (double)relevantTop10 / 10.0;

            // MRR@10
            double mrr = 0;
            for (int i = 0; i < Math.Min(results.Length, 10); i++)
            {
                if (docIdReverseMap.TryGetValue(results[i].Id, out string? extId) && relevantDocs.ContainsKey(extId))
                {
                    mrr = 1.0 / (i + 1);
                    break;
                }
            }

            // NDCG@10
            double dcg = 0;
            double idcg = 0;

            // Calculate IDCG
            var sortedRelevant = relevantDocs.Values.OrderByDescending(s => s).Take(10).ToList();
            for (int i = 0; i < sortedRelevant.Count; i++)
            {
                idcg += (Math.Pow(2, sortedRelevant[i]) - 1) / Math.Log2(i + 2);
            }

            // Calculate DCG
            for (int i = 0; i < Math.Min(results.Length, 10); i++)
            {
                if (docIdReverseMap.TryGetValue(results[i].Id, out string? extId) && relevantDocs.TryGetValue(extId, out int score))
                {
                    dcg += (Math.Pow(2, score) - 1) / Math.Log2(i + 2);
                }
            }

            double ndcg = idcg > 0 ? dcg / idcg : 0;

            totalRecall100 += recall;
            totalNdcg10 += ndcg;
            totalPrecision10 += precision;
            totalMrr10 += mrr;
            queryCount++;

            if (queryCount % 10 == 0) Console.Write($"\rEvaluated {queryCount} queries...");
        }

        Console.WriteLine($"\n\nEvaluation Results ({queryCount} queries):");
        Console.WriteLine($"NDCG@10:      {totalNdcg10 / queryCount:F4}");
        Console.WriteLine($"Recall@100:   {totalRecall100 / queryCount:F4}");
        Console.WriteLine($"Precision@10: {totalPrecision10 / queryCount:F4}");
        Console.WriteLine($"MRR@10:       {totalMrr10 / queryCount:F4}");
    }

    static int Calibrate(List<(uint Id, string Text)> docs, string modelPath, string baseDataDir)
    {
        Console.WriteLine("Calibrating Parallelism (with IntraOp=1)...");
        int[] parallelismCounts = { 1, 2, 4, 8, 12, 16 };
        long bestTime = long.MaxValue;
        int bestParallelism = 1;

        foreach (int p in parallelismCounts)
        {
            string tempDir = Path.Combine(baseDataDir, $"calib_{p}");
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            try
            {
                // Always use 1 thread for IntraOp to avoid oversubscription
                using var engine = new LragEngine(modelPath, tempDir, 1);
                var sw = Stopwatch.StartNew();

                Parallel.ForEach(docs, new ParallelOptions { MaxDegreeOfParallelism = p }, doc =>
                {
                    engine.Ingest(doc.Id, doc.Text);
                });

                sw.Stop();
                Console.WriteLine($"Parallelism: {p}, Time: {sw.ElapsedMilliseconds}ms");

                if (sw.ElapsedMilliseconds < bestTime)
                {
                    bestTime = sw.ElapsedMilliseconds;
                    bestParallelism = p;
                }
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        return bestParallelism;
    }
}
