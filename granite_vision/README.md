# Granite Vision Sample

This console sample demonstrates how to combine the Granite Vision 3.3 2B multimodal model with the managed `ErgoX.GgufX` API to generate TeX-focused documentation for scanned documents. Each run produces a narrative summary, an ASCII pipeline, and a Mermaid diagram describing the data flow.

## Prerequisites

- Model artifacts placed in `examples/granite_vision/model/`:
  - `granite-vision-3.3-2b-Q8_0.gguf`
  - `mmproj-model-f16.gguf`
- Prompt template located at `examples/granite_vision/template.jinja` (requires `UseJinja = true`).
- Input screenshots stored in `examples/_io/1024/` (shared with other samples).
- Runtime DLLs restored through the project reference to `src/ErgoX.GgufX`.

## Running the Sample

From the repository root execute:

```powershell
dotnet run --project examples/granite_vision
```

The application will:

- Load the Granite Vision model and projector using the managed multimodal session wrapper.
- Apply the bundled Jinja chat template to format requests.
- Enumerate supported images from `examples/_io/1024/`.
- Ask the model to return a prose walkthrough, ASCII pipeline, and Mermaid flowchart for each image.
- Write Markdown outputs into `examples/granite_vision/io/output/`.

## Prompt

The sample issues the following instruction to encourage diagram-rich responses:

```
Explain the data processing pipeline that ingests a scanned image, converts the content into TeX using the Granite Vision model, and summarizes the flow with both an ASCII diagram and a Mermaid graph.
```

## Sample Output

### ASCII Pipeline

```
+--------------------+    +---------------------------+    +--------------------------+    +-------------------------+
| 1. Document Image  | -> | 2. Granite Vision Encoder | -> | 3. Token to TeX Builder  | -> | 4. TeX Compiler (PDF)  |
+--------------------+    +---------------------------+    +--------------------------+    +-------------------------+
                                                                                                 |
                                                                                                 v
                                                                                      +-----------------------+
                                                                                      | 5. QA & Post-Process  |
                                                                                      +-----------------------+
```

### Mermaid Graph

```mermaid
flowchart LR
    scan[Document Image]
    scan --> vision[Granite Vision 3.3 2B\nMultimodal Session]
    vision --> tex[Structured Tokens\n(TeX + Markdown)]
    tex --> compiler[pdflatex / tectonic]
    compiler --> qa[QA Review & Publishing]
```

## Troubleshooting

- **Model missing**: Confirm both `.gguf` files exist in `examples/granite_vision/model/`.
- **Template not applied**: Ensure the console app can read `template.jinja` and that `UseJinja` remains enabled.
- **Diagrams omitted**: Re-run with the bundled prompt to ensure ASCII and Mermaid sections are requested explicitly.
