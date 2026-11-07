# Third-party notices / 제3자 고지

GAFIL is released under the MIT License (see `LICENSE`). The GAFIL executable bundles the components below.
GAFIL은 MIT 라이선스로 배포됩니다. GAFIL 실행 파일에는 아래 구성 요소가 함께 들어 있습니다.

| Component | License | Source |
|---|---|---|
| .NET Runtime 8 (incl. WPF, Windows Forms) | MIT | https://github.com/dotnet/runtime · https://github.com/dotnet/wpf · https://github.com/dotnet/winforms |
| System.Diagnostics.PerformanceCounter | MIT | https://github.com/dotnet/runtime |
| System.Management | MIT | https://github.com/dotnet/runtime |
| System.Drawing.Common | MIT | https://github.com/dotnet/winforms |

Copyright (c) .NET Foundation and Contributors. Licensed under the MIT License:

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Not bundled / 함께 들어 있지 않은 것

- **Claude Code** is Anthropic's software. GAFIL does not include or redistribute it; users install it themselves from Anthropic (the setup helper runs Anthropic's official installer). Its use is governed by Anthropic's terms.
  **Claude Code**는 Anthropic의 소프트웨어이며 GAFIL에 들어 있지 않습니다. 사용자가 Anthropic에서 직접 설치하고(시작 도우미는 공식 설치 명령을 실행할 뿐), Anthropic 약관을 따릅니다.
- **NVIDIA NVML** (`nvml.dll`) is used only if it is already installed with the NVIDIA driver; it is not shipped with GAFIL.
  **NVIDIA NVML**은 NVIDIA 드라이버와 함께 이미 설치된 경우에만 사용하며, GAFIL에 들어 있지 않습니다.
- Fonts (Malgun Gothic, Segoe UI, Segoe Fluent Icons) are Windows system fonts and are not shipped with GAFIL.
  글꼴은 Windows에 들어 있는 것을 쓰며 GAFIL에 들어 있지 않습니다.

## Original assets / 직접 만든 것

The pixel cat, outfits, icon and meow sound (synthesized in code) were made for this project and are covered by GAFIL's MIT License. The cat is not an official Anthropic character.
픽셀 고양이, 옷·소품, 아이콘, 야옹 소리(코드로 합성)는 이 프로젝트를 위해 직접 만들었으며 GAFIL의 MIT 라이선스를 따릅니다. 고양이는 Anthropic의 공식 캐릭터가 아닙니다.

## Trademarks / 상표

"Claude" and "Anthropic" are trademarks of Anthropic, PBC. GAFIL is an unofficial project and is not affiliated with, endorsed by, or sponsored by Anthropic. These names are used only to describe compatibility.
"Claude"와 "Anthropic"은 Anthropic, PBC의 상표입니다. GAFIL은 Anthropic과 관련이 없고 Anthropic이 승인하거나 후원하지 않는 비공식 프로젝트이며, 이 이름들은 호환성을 설명하는 데에만 씁니다.
