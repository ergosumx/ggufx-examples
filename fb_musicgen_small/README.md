# Facebook MusicGen Small Example

This example demonstrates how to use the `ErgoX.GgufX` library to run the Facebook MusicGen Small model for text-to-music generation.

## Overview

MusicGen is a text-to-music generation model that uses a Transformer-based decoder to predict audio tokens (codebooks) conditioned on a text prompt processed by a T5 encoder. The generated tokens are then decoded into audio waveforms using an EnCodec decoder.

## Architecture

1.  **Text Encoder (T5):** Encodes the input text prompt into a sequence of hidden states.
2.  **Decoder (Transformer):** Predicts 4 parallel streams of audio tokens (codebooks) using a "Delay Pattern".
3.  **Audio Decoder (EnCodec):** Converts the generated audio tokens into raw audio waveforms.

## Implementation Plan

The implementation is divided into the following steps:

1.  **Initialization:** Load the T5 Encoder and MusicGen Decoder ONNX models using `GgufxOnnxSession`.
2.  **Tokenization:** Tokenize the input text prompt using a T5 tokenizer.
3.  **Encoding:** Use `GgufxOnnxSession.Encode` to process the tokenized prompt.
4.  **Generation Loop (C#):**
    *   Initialize 4 parallel token streams.
    *   Implement the "Delay Pattern" where codebooks are fed into the decoder with specific offsets (0, -1, -2, -3).
    *   Call `GgufxOnnxSession.Eval` to run the decoder.
    *   Sample the next tokens for each codebook from the output logits.
5.  **Audio Decoding:** (Gap) Decode the generated tokens using EnCodec.

## Gap Analysis

### 1. EnCodec Support
**Current Status:** The `ErgoX.GgufX` library currently wraps `ggufx-lm` which focuses on Transformer-based language models. It does not natively support the EnCodec model required for the final audio decoding step.
**Workaround:** We need to either:
*   Export the EnCodec model to ONNX and run it using a standard `Microsoft.ML.OnnxRuntime` session (separate from `GgufxOnnxSession`).
*   Extend the native `ggufx-lm` library to support EnCodec.
*   For this example, we will focus on generating the *tokens* and leave the audio decoding as a placeholder or require an external script.

### 2. Complex Input Shapes
**Current Status:** The `ggufx_onnx_eval` function and `ggufx_onnx_batch` structure are optimized for standard LLM inputs (token, pos, seq_id). MusicGen's decoder expects multiple codebook inputs, often interleaved or as separate tensors.
**Risk:** If the native `ggufx_onnx_eval` hardcodes the input tensor names or shapes (e.g., expecting only `input_ids`), it might fail with MusicGen's ONNX model. We may need to modify the native `ggufx_onnx_eval` to be more generic or expose a way to set arbitrary input tensors.

### 3. Tokenizer
**Current Status:** We need a T5 tokenizer. The `falconsai` example likely uses a simple tokenizer or relies on the native one. We need to ensure we have a compatible tokenizer for MusicGen.

## Usage

```bash
dotnet run --project examples/fb_musicgen_small/fb_musicgen_small.csproj
```

## Requirements

*   `musicgen_small_encoder.onnx`
*   `musicgen_small_decoder.onnx`
*   `encodec_decoder.onnx` (Optional for now)
