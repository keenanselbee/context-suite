HEIC And AVIF Input Evaluation
=============================

Status: native, adapter and actual-worker conversion pass isolated checks,
including 336 JPEG/PNG outputs published by application orchestration, native
interruption/recovery and loader checks. Broader fidelity and packaging remain
open. No production engine
packaged or capability registration added.
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
The managed adapter below now supplies payload identity checks. Worker job/deadline
enforcement still requires integrated verification. The bridge performs no
filesystem or network operations.

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
The managed adapter below now uses it for verified JPEG/PNG conversion and
appearance checks. It remains absent from production payloads; broader
grid/auxiliary/hostile coverage and worker/publication recovery remain required.


Managed adapter conversion and color appearance
-----------------------------------------------

The private image adapter now loads the exact three-file decoder candidate from
an optional `heif` directory beside the worker. It checks compiled-in SHA-256
identities and DLL membership, rejects linked paths and retains read leases.
The managed/native boundary checks dimensions, profile sizes and ABI values and
retains cancellation. Default production packaging does not supply this directory.

HEIC/AVIF are input-only enum values. The shared planner permits only JPEG/PNG,
rejects them as targets and excludes them from image-to-PDF. The adapter currently
admits ordinary `hvc1`/`av01` items and bounded homogeneous grids with verified SDR
color interpretation. Premultiplied alpha, HDR signals, collections, sequences and other auxiliary
content remain refused. Nontrivial EXIF/XMP orientation is refused until the
container/metadata conflict policy is verified. Authored chroma, limited-range and
grid coverage is recorded below; missing/conflicting color declarations and broader hostile-property coverage still need
expansion before customer capability registration. Source compression is treated
conservatively as potentially lossy; encoded losslessness is not identified yet.

Original RGB ICC profiles remain attached to unchanged samples. For NCLX-only
BT.709 or Display P3, small embedded ICC profiles describe the existing samples;
this preserves their gamut and transfer characteristics in JPEG/PNG. The authored
[profile generator](../tools/heif-engine/ColorProfiles/Profiles.cpp) calls Little
CMS's [RGB profile API](https://github.com/mm2/Little-CMS/blob/master/src/cmsvirt.c)
using declared D65 primaries and inverse BT.709/sRGB transfer curves. It uses the
already-selected curated-engine Little CMS artifact only during generation;
no additional runtime library is introduced. Creation dates and optional IDs
are normalized for reproducible output. The retained build/repeat receipts are
under `.codex-temp/heif-profiles/5285435955f54c7894b5d47dd8936df1`.

| Generated profile | SHA-256 |
| --- | --- |
| BT.709 | `91E576DD1E810BEBCDC730AA8697329E4344E629A3579CC00C1928E71F89F761` |
| Display P3 | `067AC7D51AD60A72E74EB1138F5A6E8797BDFE082D0E7E6987D8820862CDB6CB` |

The final isolated run at
`.codex-temp/heif-conversion/aa0e93ec7b0449e287272a85c488f177` passes **1,755
checks and 216 JPEG/PNG outputs**: 108 admitted sources across both codecs and
three depths, with the remaining 18 HDR/collection sources refused. PNG samples
match independently authored values exactly after Q16 normalization. JPEG pixels
match encoding of those independent values with the selected quality/matte.
Original ICC bytes, alpha, orientation, mandatory copies and source hashes pass.
This JPEG comparison verifies adapter consistency, not a second JPEG encoder.
Twelve BT.709/P3 preview comparisons use independent transfer-curve and ICC-matrix
arithmetic; observed maximum error is one 8-bit channel value (allowance two).

Two real correctness issues were fixed without weakening output validation:
gray-looking RGB data must retain RGB storage when paired with an RGB ICC profile,
and previews now explicitly use Magick.NET's high-resolution color transform.
The default quantum transform produced red value 5 where the independent matrix
and direct Little CMS transform expected approximately 1 for the tested dark P3
pixel. Changing rendering intent or an artifact flag alone did not fix it; the
explicit `ColorTransformMode.HighRes` overload did. PNG/JPEG keep their original
color profile and samples; this display correction does not force sRGB output.

Retained failures include initial managed compilation with an incorrect search-
flag name; `dcfbd1cd5e304af7b8695dd69deab3a7` and
`c4879f348e60498f8f9f91b990eff714` withheld invalid grayscale/RGB-ICC PNG output.
`b7db5419729745769b43bdf1c4553a17`, `ea57841f71a6493e87279fec1ce13567`,
`5b4760ee50bd4ab682d7e44dfb0a9687`, `f8b27a5a6e5e4d33a9c0205f3e5b6b93` and
`f97f7b2d14f441cab042c19725c99e2a` retain the failed appearance investigation.
The first successful conversion-only run `a6e46da17efd4ffaa97cc22ed1dd2c6b`
did not establish display fidelity; `da8b1c9aeb81432f90c88ac60af2ecd7` passed
the appearance checks before the final matte/copy checks were added.

Release builds have zero warnings/errors. The existing **942 image/adapter**
and **4,130 foundation** contracts pass. These do not replace actual worker,
publication/recovery, native payload safety, packaging or human acceptance for
the newly added inputs. The reserved 1.1.1 stage and installed application remain
unchanged; a future combined candidate needs a fresh version.


Chroma, range and grid expansion
-------------------------------

The next isolated slice adds 4:2:0 and 4:2:2 with full and limited range,
limited-range 4:4:4, BT.601 and BT.2020 non-constant-luminance matrices, and
four-tile grids with distinct authored samples, alpha and rotated metadata.
The matrix is now **62 files per depth, 186 total** at 8/10/12 bits. These cases
verify decoding the declared YCbCr matrix; a BT.2020 matrix with BT.709 primaries
does not establish support for the BT.2020 gamut or HDR.

| Depth | Final fixture evidence under `.codex-temp/heif-fixtures/` |
| --- | --- |
| 8 | `676ef85e457e4f5a9e53f8539bc972eb` |
| 10 | `5481b3bcb7d241e7ab066b73ac817578` |
| 12 | `47fb5b2348ae4376a1cb1f162b8a39ba` |

Independent range expansion and matrix arithmetic allow at most one source-depth
channel value of rounding difference; observed maxima are one at each depth.
Alpha remains exact. The bridge and PNG encoder then preserve those verified
decoded samples exactly, including Q16 normalization and hidden RGB. JPEG matches
encoding of those samples with the selected matte and quality, as described above.
Thus the new evidence distinguishes bounded codec/matrix rounding from exact
transport and output preservation. It does not claim all decoded RGB values equal
the ideal floating-point matrix result byte-for-byte.

The private bridge ABI is now version 2 (56 bytes), retaining the primary item
type and adding the underlying codec. For grids it inspects at most 256 tiles,
requires one HEVC or AV1 codec, checks precision/alpha and declared color
consistency, and refuses nested compositions and unsupported tile metadata or
auxiliary capabilities. The managed adapter accepts this bounded grid result.
The revised bridge at `.codex-temp/heif-bridge/e801a7f8102d492696e3db1da3f96ee0`
has SHA-256 `55AC6DF9292D876C5689627563E846DD1F21A1CBCF5F4799879CFF8D9F71CF09`;
the pinned libheif/libde265 libraries are unchanged.

Final bridge evidence at
`.codex-temp/heif-bridge-tests/490a962ae3a0419a942dcbf953c2dcf8` passes **3,632
checks**. This includes 36 additional grid refusals: missing tiles, self-references,
truncated reference lists, excessive tile counts, uncovered width and zero height,
for each codec/depth. Every refusal leaves no native result, and a valid grid
decodes immediately afterward. The existing twelve malformed-file and callback
cancellation checks also pass.

Final adapter evidence at
`.codex-temp/heif-conversion/031604a37d114eae91a7055b47474b17` passes **2,819
checks and 336 validated JPEG/PNG outputs**, including the same 36 refusals and
subsequent adapter recovery. The eighteen HDR/collection cases remain refused.
The twelve independent BT.709/P3 appearance comparisons still pass. The managed
Release build has zero warnings/errors, and **942 existing image contracts** pass
at `.codex-temp/image-tests/engine-9e7c34e114c94017be69489b8fffb6f5`.
Foundation contracts were not rerun for this private implementation/tooling slice;
their prior 4,130-check result remains the preceding checkpoint's evidence.

The first generator attempt at `86072a76a5fe498fa289dded3ce20e28` produced an
invalid rotated grid: the full-grid encoding API applied orientation to each tile.
The authored generator now uses the incremental grid API, which applies orientation
to the complete grid. Upstream sources were not modified. The decoder correctly
refused the malformed attempt. Passing intermediate fixture, bridge and conversion
runs are retained alongside the final receipts; they do not replace the final
source-bound runs above.

This remains isolated authored evidence. Real-worker cancellation/deadlines,
publication/retry, payload loading safety, missing-engine behavior, production
packaging, realistic external-file interoperability and human acceptance remain
required. Arbitrary grids, HDR, nested compositions and conflicting declarations
are not established as supported by this slice.


Actual worker and direct-command verification
--------------------------------------------

The [worker harness](../tools/heif-engine/Test-HeifWorker.py) builds the existing
worker and public application contracts sequentially, then copies their exact
runtime files into fresh repository scratch. The ordinary production worker is
used with the same named-pipe protocol, Windows lifetime job, source checks,
output validation and application-owned publication. Additional isolated copies
omit the HEIF decoder or contain an intentionally altered bridge DLL. No installed
application, Explorer registration, formal production stage or live license is used.

Final evidence at `.codex-temp/heif-worker/16fd203d74dc4b018339e6134031d702`
passes **1,178 worker/application checks**. Source, fixture and executable hashes
are recorded and rechecked; `verified.json` binds the unchanged inputs/payloads
to the final result. All 168 admitted sources produce both JPEG and PNG, for
**336 separately validated and published outputs**. The eighteen HDR/collection
fixtures are typed unsupported refusals. Every output is re-probed for format,
geometry, precision and ICC presence. Pixel/color fidelity remains established
by the independently authored decoder and private-adapter suites above; re-probing
through the same engine is not an independent pixel oracle.

The direct-command and recovery checks cover:

- A mixed HEIC/AVIF/PNG command, with routine PNG conversions requiring no planner
  and an existing PNG kept unchanged. This verifies the quiet-workflow policy;
  it is not visible desktop acceptance.
- A refused HDR file followed by a successful ordinary input in the same command,
  with attention requested for the refused row.
- An explicit JPEG matte choice and real-worker preview for transparency, while
  retaining the existing quality-loss acknowledgement.
- Collision-safe repeated output, a controlled publication failure, and retry
  using the original target plus a changed output-folder setting.
- A stale source digest refused without publication; owned worker death at the
  dispatch boundary fails the current item and later HEIF work restarts; dispatch
  cancellation cancels pending work and leaves no output reservations.
- Thirty-six malformed grids followed by successful decoding in the same worker,
  with source bytes unchanged.
- Missing and altered decoder payloads refused before any HEIF native module is
  loaded. Repeated failures do not prevent ordinary PNG processing in that worker.

These checks exposed and fixed error categorization: unsupported compositions
and HDR now report an unsupported feature rather than a damaged image. Native
resource-limit errors retain that category. A missing or invalid optional decoder
reports an actionable `EngineUnavailable` failure with repair/update guidance;
the new shared failure category also has document-appropriate Office wording.

The final worker and contract builds have zero warnings/errors. The initial
manual contract build failed on an array-to-immutable-array call and incurred a
shared-output copy warning from concurrent builds; those logs are retained.
The committed harness keeps the builds sequential. Passing intermediate worker
runs `9731d0bff03d4459961bf96cd4d23465` and
`1aa24642dcf3485892573bbd19160154` are retained; the final run adds mixed failure,
collision and changed-settings retry checks. Focused adapter verification still
passes **2,819 checks** at
`.codex-temp/heif-conversion/25ce89ed3e3047b8aeaf7318df824ac9`.
The **942 image contracts** pass at
`.codex-temp/image-tests/engine-95f7e0758476435c8dc5e6c5f75f2cf4`, and the
**4,130 foundation contracts** pass in `.codex-temp/heif-worker-foundation.log`.

Remaining integration gates include interruption during active HEIF native work
(the new fault checks act at dispatch), deadline enforcement under that workload,
loader path/race coverage beyond missing/altered payloads, independently reviewed
redistribution and production packaging, capability adoption, realistic-file
interoperability and visible acceptance. The 1.1.1 stage remains reserved and
unchanged. None of these isolated runs creates a replacement release candidate.


Loader ownership and active-native interruption
----------------------------------------------

The next isolated slice closes the loader and active-native checks above.
The old loader accepted an already-loaded libde265 from a different directory,
even though the intended files passed their hashes. The reproduction used a
second copy of the same trusted library, not untrusted executable code. The
failed preloaded case is retained at
`.codex-temp/heif-loader/d6bc5a0936c14d5885ea1cbd8e5e5530`.

The managed loader now retains verified directory handles for every ancestor,
in addition to the pinned file handles. It refuses linked files/directories,
unexpected DLL membership and preloaded HEIF modules, then checks every loaded
module's exact path. Failed acquisition releases its owned handles. The shared
directory implementation comes from the existing Office runtime checks; Office
call sites retain their wrapper and unchanged checks. No native binary pin changes.

The [loader harness](../tools/heif-engine/Test-HeifLoader.py) passes **25 checks**
across six separate processes at
`.codex-temp/heif-loader/668251b95a2548b6ba222f3a8b48f808`: valid, preloaded,
junction, hard link, extra library and missing library. The valid case denies
writing loaded files and renaming either the engine or its host directory.
Invalid cases load no new HEIF modules. The existing Office file-lease contracts
pass **15 checks** at
`.codex-temp/heif-shared-leases/1adb29304d0b437faa83cd8aefd8ae9d`.
These are bounded ownership tests, not a claim of exhaustive race coverage.

The authored `Stress` generator produces 3072-square, 8-bit, full-range SDR
HEIC/AVIF noise images. The fixture encoder stays separate from the decoder
payload. Passing generation receipts are at
`.codex-temp/heif-stress/cda0ee399a3142c49a33fb382be783bb`.
This corpus supplies a demanding workload; it is not a new fidelity oracle.

Final worker evidence at
`.codex-temp/heif-worker/bbab96a9e1ee48c795262db79082079d` passes the existing
**1,178 checks and 336 published outputs**, plus **38 interruption checks**.
Before each fault, the test observes an instruction pointer inside the owned
worker's native library: `libde265.dll` for HEIC and `heif.dll` for AVIF, which
contains the linked AV1 decoder. This establishes active native processing;
the AVIF observation does not identify a particular AV1 function. Only the
test-owned worker thread is briefly suspended; its identity and instruction
offset are recorded. The Windows SDK verifies the x64 context layout at build.

Cancellation, owned-worker termination and controlled deadline expiry each pass
for both formats. The deadline test verifies the production 120-second setting
and triggers its existing clock seam; it does not wait 120 wall-clock seconds.
Every case preserves source bytes/timestamp, abandons the temporary reservation,
publishes no incomplete output, returns the appropriate category and successfully
probes the same image with a fresh worker. The final receipt rechecks all recorded
source, fixture and executable hashes. Worker/contract builds have zero warnings
and errors. The earlier 942 image and 4,130 foundation results were not rerun for
this focused loader/interruption slice.

The first 4096-square stress HEIC at
`.codex-temp/heif-stress/b944489f829f4c0eb9decc66df3e7329` is retained. Its
22.2 MB single-slice input exceeds libde265's 16 MiB NAL security limit. The
upstream libheif adapter ignores that push error and subsequently reports no
decoded image. The worker safely refuses it, but currently categorizes it as
invalid input. Clarifying this limit and returning the resource-limit category
remain required; do not advertise every input under the outer 128 MiB/40 MP
bounds as supported. The first worker attempt
`801056d7eb294dd1a192e80d7368054d` passed its ordinary 1,178 checks but failed
before interruption on this source. It is not passing interruption evidence.

Remaining gates include that limit/category fix, broader realistic-file
interoperability, independently reviewed redistribution, engine adoption and
production packaging, budgets and visible acceptance. The reserved 1.1.1 stage
and installed application remain unchanged.


Reproducible decoder build and NAL-limit correction
--------------------------------------------------

The [pinned build recipe](../tools/heif-engine/Build-HeifEngine.py) now creates a
fresh isolated SDK and decoder payload from the verified release archives and
selected AOM artifact files in [source inputs](../tools/heif-engine/source-inputs.json).
It bounds extraction, rejects linked archive entries, retains original source
inventories, and applies the narrow
[libheif patch](../tools/heif-engine/libheif-de265-push-errors.patch).
The patch returns failed libde265 NAL-push results instead of discarding them;
resource/security-limit and allocation errors retain their category. It does
not increase limits or change decoding. These native statuses already map to
the worker's existing resource-limit guidance.

The build at `.codex-temp/heif-engine/fbdde3d851ed43b08d28acc70a87882e` succeeds.
Post-build review verifies all **884 original source files**, with exactly the
reviewed patch changed. CMake, command/exit logs, input identities and original/
patched inventories are retained. libheif emits one upstream C4805 warning for
a bool/byte comparison in `context.cc`; this is not a zero-warning native build.
Managed worker/contract builds have zero warnings and errors.

| Payload file | SHA-256 |
| --- | --- |
| `ContextSuite.Heif.Native.dll` | `55AC6DF9292D876C5689627563E846DD1F21A1CBCF5F4799879CFF8D9F71CF09` |
| `heif.dll` | `3A847A8AE5109BB03D4102498A523E8750AF19529813DF57931CFFB668CF100B` |
| `libde265.dll` | `A0AB26C9C05B5A92D45A6852BACF5B61A46A48A6A1F36D54A36336B7F26620CF` |

The three DLLs total **3,636,224 bytes**, before notices/source materials. PE
dependency inspection finds only the other listed decoder DLLs and KERNEL32.
The bridge binary is unchanged; the private loader now pins the rebuilt
libheif/libde265. This is a controlled source-build recipe, not a claim that
timestamp-bearing upstream DLLs reproduce byte-for-byte in every build.

The [stress build runner](../tools/heif-engine/Build-HeifStress.py) now generates
both ordinary interruption inputs and, with `--oversized-nal`, the limit fixture.
New 3072-square receipts are at
`.codex-temp/heif-stress/4d2d5816f57c43629314571d247d309f`; the 4096-square
receipts are at `f51564e516b949b1b7db47bdc5052244` under the same parent.
Both oversized files exactly reproduce the previously retained bytes. The
large HEIC now returns resource-limit status, not invalid-input status.

| Verification | Final evidence under `.codex-temp/` | Result |
| --- | --- | --- |
| Bridge, repeated NAL refusal and ordinary recovery | `heif-bridge-tests/42c79b21699442cf9d368d8abed10308` | 3,634 checks over 186 authored inputs |
| Actual worker/publication plus repeated NAL refusal and same-worker recovery | `heif-worker/39fcea360a214ee8a4d9f26622e7375a` | 1,181 checks; 336 published outputs |
| Active-native cancellation, termination and controlled deadline | Same worker evidence, `interruption/results.json` | 38 checks, all six format/fault cases |
| Loader ownership/refusals with new pins | `heif-loader/f0f827d5649245fba1864a753c754f5d` | 25 checks |
| Managed output validation and independent SDR appearance | `heif-conversion/d37db1d1e44c429cb000fe0887b03cbf` | 2,819 checks; 336 outputs |

The first recipe attempt `heif-engine/f0218f0c82dc432b9e063dad4e7db648` stopped
at patch application because of CRLF patch text. Patch files now require LF.
The second `7b8af03690c14ad483e16e746e1de44d` stopped at source-membership
verification because Windows path separators differed from the expected key;
inventory keys now use forward slashes. Neither attempt built/adopted an engine.
All failures remain retained. The final successful build/checks above supersede
those attempts without replacing the earlier baseline evidence.

Source/license redistribution, engine adoption in production packaging, broader
realistic-file interoperability, performance budgets and visible acceptance
remain open. The previous general image/foundation and Office lease results
were not rerun for this dependency-pin slice. No release version, formal 1.1.1
stage, installed application, Explorer registration or live service changed.

The later [source/runtime review checkpoint](heif-distribution.md) supersedes
these payload pins with a full rebuild from the library-only-capable recipe.
It repeats bridge, worker, interruption, loader and adapter acceptance and
proves an extracted library source kit can build without the private checkout.
That record owns current identities and evidence; redistribution approval,
production adoption and broader interoperability remain open.

The later [upstream interoperability record](heif-interoperability.md) verifies
12 real-worker outputs from six independently produced still images. It also
identifies six color-admission gaps requiring resolution before acceptance.
Passing current-policy refusals does not establish those files are supported.
