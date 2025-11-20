# Configuration Testing Guide for GGUFx ASR

This document explains how to use the configuration testing tool to find optimal settings for your use case.

## Quick Start

Run the configuration tester:

```powershell
dotnet run --test --model "path/to/ggml-tiny.en-q5_1.bin" --commands "commands.txt"
```

## What Gets Tested

The tool tests 8 different configurations:

1. **Baseline (Default)** - Full Whisper defaults
2. **Fast (Current)** - Optimized for speed (no context, 3s buffer)
3. **Accurate (Full Context)** - Optimized for accuracy (full context, 5s buffer, 32 tokens)
4. **Balanced (1500 ctx)** - 1500 audio context, 3s buffer
5. **Balanced (1000 ctx)** - 1000 audio context, 3s buffer
6. **Balanced (768 ctx)** - 768 audio context, 3s buffer
7. **Fast (768 ctx, no context)** - Speed-optimized with reduced context
8. **Fast (1000 ctx, no context)** - Speed-optimized with medium context

## Key Parameters

### AudioContextSize
- **0** (auto/default ~1500): Best accuracy, slower
- **1000-1500**: Good balance
- **768**: Faster, slight accuracy loss
- Lower values reduce processing time but may hurt accuracy

### NoContext
- **false**: Keep audio context between segments (better accuracy)
- **true**: No context (faster processing)

### MaxAudioBufferSeconds
- **0**: Process all accumulated audio (most accurate for long commands)
- **2-3**: Fast processing for short commands
- **5+**: Good for longer phrases

### MaxTokens
- **1**: Single token classification (fastest, for command list mode)
- **16-32**: Multiple tokens for general transcription

## Test Process

Each test runs for 30 seconds. During each test:

1. Say various commands clearly
2. Try each command at least once: play, pause, stop, next, previous, volume up, volume down
3. The tool shows real-time results with color coding:
   - 🟢 Green: High confidence (≥0.8)
   - 🟡 Yellow: Medium confidence (0.5-0.8)
   - 🔴 Red: Low confidence (<0.5)
4. Press Ctrl+C to skip to next configuration

## Results Analysis

After all tests, you get:

### Accuracy Ranking
- Configurations sorted by average probability
- Shows high/medium/low confidence distribution
- Processing time for each config

### Speed Ranking
- Configurations sorted by processing time
- Shows accuracy vs speed trade-off

### Recommendations
- 🏆 Best Accuracy: Highest average probability
- ⚡ Fastest: Lowest processing time
- ⚖️ Best Balanced: High accuracy with reasonable speed

## Interpreting Results

### For Command Recognition
- Look for avg probability **>0.7** for reliable recognition
- Processing time **<1000ms** for responsive UX
- High confidence count should be **>70%** of total

### For General Transcription
- Consider configs with MaxTokens=32
- Higher buffer sizes (5s+) for longer phrases
- Balance accuracy with latency requirements

## Example Output

```
RANKED BY ACCURACY:
Rank  Configuration                  Avg Prob     High/Med/Low    Avg Time     Count
1     Accurate (Full Context)        0.812        12/3/1          1234ms       16
2     Balanced (1500 ctx)            0.789        10/4/2          987ms        16
3     Baseline (Default)             0.765        9/5/2           1089ms       16
4     Balanced (1000 ctx)            0.734        8/6/2           876ms        16
5     Fast (Current)                 0.612        4/7/5           654ms        16

RECOMMENDATIONS:
🏆 Best Accuracy: Accurate (Full Context)
   Avg Probability: 0.812, Processing: 1234ms
   Settings: AudioCtx=0, NoContext=False, MaxAudio=5s, MaxTokens=32

⚡ Fastest: Fast (768 ctx, no context)
   Processing: 543ms, Avg Probability: 0.589
   Settings: AudioCtx=768, NoContext=True, MaxAudio=2s, MaxTokens=1

⚖️ Best Balanced: Balanced (1000 ctx)
   Avg Probability: 0.734, Processing: 876ms
   Settings: AudioCtx=1000, NoContext=False, MaxAudio=3s, MaxTokens=1
```

## Customizing Configurations

Edit `ConfigTester.cs` to add your own test configurations:

```csharp
new TestConfig("My Custom Config",
    AudioContextSize: 1200,      // Your value
    NoContext: false,             // true/false
    MaxAudioBufferSeconds: 4,     // Seconds
    MaxTokens: 8)                 // Token count
```

## Applying Results

Once you find the optimal configuration, update your `Program.cs`:

```csharp
var options = new GgufxAsrCommandOptions
{
    ModelPath = modelPath,
    Mode = GgufxAsrCommandMode.CommandList,

    // Apply optimal settings from test results
    AudioContextSize = 1000,         // From test
    NoContext = false,                // From test
    MaxAudioBufferSeconds = 3,        // From test
    MaxTokens = 1,                    // From test

    // Keep other settings
    VadThreshold = 0.01f,
    ThreadCount = 4,
    // ...
};
```

## Tips for Best Results

1. **Consistent environment**: Test in the same acoustic conditions you'll use
2. **Natural speech**: Speak commands as you normally would
3. **Multiple tests**: Run tests multiple times and average results
4. **Monitor CPU**: Watch CPU usage during fast configs - sustained high usage may not be practical
5. **Test edge cases**: Try commands with background noise, different volumes, accents

## Troubleshooting

- **No detections**: Lower VadThreshold (try 0.01 or 0.001)
- **Too many false triggers**: Raise VadThreshold (try 0.1-0.3)
- **Processing too slow**: Reduce AudioContextSize, enable NoContext, reduce MaxAudioBufferSeconds
- **Low accuracy**: Increase AudioContextSize (or 0 for auto), disable NoContext, increase buffer size
