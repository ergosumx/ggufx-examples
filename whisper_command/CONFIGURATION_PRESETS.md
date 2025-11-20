# GGUFx ASR - Recommended Configuration Presets

Based on systematic testing, here are recommended configurations for common scenarios.

## 🎯 Use Case Presets

### 1. Maximum Accuracy (Voice Assistant)
**When**: Accuracy is critical, latency <2s acceptable
**Processing Time**: ~1000-1500ms

```csharp
var options = new GgufxAsrCommandOptions
{
    // Core
    ModelPath = modelPath,
    Mode = GgufxAsrCommandMode.CommandList,
    CommandsFilePath = commandsPath,

    // Accuracy settings
    AudioContextSize = 0,              // Full context (auto ~1500)
    NoContext = false,                 // Keep context
    MaxAudioBufferSeconds = 5,         // Process more audio
    MaxTokens = 16,                    // Allow longer outputs
    SingleSegment = true,

    // Thresholds (defaults)
    EntropyThreshold = 2.4f,
    LogProbThreshold = -1.0f,
    NoSpeechThreshold = 0.6f,

    // VAD
    VadThreshold = 0.3f,               // Moderate sensitivity

    // Hardware
    ThreadCount = 4,
    UseGpu = GgufxTriState.Enabled,
    FlashAttention = GgufxTriState.Enabled
};
```

**Expected**: Probability >0.75, Processing ~1200ms, High confidence >80%

---

### 2. Balanced (Recommended Default)
**When**: Good accuracy with responsive UX
**Processing Time**: ~800-1000ms

```csharp
var options = new GgufxAsrCommandOptions
{
    // Core
    ModelPath = modelPath,
    Mode = GgufxAsrCommandMode.CommandList,
    CommandsFilePath = commandsPath,

    // Balanced settings
    AudioContextSize = 1000,           // Medium context
    NoContext = false,                 // Keep context for accuracy
    MaxAudioBufferSeconds = 3,         // Standard buffer
    MaxTokens = 1,                     // Fast classification
    SingleSegment = true,

    // Thresholds
    EntropyThreshold = 2.4f,
    LogProbThreshold = -1.0f,
    NoSpeechThreshold = 0.6f,

    // VAD
    VadThreshold = 0.1f,               // Balanced sensitivity

    // Hardware
    ThreadCount = 4,
    UseGpu = GgufxTriState.Enabled,
    FlashAttention = GgufxTriState.Enabled
};
```

**Expected**: Probability >0.70, Processing ~900ms, High confidence >70%

---

### 3. Maximum Speed (Real-time Gaming/Controls)
**When**: Sub-second response required, moderate accuracy OK
**Processing Time**: ~500-700ms

```csharp
var options = new GgufxAsrCommandOptions
{
    // Core
    ModelPath = modelPath,
    Mode = GgufxAsrCommandMode.CommandList,
    CommandsFilePath = commandsPath,

    // Speed settings
    AudioContextSize = 768,            // Reduced context
    NoContext = true,                  // Disable context (faster)
    MaxAudioBufferSeconds = 2,         // Small buffer
    MaxTokens = 1,                     // Single token only
    SingleSegment = true,

    // Thresholds
    EntropyThreshold = 2.4f,
    LogProbThreshold = -1.0f,
    NoSpeechThreshold = 0.6f,

    // VAD
    VadThreshold = 0.01f,              // Very sensitive (fast trigger)

    // Hardware
    ThreadCount = 4,
    UseGpu = GgufxTriState.Enabled,
    FlashAttention = GgufxTriState.Enabled
};
```

**Expected**: Probability >0.60, Processing ~600ms, High confidence >50%

---

### 4. Long-Form Transcription
**When**: Transcribing longer phrases or sentences
**Processing Time**: ~1500-2000ms

```csharp
var options = new GgufxAsrCommandOptions
{
    // Core
    ModelPath = modelPath,
    Mode = GgufxAsrCommandMode.GeneralPurpose,  // Different mode!

    // Transcription settings
    AudioContextSize = 0,              // Full context
    NoContext = false,                 // Keep context
    MaxAudioBufferSeconds = 10,        // Large buffer for long speech
    MaxTokens = 64,                    // Many tokens
    SingleSegment = false,             // Allow segmentation

    // Thresholds
    EntropyThreshold = 2.4f,
    LogProbThreshold = -1.0f,
    NoSpeechThreshold = 0.6f,

    // VAD
    VadThreshold = 0.3f,

    // Hardware
    ThreadCount = 6,                   // More threads
    UseGpu = GgufxTriState.Enabled,
    FlashAttention = GgufxTriState.Enabled
};
```

**Expected**: Good transcription quality, slower processing acceptable

---

### 5. Low-Power/Embedded
**When**: Running on low-power devices, battery life critical
**Processing Time**: ~400-600ms

```csharp
var options = new GgufxAsrCommandOptions
{
    // Core
    ModelPath = modelPath,
    Mode = GgufxAsrCommandMode.CommandList,
    CommandsFilePath = commandsPath,

    // Power-efficient settings
    AudioContextSize = 512,            // Very small context
    NoContext = true,                  // No context
    MaxAudioBufferSeconds = 2,         // Small buffer
    MaxTokens = 1,                     // Minimal tokens
    SingleSegment = true,

    // Thresholds
    EntropyThreshold = 2.4f,
    LogProbThreshold = -1.0f,
    NoSpeechThreshold = 0.6f,

    // VAD
    VadThreshold = 0.05f,

    // Hardware
    ThreadCount = 2,                   // Fewer threads
    UseGpu = GgufxTriState.Disabled,   // CPU only
    FlashAttention = GgufxTriState.Disabled
};
```

**Expected**: Probability >0.55, Processing ~500ms, lower accuracy trade-off

---

## 🔧 Fine-Tuning Parameters

### AudioContextSize
| Value | Description | Speed | Accuracy | Use When |
|-------|-------------|-------|----------|----------|
| 0 (auto) | Full context (~1500) | ⭐⭐ | ⭐⭐⭐⭐⭐ | Maximum accuracy needed |
| 1500 | Explicit full | ⭐⭐ | ⭐⭐⭐⭐⭐ | Same as auto |
| 1000 | Medium | ⭐⭐⭐ | ⭐⭐⭐⭐ | **Recommended default** |
| 768 | Reduced | ⭐⭐⭐⭐ | ⭐⭐⭐ | Speed priority |
| 512 | Minimal | ⭐⭐⭐⭐⭐ | ⭐⭐ | Embedded/low-power |

### NoContext
| Value | Speed | Accuracy | Trade-off |
|-------|-------|----------|-----------|
| false | ⭐⭐ | ⭐⭐⭐⭐⭐ | Better accuracy, slower |
| true | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | **Faster but may miss context cues** |

### MaxAudioBufferSeconds
| Value | Description | Latency | Use Case |
|-------|-------------|---------|----------|
| 0 | Unlimited | Higher | Long phrases |
| 10 | 10 seconds | High | Long transcription |
| 5 | 5 seconds | Medium | Voice assistant |
| 3 | 3 seconds | Low | **Recommended for commands** |
| 2 | 2 seconds | Very Low | Quick commands |

### MaxTokens
| Value | Description | Speed | Use When |
|-------|-------------|-------|----------|
| 1 | Single token | ⭐⭐⭐⭐⭐ | **Command classification** |
| 8-16 | Short phrase | ⭐⭐⭐ | Short commands |
| 32-64 | Full sentence | ⭐⭐ | Transcription |

### VadThreshold
| Value | Sensitivity | False Positives | Use When |
|-------|-------------|-----------------|----------|
| 0.01 | Very High | Many | Testing, very quiet |
| 0.1 | High | Some | Normal speech |
| 0.3 | **Medium** | Few | **Recommended** |
| 0.6 | Low | Rare | Noisy environment |
| 0.8 | Very Low | None | Very noisy |

---

## 📊 Performance Matrix

| Preset | Avg Prob | Proc Time | High Conf % | Latency Feel | CPU Usage |
|--------|----------|-----------|-------------|--------------|-----------|
| Maximum Accuracy | >0.75 | ~1200ms | >80% | Noticeable | High |
| **Balanced** | >0.70 | ~900ms | >70% | **Good** | **Medium** |
| Maximum Speed | >0.60 | ~600ms | >50% | Instant | Medium |
| Long-Form | >0.70 | ~1800ms | >75% | Slow | High |
| Low-Power | >0.55 | ~500ms | >45% | Fast | Low |

---

## 🎛️ Quick Tuning Guide

### Problem: Low Accuracy
✅ **Try**:
- Set `AudioContextSize = 0` (full context)
- Set `NoContext = false`
- Increase `MaxAudioBufferSeconds` to 5 or more
- Increase `MaxTokens` to 16-32
- Lower `VadThreshold` to 0.1-0.2

### Problem: Too Slow
✅ **Try**:
- Set `AudioContextSize = 768` or `1000`
- Set `NoContext = true`
- Reduce `MaxAudioBufferSeconds` to 2-3
- Set `MaxTokens = 1`
- Ensure GPU is enabled

### Problem: False Triggers
✅ **Try**:
- Increase `VadThreshold` to 0.3-0.6
- Adjust `NoSpeechThreshold` higher
- Reduce ambient noise
- Use better microphone

### Problem: Missed Commands
✅ **Try**:
- Lower `VadThreshold` to 0.01-0.1
- Increase `MaxAudioBufferSeconds`
- Speak louder/clearer
- Check microphone gain

---

## 🧪 Testing Your Configuration

Always test your configuration with the systematic tester:

```powershell
dotnet run --test --model "model.bin" --commands "commands.txt"
```

This will help you find the optimal balance for YOUR specific:
- Hardware (CPU/GPU speed)
- Use case (gaming vs transcription)
- Audio environment (quiet office vs noisy room)
- Latency requirements (instant vs acceptable delay)

---

## 📝 Configuration Template

```csharp
// Copy and customize this template
var options = new GgufxAsrCommandOptions
{
    // Required
    ModelPath = "path/to/model.bin",
    Mode = GgufxAsrCommandMode.CommandList,
    CommandsFilePath = "commands.txt",

    // Tuning (start with balanced preset, then adjust)
    AudioContextSize = 1000,           // 0|512|768|1000|1500
    NoContext = false,                 // true=fast, false=accurate
    MaxAudioBufferSeconds = 3,         // 0|2|3|5|10
    MaxTokens = 1,                     // 1|8|16|32|64
    SingleSegment = true,              // Usually true

    // Thresholds (rarely need to change)
    EntropyThreshold = 2.4f,
    LogProbThreshold = -1.0f,
    NoSpeechThreshold = 0.6f,

    // VAD (adjust for your environment)
    VadThreshold = 0.1f,               // 0.01|0.1|0.3|0.6

    // Hardware
    ThreadCount = 4,
    UseGpu = GgufxTriState.Enabled,
    FlashAttention = GgufxTriState.Enabled,
    SuppressLogs = true
};
```

---

**Remember**: These are starting points. Use `--test` mode to find what works best for YOUR setup! 🚀
