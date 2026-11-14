# Third-party components

## DeepFilterNet

Upstream: https://github.com/Rikorose/DeepFilterNet

Copyright (c) 2021 Hendrik Schröter.
Upstream offers MIT or Apache-2.0, at the recipient's option. Full upstream texts are included here.

The original `enc.onnx`, `erb_dec.onnx`, and `df_dec.onnx` come from the DeepFilterNet3 ONNX model archive. The `.state.onnx` variants expose GRU state inputs and outputs. `enc.gru-x-trace.onnx` exposes the encoder GRU input. `enc.gru-x-injected.onnx` accepts an explicit GRU input. CLEARCAST makes these interface modifications for bounded temporal inference, retaining the original weights. Exact artifact hashes are in `models/model-hashes.json`.

The analysis, normalization, and filtering implementation follows the upstream DSP contracts, with a C# implementation and CLEARCAST synthesis gain/limiting behavior. These files retain upstream attribution through this notice and the included license files.

## ONNX Runtime

Upstream version: https://github.com/microsoft/onnxruntime/tree/v1.22.0

Copyright (c) Microsoft Corporation. MIT license; see ONNXRuntime-LICENSE.txt. The source project restores the NuGet package, rather than checking runtime binaries into Git. Runtime third-party notices are included for packaged distributions.

## VB-CABLE and .NET

Installed separately from their respective vendors. Their installers are not included in this repository or package.
