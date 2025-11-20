# GGUFx ASR Command Recognition Example

Real-time voice command recognition with Whisper models using Jabra devices or any audio input.

## Features

- **Three Recognition Modes**:
  - **CommandList**: Classify speech into predefined commands (e.g., "play", "pause", "stop")
  - **AlwaysPrompt**: Require activation phrase before accepting commands
  - **GeneralPurpose**: Free-form transcription of all speech
- **Grammar Support**: Optional GBNF grammar files for constrained recognition
- **Multi-Device**: Works with Jabra headsets, USB microphones, system default, or loopback devices
- **Real-Time Processing**: Voice activity detection (VAD) triggers automatic transcription

## Quick Start

### Prerequisites

1. **Whisper Model**: Download a Whisper GGML model (base.en recommended)
   ```bash
   # Download base.en model (~150MB)
   curl -L "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin" -o models/ggml-base.en.bin
   ```

2. **Audio Device**:
   - Built-in microphone works
   - For Jabra devices: Install [Jabra Direct](https://www.jabra.com/software-and-services/jabra-direct)
   - Verify device in Windows Sound Settings (Right-click speaker icon → Sounds → Recording)

### Run Examples

```bash
# Command list mode (classify into predefined commands)
dotnet run --project examples/whisper_command -- --model models/ggml-base.en.bin --mode list

# Always-prompt mode (require "hey assistant" activation phrase)
dotnet run --project examples/whisper_command -- --model models/ggml-base.en.bin --mode prompt

# General-purpose mode (free-form transcription)
dotnet run --project examples/whisper_command -- --model models/ggml-base.en.bin --mode general

# Use specific device (e.g., Jabra Evolve2 65)
dotnet run --project examples/whisper_command -- --model models/ggml-base.en.bin --mode list --device 2
```

## Usage

### Command Line Options

```
whisper_command --model <path> [--mode list|prompt|general] [--device <id>]

Options:
  --model <path>    Path to Whisper GGML model (required)
  --mode <mode>     Recognition mode: list, prompt, or general (default: list)
  --device <id>     Audio device ID (-1 for default, 0+ for specific device)
```

### Mode Details

#### 1. Command List Mode (`--mode list`)

Classifies speech into predefined commands from `commands.txt`.

**Best for**:
- Voice shortcuts
- Media controls
- Smart home commands

**Example**:
```
Say: "play"
Output: ► Command: play (Probability: 0.95)

Say: "volume up"
Output: ► Command: volume up (Probability: 0.89)
```

**Customization**:
Edit `commands.txt` to add your own commands:
```
open browser
close window
save file
undo
redo
```

#### 2. Always-Prompt Mode (`--mode prompt`)

Requires activation phrase ("hey assistant") before accepting commands.

**Best for**:
- Preventing false positives
- Privacy-conscious applications
- Hands-free assistants

**Example**:
```
Say: "hey assistant"
Output: ✓ Activation phrase matched (confidence: 0.82)

Say: "play music"
Output: ► Command: play (Probability: 0.91)
```

**Configuration**:
- Activation phrase: "hey assistant" (hardcoded in example)
- Activation threshold: 0.7 (70% confidence required)
- Timeout: 5 seconds to detect activation, 8 seconds for command

#### 3. General-Purpose Mode (`--mode general`)

Transcribes all detected speech without classification.

**Best for**:
- Dictation
- Meeting transcription
- Voice notes

**Example**:
```
Say: "This is a test of the transcription system"
Output: ► Transcription: "This is a test of the transcription system"
```

**Configuration**:
- Max tokens: 128 (longer transcriptions)
- Duration: 10 seconds per utterance

## Grammar Files (Optional)

Place GBNF grammar files in `grammars/` directory to constrain recognition:

### Simple Commands Grammar (`simple-commands.gbnf`)
Allows only predefined command patterns:
```gbnf
command ::= play | pause | stop | volume
play ::= "play" | "start" | "resume"
volume ::= "volume up" | "louder" | "increase volume"
```

### Meeting Controls Grammar (`meeting-controls.gbnf`)
Specialized for video conferencing:
```gbnf
meeting_command ::= mute_command | video_command | screen_command
mute_command ::= ("mute" | "unmute") ws? ("microphone" | "mic")?
screen_command ::= ("share" | "stop sharing") ws? ("screen")?
```

**To use grammar**:
Modify `Program.cs` to add `GrammarPath` option:
```csharp
var options = new GgufxAsrCommandOptions
{
    ModelPath = modelPath,
    Mode = GgufxAsrCommandMode.CommandList,
    CommandsFilePath = "commands.txt",
    GrammarPath = "grammars/simple-commands.gbnf",  // Add this line
    // ... other options
};
```

## Jabra Device Setup

### Device Selection

1. **List Devices** (Windows PowerShell):
   ```powershell
   Get-PnpDevice -Class AudioEndpoint | Where-Object {$_.Status -eq "OK"} | Select-Object FriendlyName
   ```

2. **Find Device ID**:
   Run the example - device IDs are shown in output (future enhancement)
   For now, use trial-and-error with `--device 0`, `--device 1`, etc.

3. **Set Default Device** (Alternative):
   - Right-click speaker icon → Sounds → Recording tab
   - Select your Jabra device → Set as Default
   - Use `--device -1` (default)

### Jabra-Specific Features

- **Mute Button**: Physical mute button works independently of software
- **Audio Quality**: Jabra devices typically use 16kHz sampling (matches Whisper requirements)
- **Noise Cancellation**: Hardware noise cancellation improves recognition accuracy
- **LED Indicators**: Mute/unmute status visible on device

### Troubleshooting Jabra

**No audio detected**:
1. Check Jabra Direct is running
2. Verify device is not muted (LED indicator)
3. Test device in Windows Sound Recorder
4. Try updating Jabra firmware via Jabra Direct

**Poor recognition**:
1. Adjust microphone position (1-2 inches from mouth)
2. Lower `VadThreshold` in code (default 0.6 → try 0.4)
3. Increase `CommandDurationMs` for slower speech
4. Use larger Whisper model (base.en → small.en → medium.en)

**Device not found**:
1. Unplug and reconnect Jabra device
2. Restart Jabra Direct service
3. Check Windows Device Manager for driver issues

## Performance Tuning

### Model Selection

| Model | Size | Speed | Accuracy | Use Case |
|-------|------|-------|----------|----------|
| tiny.en | 75MB | Fastest | Good | Testing, simple commands |
| base.en | 142MB | Fast | Better | **Recommended** for most use cases |
| small.en | 466MB | Medium | Great | High-accuracy requirements |
| medium.en | 1.5GB | Slow | Excellent | Professional transcription |

### GPU Acceleration

Enabled by default if CUDA/Metal/Vulkan available:
```csharp
UseGpu = true,          // Enable GPU
FlashAttention = true,  // Enable Flash Attention (faster)
```

### CPU Tuning

```csharp
ThreadCount = 4,  // Adjust based on CPU cores (4-8 typical)
```

### VAD Sensitivity

```csharp
VadThreshold = 0.6f,  // Lower = more sensitive (0.4-0.8 range)
```

- **0.4**: Picks up quiet speech, more false positives
- **0.6**: Balanced (default)
- **0.8**: Only loud/clear speech, fewer false positives

## Code Structure

```
examples/whisper_command/
├── Program.cs                          # Main application
├── whisper_command.csproj              # Project file
├── commands.txt                        # Predefined commands
├── grammars/
│   ├── simple-commands.gbnf            # Media control grammar
│   └── meeting-controls.gbnf           # Video conference grammar
└── README.md                           # This file
```

## Advanced Usage

### Custom Commands

Edit `commands.txt` to recognize domain-specific commands:

**Smart Home**:
```
turn on lights
turn off lights
set temperature to 72
lock the door
unlock the door
```

**Code Editor**:
```
save file
close tab
find in files
run tests
debug
```

**Medical Dictation**:
```
new patient
open chart
prescribe medication
order lab test
```

### Multiple Command Files

Modify `Program.cs` to switch command sets:
```csharp
var commandFile = mode switch
{
    "media" => "commands-media.txt",
    "smarthome" => "commands-smarthome.txt",
    "editor" => "commands-editor.txt",
    _ => "commands.txt"
};

var options = new GgufxAsrCommandOptions
{
    // ...
    CommandsFilePath = commandFile,
};
```

### Event-Driven Actions

Extend `OnCommandRecognized` to trigger actions:
```csharp
private static void OnCommandRecognized(object? sender, GgufxAsrCommandResultEventArgs e)
{
    var result = e.Result;

    if (result.BestCommand?.Command == "play")
    {
        // Execute action
        PlayMedia();
    }
    else if (result.BestCommand?.Command == "volume up")
    {
        IncreaseVolume();
    }
}
```

## API Reference

See main documentation for complete API:
- `GgufxAsrCommandOptions`: Configuration options
- `GgufxAsrCommandSession`: Session lifecycle (Start/Stop/Dispose)
- `GgufxAsrCommandResult`: Recognition results
- `GgufxAsrCommandMode`: Recognition modes

## License

This example is part of the GGUFx project and follows the same MIT license as llama.cpp.

## See Also

- [GGUFx ASR Batch API](../whisper/README.md)
- [GGUFx ASR Streaming API](../whisper_stream/README.md)
- [Whisper.cpp Documentation](https://github.com/ggerganov/whisper.cpp)
- [GBNF Grammar Guide](https://github.com/ggerganov/llama.cpp/blob/master/grammars/README.md)
