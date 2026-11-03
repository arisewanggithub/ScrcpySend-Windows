# Third-party dependencies

Dependencies are restored through NuGet; their upstream licenses and notices
apply to redistributions. No Android capture backend is bundled.
`tools/Publish.ps1` preserves package license/notice files in the portable
distribution, plus package manifests with declared license expressions/URLs
where license text is not included upstream.
`THIRD_PARTY_NOTICES.txt` includes the MIT notices for Vortice and
SharpGen packages that declare their licenses by expression.

| Dependency | Use | License/source |
| --- | --- | --- |
| Windows App SDK 2.6.4 / WinUI 3 | Native desktop UI/runtime | [Microsoft Windows App SDK](https://github.com/microsoft/WindowsAppSDK) and accompanying package terms/notices |
| .NET 10 | Managed runtime and SDK | [MIT and third-party notices](https://github.com/dotnet/runtime) |
| Vortice.Direct3D11 / Vortice.MediaFoundation 3.8.3 and transitive Vortice libraries | Typed Direct3D/DXGI/Media Foundation interop | [MIT](https://github.com/amerkoleci/Vortice.Windows) |
| SharpGen.Runtime and transitive SharpGen packages | Native interop runtime used by Vortice | [MIT](https://github.com/SharpGenTools/SharpGenTools) |
| FFmpeg.AutoGen 7.1.1 | Typed FFmpeg native bindings | [LGPL-3.0](https://github.com/Ruslan-B/FFmpeg.AutoGen) and accompanying NuGet terms |
| FFmpeg 7.1.1 ARM64/x64 shared LGPL builds, release ffmpeg-7.1.1-r2 | Standard fragmented MP4 muxing / AAC-LC encoding | [FFmpeg LGPL licensing](https://ffmpeg.org/legal.html), [exact prebuilt release and build sources](https://github.com/System233/ffmpeg-msvc-prebuilt/releases/tag/ffmpeg-7.1.1-r2) |
| libsrt 1.5.7, ARM64/x64, encryption disabled | SRT transport | [MPL-2.0](https://github.com/Haivision/srt/tree/v1.5.7); matching source ZIP and license bundled |
| Windows SDK BuildTools / Windows SDK .NET projections | Windows API build tools/projections | Accompanying Microsoft NuGet package license terms |
| xUnit / Microsoft.NET.Test.Sdk | Development tests only | Respective NuGet package license terms; not app dependencies |

Hardware video encoding is performed by the installed Windows hardware encoder
MFT/driver, not by a bundled software codec. Distribution licensing for hardware,
drivers and codec patents is separate from the open-source interop bindings.

`tools/PrepareSrt.ps1` verifies SHA256 for every native dependency/tool archive.
The native distribution includes the upstream FFmpeg license, copyright and
exact dependency SPDX/build metadata; source/build references are preserved
below. FFmpeg is dynamically linked and its DLLs can be replaced by compatible
user-built versions. No FFmpeg software video encoder is used by this app.

- FFmpeg source: https://github.com/FFmpeg/FFmpeg/tree/n7.1.1
- FFmpeg prebuilt build scripts/source provenance: https://github.com/System233/ffmpeg-msvc-prebuilt/tree/ffmpeg-7.1.1-r2
- Bundled static dependency source/version URLs: `native/win-{arm64,x64}/FFmpeg-dependencies.spdx.json`
- libsrt exact source: `native/win-{arm64,x64}/SRT-1.5.7-source.zip`
- Native toolchain: Zig 0.14.1, CMake 3.31.6, Ninja 1.13.1 (build tools only).
