DDS Analysis And 2D Conversion Goal
==================================

Status: completed bounded local implementation and acceptance; commercial release gates remain separate

Implement [decision 0011](decisions/0011-dds-engine-and-texture-policies.md) without
regressing the existing five-format conversion and safe-publication behavior.

Acceptance checklist
--------------------

- [x] Public bounded DDS parser and read-only file analysis, with raw unknown
  identifiers, declared-versus-unknown color semantics, texture structure and
  checked payload/mip accounting. Independent malformed and valid fixtures.
- [x] Analyzer application surface for single/mixed selections, per-file errors,
  cancellation and no trial consumption or source writes.
- [x] Pinned CPU DirectXTex bridge, native build recipe, loaded identity and
  deployment/notices gates. No new codec dependency in WPF or Explorer.
- [x] Typed 2D conversion plans, color/interpretation/channel/alpha and mip choices,
  safe rejection of unsupported structures and precise no-op behavior.
- [x] BC1-BC5/BC7 and selected uncompressed encode/decode contracts, independent
  header/payload validation, exact lossless fixtures and bounded lossy metrics.
- [x] Mip preservation/regeneration/removal tests, linear-light and alpha-aware
  filtering, normals and cutout coverage tests before exposing these options.
- [x] DDS planner/previews and trial-gated worker execution through the existing
  app-owned publisher; mixed batches, collisions, interruption and source safety.
- [x] Packaging/regression checks, focused desktop evidence and current docs.

BC6H/HDR and cube/array/volume conversion remain separate follow-ups. Analysis
is not a claim of conversion support. Record partial progress honestly; completion
requires all enabled paths to pass their applicable tests. No publishing, commits,
installation, real-trial reset or live Polar changes are part of this goal.

Current evidence (2026-09-07)
-----------------------------

- `tools/Test-DdsCodec.ps1 -SkipNativeBuild`: 121 contracts passed, including
  all 18 output representations, independent BC1/BC5 signed blocks, gradient
  error bounds, authored mips, color versus declaration changes, cutout coverage,
  normal-vector filtering, all five ordinary-image inputs and selected-mip PNG export.
  Evidence: `.codex-temp/dds-tests/codec-21826d42d3524dbb9d6f0e02e21792d6`.
- `tools/Test-Foundation.ps1 -Configuration Release -Integration`: 413 passed. Includes DDS view-model
  consent/preview, actual worker IPC, collision-safe named output, trial expiry,
  malformed input and active 4096x4096 conversion-pipeline cancellation/crash/timeout.
  The interruption test observes worker CPU and its exclusive output reservation;
  it does not prove which individual native instruction was running at the fault.
- The subsequent bounded-parser regression run passes 315 public foundation
  contracts, including exact retention of unknown alpha-mode flag bits. The
  latest safety review also covers no-op/reinterpret BC1 alpha, negative-Z normal
  rejection and packed-data loss through transparent BC1 blocks.
- `tools/Test-ImageConversion.ps1`: 339 existing engine/adapter regressions passed.
  Evidence: `.codex-temp/image-tests/engine-631edd96373d4af9b1efdd1d7bf2414b`.
- Development build passes with no compiler warnings. DDS native/identity/license
  checks and the production file allowlist are integrated. Nine packaging checks
  passed, including altered DDS native/identity/license rejection; evidence:
  `.codex-temp/production-packaging-5e0d27edee154549b05ae0dc42b913e4`.
  Updated desktop smoke passes all 33 checks on the idle unlocked desktop.

`tools/Test-DesktopSmoke.ps1 -Configuration Release -Images` passes 33 checks.
Evidence: `.codex-temp/desktop-smoke/images-883ea7a9ede344d6b640bfce8519623c`.
This run verifies explicit DDS interpretation, actual encoded-preview gating and
invalidation after changing storage, alongside all previous image UI checks.
The DDS-controls and shared before/after screenshots were visually reviewed in
the current dark theme. DDS encoded-pixel correctness is additionally covered
by adapter and real-worker tests. This is focused local acceptance, not an
accessibility certification, all-DPI/theme review or commercial release approval.

Implementation limits
---------------------

Only ordinary 2D typed non-HDR DDS is converted; no silent structure flattening,
premultiplied-alpha interpretation, typeless casting, resize, or numeric PNG export.
Color source interpretation is explicit unless DDS declares sRGB. Ordinary color
images enter as sRGB after ICC transformation; numeric input with ICC is rejected.
Profiles/descriptive metadata require explicit removal for DDS. Mip generation
uses area footprints within the raster, so clamp/wrap are equivalent for this
filter; no misleading edge toggle is exposed. Signed preview channels map -1..1
to 0..1 for display, not a material rendering. No-op compressed pixels are retained;
copy publication still creates the requested output. Conversion performance on
large BC7 textures is not yet a benchmark-backed promise.
BC5 normal conversion assumes positive-Z reconstruction: RGB negative-Z normals
and one-channel normal inputs are rejected rather than inventing/flipping a
direction. Explicit Data conversion remains available for uninterpreted channel
storage. BC1 data output requires an all-opaque fourth channel because transparent
BC1 blocks also discard RGB data; use BC3/BC7 when the fourth channel carries data.
Compressed-data precision loss is disclosed before execution.

Build and distribution
----------------------

Run `tools/dds-engine/Build-DdsEngine.ps1` with the private checkout and Visual
Studio C++ tools, then the normal production build. Upstream DirectXTex is pinned
to mar2026 revision `5ddca91f9ad07308957bd9d4dffe204914a98acb`; downloaded
source archives are SHA256 checked. The local identity records the native binary,
private bridge source, CMake definition and MIT notice hashes. These development
provenance checks are not a signed/reviewed release artifact approval.

The inspected bridge imports Windows Kernel32/OLE32 and the Microsoft C++/UCRT
runtime; no GPU DLL or external image library is a direct import. Static code
inventory and Microsoft runtime/tooling eligibility still need the separate
[redistribution review](release-redistribution.md). No runtime installer is bundled.
