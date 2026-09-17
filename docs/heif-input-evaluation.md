HEIC And AVIF Input Evaluation
=============================

Status: isolated native builds and 126 authored 8/10/12-bit decoder fixtures pass;
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

Use a narrow private decoder bridge inside the existing sequential worker,
feeding the existing Q16 output and validation path. Extending the curated
ImageMagick DLL would change the already-verified image engine and its delegate
allowlist; it would still require explicit container/color admission. A separate
bridge exposes source ICC/NCLX, item structure and original sample precision
without replacing that engine. Pin and verify the bridge and decoder dependencies
as one optional payload. This is the selected implementation direction, not an
implemented production capability. It adds no resident process or Explorer parser.

The bridge must carry source color declarations independently of decoded pixels,
normalize 10/12-bit samples to Q16, and apply container geometry exactly once.
ICC-managed output must keep a consistent profile/sample pair; NCLX-only SDR
needs independently verified explicit color handling, including common BT.709
and Display P3 cases. Refuse HDR/gain-map content, sequences, multiple meaningful
images and unsupported auxiliary content before publication. Supported ordinary
still composition and alpha must retain their appearance; thumbnail omission
follows Automatic's mandatory-copy rule. Do not treat all auxiliary items as
discardable metadata or advertise a variant before its integration tests pass.


Authored orientation, alpha, color and metadata fixtures
-------------------------------------------------------

The repository-owned [fixture tools](../tools/heif-engine/README.md) now generate
24 deterministic 64-by-48 inputs at each of 8, 10 and 12 bits. Each codec has all eight orientations, a
partial-alpha stripe pattern, a rotated alpha/EXIF/XMP case and two full-range
NCLX color cases. C++ fixture generation and Python expected-pixel calculation
are separate; the decoder consumes compressed inputs through the existing
decode-only DLLs. The color expectations use a full-range BT.709 matrix with
sRGB transfer characteristics. They do not test general ICC/HDR conversion.

The current precision matrix is retained under `.codex-temp/heif-fixtures/`:

| Sample depth | Run | Result |
| --- | --- | --- |
| 8 bits | `5831eb7ee63d44ee9bb51d64c0c254d3` | 24 exact-pixel decodes and four refusals |
| 10 bits | `56614f4441ff4119967b1e5c5cef2005` | 24 exact-pixel decodes and four refusals |
| 12 bits | `60967d792f654adb8e4b23ddb2a60276` | 24 exact-pixel decodes and four refusals |

All **72 decodes and 12 malformed/oversized refusals pass**. Geometry, alpha,
EXIF/XMP bytes and original/SDK/source hashes match. RGB channels are checked
against the authored values with a maximum one-code-value allowance; alpha must
match exactly. All 72 observed outputs match every expected channel sample exactly.
Higher-depth patterns contain low-bit detail, and the probe verifies that decoded
precision matches the source. Ten- and twelve-bit output uses little-endian
16-bit storage in the original sample range; a future Q16 bridge must normalize
that range explicitly. These checks do not establish HDR or arbitrary color support.
Both formats reject the 24-byte truncation and the oversized
image declaration without creating decoded outputs. Receipts retain every
command, exit, log, fixture identity and pixel comparison. This is not JPEG/PNG
output validation, application integration or a comprehensive hostile corpus.

Fixture generation uses a separate encoder-enabled libheif SDK. Its x265 4.3
source archive comes from the [upstream release](https://github.com/Multicorewareinc/x265/releases/tag/4.3),
SHA-256 `83C53E4C8BBB8F1E33ED59E10A7D621D1D7801CA853910C3EB41F038B8FFB121`.
It is built locally with assembly disabled and the static CRT, solely for
authored HEIC fixtures. A separate `generator-high-sdk` combines locally built
8/10/12-bit x265 static libraries for higher-depth fixtures; the original
eight-bit generator SDK remains available. Build logs and the local
`BuildHeifHighDepth.py` procedure are retained under `.codex-temp`. The AOM encoder is also enabled only in this separate
SDK. Its native binaries are never placed in the decoder or production stage;
x265 redistribution is not selected. The earlier downloaded upstream Windows
archive is retained research material and is not used by these tools.

Retained setup failures are explicit: libheif initially did not discover the
installed `x265-static.lib`; the first explicit link then failed because x265
used the dynamic CRT. Passing builds specify `X265_LIBRARY` and x265's own
`STATIC_LINK_CRT=ON`. The first tool invocation at
`.codex-temp/heif-fixtures/cdd8e5f2f3044897b0cbc0c9561156d7` failed compilation
because the probe needed an explicit string header; no fixtures ran. The next
20-fixture grayscale run passed, followed by the eight-bit color-expanded run
`6d521c1dedb94a018b47e854eb1f38ea` and the precision matrix above.
None of those failures is substituted for a successful result.


Color declarations and image structure
--------------------------------------

The expanded corpus adds nine cases per codec at each precision: BT.709 transfer,
Display P3 primaries, PQ and HLG transfer signals, sRGB ICC, Adobe RGB ICC with two
NCLX transfer declarations, a two-image collection and a thumbnail. Existing
orientation/alpha/metadata/color cases remain. ICC fixtures use the existing
pinned stage's managed `Magick.NET.Core.dll` profile data; the helper and assembly
are hashed with the run, and generated profile bytes are preserved separately.

| Sample depth | Run under `.codex-temp/heif-fixtures/` | Result |
| --- | --- | --- |
| 8 bits | `c572b8c49a6544b0a95aaaa9197b6dea` | 42 exact-pixel decodes and four refusals |
| 10 bits | `e83dfb1e37fc4893b500a450656d7eb8` | 42 exact-pixel decodes and four refusals |
| 12 bits | `da0ec01bac8e414d93421b47e064df9f` | 42 exact-pixel decodes and four refusals |

All 126 outputs match their independently calculated samples exactly, including
18 cases retaining source ICC bytes exactly through decode. Source NCLX values,
collection counts, thumbnail counts and original identities match. These are
transport tests: they do not prove final color-managed appearance, HDR support,
collection conversion, grid/depth/gain-map handling or arbitrary container safety.

The pinned `heif_decoding.h` documents that null `output_image_nclx_profile`
defaults to sRGB conversion, including for HDR. The probe now explicitly enables
`output_image_nclx_profile_passthrough`. The source handle returns each expected
NCLX declaration, but the decoded interleaved image returns no NCLX in all 126
cases. Independent pixel comparisons establish retained sample values; the
bridge must preserve the source declaration itself. An absent decoded profile
must never be interpreted as permission to assume sRGB. The exact cause of the
decoded-profile omission has not been established.

Retained attempts: `872c0fbac61b4ecba7ef07d1621ff7c6` stopped before native tests
because Windows treated `Profiles` and `profiles` as the same directory;
`2793d65ed48347a38c7b06a3e372c4df` failed to compile until the sequence API header
was included. `6464e2c8c21b48e4b908d7782c1e8b50` exposed the decoded-NCLX omission.
`c1e6f40ff7b24cd69c8476c8debd5e5e` exposed reversed thumbnail assignment in the
fixture-only upstream C wrapper: the wrapper passes its documented arguments in
the opposite order to the internal function. Fixtures now use the dedicated
thumbnail encoder, whose source calls that internal function in the declared
order. The passing runs verify one thumbnail and one top-level image. No upstream
source, decoder SDK, existing production stage or installed application changed.


Private in-memory decoder bridge
--------------------------------

The private `ContextSuite.Heif.Native` library now exposes a versioned, fixed-width
C ABI for bytes-in decoding. It returns geometry, original precision, source
NCLX, item/capability flags, ICC, TIFF EXIF, XMP and normalized Q16 RGBA samples.
Native allocation ownership stays inside the bridge; copies require an exact
caller buffer size, and every failed decode leaves a null result. The bridge
accepts cancellation callbacks and applies the existing 40-million-pixel and
16,384-dimension bounds, plus explicit library allocation/item/tile limits.
Worker job/deadline enforcement and managed payload identity loading are still
required at integration. The bridge performs no filesystem or network operations.

The isolated build at
`.codex-temp/heif-bridge/4f584b8f3d684488bf87b12b990eaf21` uses the unchanged
decoder SDK, an exact libheif 1.23.4 CMake requirement and warnings-as-errors.
The final run at
`.codex-temp/heif-bridge-tests/79f899550e984901baeeddfc0bd36541` passes **2,420
checks across 126 authored images**. It verifies exact Q16 sample normalization,
orientation, ICC/EXIF/XMP retention, item flags, all 12 malformed refusals, six
early/during-operation cancellations, subsequent successful decoding and invalid
ABI/buffer requests. Recorded source, fixture and binary identities are unchanged.

The first bridge attempt, `be2bfdc33f2646bb84cb1c38afac10aa`, failed its expected
transformation flag because x265's unrotated fixtures also carry a clean aperture
for encoder padding. The verifier now accounts for that authored crop; final
geometry and pixels remain independently exact. The passing intermediate run
`c75564e7f09741638b8ee16b776e8fb5` preceded the explicit CMake version and ABI-size
assertions; the final run above includes them.

This is a decoding primitive. It deliberately reports collections, HDR transfer
signals and auxiliary capabilities rather than authorizing their conversion.
Managed admission must enforce the selected still-image policy, consistent color
and EXIF/container orientation, alpha handling and mandatory-copy exceptions.
It is not yet called by the application or included in a production payload.
Actual JPEG/PNG conversion, appearance checks, grid/auxiliary/hostile coverage and
worker/publication recovery remain required.
