# Granite Dockling 258M OCR Sample

This console application demonstrates Granite Docling 258M multi-modal inference using the GGUFx bindings. It loads the text model and associated `mmproj` projector, tokenises document images, and produces GitHub-flavoured Markdown transcriptions.

## Prerequisites

- Granite Docling 258M model files placed under `.models/granite-docling-258M-GGUF/`:
  - `granite-docling-258M-Q8_0.gguf`
  - `mmproj-granite-docling-258M-Q8_0.gguf`
- Native runtimes extracted into `runtimes/` (CPU or GPU backend).
- Example inputs located in `examples/GraniteDockling-258M/Inputs/`.

## Running the sample

```powershell
pwsh> dotnet run --project examples/GraniteDockling-258M/GraniteDockling-258M.csproj
```

The application processes every image in the `Inputs` folder and emits Markdown files into `examples/GraniteDockling-258M/Output/`. Each document includes generation metadata (duration, token count, feature flags).
