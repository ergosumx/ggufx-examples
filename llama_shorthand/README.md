# LLaMA Shorthand

This sample demonstrates how to call the exported `llama_mtmd_cli_main` entry point from the native `ggufx-mtmd-cli` runtime using a minimal .NET console app. The program loads the prebuilt runtime found under `runtimes/windows-x64/native`, prepares the required CLI arguments, and invokes the native main method.

## Prerequisites

- Windows x64 host with .NET 8.0 SDK installed.
- The native runtime binaries published under `runtimes/windows-x64/native`.
- The Granite Docling models downloaded to `.models/granite-docling-258M-GGUF`.

## Running the sample

From the repository root:

```powershell
cd examples/llama_shorthand
dotnet run
```

The sample will call into the native CLI with the Granite Docling vision models and convert the default image to Markdown using the prompt `Convert to Markdown`.
