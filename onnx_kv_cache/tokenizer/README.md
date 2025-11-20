---
base_model: t5-small
library_name: transformers.js
---

https://huggingface.co/t5-small with ONNX weights to be compatible with Transformers.js.

## Usage (Transformers.js)

If you haven't already, you can install the [Transformers.js](https://huggingface.co/docs/transformers.js) JavaScript library from [NPM](https://www.npmjs.com/package/@huggingface/transformers) using:
```bash
npm i @huggingface/transformers
```

**Example:** Text-to-text generation.

```js
import { pipeline } from '@huggingface/transformers';

const generator = await pipeline('text2text-generation', 'Xenova/t5-small');
const output = await generator('how can I become more healthy?', {
  max_new_tokens: 100,
});
```

Note: Having a separate repo for ONNX weights is intended to be a temporary solution until WebML gains more traction. If you would like to make your models web-ready, we recommend converting to ONNX using [🤗 Optimum](https://huggingface.co/docs/optimum/index) and structuring your repo like this one (with ONNX weights located in a subfolder named `onnx`).