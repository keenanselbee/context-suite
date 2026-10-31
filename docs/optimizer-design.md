Context Optimizer Design
========================

Current product policy: [decision 0021](decisions/0021-free-product-and-public-implementation.md)
removes trial/paid admission and License UI. Operations still validate confirmed
plans, batch identity, choices, cancellation, and safe publication. Third-party
notices are available in Settings. [Project status](../README.md#project-status)
owns current source, candidate, and installed state. Dated evidence below records
its original checkpoint; legacy access checks are historical, not current requirements.

Current source, 2026-10-19: the [preset expansion](optimization-presets-goal.md)
adds four meaningful presets for PNG, JPEG, still WebP, PDF, and DOCX/XLSX/PPTX.
Auto/Lossless also support FLAC, TGA, ODT/ODS/ODP, and EPUB. New image and document
paths always create copies, share one batch admission and sequential worker, and
publish only validated smaller results. Inapplicable presets explain the limit.

The [first expansion](optimization-expansion-goal.md) and
[preset goal](optimization-presets-goal.md) retain dated implementation and
installation evidence. Refer to project status above for the current payload;
scratch builds alone do not establish installation or release acceptance.

PNG, FLAC, and structural PDF dispatch are implemented. PDF always produces a copy. Missing required engines produce an
unavailable result before admission; format recognition alone never enables
processing. The [document design](document-design.md) owns broader PDF bounds.

FLAC uses exact decoded-sample validation and reconciles admitted original
metadata with the new STREAMINFO. Seek tables rebuild against complete frame
indexes with CRC and seeked-decoding validation; frame validation also applies
without seek tables. Unsupported application and unknown metadata blocks remain
refusals. See the [audio policy](audio-conversion-policy.md) and
[recorded engine evidence](audio-engine-evaluation.md). Earlier candidate-only
records describe their dated checkpoints, not current payload absence.

Current scope: [decision 0018](decisions/0018-context-menu-utility-and-output-preference.md)
removes the manual optimization workspace and routine preset planner. Direct
presets default to copies and honor an explicit Settings choice for safe
replacement. See the [simplification goal](context-menu-simplification-goal.md)
for current evidence and remaining acceptance.

Status: bounded lossless PNG recompression implemented under
[decision 0013](decisions/0013-lossless-png-optimization.md). See
[goal evidence](png-optimization-goal.md). Bounded [Balanced/Smallest presets](png-lossy-presets.md)
are implemented under [decision 0014](decisions/0014-png-precision-presets.md).
Direct Auto/Lossless/Balanced/Smallest quick actions and lossless fallback are
implemented. The researched `fdEC` exception now follows the selected output policy
with ordinary quiet success instead of rejecting optimization or warning. Interactive release acceptance and
general selectable metadata policies remain planned.

Accepted UX direction: [decision 0015](decisions/0015-simple-context-menu-workflows.md)
and the [quiet-first UX plan](quiet-first-ux-goal.md) replace mandatory planning
with direct Auto-first presets and best-effort processing. This includes
saving a useful permitted fallback. The initial slice has local policy, worker,
shell, and isolated quiet-launch evidence. The simplification goal records later
owner acceptance; remaining environment and compatibility checks retain their
specific scope.

[Decision 0016](decisions/0016-fixed-optimization-recipes.md) now refines the
implemented PNG policy: fixed recipes, Auto-only preflight routing, one
primary attempt and at most one safety fallback. It prioritizes gradient/detail
quality over reference file sizes. See [current integration evidence](fixed-preset-integration.md).


Purpose
-------

Context Optimizer reduces file size while preserving the current format. It
makes the loss policy explicit, measures the result, and never treats a smaller
file as successful if it violates the selected quality or capability contract.


Explorer Experience
-------------------

**Optimize** is a top-level Explorer command, hidden when no processing preset
applies to the selection. Source Auto and Lossless accept PNG, JPEG, WebP, FLAC,
PDF, DOCX, XLSX, PPTX, TGA, ODT, ODS, ODP, and EPUB filename hints; JPEG includes `.jpg`, `.jpeg`, `.jpe`,
and `.jfif`. Balanced and Smallest accept PNG, JPEG, WebP, PDF, DOCX, XLSX, and PPTX. Settings does not count as a
processing action. Mixed selections retain presets useful for at least one file.
Direct preset IDs capture the saved output choice. The bounded shell checks only
selection metadata and filenames; content support is checked outside Explorer
and inapplicable files get per-file explanations.
No manual picker or routine preset planner is exposed. Actionable problems
offer relevant recovery and Retry failed; retry retains the
selected file and preset. Legacy choose-preset activation uses conservative
Lossless directly. Direct Explorer Auto remains first.
The direct actions are:

- **Auto** — the first/recommended action; a conservative, image-specific choice
  of one conservative recipe, with worthwhile savings and bounded visual loss.
- **Lossless** — pixels decode identically.
- **Balanced** — bounded visual loss under a documented quality floor.
- **Smallest** — stronger, clearly identified lossy reduction under its own
  quality floor.
- **Settings...** — after a separator at the bottom, opens the Optimize section
  of shared settings without starting work.

Direct presets should complete without a success window: one optional Windows
chime per successful batch, including No smaller result. Show compact cancellable
progress only when useful; problems and necessary decisions get actionable UI.

The default output is a collision-safe sibling file. Replacing the source is an
explicit option and must use recoverable transactional publication.
JPEG, WebP, TGA, PDF, and document package optimization always use copies in the
current source, regardless of the saved replacement preference.

Use `name - Optimized.ext`, then `name - Optimized (2).ext` for collisions.
Preserve the source basename including existing suffixes. Replacement is disabled
by default. Saving Overwrite originals in Settings authorizes future commands;
legacy permission-only settings migrate to copies. Recycle the uniquely named
original backup only after publication succeeds. If recycling fails, keep the
backup and show its location. Follow
[decision 0018](decisions/0018-context-menu-utility-and-output-preference.md) and
the unchanged publication/recovery rules from
[decision 0008](decisions/0008-output-naming-settings-and-replacement.md).


Core Invariants
---------------

- Output format equals source format.
- Lossless means decoded content satisfies the format-specific equality
  contract, not merely that the engine describes itself as lossless.
- Lossy presets have fixed limits and never silently retry below their promised
  quality floor.
- An output that is not smaller is reported as **No smaller result** and is not
  published by default.
- Transparency, animation, color behavior, and required metadata are governed
  by explicit policies.
- Pre-publication cancellation or failure preserves the original. Completed
  publications remain completed during batch cancellation; a later recycling
  failure retains the original backup and is reported as a cleanup warning.


JPEG And Still WebP
-------------------

Lossless, and Auto on already-lossy input, select exact preservation. JPEG uses the existing curated Magick.NET coefficient optimizer with optimized Huffman
coding and baseline output, without a search across encodings. Original frame
components/sampling and quantization tables must agree, decoded samples must be
identical, and original admitted application/comment segments are restored
byte-for-byte. Orientation, ICC, EXIF, XMP, density, and admitted thumbnails are
retained without auto-orienting or rewriting descriptive metadata. This is not a
decode-and-save JPEG transcode.

JPEG admission covers 8-bit grayscale or three-component baseline/progressive
images with ordinary JFIF, EXIF/XMP, ICC, Adobe, and comment markers. CMYK,
higher-precision or other unsupported JPEG modes, MPO/MPF, HDR/gain maps, unknown
application markers, metadata after a scan, and changing quantization tables
between scans are refused. Malformed or conflicting metadata also stops work.

WebP admits one ordinary still 8-bit VP8 or VP8L image, optionally inside a
supported VP8X envelope with ICCP, EXIF, and XMP. Lossless uses method 6, quality
100, exact alpha/hidden RGB, and exact decoded validation. A lossless encoding of
already-lossy input does not restore lost detail and often gives no smaller result.
Animation, unknown or duplicate chunks, and conflicting ordering/flags are refused.
Metadata chunks remain byte-identical; container framing adapts to the encoding.

Balanced and Smallest use fixed quality 92 and 84 respectively: JPEG retains
4:4:4 sampling; WebP uses method 6. These quality numbers are codec settings, not
percentages or universal quality scores. Full decoded local, edge, gradient,
color-bias, and sample-error checks can reject a candidate. Photograph texture
has a separately bounded tolerance; flat regions and text edges retain stricter
checks. Alpha-bearing inputs currently stay exact. Balanced may fall back once
to Lossless; Smallest may fall back once to Balanced, with no recursive attempt.
Auto may select Balanced for an opaque lossless WebP with conservative preflight;
it stays Lossless when uncertain and never selects Smallest. All attempts start
from the original and share a 110-second deadline. Result details report the
requested preset, actual recipe, attempts, and fallback reason.

Both paths have a 128 MiB source/output bound and a 40-million-pixel limit.
JPEG permits at most 16,384 pixels per axis; WebP encoding is limited to 16,383.
Existing color/profile and malformed-input checks still apply. Only validated
smaller copies are published; a larger or equal result leaves the original
unchanged. These recipes reuse the pinned JPEG/WebP engines already in the
curated image dependency; they add no native executable or library.


Office Package Recompression
----------------------------

Auto and Lossless admit ordinary DOCX, XLSX, and PPTX packages with agreeing
content types, main relationships, XML roots, and filename suffixes. Each member
gets one fixed .NET Deflate attempt; its original compressed representation is
kept unless the attempt is smaller and its extraction version permits the change.
Complete decoded member bytes, CRCs, and hashes must agree. Names, order, flags,
versions, dates, attributes, comments, and required package declarations remain
intact; only storage method, compressed lengths, and dependent offsets may change.
Signed 16-byte and unsigned 12-byte ZIP32 data descriptors retain their framing,
CRC, expanded size, and zero/populated local declaration style, with compressed
lengths updated consistently. Output is always a validated smaller copy.

The initial boundary is a single-disk ZIP32 package of at most 64 MiB, with at
most 4,096 members and a 4 MiB central directory. Each expanded member is limited
to 64 MiB and the expanded package to 256 MiB. Inspected declarations, the main
XML part, and each relationship part have an 8 MiB XML limit and depth 64.
Only stored and Deflate members with supported ZIP 1.0/2.0 extraction declarations
are admitted; descriptors require Deflate and ZIP 2.0. Names must be unambiguous
ASCII or explicitly UTF-8, without unsafe/noncanonical path segments. Local records
must follow central-directory order without gaps or preambles.

Encrypted, signed, macro-enabled, template, and slideshow packages, ZIP64, split
archives, extra fields, unsupported flags/methods, conflicting descriptors or
headers, malformed XML, DTDs, and unsupported names are refused. The optimizer
does not open Office, recalculate spreadsheets, reserialize document XML, or
refresh external links. It uses existing .NET compression and adds no Office or
native dependency to Optimize. Exact member-byte preservation is distinct from
the broader rendering/fidelity acceptance required for Office-to-PDF conversion.


Document Image Presets And Additional Containers
------------------------------------------------

PDF Balanced/Smallest replace only validated ordinary JPEG image streams using
the same bounded image recipes. Eligible streams must have direct DeviceRGB or
DeviceGray, eight-bit samples, matching JPEG components, and a restricted image
dictionary. Masks, attachments, transfer functions, or display-color overrides
keep all image streams exact. Unsupported images stay unchanged. The complete
expected object graph is compared after qpdf rewrites it; text, fonts, vectors,
page structure, and all other content remain exact. Auto/Lossless remain structural
compression only. No page rasterization, downsampling, or generic lossy qpdf flag
is enabled. Existing signed/encrypted refusals and the 16 MiB input bound remain.

DOCX/XLSX/PPTX Balanced/Smallest may replace individually certified JPEG/WebP
members referenced as images, while preserving every other member byte and ZIP
property. Ambiguous relationships or non-image aliases prevent image replacement.
PNG and other embedded formats remain unchanged in this increment. Auto/Lossless
remain package-only. Recognized active edit/write protection is refused. Image
processing is bounded to 128 images and 80 million total pixels, within the
package deadline. No Office application, recalculation, XML rewriting, or new
native dependency is involved. Package reopen evidence is separate from universal
rendering/fidelity acceptance.

ODT, ODS, ODP, and EPUB support Auto/Lossless exact package compression through
the same bounded ZIP32 parser. The first stored `mimetype` and all expanded member
bytes remain unchanged. Identity, required manifests/references, and passive
content are checked. EPUB supports a single rootfile with ordinary EPUB 2/3
navigation. Encryption/obfuscated fonts, signatures, scripts, DTDs, and recognized
protection are refused. This is bounded package admission, not full schema or
reader conformance. Balanced/Smallest are unavailable rather than duplicate labels.

TGA Auto/Lossless use exact RLE packets for admitted 24/32-bit true-color images.
Samples including hidden RGB, dimensions, origin, image ID, and the supported
footer remain exact. Extended/developer footer areas remain unsupported. Existing
128 MiB and 40-million-pixel image bounds apply. No lossy TGA policy is offered.


PNG Version 1
-------------

The PNG engine design uses independently selected and pinned tools:

- Implemented lossless recompression through pinned `oxipng` 10.1.0, with no
  representation reductions and original metadata chunks retained exactly,
  except the explicit `fdEC` removal exception below.
- Implemented bounded RGB7 and dark-protected palette reduction followed by the
  same cleanup, with curated Magick.NET admission/reopen validation. Smallest
  uses a separately pinned ExoQuant helper; no paid quantizer is introduced.

The original [precision evaluation](png-quantization-improvements.md) is historical.
Balanced now uses only RGB7, with one lossless fallback. Smallest uses the fixed
dark-protected palette, with one RGB7 fallback and no further lossless attempt.
Palette-ineligible inputs go directly to RGB7. Lossy-ineligible but
lossless-supported representations use Lossless directly. Accepted palette output
is indexed PNG with exact alpha and hidden RGB; admitted metadata is retained.
ICC/indexed/grayscale and unsupported chunks remain outside lossy admission.

PNG fdEC compatibility exception
-------------------------------

The owner approved automatic removal of the researched `fdEC` chunk from an
in-memory working copy. Earlier five-image tests found identical decoded pixels,
alpha and measured properties after removal; this does not establish the
creator application's private semantics. The owner nevertheless explicitly
approved this chunk's removal without warnings or a special copy-only restriction.

- Validate the source, including CRCs and chunk ordering, before normalization.
- Remove only `fdEC`; retain all other chunks byte-for-byte. Existing animation,
  color/profile and unknown unsafe-chunk restrictions still apply.
- Run the selected fixed preset on the normalized image. Lossless retains exact
  samples; lossy presets keep their existing quality limits and attempt budget.
- Probe and worker agree on a typed removal flag; the application rejects
  disagreement. Follow normal output selection: copy by default, or replacement
  when enabled and confirmed. No `fdEC`-specific forced-copy restriction remains.
  Normal platform admission, transactional publication and recovery still apply.
- Publish only a validated smaller output. Removal alone may save 17 bytes for
  the observed five-byte payload, without changing image samples.
- The owner explicitly approved removal without a warning for `fdEC` only.
  Report normal **Completed**, with ordinary quiet-success behavior: no metadata
  warning text, warning sound or problem window solely for this chunk. Play the
  optional success chime once per batch, respecting mute. Normal delayed progress
  and unrelated errors/cleanup warnings remain unchanged. Keep the removal flag
  in internal validation and research evidence; it is not a user warning.

This exception is implemented for PNG optimization, not a blanket conversion
metadata policy. Generic conversion's explicit metadata decisions remain intact.
Earlier research-only/unsupported notes are historical, superseded for `fdEC`
optimization by this policy.

Original implementation verification: 898 engine/adapter contracts passed in both
Debug and Release. The original real-worker contract enforced copy-only removal;
that policy is now superseded as described below. Generic warning policy tests
still cover successful outcomes and one signal per batch.
The reported Silksong screenshot was copied into `.codex-temp` and all four
production recipes validated: 4,716,891 input bytes became 3,560,293 Lossless,
2,603,988 Auto/Balanced, and 1,259,856 Smallest. All outcomes reported `fdEC`
removal, exact alpha and hidden RGB, other retained metadata, and unchanged source.
The initial warning presentation and forced-copy rule are superseded by the
owner's approved-exception decision. Earlier copy-preservation tests are historical;
the replacement contract now checks validated overwrite and exact recovery backup
retention with a disposable fixture and a recycler that declines cleanup. Unrelated
recovery warnings must still be reported. Image-quality evidence remains applicable.
Release foundation/integration verification passed 584 contracts. An earlier
Debug run missed the timing window for observing the second Smallest encoder;
the complete Release retry passed that cancellation case and the rest of the
suite. No unrelated timing-test thresholds were weakened.
Auto selects RGB7 only for lossy-admitted inputs of at least 4096 pixels, otherwise
Lossless. A lossy Auto output must save at least 5% against the source, without
encoding a baseline for comparison. RGB7 quality/benefit rejection permits one
Lossless fallback. Other presets require strictly smaller outputs.
Encoder, cancellation and validation failures are not masked as fallback success.
Interactive production acceptance is still required.

The production implementation must not resolve tools from `reference/`. Each
engine requires a pinned version, integrity hash, packaging location, supported
operation contract, and deterministic fixtures.

Lossless validation should decode source and output into a canonical pixel
representation and compare dimensions and pixels. Metadata equality is governed
separately because removing optional metadata may be part of the selected policy.


Preset Semantics
----------------

Implemented defaults follow [decision 0016](decisions/0016-fixed-optimization-recipes.md):
Auto routes to one conservative recipe, Lossless recompresses once, Balanced uses
RGB7 with artifact safeguards, and Smallest uses one dark-protected ExoQuant
recipe with detail anchors and adaptive dithering. No per-file competition among
palettes, dithering settings or encoders. Each primary recipe has at most one
predetermined fallback within the total deadline; no recursive retry ladder.

Smallest may produce a larger result than a reference optimizer to avoid obvious
banding or distracting texture. Its versioned numeric limits and bounded visual
approval are recorded in [integration evidence](fixed-preset-integration.md).
Earlier [palette experiments](png-palette-comparison.md) are historical research,
not additional runtime candidates. Metadata and transparency policies do not vary implicitly by
preset, and ordinary users need no colour-count or dithering controls.

Each preset is a versioned policy, not a label over an open-ended retry ladder.
It defines:

- Whether pixel changes are allowed.
- Hard minimum quality or quantization limits.
- Dithering behavior where relevant.
- Metadata preservation or removal.
- Required preservation of alpha, animation, color information, and dimensions.
- A maximum acceptable output-size relationship.
- The exact validation contract.

Do not add content-specific presets or technical controls for this implementation.
Keep those distinctions inside the tested recipes and conservative Auto routing.


Optimization Plan
-----------------

Before execution, the plan records:

- Detected format and source properties.
- Selected preset and its policy version.
- Metadata policy.
- Destination and collision behavior.
- Whether replacement is requested and how rollback works.
- Required engines and their availability.
- Warnings and any unresolved decisions.

Optimizer must never reinterpret a failed “skip if larger” result as permission
to use a more destructive preset.


Batch Contract
--------------

Selecting multiple files and invoking Optimize creates one coordinated batch.

- One named optimization policy applies consistently to all files in a direct
  batch action.
- A direct preset remains useful when at least one selected filename qualifies.
  Content admission handles each file separately; unsupported items do not block
  supported ones. All supported families share one validated batch admission and
  settings snapshot, without a trial or license dependency.
- One queue, progress surface, and cancellation control represent the batch.
- Work uses bounded concurrency and prevents conflicting operations on the same
  source or destination.
- Each file is planned, written, validated, compared, and published separately.
- A file that cannot produce a smaller acceptable result is **Unchanged**, not a
  batch failure and not a reason to weaken the policy for that file.
- The final summary distinguishes succeeded, unchanged, failed, cancelled, and
  unsupported items and reports aggregate bytes saved only from successful
  outputs.
- Cancelling stops pending work and requests cancellation of running work;
  already published outputs remain intact and are reported accurately.


Execution And Publication
-------------------------

1. Validate the source and plan.
2. Create a unique temporary path on the intended destination volume.
3. Run the selected pipeline with structured arguments, cancellation, timeout,
   and captured diagnostics.
4. Validate format, dimensions, required capabilities, policy constraints, and
   decodability.
5. Compare final size with the source.
6. Publish to a reserved sibling path, or perform an explicit recoverable
   replacement.
7. Remove temporary files and present a per-file result.


Results
-------

Each result should show:

- Original and output sizes.
- Bytes and percentage saved.
- Selected preset and whether it is lossless or lossy.
- Relevant property or metadata changes.
- Engine names and versions in diagnostics.
- Duration, failure stage, and whether no smaller acceptable result existed.

Batch summaries distinguish succeeded, unchanged, cancelled, unsupported, and
failed files.


Further Lossy Processing
------------------------

JPEG/WebP now have explicit bounded lossy presets as described above. Auto on
already-lossy images remains exact; selecting Balanced/Smallest permits another
lossy generation on a copy. Lowering the bitrate of MP3, AAC, or
Opus is a lossy transcode and requires the same explicit warning and bounded
policy used by lossy image optimization. Changing an audio container or codec
belongs to Converter.


Validation
----------

- Maintain representative photographic, graphical, transparent, gradient,
  indexed, high-bit-depth, metadata-bearing, malformed, and unusually large PNG
  fixtures.
- Test that every lossless output decodes identically.
- Use perceptual metrics and human-reviewed fixtures for lossy policies; file
  size alone is insufficient.
- Test metadata keep/remove policies independently.
- Test no-change, larger-output, timeout, cancellation, engine crash, invalid
  output, Unicode path, collision, and replacement rollback cases.
- Record fixture source, preset, command policy, tool version, and expected
  semantic result so comparison outputs remain reproducible.


Non-Goals
---------

Optimizer does not silently change format, upscale media, apply creative edits,
promise imperceptible loss, or select a more aggressive policy merely because a
gentler one did not save space.
