# GGUFx Whisper Example

This console sample demonstrates how to run GGUFx automatic speech recognition (ASR) against a local audio file using the `ggml-large-v3-turbo-q5_0.bin` transcription model, the `ggml-small.en-tdrz.bin` tiny-diarisation variant, and a Silero voice activity detection (VAD) model.

## Prerequisites

- .NET 8 SDK
- GGUF Whisper model at `examples/whisper/model/ggml-large-v3-turbo-q5_0.bin`
- Tiny diarisation model at `examples/whisper/model/ggml-small.en-tdrz.bin`
- Silero VAD model at `examples/whisper/model/ggml-silero-v5.1.2.bin`
- A 16 kHz, mono WAV file you wish to transcribe
- (Optional) An MP3 file – the sample automatically converts to 16 kHz mono at runtime

Download the Silero VAD model by running the helper script from the repository root:

```powershell
.ext/ggufx-asr.cpp/models/download-vad-model.cmd silero-v5.1.2 examples/whisper/model
```

The command places `ggml-silero-v5.1.2.bin` in the sample's `model/` directory so the runtime can enable VAD.

## Running the sample

```pwsh
# From the repository root
dotnet run --project examples/whisper/whisper.csproj
```

Running without arguments transcribes the bundled JFK recording at `examples/_io/audio/wav/jfk.wav`, printing each decoded segment with timestamps followed by the aggregated transcript.

### Realtime streaming

Pass the `stream` switch to exercise `GgufxAsrStreamingSession` with your default microphone (or loopback device when no microphone is detected):

```pwsh
dotnet run --project examples/whisper/whisper.csproj -- stream
```

The console lists all detected audio devices, starts streaming for roughly 20 seconds, and emits partial/final tokens as they arrive. Press **Ctrl+C** at any time to stop early.

## Notes

- The sample decodes the bundled WAV assets via `GgufxAsrAudioDecoder`, resampling to 16 kHz mono automatically.
- The GGUFx runtime DLLs (including `ggufx-asr.dll`) must be present under `src/ErgoX.GgufX/runtimes/win-x64` so they are resolved at runtime.
- Streaming requires a capture device that supports the configured sample rate (16 kHz by default); the helper prints the selected device and sample rate before opening the session.
