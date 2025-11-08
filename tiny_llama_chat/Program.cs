namespace TinyLlamaChat
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using ErgoX.GgufX;
    using ErgoX.GgufX.Text;

    internal static class Program
    {
        private const string ModelFileName = "tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf";

        private static async Task Main()
        {
            try
            {
                var modelPath = LocateModel();
                Console.WriteLine($"Using model: {modelPath}");

                await RunDebateDemoAsync(modelPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("An error occurred while running the demo: ");
                Console.WriteLine(ex);
                Console.ResetColor();
                throw;
            }
        }

        private static async Task RunDebateDemoAsync(string modelPath)
        {
            Console.WriteLine();
            Console.WriteLine("=== Climate Policy Debate with Rich Historical Context ===");

            // Comprehensive debate context to ground the discussion
            var contextDocument = @"
CLIMATE CRISIS AND TECHNOLOGY: A HISTORICAL AND POLICY OVERVIEW

The global climate crisis represents one of humanity's most pressing challenges. Since the industrial revolution, atmospheric CO2 levels have risen from 280 ppm to over 420 ppm today, driving unprecedented warming. The 2015 Paris Agreement committed 194 nations to limiting warming to 1.5-2°C above pre-industrial levels. Yet current policies and national commitments put us on a path toward 2.5-3°C of warming by 2100, with catastrophic consequences: sea level rise displacing hundreds of millions, agricultural collapse threatening food security, mass extinction events, and climate-driven migration crises.

ROLE OF AI IN CLIMATE SOLUTIONS:

Advocates argue AI is transformative for climate action. Machine learning models can optimize renewable energy grids by predicting wind and solar output with 85-95% accuracy, reducing curtailment losses. AI enables precision agriculture, reducing water use by 20-30% while improving yields. Climate modeling powered by AI has accelerated hurricane prediction, wildfire forecasting, and coastal erosion planning. Companies like DeepMind used AI to cut data center cooling energy by 40%. Carbon capture startups employ AI to discover new materials for CO2 removal at 10x faster rates than traditional R&D.

COUNTERARGUMENTS ON AI AND CLIMATE:

Critics highlight that large AI models consume enormous energy—training GPT-3 generated ~550 metric tons of CO2. Data centers globally consume 4% of electricity, growing 15% annually. GPU manufacturing demands rare earth mining, devastating ecosystems. Algorithmic bias in climate adaptation funding has historically favored wealthy nations and urban areas, leaving island nations and sub-Saharan Africa underfunded despite bearing 50% of climate impacts. Over-reliance on technological fixes delays systemic change: we need consumption reduction, fossil fuel phase-out, and wealth redistribution—not just smarter algorithms.

THE POLICY LANDSCAPE:

The EU's Green Deal allocates €1 trillion to climate action, with emerging AI regulations. The US Inflation Reduction Act invests $369 billion but prioritizes EV production and renewable energy—less emphasis on AI integration. China leads in renewable deployment (50% of global solar) but also expands coal capacity. Developing nations demand climate justice: historical emitters (US, EU, China) caused 75% of cumulative emissions but developing economies suffer 80% of losses.
";

            Console.WriteLine(contextDocument);
            Console.WriteLine("\n--- Debate Begins ---\n");

            var climatePrimer = @"Key briefing facts:
- Atmospheric CO2 now exceeds 420 ppm; unchecked trends imply ~3°C warming by 2100.
- AI already optimises renewables, grids, and buildings (e.g., DeepMind cut data-centre cooling energy 40%).
- Training frontier AI models can emit hundreds of tonnes CO2; hardware supply chains drive mining impacts.
- Climate finance and adaptation tools often sideline frontline communities due to biased data.
- Proven levers remain fossil phase-out, demand reduction, redistribution, and tech transfer.";

            var proTalkingPoints = new[]
            {
                new[]
                {
                    "DeepMind cut Google data-centre cooling energy use by roughly 40% using reinforcement learning controllers.",
                    "National Grid ESO in the UK uses ML forecasts to bring an extra 1.4 GW of renewables onto the grid during peak periods.",
                    "Carbon Re and Carbon Minds apply AI to cement kilns, trimming process emissions by 20-30% without major retrofits.",
                },
                new[]
                {
                    "AI-based climate scenario models let policymakers test carbon pricing paths and loss-and-damage packages in hours rather than weeks.",
                    "Satellite image classifiers track deforestation and methane leaks, supporting enforcement of Paris alignment checks.",
                    "Automated MRV (measurement, reporting, verification) platforms help nations publish transparent inventories for 2050 net-zero pledges.",
                },
                new[]
                {
                    "Kenya's SunCulture uses AI irrigation advice to boost smallholder yields 2-3x while cutting water use a third.",
                    "Bangladesh's FloodNet blends radar and AI hydrology models to warn delta villages up to five days earlier.",
                    "Chile's desert microgrids apply machine learning dispatch to keep clinics powered through drought-driven outages.",
                },
                new[]
                {
                    "Training a 500 tCO2e model is amortised across millions of inferences that drive real-world emission cuts.",
                    "Running inference on renewable-backed data centres halves operational emissions compared with coal-intensive baselines.",
                    "AI enables predictive maintenance on wind and rail assets, extending equipment life and reducing material throughput.",
                },
            };

            var cautionTalkingPoints = new[]
            {
                new[]
                {
                    "Training GPT-3 emitted ~550 tCO2e; hyperscale data centres already consume around 4% of global electricity.",
                    "GPU supply chains rely on cobalt and rare earth extraction that pollutes rivers in the DRC and Inner Mongolia.",
                    "Many AI labs still procure fossil-heavy electricity, so 'green AI' promises often outsource emissions to other regions.",
                },
                new[]
                {
                    "History shows techno-fixes delayed tougher choices: catalytic converters extended oil dominance; offsets let emitters avoid cuts.",
                    "Shell's 'Sky' scenarios used optimistic tech assumptions to justify slower fossil phase-out even after the Paris Agreement.",
                    "AI dashboards can become corporate fig leaves, masking rising production while marketing 'smart' efficiencies.",
                },
                new[]
                {
                    "UN climate finance data reveals 70% of adaptation funds still flow to wealthier countries and megaprojects.",
                    "Bias in damage models undervalues informal settlements, so AI triage can deprioritise frontline Black and Indigenous communities.",
                    "When climate risk scores inform insurance or credit, marginalised groups face higher premiums and forced displacement.",
                },
                new[]
                {
                    "Tuvalu and Kiribati need relocation funding now; waiting for AI breakthroughs does nothing as seas rise centimetres yearly.",
                    "Proven interventions—fossil phase-out timelines, wealth transfers, agroecology—deliver benefits without expensive compute.",
                    "Tech monopolies capture IP and hardware, so Global South governments rent access instead of owning climate infrastructure.",
                },
            };

            var proSystemPrompt = $"You are a climate technology advocate and environmental scientist. You build concise, evidence-backed arguments for using AI to meet climate targets. You respect justice critiques but maintain rapid AI deployment, paired with policy reform, is indispensable. Reference concrete data, avoid repeating phrasing, and never fabricate citations.\n\n{climatePrimer}\n\nResponse style: 4-5 sentences, confident but grounded.";

            var conSystemPrompt = $"You are a climate justice advocate and policy analyst. You scrutinise AI's climate role, emphasising embodied emissions, bias, and power imbalances. Highlight structural alternatives and ensure each answer is factual. Do not recycle wording or invent citations.\n\n{climatePrimer}\n\nResponse style: 4-5 sentences, analytic and people-focused.";

            using var proChat = GgufxTextChatEndpoint.Create(
                CreateContextOptions(modelPath),
                new GgufxTextChatOptions
                {
                    SystemPrompt = proSystemPrompt,
                });

            using var conChat = GgufxTextChatEndpoint.Create(
                CreateContextOptions(modelPath),
                new GgufxTextChatOptions
                {
                    SystemPrompt = conSystemPrompt,
                });

            const int rounds = 4;
            var topic = "AI deployment is necessary and justified for addressing the climate crisis.";
            string? proLast = null;
            string? conLast = null;

            for (var round = 0; round < rounds; round++)
            {
                var proPoints = proTalkingPoints[round % proTalkingPoints.Length];
                var cautionPoints = cautionTalkingPoints[round % cautionTalkingPoints.Length];

                var proBulletBlock = string.Join(Environment.NewLine, proPoints);
                var cautionBulletBlock = string.Join(Environment.NewLine, cautionPoints);

                var proPrompt = round == 0
                    ? $"You are the climate technology advocate. Support the claim that {topic} by weaving the bullet points into four crisp sentences. Avoid referencing instructions and keep the tone confident.\n\nBullet points:\n- {proBulletBlock.Replace(Environment.NewLine, "\n- ")}"
                    : $"Your opponent argued: \"{conLast}\". Rebut them while reinforcing that {topic}. Use these bullet points to ground your four sentences:\n- {proBulletBlock.Replace(Environment.NewLine, "\n- ")}";

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Round {round + 1} | Tech Advocate]");
                Console.ResetColor();

                GgufxTextTurnResult proTurn;
                try
                {
                    proTurn = await StreamResponseAsync(
                        proChat,
                        proPrompt,
                        ConsoleColor.Green,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (GgufxNativeException ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Generation halted for tech advocate: {ex.Message}");
                    Console.ResetColor();
                    break;
                }

                proLast = proTurn.Content.Trim();
                proChat.Reset();

                var conPrompt = $"Your opponent argued: \"{proLast}\". Challenge them from a climate justice perspective. Convert these bullet points into four grounded sentences that centre frontline communities:\n- {cautionBulletBlock.Replace(Environment.NewLine, "\n- ")}";

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[Round {round + 1} | Justice Advocate]");
                Console.ResetColor();

                GgufxTextTurnResult conTurn;
                try
                {
                    conTurn = await StreamResponseAsync(
                        conChat,
                        conPrompt,
                        ConsoleColor.Yellow,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (GgufxNativeException ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Generation halted for justice advocate: {ex.Message}");
                    Console.ResetColor();
                    break;
                }

                conLast = conTurn.Content.Trim();
                conChat.Reset();

                Console.WriteLine();
            }
        }

        private static async Task<GgufxTextTurnResult> StreamResponseAsync(
            GgufxTextChatEndpoint chat,
            string prompt,
            ConsoleColor color,
            CancellationToken cancellationToken)
        {
            var tokens = new List<string>();
            var result = await chat.SendAsync(
                prompt,
                token =>
                {
                    if (string.IsNullOrEmpty(token))
                    {
                        return;
                    }

                    if (token.Contains("</s>", StringComparison.Ordinal) || token.Contains("<|", StringComparison.Ordinal))
                    {
                        return;
                    }

                    tokens.Add(token);
                },
                cancellationToken).ConfigureAwait(false);

            var sanitized = SanitizeResponse(result.Content);
            StreamSanitized(sanitized, color);
            return result;
        }

        private static string SanitizeResponse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "(no response)";
            }

            var segments = raw
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var sentences = new List<string>();

            foreach (var segment in segments)
            {
                var cleaned = segment.Trim().TrimStart('-', ' ', '\t', '"').TrimEnd('"');
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    continue;
                }

                if (cleaned.Contains("instruction", StringComparison.OrdinalIgnoreCase) || cleaned.Contains("tone", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sentences.Add(NormaliseSentence(cleaned));
                if (sentences.Count >= 4)
                {
                    break;
                }
            }

            if (sentences.Count == 0)
            {
                var fallback = raw.Length > 320 ? raw[..320] : raw;
                return NormaliseSentence(fallback);
            }

            return string.Join(' ', sentences);
        }

        private static string NormaliseSentence(string text)
        {
            var trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return trimmed;
            }

            trimmed = char.ToUpperInvariant(trimmed[0]) + (trimmed.Length > 1 ? trimmed[1..] : string.Empty);
            if (!trimmed.EndsWith('.') && !trimmed.EndsWith('!') && !trimmed.EndsWith('?'))
            {
                trimmed += '.';
            }

            return trimmed;
        }

        private static void StreamSanitized(string text, ConsoleColor color)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                WriteColored(word + ' ', color);
            }

            Console.WriteLine();
        }

        private static GgufxTextContextOptions CreateContextOptions(string modelPath)
        {
            var options = new GgufxTextContextOptions(modelPath)
            {
                UseJinjaTemplate = false,
                ResponseBufferSize = 2 * 1024 * 1024,
            };

            options.Runtime.ThreadCount = Math.Max(2, Environment.ProcessorCount / 2);
            options.Runtime.GpuLayers = 0;
            options.Runtime.ContextLength = 4096;
            options.Runtime.PredictLength = 512;
            options.Decode.PromptBatchSize = 128;
            options.Decode.ResponseBatchSize = 64;

            options.Sampling.Seed = 42;
            options.Sampling.Temperature = 0.75f;
            options.Sampling.TopK = 50;
            options.Sampling.TopP = 0.9f;
            options.Sampling.RepeatPenalty = 1.2f;
            options.Sampling.PenaltyLastN = 256;

            return options;
        }

        private static string LocateModel()
        {
            var root = LocateSolutionRoot();
            var modelPath = Path.Combine(root, "examples", "tiny_llama_chat", "model", ModelFileName);

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException(
                    $"The sample expects the model file to be present at '{modelPath}'. " +
                    "Copy the TinyLLaMA GGUF model into this location before running the demo.",
                    modelPath);
            }

            return modelPath;
        }

        private static string LocateSolutionRoot()
        {
            var directory = Path.GetFullPath(AppContext.BaseDirectory);
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "ErgoX.GgufX.sln")))
                {
                    return directory;
                }

                var parent = Directory.GetParent(directory);
                if (parent is null)
                {
                    break;
                }

                directory = parent.FullName;
            }

            throw new InvalidOperationException("Unable to locate the solution root from the current working directory.");
        }

        private static void WriteColored(string value, ConsoleColor color)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            var original = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(value);
            Console.ForegroundColor = original;
        }
    }
}
