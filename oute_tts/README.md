# GGUFx OuteTTS Example

This console sample demonstrates how to run GGUFx text-to-speech (TTS) using the OuteTTS model and WavTokenizer vocoder.

## Prerequisites

- .NET 8 SDK
- OuteTTS model at `examples/oute_tts/model/OuteTTS-0.2-500M-Q5_K_M.gguf`
- WavTokenizer model at `examples/oute_tts/model/wavtokenizer-large-75-ggml-f16.gguf`

## Running the sample

```pwsh
# From the repository root
dotnet run --project examples/oute_tts/oute_tts.csproj -- "examples/oute_tts/model/OuteTTS-0.2-500M-Q5_K_M.gguf" "examples/oute_tts/model/wavtokenizer-large-75-ggml-f16.gguf" "Hello world" "output.wav"
```

The arguments are:
1. Path to the TTC (Text-to-Codes) model.
2. Path to the Vocoder model.
3. Text to synthesize.
4. Output WAV file path.

If no arguments are provided, it attempts to find the models in `examples/oute_tts/model/` or `model/` relative to the execution directory.

```pwsh
dotnet run --project examples/oute_tts/oute_tts.csproj
```
