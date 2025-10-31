# Granite Docling Sample

This console sample demonstrates how to consume the `ErgoX.VecraX.GgufX` managed library to run Granite Docling multimodal inference. It loads the production wrapper, processes images from the example's local I/O folder, and exports Markdown outputs using the runtime binaries distributed with the library.

## Prerequisites

- Granite Docling model artifacts placed in `examples/granite_docling/models/`:
  - `granite-docling-258M-Q8_0.gguf`
  - `mmproj-granite-docling-258M-Q8_0.gguf`
- Runtime binaries (copied into `src/ErgoX.VecraX.GgufX/runtimes/win-x64/`) are restored automatically via the project reference.
- Input screenshots located under `examples/granite_docling/io/input/`.

## Running the Sample

From the repository root execute:

```powershell
dotnet run --project examples/granite_docling
```

The application will:

- Load the Granite Docling model and projector through the managed GGUFx session API.
- Enumerate all supported image files in `examples/granite_docling/io/input/`.
- Generate GitHub-flavoured Markdown responses for each document.
- Persist the Markdown into `examples/granite_docling/io/output/` with matching file names.

## Output

Each processed document produces a `.md` file in `examples/granite_docling/io/output/`. Existing files are overwritten so you can re-run the sample repeatedly during development.

## Troubleshooting

- **Model not found**: Ensure the `.gguf` assets exist inside the `models/` folder and paths are correct.
- **Missing runtime DLLs**: Verify the latest DLLs were copied into `src/ErgoX.VecraX.GgufX/runtimes/win-x64/` and rebuild the solution.
- **No input media**: Add PNG/JPEG/BMP/WEBP images into `examples/granite_docling/io/input/` before running.
