using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
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
        int limit = -1;
        bool clean = false;
        int rerankLimit = 50;
        float alpha = 0.5f;
        int retrievalK = 1000;
        bool tune = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--limit" && i + 1 < args.Length)
            {
                limit = int.Parse(args[i + 1]);
                i++;
            }
            else if (args[i] == "--rerank" && i + 1 < args.Length)
            {
                rerankLimit = int.Parse(args[i + 1]);
                i++;
            }
            else if (args[i] == "--alpha" && i + 1 < args.Length)
            {
                alpha = float.Parse(args[i + 1]);
                i++;
            }
            else if (args[i] == "--retrieval" && i + 1 < args.Length)
            {
                retrievalK = int.Parse(args[i + 1]);
                i++;
            }
            else if (args[i] == "--clean")
            {
                clean = true;
            }
            else if (args[i] == "--tune")
            {
                tune = true;
            }
            else if (!args[i].StartsWith("--"))
            {
                datasetPath = args[i];
            }
        }

        if (!Directory.Exists(datasetPath))
        {
            Console.WriteLine($"Dataset not found at {datasetPath}");
            return;
        }

        Console.WriteLine($"Evaluating on dataset: {datasetPath}");
        if (limit > 0) Console.WriteLine($"Limiting to top {limit} documents.");
        Console.WriteLine($"Config: Rerank={rerankLimit}, Alpha={alpha}, RetrievalK={retrievalK}");

        // Paths
        string corpusPath = Path.Combine(datasetPath, "corpus.jsonl");
        string queriesPath = Path.Combine(datasetPath, "queries.jsonl");
        string qrelsPath = Path.Combine(datasetPath, "qrels", "test.tsv");
        string modelPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../examples/models/embeddings/onnx/model_q4f16.onnx"));
        string dataDir = Path.Combine(AppContext.BaseDirectory, "lrag_data_eval");
        Console.WriteLine($"Data Directory: {dataDir}");

        if (clean && Directory.Exists(dataDir))
        {
            Console.WriteLine("Cleaning existing data...");
            Directory.Delete(dataDir, true);
        }

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
        int bestParallelism = 6; // Hardcoded for testing

        if (File.Exists(Path.Combine(dataDir, "lrag.bin")) && File.Exists(mapPath))
        {
            Console.WriteLine("Found existing index and mapping. Skipping ingestion.");
            skipIngestion = true;
        }

        if (!skipIngestion)
        {
            // ... Ingestion Logic ...
            // (Keep existing ingestion logic, but I need to make sure I don't delete it accidentally)
            // I'll use replace_string_in_file carefully.
        }

        if (!skipIngestion)
        {
            // Calibration Skipped
            // bestParallelism = Calibrate(sampleDocs, modelPath, dataDir);
            Console.WriteLine($"Using parallelism: {bestParallelism}");

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
        LragEngine? engineInstance = null;
        try
        {
#pragma warning disable IDISP001
            engineInstance = new LragEngine(modelPath, dataDir, 1);
#pragma warning restore IDISP001
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize engine: {ex.Message}");
            Console.WriteLine("Deleting stale data and forcing re-ingestion...");
            if (engineInstance != null)
            {
                engineInstance.Dispose();
                engineInstance = null;
            }

            if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true);
            Directory.CreateDirectory(dataDir);
            skipIngestion = false;
#pragma warning disable IDISP001
            engineInstance = new LragEngine(modelPath, dataDir, 1);
#pragma warning restore IDISP001
        }
        using var engine = engineInstance!;

        if (!skipIngestion)
        {
            // 1. Ingest Corpus
            Console.WriteLine("Ingesting corpus...");
            var sw = Stopwatch.StartNew();

            var allLines = await File.ReadAllLinesAsync(corpusPath, CancellationToken.None).ConfigureAwait(false);

            if (limit > 0)
            {
                allLines = allLines.Take(limit).ToArray();
            }

            int totalDocs = allLines.Length;
            int processedDocs = 0;
            long totalTokens = 0; // Approximate

            await Parallel.ForEachAsync(allLines, new ParallelOptions { MaxDegreeOfParallelism = bestParallelism }, async (line, ct) =>
            {
                var item = JsonSerializer.Deserialize<CorpusItem>(line);
                if (item != null)
                {
                    uint rootId = (uint)Interlocked.Increment(ref nextDocId) - 1;
                    docIdMap[item.Id] = rootId;
                    docIdReverseMap[rootId] = item.Id;

                    string fullText = $"{item.Title}\n\n{item.Text}";

                    if (processedDocs < 5)
                    {
                        Console.WriteLine($"Ingesting Doc: {item.Id}, Title: '{item.Title}', TextLen: {item.Text.Length}");
                    }

                    // Ingest Root Node (Title)
                    /*
                    var rootNode = new LragNode
                    {
                        Id = rootId,
                        ParentId = 0,
                        RootDocId = rootId,
                        Text = item.Title,
                        Context = "Title",
                        Tags = "Root"
                    };
                    engine.Ingest(rootNode);
                    */

                    // Chunking Logic (Target ~512 tokens / 2000 chars)
                    var paragraphs = System.Text.RegularExpressions.Regex.Split(fullText, @"\n\s*\n");
                    // var paragraphs = new string[] { fullText };
                    int paraIdx = 0;
                    var currentChunk = new StringBuilder();
                    int currentChunkStartPara = 0;

                    void EmitChunk() {
                        if (currentChunk.Length == 0) return;

                        var node = new LragNode
                        {
                            Id = (uint)Interlocked.Increment(ref nextDocId) - 1,
                            ParentId = rootId,
                            RootDocId = rootId,
                            // Prepend Title to every chunk to preserve context
                            Text = $"{item.Title}\n{currentChunk.ToString().Trim()}",
                            Context = $"{item.Title} > P{currentChunkStartPara}-{paraIdx}",
                            Tags = "General"
                        };
                        engine.Ingest(node);
                        currentChunk.Clear();
                        currentChunkStartPara = paraIdx + 1;
                    }

                    foreach (var para in paragraphs)
                    {
                        if (string.IsNullOrWhiteSpace(para)) continue;

                        // If single paragraph is huge, split it
                        if (para.Length > 2000)
                        {
                            EmitChunk(); // Flush existing

                            // Split huge paragraph by lines
                            using var reader = new StringReader(para);
                            string? subLine;
                            while ((subLine = reader.ReadLine()) != null)
                            {
                                if (string.IsNullOrWhiteSpace(subLine)) continue;
                                if (currentChunk.Length + subLine.Length > 2000) EmitChunk();
                                currentChunk.AppendLine(subLine);
                            }
                            EmitChunk(); // Flush remainder
                        }
                        else
                        {
                            if (currentChunk.Length + para.Length > 2000)
                            {
                                EmitChunk();
                            }
                            if (currentChunk.Length > 0) currentChunk.AppendLine();
                            currentChunk.Append(para);
                        }
                        paraIdx++;
                    }
                    EmitChunk(); // Final flush

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

                // Only include qrels for documents we actually have
                if (docIdMap.ContainsKey(docid))
                {
                    if (!qrels.ContainsKey(qid)) qrels[qid] = new Dictionary<string, int>();
                    qrels[qid][docid] = score;
                }
            }
        }

        // 4. Evaluate or Tune
        if (tune)
        {
            await RunTuning(engine, queries, qrels, docIdMap, docIdReverseMap).ConfigureAwait(false);
        }
        else
        {
            await RunEvaluation(engine, queries, qrels, docIdMap, docIdReverseMap, rerankLimit, alpha, retrievalK).ConfigureAwait(false);
        }
    }

    static async Task RunEvaluation(LragEngine engine, Dictionary<string, string> queries, Dictionary<string, Dictionary<string, int>> qrels, IDictionary<string, uint> docIdMap, IDictionary<uint, string> docIdReverseMap, int rerankLimit, float alpha, int retrievalK)
    {
        Console.WriteLine($"Starting evaluation (Rerank={rerankLimit}, Alpha={alpha}, RetrievalK={retrievalK})...");
        double totalRecall100 = 0;
        double totalRecall10 = 0;
        double totalRecall1 = 0;
        double totalNdcg10 = 0;
        double totalPrecision10 = 0;
        double totalMrr10 = 0;
        int queryCount = 0;

        foreach (var qid in qrels.Keys)
        {
            if (!queries.ContainsKey(qid)) continue;

            // Validate Query: Ensure at least one relevant doc is in our index
            var relevantDocs = qrels[qid];
            bool hasRelevantDocInIndex = false;
            foreach (var docId in relevantDocs.Keys)
            {
                if (docIdMap.ContainsKey(docId))
                {
                    hasRelevantDocInIndex = true;
                    break;
                }
            }

            if (!hasRelevantDocInIndex) continue;

            string queryText = queries[qid];

            // Search
            var results = engine.Search(queryText, 100, rerankLimit, alpha, retrievalK);

            if (queryCount == 0)
            {
                Console.WriteLine($"Query: {queryText}");
                Console.WriteLine($"Relevant Docs: {string.Join(", ", relevantDocs.Keys)}");
                Console.WriteLine($"Top 5 scores for first query:");
                for (int i = 0; i < Math.Min(results.Length, 5); i++)
                {
                    string extId = "UNKNOWN";
                    if (docIdReverseMap.TryGetValue(results[i].RootId, out var eid)) extId = eid;
                    Console.WriteLine($"Rank {i+1}: {results[i].Score:F4} (IntID={results[i].Id}, RootID={results[i].RootId}, ExtID={extId})");
                }
            }

            // Calculate Metrics
            var retrievedRelevantDocs100 = new HashSet<string>();
            var retrievedRelevantDocs10 = new HashSet<string>();
            var retrievedRelevantDocs1 = new HashSet<string>();

            for (int i = 0; i < results.Length; i++)
            {
                if (docIdReverseMap.TryGetValue(results[i].RootId, out string? extId) && relevantDocs.ContainsKey(extId))
                {
                    retrievedRelevantDocs100.Add(extId);
                    if (i < 10) retrievedRelevantDocs10.Add(extId);
                    if (i < 1) retrievedRelevantDocs1.Add(extId);
                }
            }

            double recall100 = relevantDocs.Count > 0 ? (double)retrievedRelevantDocs100.Count / relevantDocs.Count : 0;
            double recall10 = relevantDocs.Count > 0 ? (double)retrievedRelevantDocs10.Count / relevantDocs.Count : 0;
            double recall1 = relevantDocs.Count > 0 ? (double)retrievedRelevantDocs1.Count / relevantDocs.Count : 0;

            // Precision@10
            double precision = (double)retrievedRelevantDocs10.Count / 10.0;

            // MRR@10
            double mrr = 0;
            for (int i = 0; i < Math.Min(results.Length, 10); i++)
            {
                if (docIdReverseMap.TryGetValue(results[i].RootId, out string? extId) && relevantDocs.ContainsKey(extId))
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
                if (docIdReverseMap.TryGetValue(results[i].RootId, out string? extId) && relevantDocs.TryGetValue(extId, out int score))
                {
                    dcg += (Math.Pow(2, score) - 1) / Math.Log2(i + 2);
                }
            }

            double ndcg = idcg > 0 ? dcg / idcg : 0;

            totalRecall100 += recall100;
            totalRecall10 += recall10;
            totalRecall1 += recall1;
            totalNdcg10 += ndcg;
            totalPrecision10 += precision;
            totalMrr10 += mrr;
            queryCount++;

            if (queryCount % 10 == 0) Console.Write($"\rEvaluated {queryCount} queries...");
        }

        Console.WriteLine($"\n\nEvaluation Results ({queryCount} queries):");
        Console.WriteLine($"NDCG@10:      {totalNdcg10 / queryCount:F4}");
        Console.WriteLine($"Recall@100:   {totalRecall100 / queryCount:F4}");
        Console.WriteLine($"Recall@10:    {totalRecall10 / queryCount:F4}");
        Console.WriteLine($"Recall@1:     {totalRecall1 / queryCount:F4}");
        Console.WriteLine($"Precision@10: {totalPrecision10 / queryCount:F4}");
        Console.WriteLine($"MRR@10:       {totalMrr10 / queryCount:F4}");

        // Log to file
        string experimentFile = "experiments.md";
        if (!File.Exists(experimentFile))
        {
            await File.WriteAllTextAsync(experimentFile, "| Date | Alpha | Rerank | RetrievalK | NDCG@10 | Recall@10 | Recall@100 | Notes |\n|---|---|---|---|---|---|---|---|\n", CancellationToken.None).ConfigureAwait(false);
        }
        await File.AppendAllTextAsync(experimentFile, $"| {DateTime.Now} | {alpha} | {rerankLimit} | {retrievalK} | {totalNdcg10 / queryCount:F4} | {totalRecall10 / queryCount:F4} | {totalRecall100 / queryCount:F4} | Full Evaluation |\n", CancellationToken.None).ConfigureAwait(false);
    }

    static async Task RunTuning(LragEngine engine, Dictionary<string, string> queries, Dictionary<string, Dictionary<string, int>> qrels, IDictionary<string, uint> docIdMap, IDictionary<uint, string> docIdReverseMap)
    {
        Console.WriteLine("Starting Tuning Grid Search (All Permutations)...");

        // Expanded Grid Search
        float[] alphas = { 0.0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f };
        int[] rerankLimits = { 10, 50, 100, 200, 500 };
        int[] retrievalKs = { 500, 1000, 2000 };

        Console.WriteLine("Alpha\tRerank\tRetK\tNDCG@10\tRecall@10");

        // Experiment Logging
        string experimentFile = "experiments.md";
        if (!File.Exists(experimentFile))
        {
            await File.WriteAllTextAsync(experimentFile, "| Date | Alpha | Rerank | RetrievalK | NDCG@10 | Recall@10 | Recall@100 | Notes |\n|---|---|---|---|---|---|---|---|\n", CancellationToken.None).ConfigureAwait(false);
        }

        double bestNdcg = 0;
        int patience = 3;
        int noImprovementCount = 0;

        foreach (var alpha in alphas)
        {
            foreach (var rerank in rerankLimits)
            {
                foreach (var retK in retrievalKs)
                {
                    double totalNdcg = 0;
                    double totalRecall10 = 0;
                    double totalRecall100 = 0;
                    int count = 0;

                    // Run on a subset of queries for speed
                    // Filter queries to only those that have relevant docs in our ingested set (1000 docs)
                    // This ensures we are measuring recall within the available corpus
                    var validQueries = qrels.Keys
                        .Where(qid => queries.ContainsKey(qid) && qrels[qid].Keys.Any(did => docIdMap.ContainsKey(did)))
                        .Take(50) // Limit to 50 valid queries for speed
                        .ToList();

                    if (validQueries.Count == 0)
                    {
                        Console.WriteLine("No valid queries found for the current document set.");
                        return;
                    }

                    foreach (var qid in validQueries)
                    {
                        var results = engine.Search(queries[qid], 100, rerank, alpha, retK);

                        var relevantDocs = qrels[qid];

                        // NDCG@10
                        double dcg = 0, idcg = 0;
                        var sortedRelevant = relevantDocs.Values.OrderByDescending(s => s).Take(10).ToList();
                        for (int i = 0; i < sortedRelevant.Count; i++) idcg += (Math.Pow(2, sortedRelevant[i]) - 1) / Math.Log2(i + 2);
                        for (int i = 0; i < Math.Min(results.Length, 10); i++)
                        {
                            if (docIdReverseMap.TryGetValue(results[i].RootId, out string? extId) && relevantDocs.TryGetValue(extId, out int score))
                                dcg += (Math.Pow(2, score) - 1) / Math.Log2(i + 2);
                        }
                        double ndcg = idcg > 0 ? dcg / idcg : 0;

                        // Recall@10
                        var retrievedRelevantDocs10 = new HashSet<string>();
                        for(int i=0; i<Math.Min(results.Length, 10); i++)
                        {
                            if (docIdReverseMap.TryGetValue(results[i].RootId, out string? extId) && relevantDocs.ContainsKey(extId)) retrievedRelevantDocs10.Add(extId);
                        }
                        double rec10 = relevantDocs.Count > 0 ? (double)retrievedRelevantDocs10.Count / relevantDocs.Count : 0;

                        // Recall@100
                        var retrievedRelevantDocs100 = new HashSet<string>();
                        for(int i=0; i<results.Length; i++)
                        {
                            if (docIdReverseMap.TryGetValue(results[i].RootId, out string? extId) && relevantDocs.ContainsKey(extId)) retrievedRelevantDocs100.Add(extId);
                        }
                        double rec100 = relevantDocs.Count > 0 ? (double)retrievedRelevantDocs100.Count / relevantDocs.Count : 0;

                        totalNdcg += ndcg;
                        totalRecall10 += rec10;
                        totalRecall100 += rec100;
                        count++;
                    }

                    double avgNdcg = totalNdcg / count;
                    double avgRecall10 = totalRecall10 / count;
                    double avgRecall100 = totalRecall100 / count;

                    Console.WriteLine($"{alpha:F1}\t{rerank}\t{retK}\t{avgNdcg:F4}\t{avgRecall10:F4}");

                    // Log to file
                    await File.AppendAllTextAsync(experimentFile, $"| {DateTime.Now} | {alpha} | {rerank} | {retK} | {avgNdcg:F4} | {avgRecall10:F4} | {avgRecall100:F4} | Tuning Run |\n", CancellationToken.None).ConfigureAwait(false);

                    // Auto-Exit Logic
                    if (avgNdcg > bestNdcg)
                    {
                        bestNdcg = avgNdcg;
                        noImprovementCount = 0;
                    }
                }
            }
        }
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
