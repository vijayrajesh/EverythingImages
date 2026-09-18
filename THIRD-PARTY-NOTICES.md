# Third-party notices

The released packages (not this repository) include the AI engine in
`ai-engine\`, fetched by `scripts\fetch-ai-engine.ps1`:

| Component | License | Source |
| --- | --- | --- |
| llama.cpp and ggml (Vulkan build b10909) | MIT | https://github.com/ggml-org/llama.cpp |
| LLVM OpenMP runtime (`libomp.dll`) | Apache-2.0 with LLVM exceptions, text in `ai-engine\LICENSE-LLVM-OpenMP` | https://github.com/llvm/llvm-project |

The AI models are not included. The app downloads Liquid AI's vision models
from Hugging Face when you ask it to. Each model comes under Liquid AI's own
license, shown on its Hugging Face page (https://huggingface.co/LiquidAI).
Read it before you use a model, especially for commercial use.

The Windows OCR and imaging APIs are part of Windows.
