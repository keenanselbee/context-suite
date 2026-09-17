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
HEVC/AV1 encoders, decodes strict 8/10/12-bit RGBA research output and writes raw
pixels and bounded metadata into a new output directory. This is not a production
admission policy; it does not establish HDR or collection support. Higher-depth
samples use little-endian 16-bit storage with their original 10/12-bit range,
not values normalized to 65535. The probe rejects changed sample precision.
Its library allocation limits do not replace Windows job/deadline controls.
NCLX passthrough is explicitly enabled: a null output profile without that flag
requests the library's implicit sRGB conversion. The probe records source and
decoded NCLX separately, retains source ICC bytes and checks decoded ICC equality.
The selected engine currently returns no NCLX on its interleaved RGB output;
callers must carry the source declaration alongside the verified samples.

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
  --cmake '<Visual Studio CMake path>/cmake.exe' `
  --bit-depth 8
```

Add `--profile-assembly '<pinned local stage>/Magick.NET.Core.dll'` to run 42
fixtures per depth. A scratch .NET 10 helper exports the existing managed
library's sRGB and Adobe RGB profiles, without loading the image native engine.
The additional cases cover BT.709 transfer, Display P3 primaries, PQ/HLG signals,
ICC with NCLX, two-image collections and thumbnails. Assembly, helper and
generated profile identities join the run receipts. This verifies color-sample
and profile transport, not final display appearance or HDR conversion support.

Add `--variants` with `--profile-assembly` for 62 cases per depth, including
4:2:0/4:2:2, limited range, BT.601/BT.2020 matrices and four-tile grids with
alpha/orientation. The grid generator uses the incremental API so orientation
applies to the complete image. Expected pixels come from independent range,
matrix and geometry arithmetic, with at most one source-depth value of rounding
difference and exact alpha. Tile patterns differ to detect incorrect placement.

Both SDKs must be under this repository's `.codex-temp` and must be separate.
The caller prepares them from the recorded sources; the harness does not download
or select a replacement engine. Use Visual Studio 18 x64. Match the static CRT
setting across fixture dependencies: x265 uses its own `STATIC_LINK_CRT=ON`
option. Its installed library is `x265-static.lib`, which must be supplied
explicitly as `X265_LIBRARY` to libheif's configuration. Retain the distinct
decoder SDK and its binary hashes while preparing the generator SDK.

Run each of `--bit-depth 8`, `10` and `12` for the full precision matrix. The
10/12-bit HEIC fixtures require an x265 multidepth fixture SDK; retain separate
build receipts for each depth and the combined static library. Higher-depth
patterns include low-bit detail to detect an accidental eight-bit intermediate.
The harness compares sample values, including exact alpha, rather than merely
checking the declared depth or 16-bit storage size.

Fixture generation covers 8/10/12-bit grayscale/color patterns and partial
alpha. Final ICC/NCLX color transforms, HDR admission, auxiliary images,
collection admission, JPEG/PNG publication and application recovery remain
separate from this decoder-fixture test.
Do not redistribute these native builds or upstream example fixtures as part
of the application without their separate source/license and payload review.


Private bridge verification
---------------------------

The private `src/ContextSuite.Heif.Native` project exposes an in-memory decode
ABI. Build it with Visual Studio 18 x64 and `CMAKE_PREFIX_PATH` pointing to the
pinned decoder SDK; place that SDK's `heif.dll` and `libde265.dll` beside the
result inside a fresh `.codex-temp` build. No production stage is required.

```powershell
python -B tools/heif-engine/Test-HeifBridge.py `
  --bridge '<repository-local scratch>/ContextSuite.Heif.Native.dll' `
  --fixture-evidence '<repository-local scratch>/heif-fixtures/<8-bit run>' `
  --fixture-evidence '<repository-local scratch>/heif-fixtures/<10-bit run>' `
  --fixture-evidence '<repository-local scratch>/heif-fixtures/<12-bit run>'
```

Use passing expanded fixture runs from this harness, with their inputs unchanged.
The bridge verifier checks normalized Q16 pixels against the independently
calculated fixture results, source color declarations, item flags, exact profiles,
buffer bounds, refusals, cancellation and subsequent decoding. It hashes private
source identities without copying private source into public outputs. The bridge
reports capabilities; it is not a conversion admission policy, and this test does
not validate final JPEG/PNG output, publication or application recovery.
The current bridge ABI is version 2; it reports the primary item type and
underlying codec separately. Expanded fixture runs also exercise six malformed
grid mutations per codec/depth, with a valid grid decode after each refusal.

The private image contract host also accepts a new absolute evidence directory,
`--heif-conversion` and one or more expanded fixture-evidence directories. Build
the ordinary private image tests, copy their output into fresh repository scratch
and place the three pinned decoder DLLs in that copy's `heif` subdirectory before
running. This exercises the actual adapter and existing output validation without
refreshing a production stage or installed application. Keep the exact host
inventory and command/exit receipts with the result.
Append `--grid-refusals '<passing bridge evidence directory>'` to verify those
same malformed-grid refusals and subsequent recovery through the managed adapter.

`ColorProfiles` builds the offline BT.709/P3 ICC generator against the selected
curated-engine artifact directory (`LCMS_ARTIFACTS`). Its generated resources are
tracked with the private implementation, not published from these tools. A repeat
generation must match the embedded profiles byte-for-byte. See the evaluation
record for identities and the independent appearance checks; the generator adds
no runtime dependency or machine installation.
