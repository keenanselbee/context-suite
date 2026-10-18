AVIF encoder bridge
===================

This is an isolated Windows x64, static CRT, still-image AVIF output engine.
It does not install, register, package, or change the application. The private
bridge source remains under `proprietary/src/ContextSuite.Avif.Native`; this
public directory contains the repeatable source and build recipes and ABI
tests. Existing AVIF input uses the separate HEIF decoder.


Pinned sources and license evidence
-----------------------------------

- libavif v1.4.2 official tag archive:
  `2B645287340BA5A631D268B551DC2D72BD73AC33335962DD36DCDB6D8366921D`
- AOM 3.15.1 source at commit `44d0a57786f432d933ff64b653347c66f4d0fa1d`,
  official Gitiles archive:
  `A8E348AD4F009B284BA9A4B5609E9D422A8403B074D38DF6E172F71D92994FD2`

The fetch URLs, exact archive byte lengths and SHA-256 pins live in
`Prepare-AvifSource.py`. That tool enforces archive identity and safe extraction,
and writes a per-file source inventory. `Build-AvifEngine.py` verifies the
complete extracted inventory and copied license/notice files against those
sources before compiling. Original license, patent, and upstream third-party
notice files are under `../../assets/third-party/avif/`. Their presence does not
assert that every upstream optional component is linked; it keeps the applicable
notice review conservative. No upstream source is patched.


Build and test
--------------

Use a fresh destination under the parent repository's `.codex-temp`. These
commands require Windows x64, Visual Studio 18 2026 C++ tools, CMake, NASM and
Perl. `--archives` may point to previously downloaded exact pinned archives;
omit it to fetch the official URLs. Quote actual absolute tool paths.

```powershell
python tools/avif-engine/Prepare-AvifSource.py --output .codex-temp/avif-source-review
python tools/avif-engine/Build-AvifEngine.py --sources .codex-temp/avif-source-review --output .codex-temp/avif-build-review --cmake '<cmake.exe>' --nasm '<nasm.exe>' --perl '<perl.exe>'
python tools/avif-engine/Test-AvifBridge.py .codex-temp/avif-build-review/bridge-build/Release/ContextSuite.Avif.Native.dll
```

The build disables AOM decoding, tools and test applications and uses libavif
with only AOM encoding. It writes one log per step, `inputs.json`, `progress.json`
and `results.json` in the isolated destination, including source/tool hashes and
the final DLL hash. Failed builds retain those logs for diagnosis. The runtime
inventory is the single `ContextSuite.Avif.Native.dll`; the static libraries
are build outputs, not sidecar DLLs. Build from a fresh destination for another
source-to-binary comparison. A matching source/archive pin alone is not a claim
that unrelated compiler versions produce byte-identical DLLs.


ABI and policy
--------------

`Encoder.h` is the private C ABI declaration. ABI version is 1, exposed by
`cs_avif_abi`. Input is tightly packed, straight RGBA8 with normalized sRGB
color and orientation. Width and height are at most 16,384 pixels and at most
40 million pixels together. Each ICC, TIFF EXIF, or XMP byte payload is at most
4 MiB. A successful encoded result is at most 128 MiB and belongs to the
caller until `cs_avif_release`. Metadata is passed through to libavif; caller
must normalize orientation and color before encoding. The bridge emits one
8-bit SDR YUV444/full-range AVIF image with sRGB CICP, quality 95, lossless
alpha, speed 6 and at most two codec threads. It does not emit grids,
transforms, gain maps, image sequences or animated output.

The callback is polled before processing, after RGB conversion and after the
blocking encode. Active cancellation and memory/time limits are enforced by
the owning worker's native job; the codec call itself is not preemptible through
this ABI. Status values match the HEIF bridge: success 0, bad argument 1,
invalid input 2, unsupported 3, resource limit 4, cancelled 5 and engine
failure 6. The managed adapter validates encoded content and retains final
publication ownership.
