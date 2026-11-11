# Third-party components

The optional KS Pro display connection uses System.IO.Ports 10.0.0 (Microsoft/.NET, MIT), covered by the .NET notice below. Its independently written wire implementation was informed by the protocol documentation in [giantorth/AZOM](https://github.com/giantorth/AZOM/tree/6f820a8dd373d51764600b42e5c922204f663743/docs/protocol), whose repository is GPL-3.0. DeltaTR does not bundle AZOM code or binaries. This experimental connection is not an official MOZA SDK display-text API. Dashboard artwork and procedural GIF animations are generated locally by DeltaTR.

SenseVoice recognition uses [SenseVoiceSmall (Alibaba / FunAudioLLM, now QwenAudio)](https://github.com/QwenAudio/SenseVoice) through the existing sherpa-onnx 2.0.1 CPU runtime. The unmodified int8 ONNX model and tokens come from [csukuangfj's conversion](https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17/tree/2365baeacb507f821a0c8120fcee3d484dba7a07), pinned to revision `2365baeacb507f821a0c8120fcee3d484dba7a07`; installer SHA-256 checks cover both files. The conversion's LICENSE refers to FunASR's model licensing terms, which are distinct from its MIT code license. See `licenses/SenseVoice-MODEL-LICENSE.txt`, `licenses/SenseVoice-CODE-LICENSE.txt` and `licenses/SenseVoice-conversion-LICENSE.txt` (retrieved 2026-11-07). The native layout declarations follow the sherpa-onnx v2.0.1 C header, covered by its included Apache-2.0 license.


The iRacing offline reference metadata is derived from [SVappsLAB/iRacingTelemetrySDK](https://github.com/SVappsLAB/iRacingTelemetrySDK/tree/9ae151b8b1acbd8b3a3eb4a69e15f8e83a5423e3), Copyright (C) 2024-2026 Scott Velez, Apache-2.0. DeltaTR extracts variable metadata and session leaf paths into `IRacingReference.json`, and adds its own labels, runtime discovery and availability checks. The original license and attribution are included in `licenses/iracing-reference-Apache-2.0.txt`.

The iRacing adapter uses [YamlDotNet 16.3.0](https://github.com/aaubry/YamlDotNet/tree/v16.3.0) to read SDK session metadata. Its MIT license is included in the licenses directory.

Zipformer recognition uses the win-x64 CPU runtime from [sherpa-onnx 2.0.1](https://github.com/k2-fsa/sherpa-onnx/tree/v2.0.1) (Apache-2.0), including Microsoft ONNX Runtime (MIT). The distributed [bilingual Zipformer model](https://huggingface.co/csukuangfj/sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20/tree/98590b7ed6443e77b714204da2757d75e1a642f4) is marked Apache-2.0 by its publisher. The model and token files are distributed unmodified. See the accompanying licenses directory.

DeltaTR uses System.Speech 10.0.0 (Microsoft/.NET, MIT), NAudio.WinMM 2.2.1 and NAudio.Core 2.2.1 (Mark Heath, MIT). Windows speech engines and voices are operating-system components and are not redistributed by this project.

- System.Speech: https://www.nuget.org/packages/System.Speech/10.0.0
- .NET license: https://github.com/dotnet/runtime/blob/v10.0.0/LICENSE.TXT
- NAudio: https://github.com/naudio/NAudio/tree/v2.2.1
- NAudio license: https://github.com/naudio/NAudio/blob/v2.2.1/license.txt

The following MIT permission and disclaimer apply to these components with their respective notices:

Copyright (c) .NET Foundation and Contributors. All rights reserved.

Copyright 2020 Mark Heath

Vortice.DirectInput and Vortice.DirectX 3.8.3, and Vortice.Mathematics 2.1.0: Copyright (c) Amer Koleci and Contributors. DirectInput includes work Copyright (c) 2010-2014 SharpDX - Alexandre Mutel. MIT license.

SharpGen.Runtime and SharpGen.Runtime.COM 2.4.2-beta: Copyright (c) 2010-2017 Alexandre Mutel, 2017-2023 Jeremy Koritzinsky, 2023-2024 Amer Koleci. MIT license.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
