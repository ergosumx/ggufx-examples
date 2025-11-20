# GGUFx Examples

The `examples/` folder collects self-contained samples that exercise specific parts of the GGUFx managed API surface.

| Example | Description |
| --- | --- |
| `whisper` | Batch transcription pipeline that processes pre-recorded audio files. |
| `whisper_stream` | Console app that mirrors whisper.cpp streaming flags (device selection, GPU, loopback). |
| `whisper_command` | Real-time voice command recognition with three modes: CommandList, AlwaysPrompt, and GeneralPurpose. |

## whisper_stream quick start

Ensure you have a Whisper model such as `ggml-small.en-tdrz.bin` in `examples/whisper/model/` (or provide a custom path). Run the streaming sample and optionally disable native Whisper logs via the new flag:

```pwsh
cd examples/whisper_stream
dotnet run -- --model ../whisper/model/ggml-small.en-tdrz.bin --device-id 5 --loopback --suppress-native-logs
```

Omit `--suppress-native-logs` (or pass `--show-native-logs`) when you want to see the raw Whisper output for troubleshooting.

## whisper_command quick start

Voice command recognition with Whisper models. Supports three modes:

- **CommandList**: Classify speech into predefined commands (e.g., "play", "pause", "stop")
- **AlwaysPrompt**: Require activation phrase before accepting commands
- **GeneralPurpose**: Free-form transcription of all speech

```pwsh
cd examples/whisper_command

# Command list mode (classify into predefined commands)
dotnet run -- --model ../whisper/model/ggml-base.en.bin --mode list

# Always-prompt mode (require "hey assistant" activation phrase)
dotnet run -- --model ../whisper/model/ggml-base.en.bin --mode prompt

# General-purpose mode (free-form transcription)
dotnet run -- --model ../whisper/model/ggml-base.en.bin --mode general
```

See [whisper_command/README.md](whisper_command/README.md) for detailed documentation including grammar support, Jabra device setup, and customization options.
