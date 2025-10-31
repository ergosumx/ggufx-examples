# LightOn OCR Sample

This console sample reuses the shared Granite Docling assets to perform OCR-style extraction through the `ErgoX.GgufX` managed API. It reads images from the Granite Docling example, generates plain-text output, and writes the results alongside the Granite Markdown exports.

## Prerequisites

- Granite Docling model artifacts located in `examples/granite_docling/models/`:
  - `granite-docling-258M-Q8_0.gguf`
  - `mmproj-granite-docling-258M-Q8_0.gguf`
- Runtime binaries restored through the project reference (copied to `src/ErgoX.GgufX/runtimes/win-x64/`).
- Input screenshots present in `examples/granite_docling/io/input/`.

## Running the Sample

From the repository root run:

```powershell
dotnet run --project examples/light_on_ocr
```

The application will:

- Load the Granite Docling model via the managed GGUFx session wrapper.
- Enumerate all supported image files in `examples/granite_docling/io/input/`.
- Produce plain-text OCR output for each image.
- Write `.txt` files into `examples/granite_docling/io/output/` using the same file names.

## Output

Each processed document generates a `.txt` file beneath `examples/granite_docling/io/output/`. Existing files are overwritten.

## Troubleshooting

- **Missing models**: Ensure both `.gguf` artifacts exist under `examples/granite_docling/models/`.
- **No input images**: Add PNG/JPEG/BMP/WEBP assets into `examples/granite_docling/io/input/`.
- **Native runtime errors**: Rebuild native assets and recopy the DLLs into `src/ErgoX.GgufX/runtimes/win-x64/`.
