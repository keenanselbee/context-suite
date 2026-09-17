HEIC And AVIF Input Evaluation
=============================

Status: isolated native builds and 24 authored eight-bit decoder fixtures pass;
broader fidelity and integration remain open. No production conversion or menu
capability enabled.
Owner selected HEIC/AVIF to JPEG/PNG on 2026-09-16.

The existing curated Q16 engine deliberately excludes HEIF and AV1 delegates.
Its configuration and linker allowlist in
[Test-CuratedPayload.ps1](../tools/curated-engine/Test-CuratedPayload.ps1) admit
JPEG, PNG, WebP, color management and XML only. A menu extension alone cannot
provide the new conversions. Keep the existing selected engine and 1.1.1 stage
unchanged while evaluating the additional decoder path.


Candidate inputs
----------------

The first evaluation uses independently acquired upstream sources, not local
reference binaries. Sources and generated builds stay under
`.codex-temp/heif-evaluation/ae6596628237435b9d2259d652f8dcb6`.

| Component | Input and identity | Evaluation role |
| --- | --- | --- |
| libheif | [1.23.4 release](https://github.com/strukturag/libheif/releases/tag/v1.23.4), archive SHA-256 `D0C02B4B0E978F34A1974B6F3EEA7975A537BF7A9195FFEEA38E7242FF316FDD` | HEIF/AVIF container, metadata and pixel API; shared-library build |
| libde265 | [1.1.3 release](https://github.com/strukturag/libde265/releases/tag/v1.1.3), archive SHA-256 `554228BD17788C99A7E63B37AB5634722190E6E2BF60C1DCB01CEF328E133905` | HEVC decoder; independently built shared library |
| libaom | ImageMagick dependency artifact `2026.09.01.0503`, revision `79de389e4abced53cb6e85b7574826dbc227cd9c`, resource version 3.15.0 | AV1 decoder; selected headers/static library from the retained, hash-verified upstream dependency archive |

The dependency archive SHA-256 is
`28BF8CF9CD10DCDA5F7ED0018E8D708286C88A5D790618942D406199EB645B68`.
Individual selected files are hashed in `aom-inputs.json`; the upstream
dependency source-pin script is retained alongside it. This does not claim a
local rebuild of libaom. Release archives, source notices, commands, logs and
exit receipts are retained with the evaluation.

The candidate configuration enables libde265 and AOM decoding, disables encoder
backends, unrelated codecs and dynamic plugin loading, and builds no upstream
example applications. CMake's local SDK destination is inside this workspace;
there is no machine-wide installation or Explorer change. Both shared libraries
and an independently authored C++ probe build successfully with Visual Studio
18 x64 and CMake 4.3.1-msvc1. Build command/exit receipts accompany each log.

The upstream [libheif license](https://github.com/strukturag/libheif/blob/v1.23.4/COPYING)
and [libde265 license](https://github.com/strukturag/libde265/blob/v1.1.3/COPYING)
require a separate redistribution review. Preserve their source/build materials
and notices. This evaluation makes no commercial licensing or patent-clearance
claim and does not select the final shipping dependency arrangement.


Initial decoder results
-----------------------

The probe confirms registered HEVC and AV1 decoders, with no registered encoder
for either format. It reads at most 16 MiB, uses strict decoding and explicit
library image/item/tile/memory limits, then reports the primary decoded RGBA
plane. This research probe is not a hardened production host or fidelity oracle.

| Input | Observed result |
| --- | --- |
| Upstream `examples/example.heic` | Primary image decodes at 1280 by 854, eight-bit luma, no alpha; container has two top-level images |
| Upstream `examples/example.avif` | Image decodes at 800 by 533, eight-bit luma, no alpha; one top-level image |
| Generated 24-byte truncated HEIC | Rejected, no decoded output |
| Generated non-image AVIF filename | Rejected, no decoded output |

All four input hashes remain unchanged. `probe-results.json` records actual
exits, dimensions, pixel fingerprints and durations; `probe-identities.json`
records the probe source and exact executable/library hashes. The retained
`probe/` sources and CMake project independently exercise the public C API.
No fixture bytes or upstream binaries are imported into production or Git.

The two-image HEIC result demonstrates why primary-image decoding alone cannot
establish safe conversion admission. Required collection/sequence policy remains
open. These two upstream examples are not acceptance evidence for color, HDR,
orientation, metadata, alpha, output encoding or all HEIC/AVIF variants. The
library allocation limits also do not replace worker job/deadline enforcement.


Required evidence before integration
------------------------------------

Use a finite, manifest-backed authored corpus for all four required pairs, with
original hashes, fixture-generation commands and independent expected results.
Upstream example decoding, if performed, establishes API feasibility only; it
does not replace this corpus or grant fixture redistribution rights.

| Coverage group | Required checks |
| --- | --- |
| Ordinary still images | HEVC and AV1 inputs, supported chroma variants, small and boundary dimensions; actual JPEG and PNG output validation |
| Precision and color | 8/10/12-bit SDR cases, ICC and NCLX interpretation, tagged versus unspecified color, conflicting profiles and known pixel comparisons; determine HDR/gain-map policy before claiming support |
| Geometry | Container rotation/mirroring/cropping and EXIF orientation, including conflicting declarations; transformations applied exactly once |
| Transparency | Opaque and partial alpha, straight/premultiplied declarations, PNG preservation and explicit JPEG matte handling |
| Metadata | EXIF/XMP, resolution, stale thumbnails, unknown extras and mandatory-copy behavior; retain existing quiet Automatic policy without silently dropping meaningful capabilities |
| Multiple items | Primary image, grids, thumbnails, auxiliary/depth/gain-map items, collections and sequences; explicitly distinguish supported still composition from unsupported meaningful content |
| Hostile inputs | Truncated boxes, invalid extents, large declarations, cyclic references, unsupported essential properties and codecs, protected content and misleading extensions |
| Application integration | Both targets, mixed selections, optional engine absence, license admission, preview/matte decisions, cancellation, failed publication, retry and unchanged originals |

Do not infer that all HEIF containers contain HEVC or that an AVIF filename
proves AV1 content. New types are input-only: target validation must continue to
reject HEIC/AVIF output, and existing image-to-PDF or DDS routes must not broaden
accidentally when adding enum values. Recognition and menu routing remain cheap;
native decoding stays in the worker.

The integration choice must compare extending the curated decoder against a
separate narrow decoder bridge feeding the existing Q16 output path. Consider
metadata visibility, color/precision fidelity, interruption, native dependency
identity and redistribution. Do not replace the curated engine with a stock
all-format build simply to make a fixture pass.


Authored orientation, alpha, color and metadata fixtures
-------------------------------------------------------

The repository-owned [fixture tools](../tools/heif-engine/README.md) now generate
24 deterministic 64-by-48 inputs. Each codec has all eight orientations, a
partial-alpha stripe pattern, a rotated alpha/EXIF/XMP case and two full-range
NCLX color cases. C++ fixture generation and Python expected-pixel calculation
are separate; the decoder consumes compressed inputs through the existing
decode-only DLLs. The color expectations use a full-range BT.709 matrix with
sRGB transfer characteristics. They do not test general ICC/HDR conversion.

The current run is
`.codex-temp/heif-fixtures/6d521c1dedb94a018b47e854eb1f38ea`.
All **24 decodes and four malformed/oversized refusals pass**. Geometry, alpha,
EXIF/XMP bytes and original/SDK/source hashes match. RGB channels are checked
against the authored values with a maximum one-code-value allowance; alpha must
match exactly. All 24 observed outputs match every expected channel byte exactly.
Both formats reject the 24-byte truncation and the oversized
image declaration without creating decoded outputs. Receipts retain every
command, exit, log, fixture identity and pixel comparison. This is not JPEG/PNG
output validation, application integration or a comprehensive hostile corpus.

Fixture generation uses a separate encoder-enabled libheif SDK. Its x265 4.3
source archive comes from the [upstream release](https://github.com/Multicorewareinc/x265/releases/tag/4.3),
SHA-256 `83C53E4C8BBB8F1E33ED59E10A7D621D1D7801CA853910C3EB41F038B8FFB121`.
It is built locally with assembly disabled and the static CRT, solely for
authored HEIC fixtures. The AOM encoder is also enabled only in this separate
SDK. Its native binaries are never placed in the decoder or production stage;
x265 redistribution is not selected. The earlier downloaded upstream Windows
archive is retained research material and is not used by these tools.

Retained setup failures are explicit: libheif initially did not discover the
installed `x265-static.lib`; the first explicit link then failed because x265
used the dynamic CRT. Passing builds specify `X265_LIBRARY` and x265's own
`STATIC_LINK_CRT=ON`. The first tool invocation at
`.codex-temp/heif-fixtures/cdd8e5f2f3044897b0cbc0c9561156d7` failed compilation
because the probe needed an explicit string header; no fixtures ran. The next
20-fixture grayscale run passed, followed by the current color-expanded run.
None of those failures is substituted for a successful result.
