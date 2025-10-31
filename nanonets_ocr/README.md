# Nanonets OCR Sample

This console sample demonstrates how to drive the Nanonets OCR multimodal model through the managed `ErgoX.VecraX.GgufX` library. It converts documents to Markdown using local IO folders and the shared runtime payload.

## Prerequisites

- Place the model assets in `examples/nanonets_ocr/model/`:
  - `Nanonets-OCR-s-UD-Q6_K_XL.gguf`
  - `mmproj-F16.gguf`
- Ensure the GGUFx runtime DLLs exist under `src/ErgoX.VecraX.GgufX/runtimes/win-x64/` (handled by the library project).
- Add page images into `examples/nanonets_ocr/io/input/`.

## Running

```powershell
dotnet run --project examples/nanonets_ocr
```

The application:

- Loads the Nanonets OCR model through `GgufxMultimodalSession`.
- Enumerates images in `examples/nanonets_ocr/io/input/`.
- Issues the "Convert to Markdown." prompt for each file.
- Writes Markdown responses into `examples/nanonets_ocr/io/output/`.

## Output

Each input image generates a Markdown file with the same base name. Existing files are overwritten on rerun.

## Troubleshooting

- **Model missing**: Confirm both `.gguf` files are present in the `model/` folder.
- **No input media**: Add PNG/JPEG/BMP/WEBP images to `io/input/`.
- **Native errors**: Rebuild native assets and ensure runtimes were copied to `src/ErgoX.VecraX.GgufX/runtimes/win-x64/`.
