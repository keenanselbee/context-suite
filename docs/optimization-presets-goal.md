Optimize Preset Expansion Goal
==============================

Status: complete for the bounded source implementation and verification below.
Owner authorized on 2026-10-19. This follows the completed
[first expansion](optimization-expansion-goal.md); it does not change the
then-installed 1.5.2 application or the reserved 1.5.3 candidate.

Installation follow-up, 2026-10-20: these source changes are included in installed
1.5.5. The exact package passes 82 preset workflow checks across nine representative
formats, plus expansion/interruption checks. See the
[PDF compatibility follow-up](pdf-dark-lecture-support-goal.md#broader-pdf-compatibility-2026-10-20)
for the complete payload, installed verification, and retained rollback evidence.

Deliver useful Auto, Lossless, Balanced, and Smallest choices wherever the format
has a defensible quality tradeoff. Do not expose duplicate settings just to fill
four slots. Keep the existing direct-action workflow, bounded recipes, smaller
validated outputs, and compact dependency footprint.


Quality And Safety
------------------

Follow [decision 0016](decisions/0016-fixed-optimization-recipes.md).
Lossless preserves decoded content and admitted metadata. Balanced and Smallest
allow different, explicitly bounded quality reductions. Auto uses conservative
preflight, chooses Lossless when uncertain, and never chooses Smallest.
Each recipe has at most one predetermined fallback, starts from the original,
and shares one deadline. A larger, invalid, or unacceptable candidate is never
published. No smaller result is a valid outcome.

Protect dimensions, orientation, color interpretation, transparency, hidden RGB,
animation timing, page structure, formulas, and other meaningful content where
applicable. Do not silently resize, remove metadata, flatten documents, change
formats, or add a generation of lossy processing under the Lossless label.
Expanded image and document paths retain mandatory copies. Warn about lossy
processing in the selected action's description and preserve that policy in
result details. Auto stays lossless for already-lossy images until a conservative
automatic policy has its own evidence.


Coverage And Sequence
----------------------

| Family | Work and acceptance boundary |
| --- | --- |
| PNG | Retain all four existing recipes and their regression evidence. |
| JPEG | Add fixed Balanced/Smallest recipes with local error, edge, and gradient checks; keep exact coefficient optimization for Lossless and conservative Auto. |
| WebP | Admit ordinary still lossy inputs, preserve exact alpha and metadata, and add meaningful quality presets. Animation requires separate timing/frame evidence. |
| PDF | Assess and implement selective embedded-image optimization with explicit image validation and unchanged non-image structure; never enable generic lossy engine switches without a preservation oracle. |
| DOCX/XLSX/PPTX | Extend exact package recompression with individually validated embedded-image substitutions, unchanged other members, and document reopen evidence. Preserve formulas and cached results. |
| FLAC | Retain Auto/Lossless; compression effort is not a lossy quality tier. |
| GIF/TIFF/ICO | Evaluate existing engines, metadata/frame/page preservation, and useful savings before enabling a bounded subset. |
| SVG/ODF/EPUB | Evaluate safe structural/package compression first; only add quality tiers if a real validated media transformation is available. |
| AVIF/HEIF, other audio, video | Assess separately against Optimize-only dependency cost and quality guarantees; no implicit large engine addition or new video scope. |

Compatibility work must be driven by reproducible ordinary files. Revisit size
and representation limits with measured memory/time bounds, not blanket increases.
Every assessed family ends with implemented evidence or a concrete exclusion and
reason. An unimplemented stage remains open unless its safety/dependency review
establishes that it is unsuitable for this scope.


Execution And Verification
---------------------------

- [x] Freeze recipe identities, preservation rules, and supported representations.
- [x] Implement JPEG/WebP contracts, private recipes, worker validation, and direct actions.
- [x] Verify real savings and refusal/fallback behavior on a representative image corpus.
- [x] Implement viable document image policies and verify non-image preservation.
- [x] Assess and implement useful low-dependency additional format paths.
- [x] Verify mixed batches, retries, cancellation, timeouts, source changes, and publication.
- [x] Verify Optimize-only dependencies and measure payload growth.
- [x] Run combined checks, review scoped diffs, and commit coherent public/private milestones.
- [x] Update current support claims and clean task-owned disposable output.

Use small authored fixtures for deterministic contracts and independent,
appropriately licensed examples for representative quality evaluation. Include
text, diagrams, photographs, gradients, transparency, metadata, and already
compressed inputs. Report savings and acceptance rates, including no-benefit
cases. Automated metrics do not establish universal visual quality.

Reuse the existing engines and test/build tooling. Keep task builds under one
unique `.codex-temp` root, preserve compact receipts, and remove successful
disposable output after processes stop. Preserve reserved candidates, installed
payloads, reference inputs, and earlier evidence. No installation or push is
authorized by this goal.


Implemented Coverage
--------------------

| Format family | Auto | Lossless | Balanced / Smallest |
| --- | --- | --- | --- |
| PNG | Existing conservative selection | Exact samples | Existing RGB7 / palette policies unchanged |
| JPEG | Exact coefficient recompression | Exact coefficient recompression | Fixed q92 / q84, 4:4:4, validated samples and exact metadata |
| Still WebP | Exact for lossy input; conservative Balanced eligible for opaque lossless input | Exact decoded samples | Fixed q92 / q84 method 6; alpha-bearing input stays exact |
| PDF | Structural compression | Structural compression | Selectively recompress eligible JPEG streams; keep other content exact |
| DOCX / XLSX / PPTX | Package compression | Package compression | Selectively recompress eligible JPEG/WebP members; keep other members exact |
| FLAC | Exact samples | Exact samples | Unavailable: quality reduction would require a different audio policy |
| TGA | Exact RLE | Exact RLE | Unavailable: no validated lossy TGA recipe |
| ODT / ODS / ODP / EPUB | Exact package compression | Exact package compression | Unavailable: no validated embedded-media policy for these containers |

This is thirteen families, compared with eight after the first expansion.
Four choices now have meaningful behavior for seven families. Identical results
or no smaller result remain valid; Smallest is not a guarantee of a smaller file
than Balanced. All newly expanded paths retain mandatory copies.

The [optimizer design](optimizer-design.md) defines representation, package,
metadata, protection, and resource limits. In particular, this does not claim
all variants of these extensions. PDF images with masks/color overrides remain
exact, Office PNG images remain unchanged, and signed/encrypted documents remain
unsupported. The bounded ODF/EPUB parser does not establish universal reader
conformance or optimize scripted/DRM containers.


Quality Policy And Evidence
---------------------------

JPEG/WebP use version-one `*-preserve-v1` policies. The existing lossless identities
remain compatible; new preset identities are explicit in plans and worker results.
PDF uses `pdf-structural-1`, `pdf-auto-1`, and `pdf-images-balanced-1` /
`pdf-images-smallest-1`. Document package requests retain the versioned
`ooxml-package-{preset}-v1` protocol family for compatibility, including exact
OpenDocument/EPUB requests. No engine version or native pin changed.

The image contract compares original decoded samples without a display transform.
Balanced/Smallest global RGB RMSE limits are 2.5/5.0 levels and channel bias limits
0.6/1.2. Artwork uses maximum per-sample errors 18/32 and 8x8 tile mean errors
2.5/5. A source with at least 60% classified texture tiles may use the photographic
contract: texture maximum errors 48/64 and tile means 6/8, still bounded by sparse
outlier counts, strong-edge limits, and the unchanged global RMSE/bias limits.
Nontexture regions retain stricter error bounds. Coherent low-contrast text edges
retain at least 80%/65% aggregate contrast per qualifying tile; genuine flat and
planar-gradient neighborhoods have separate safeguards. Exact alpha, hidden RGB,
geometry, color interpretation, and admitted metadata remain independent gates.
These numbers are bounded engineering safeguards, not universal perceptual scores.

Three independently sourced, licensed photographs were exercised as original
JPEG, prepared high-quality JPEG, and prepared lossless WebP, with two explicit
lossy requests each. Six of eighteen requests published a lossy result; the other
twelve retained exact content or the original. All three already-compressed
original JPEGs and the uncertain third-photo holdout retained exact results.
The first photograph's prepared JPEG saved 62.2% / 69.4% and prepared WebP saved
73.6% / 81.4%. The second prepared JPEG saved 60.6%; Smallest fell back to Balanced.
Prepared-source savings must not be represented as typical savings on downloads.

Independent diagram and text-on-photo controls retained exact content; a smooth
JPEG gradient accepted Balanced and used that same permitted fallback for Smallest.
Local before/after crops were inspected. This small corpus and automated bounds
support a conservative implementation, not universal visual approval. Broader
customer-file and native UI acceptance remain release work.

Two representative WebP adapter calls measured 0.47 seconds / 62.7 MiB cumulative
process peak for an accepted result, and 12.55 seconds / 154.1 MiB for a quality
rejection followed by lossless fallback. The latter measurement includes source
preparation and is not incremental memory or a worst-case bound. Recipes share
one deadline and do not run an encoder tournament. Large-input latency remains
bounded by the worker and format limits rather than guaranteed by these samples.


Verification And Footprint
---------------------------

| Check | Result |
| --- | --- |
| Public foundation | 5,094 contracts; successful fixtures automatically removed |
| Private image presets / exact image preservation | 77 / 127 checks |
| Private Office image / baseline package | 1,211 / 280 checks |
| OpenDocument/EPUB / TGA / PDF presets | 342 / 99 / 37 checks |
| Mixed document/TGA actual-worker workflow | 82 checks, nine input families and all four selections |
| JPEG/WebP/package actual-worker workflow | 105 checks |
| Active cancellation, injected timeout, and killed worker | 54 checks with recovery and original preservation |
| Existing PNG actual-worker and interruption regression | 98 checks |
| Native shell | 880 conversion and 182 optimization/Analyze availability checks; COM and icon checks pass |
| Installer component projection | 77 checks; no installer executed |

The independent PDF renderer reopened both pages and retained extracted text and
non-image rendered regions exactly; the authored gradient-image output had maximum
rendered error 1/2 for Balanced/Smallest. Separately generated python-docx,
openpyxl, and python-pptx packages passed all four app requests and independently
reopened with their text, tables, formulas, and shapes preserved. Every non-image
member remained byte-identical. This is library reopen and structural evidence,
not a Microsoft Office visual acceptance claim.

The scratch worker passes these new workflows with only shared image/PDF engines
and no Convert engine directory. Overlaying current application, worker, managed
assemblies, symbols, and shell onto the immutable 1.5.3 inventory adds **69,124 bytes
(67.5 KiB)**. The projected common payload is **45,299,494 bytes (43.2 MiB)** and
full payload **572,658,994 bytes (546.1 MiB)**. No runtime files, native engines, or
package dependencies were added. This is a source-overlay estimate, not a newly
packaged installer or download measurement. Optimize-only dependency selection
remains unchanged.

Local-only evidence is under `.codex-temp/presets-8368c44c`, with photo source
attribution, hashes, reproduction code, measurements, and crops in `photo-evidence`.
The source revision and commit review receipts bind the verification; the reserved
1.5.3 inventory remains unchanged. Installed 1.5.2 is untouched.


Assessed Exclusions
--------------------

| Family | Reason to leave it outside this increment |
| --- | --- |
| BMP | Admitted uncompressed storage has no useful size reduction without introducing indexed/RLE representations; explicit PNG/WebP conversion already fits. |
| GIF | No preservation writer for every frame, timing, disposal rule, and extension. |
| TIFF | Existing read support is not an IFD/tag/page-preserving writer or equality oracle. |
| ICO | Existing conversion selects or synthesizes frames; safe optimization needs a separate all-entry container rewriter. |
| SVG | The renderer cannot prove semantic XML preservation; raster quality labels are inappropriate for structural minification. |
| AVIF | Existing conversion uses a fixed quality/color policy; optimization requires a new native recipe and preservation evidence. |
| HEIF | No existing HEIF encoder; adding one carries engine-size and redistribution costs. |
| DDS | Block format, mipmaps, and dimensions are meaningful texture choices; existing Convert owns them. |
| WAV / AIFF | Uncompressed sample storage has no substantial same-format lossless saving; FLAC conversion is explicit. |
| MP3 / AAC / Vorbis / Opus / WMA / WEM | No reviewed same-format optimization contract covering listening quality, timing/gapless behavior, metadata, and all codec variants. |
| Video | Existing extraction is not full video processing; no implicit video engine/scope expansion. |
| Generic ZIP / other packages | Unknown container identities, signatures, and storage constraints lack the format-specific preservation admission added here. |

These exclusions are deliberate rather than unfinished implementations hidden
behind four identical labels. The next useful work is broader representative
acceptance and targeted preservation writers, not more codecs by default.


Closeout
---------

Private implementation checkpoint: `33ba125`; public workflow, tests, and owning
documentation are committed separately. No installation, push, release staging,
or reserved version mutation was performed. Repository checks validate 230 public
documentation files and the public/private source boundary.

Task cleanup removed 2,641 generated/redundant files totaling 252,096,142 bytes
(240.4 MiB), after stopping processes and checking exact owned paths and reparse
points. The foundation runner independently removed its 96,621,838-byte fixture
set. About 7 MiB of local-only logs, provenance, measurements, source hashes,
small authored files, and photo crops remain for review/reproduction. Retention
is tied to broader visual/release acceptance; no shared caches or prior evidence
were deleted. An earlier helper cleanup attempt was rejected automatically;
the main agent completed cleanup with explicit inventory and per-file checks.

The goal is complete as a bounded development change. It does not claim that the
installed application has these features, that every file with an admitted suffix
will optimize, or that commercial release/visual acceptance is complete.
