# Nanonets OCR Example

This example demonstrates optical character recognition (OCR) using the Nanonets OCR model with multi-modal vision capabilities.

## Model

- **Model**: Nanonets-OCR-s-UD-Q6_K_XL.gguf
- **Projector**: mmproj-F16.gguf
- **Source**: Nanonets OCR specialized model for document text extraction

## Requirements

1. Download the Nanonets OCR model files:
   ```bash
   # Download from Hugging Face or model source
   # Place files in: .models/Nanonets-OCR-s-GGUF/
   ```

2. Add document images to process:
   ```bash
   # Place images in: examples/Nanonets/Inputs/
   ```

## Usage

Run the example from the repository root:

```bash
dotnet run --project examples/Nanonets
```

The example will:
1. Load the Nanonets OCR model with Vulkan GPU acceleration
2. Process all images in the `Inputs/` directory
3. Extract text using vision-based OCR
4. Save results to `Output/` directory as `.txt` files

## Output

Each processed image produces a text file containing:
- Extracted text from the document
- Processing metadata (duration, token count)
- Model capabilities information

## Configuration

The example uses:
- **Context Length**: 2048 tokens
- **Batch Size**: 128 tokens
- **Max Generation**: 1024 tokens
- **Temperature**: 0.2 (precise OCR)
- **Backend**: Vulkan GPU acceleration

## Performance

With GPU acceleration (Vulkan):
- Model layers offloaded to GPU for faster processing
- Typical processing time: ~10-30 seconds per image (depends on GPU)
- Memory usage: ~2-4GB VRAM

## Troubleshooting

If you encounter issues:

1. **Model not found**: Ensure model files are in `.models/Nanonets-OCR-s-GGUF/`
2. **No images**: Add images to `examples/Nanonets/Inputs/`
3. **GPU errors**: Falls back to CPU if Vulkan unavailable
4. **Out of memory**: Reduce context length or batch size
