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

```bash
# From the repository root
dotnet run --project examples/whisper/whisper.csproj
```

Running without arguments executes a full demonstration using the bundled JFK recordings:

- `examples/_io/audio/wav/jfk.wav`
- `examples/_io/audio/mp3/jfk.mp3`

Each clip is transcribed twice (baseline and grammar-guided) and the resulting transcripts are written to `examples/whisper/output/`.

To target your own audio file, pass CLI arguments:

```bash
dotnet run --project examples/whisper/whisper.csproj -- --audio <path-to-audio>
```

During execution the sample prints each decoded segment with timestamps, followed by the aggregated transcript.

### Grammar-guided decoding

The `examples/whisper/grammars/letters-only.gbnf` grammar restricts decoder output to alphabetic tokens and common punctuation. Combine it with the bundled JFK sample to observe grammar guidance end-to-end:

```powershell
dotnet run --project examples/whisper/whisper.csproj -- --audio examples/_io/audio/wav/jfk.wav --grammar examples/whisper/grammars/letters-only.gbnf --grammar-rule root
```

The console will confirm the active grammar and proceed with transcription under the specified constraints. Supply `--grammar-penalty <value>` to bias the decoder further away from out-of-grammar tokens when operating on noisy inputs.

## Notes

- The sample accepts WAV or MP3 input. MP3 clips are decoded and resampled to 16 kHz mono automatically.
- The GGUFx runtime DLLs (including `ggufx-asr.dll`) must be present under `src/ErgoX.GgufX/runtimes/win-x64` so they are resolved at runtime.
