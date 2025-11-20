# ONNX KV Cache Example

This example demonstrates how to use the `onnx-kv` plugin to run inference with ONNX models using `llama.cpp`'s KV cache management.

## Prerequisites

1.  **ONNX Runtime**: You must have ONNX Runtime installed and configured in your build environment.
    *   Set `ONNXRUNTIME_DIR` CMake variable to your ONNX Runtime installation directory when building the native plugins.
2.  **Models**: Place your ONNX models in the `model` directory:
    *   `model/encoder_model_q4f16.onnx`
    *   `model/decoder_model_merged_q4f16.onnx`

## Building

1.  Build the native plugins with ONNX Runtime support:
    ```bash
    cmake -S vecrax -B build -DONNXRUNTIME_DIR=/path/to/onnxruntime
    cmake --build build --config Release
    ```
2.  Build the C# solution:
    ```bash
    dotnet build ErgoX.GgufX.sln
    ```

## Running

```bash
dotnet run --project examples/onnx_kv_cache/onnx_kv_cache.csproj
```
