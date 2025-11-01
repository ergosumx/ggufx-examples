# Tiny LLaMA Chat Demo

This console sample showcases the `GgufxTextChatEndpoint` in two modes:

1. **Summarisation with Streaming Tokens** – streams tokens to the console while the model produces a concise summary.
2. **Debate Simulation** – spins up two independent chat instances that debate the proposition *"Advanced AI will ultimately benefit humanity"* for ten exchanges.

## Prerequisites

1. Download the TinyLLaMA chat model `tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf` (available from the GGUF community mirrors).
2. Copy the model into `examples/tiny_llama_chat/model/` so the final path matches:
   ```text
   examples/tiny_llama_chat/model/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf
   ```
3. Ensure the native GGUFx runtimes for your platform are present under `src/ErgoX.GgufX/runtimes/` (the library project already ships them for Windows x64).

## Running the Sample

```powershell
cd examples/tiny_llama_chat
dotnet run
```

The program will report the model path, print streamed tokens for the summariser, and then alternate pro/contra debate responses for ten rounds. All native handles are disposed automatically when the sample exits.
