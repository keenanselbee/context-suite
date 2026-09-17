HEIC And AVIF Evaluation Tools
=============================

These are research and verification tools, not application engines. Follow the
[evaluation record](../../docs/heif-input-evaluation.md) for pinned sources,
retained command receipts and remaining acceptance. No production stage or menu
is changed by these tools.

`Fixtures` creates twenty-four independently authored 64-by-48 patterns:
all eight orientations, alpha stripes and a rotated alpha image with EXIF/XMP,
for both HEIC and AVIF, plus full-range BT.709 matrix color patterns with sRGB
transfer characteristics. The fixture-only libheif build requires x265 and AOM
encoders. x265 is never copied into the decoder/application candidate. Lossless
encoding, full-range NCLX and 4:4:4 samples make pixel expectations explicit.

`Probe` requires the separate decoder-only libheif build. It rejects registered
HEVC/AV1 encoders, decodes strict eight-bit RGBA research output and writes raw
pixels and bounded metadata into a new output directory. This is not a production
admission policy; it does not establish HDR, higher-depth or collection support.
Its library allocation limits do not replace Windows job/deadline controls.

`Test-HeifFixtures.py` builds both tools in a fresh repository scratch directory,
copies each tool's selected DLLs, creates the authored files and calculates
expected transformed pixels independently. It verifies geometry, alpha, metadata,
truncated/oversized input refusals and source/SDK/fixture identities. Each command,
exit and log is retained, including failures. A passing result applies only to
the recorded fixture/engine versions, not general format support.

```powershell
python -B tools/heif-engine/Test-HeifFixtures.py `
  --decoder-sdk '<repository-local evaluation>/sdk' `
  --encoder-sdk '<repository-local evaluation>/generator-sdk' `
  --cmake '<Visual Studio CMake path>/cmake.exe'
```

Both SDKs must be under this repository's `.codex-temp` and must be separate.
The caller prepares them from the recorded sources; the harness does not download
or select a replacement engine. Use Visual Studio 18 x64. Match the static CRT
setting across fixture dependencies: x265 uses its own `STATIC_LINK_CRT=ON`
option. Its installed library is `x265-static.lib`, which must be supplied
explicitly as `X265_LIBRARY` to libheif's configuration. Retain the distinct
decoder SDK and its binary hashes while preparing the generator SDK.

Fixture generation currently covers eight-bit grayscale/color patterns and partial
alpha. Other color interpretations/ICC, higher precision, HDR, auxiliary images, grids, collections,
JPEG/PNG publication and application recovery remain separate required work.
Do not redistribute these native builds or upstream example fixtures as part
of the application without their separate source/license and payload review.
