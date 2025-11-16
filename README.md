# GGUFx Examples

The `examples/` folder collects self-contained samples that exercise specific parts of the GGUFx managed API surface.

| Example | Description |
| --- | --- |
| `whisper` | Batch transcription pipeline that processes pre-recorded audio files. |
| `whisper_stream` | Console app that mirrors whisper.cpp streaming flags (device selection, GPU, loopback). |

## whisper_stream quick start

Ensure you have a Whisper model such as `ggml-small.en-tdrz.bin` in `examples/whisper/model/` (or provide a custom path). Run the streaming sample and optionally disable native Whisper logs via the new flag:

```pwsh
cd examples/whisper_stream
dotnet run -- --model ../whisper/model/ggml-small.en-tdrz.bin --device-id 5 --loopback --suppress-native-logs
```

Omit `--suppress-native-logs` (or pass `--show-native-logs`) when you want to see the raw Whisper output for troubleshooting.
