# whisper_capture

The `whisper_capture` sample records audio from a ggufx-asr reported device into a WAV file so that device
selection issues can be diagnosed outside of the streaming pipeline.

## Prerequisites

- Windows with WASAPI support (the sample relies on `NAudio` loopback/capture implementations).
- A GGUFx runtime build located in `src/ErgoX.GgufX/runtimes/win-x64`.

## Usage

```bash
# List devices exposed by ggufx-asr.dll
 dotnet run --project examples/whisper_capture/whisper_capture.csproj -- --list-devices

# Capture from a specific device id for 30 seconds
 dotnet run --project examples/whisper_capture/whisper_capture.csproj -- --device 1000000 --duration 30

# Capture until ENTER is pressed and write to a custom path
 dotnet run --project examples/whisper_capture/whisper_capture.csproj -- --device 0 --output "C:/temp/loopback.wav"
```

When no output path is provided the tool writes a timestamped file (UTC) to the current working directory.
