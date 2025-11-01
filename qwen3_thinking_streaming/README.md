# Qwen3 Thinking Streaming Demo

This sample demonstrates how to stream a reasoning-capable model (Qwen3 "Thinking") using `GgufxTextChatEndpoint`. The app separates the model's `<think>...</think>` reasoning trace from the final answer and renders both live in the console.

## Prerequisites

1. Download the thinking checkpoint `Qwen3-4B-Thinking-2507-Q4_K_M.gguf`.
2. Copy it to `examples/qwen3_thinking_streaming/model/` so that the final path is:
   ```text
   examples/qwen3_thinking_streaming/model/Qwen3-4B-Thinking-2507-Q4_K_M.gguf
   ```
3. Ensure the GGUFx native runtimes for your platform are available under `src/ErgoX.GgufX/runtimes/`.

## Running the Sample

```powershell
cd examples/qwen3_thinking_streaming
dotnet run
```

The app prints the prompt, streams the `<think>` block in dark yellow, and then streams the assistant's final answer in cyan. At the end it also prints the sanitized assistant reply with reasoning removed.
