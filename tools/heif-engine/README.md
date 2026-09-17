HEIC And AVIF Evaluation Tools
=============================

These are research and verification tools, not application engines. Follow the
[evaluation record](../../docs/heif-input-evaluation.md) for pinned sources,
retained command receipts and remaining acceptance. No production stage or menu
is changed by these tools.


Pinned decoder build
--------------------

`Build-HeifEngine.py` verifies the release archives and selected AOM files in
`source-inputs.json`, extracts fresh bounded source trees, applies the reviewed
NAL-error, color-signal and hidden-HDR patches, and builds libde265, libheif and the private
bridge. It retains archive/source/patch identities, command exits, logs, a local
SDK and a three-DLL `payload`. The build does not update application pins or
register/adopt the engine. The AOM static artifact is verified, not rebuilt.

`--libraries-only` builds just libheif/libde265 without reading the private
checkout. It is also used by the verified source-kit rebuild below.

```powershell
python -B tools/heif-engine/Build-HeifEngine.py `
  --sources '<repository scratch directory containing libheif.tar.gz and libde265.tar.gz>' `
  --aom '<repository scratch selected AOM artifact directory>' `
  --cmake '<Visual Studio CMake path>/cmake.exe'
```

The local patch preserves the decoder's NAL-size/image-size and allocation errors
instead of discarding them. It changes no limit or media transformation. Use the
generated bridge in `Test-HeifBridge.py`, `Test-HeifWorker.py` and
`Test-HeifLoader.py` before accepting new private binary pins. Source/license
redistribution, formal payload adoption and commercial clearance remain separate.

`Review-HeifDistribution.py prepare '<full candidate build>' --aom-source
'<pinned local AOM source ZIP>'` prepares local review archives. The `verify`
operation checks an existing bundle against fixed source/runtime identities,
exact membership and derived notices without extracting or executing it.
`Test-HeifDistribution.py '<bundle directory>'` exercises tampered bundles.
`Rebuild-HeifSource.py '<bundle directory>' --cmake '<Visual Studio CMake path>'`
verifies and rebuilds the extracted library-only source kit in fresh scratch.
All inputs and results stay under repository `.codex-temp`. See the
[distribution evidence](../../docs/heif-distribution.md) for exact identities,
commands, limitations and unresolved release decisions.

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
The current bridge ABI is version 3; it reports the primary item type and
underlying codec separately, plus original and effective color declarations.
Expanded fixture runs also exercise six malformed
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


Actual worker verification
--------------------------

`Test-HeifWorker.py --bridge '<pinned bridge DLL>' --grid-evidence '<passing
bridge evidence>' --fixture-evidence '<passing fixture evidence>'` builds and
copies the real worker and public contract host into a fresh `.codex-temp`
directory. Repeat `--fixture-evidence` for all three depths. Use the current
source-bound grid/fixture evidence; the script rejects changed inputs.

It tests the existing named-pipe and direct-command path, safe publication,
mixed results, matte preview, collision handling, changed-settings retry,
dispatch cancellation/death, malformed-grid recovery and missing/altered optional
decoder behavior. Sources and payloads are hashed before and after the run.
No production stage, installed app, Explorer registration or live license is
changed. The ordinary suite tests dispatch interruption; add `--stress-evidence`
as described below to test interruption during observed native work. Neither
suite establishes visible usability.

`Test-HeifInterop.py --inputs '<pinned upstream corpus>' --bridge '<pinned bridge>'`
verifies `interop-inputs.json` against local files, then builds/copies the real
worker and public contract host into fresh scratch. The manifest retains source
URLs/archive identities; the runner does not download or execute upstream tools.
It checks conversion/publication, typed refusals, same-worker recovery and source
preservation. The manifest separates supported files from intentional feature
and alpha-alignment refusals; the current color checkpoint resolves the earlier
pending-color cases. See the
[upstream record](../../docs/heif-interoperability.md) for results and limits.

`Test-HeifColor.py --bridge '<pinned bridge>' --fixture-evidence '<authored run>'`
creates and verifies missing/unspecified-color variants. Repeat fixture evidence
for all three depths. A copied private image host accepts `<new evidence root>
--heif-color <passing color evidence>` for output fidelity and HDR/copy checks.
Pass the same `--color-evidence` to the interoperability runner for actual-worker
publication and recovery. See [the color policy](../../docs/heif-color-fallback.md).

`Test-HeifLoader.py --bridge '<pinned bridge DLL>' --fixture '<authored HEIC>'`
builds the private image host and creates six disposable copies for valid,
preloaded-library, junction, hard-link, extra-library and missing-library cases.
Inputs must be under repository scratch. The preloaded case uses another copy
of the trusted libde265; all filesystem changes stay within those test copies.

`Build-HeifStress.py --encoder-sdk '<pinned fixture SDK>' --decoder-sdk '<pinned
decoder SDK>' --cmake '<Visual Studio CMake path>/cmake.exe'` builds `Stress` in
a fresh scratch directory, creates 3072-square 8-bit noise HEIC/AVIF files and
retains source/SDK/fixture identities and configure/build/generate receipts.
Add `--oversized-nal` for a separate 4096-square corpus whose HEIC exceeds the
decoder's 16 MiB NAL limit. Keep fixture encoders out of production payloads.
The evaluation document identifies passing retained generation receipts.

Pass that directory as `--stress-evidence '<passing stress evidence>'` to the
worker harness. After the ordinary worker suite, it observes the owned worker's
instruction pointer inside libde265/heif before delivering cancellation, death
or controlled deadline expiry. It records typed results, unchanged originals,
no partial publication and fresh-worker recovery for each codec/fault. The x64
context offsets are checked against the Windows SDK by the generator build.
This test briefly suspends only an identified thread in its own disposable worker.
Add `--limit-fixture '<oversized corpus>/fixtures/stress.heic'` to both the bridge
and worker harnesses to require a resource-limit result on repeated refusals,
with later ordinary inputs still working in the same native process.
