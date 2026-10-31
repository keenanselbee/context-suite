Product Implementation History
==============================

Historical checkpoints preserved from [product design](product-design.md).
Version, installation, candidate, and pending-work statements below describe
their dated checkpoints. For current state, use [project status](../README.md#project-status)
and the [free transition record](free-access-goal.md). Commercial policies are
superseded by [decision 0021](decisions/0021-free-product-and-public-implementation.md).


Owner-approved extension, 2026-10-20: broaden PDF dark-mode compatibility using
a local 50-document gap corpus, while preserving originals, searchable text,
document structure, and readable appearance. Prioritize passive document
features and validated color handling; larger limits and ambiguous rendering
require separate evidence. Reuse existing engines. Implementation and installation
acceptance are tracked in the [PDF support record](pdf-dark-lecture-support-goal.md).

Owner-approved extension, 2026-10-19: the
[Optimize preset expansion goal](optimization-presets-goal.md) extends meaningful
quality choices across supported formats, with bounded fixed recipes, mandatory
copies for new paths, and format-specific preservation and quality evidence.
The bounded source implementation and verification are complete; the first
expansion's verified candidate remains separate from these later source changes.

Owner-approved extension, 2026-10-19: the
[Optimize expansion goal](optimization-expansion-goal.md) adds bounded lossless
JPEG, still WebP and DOCX/XLSX/PPTX package recompression, and broadens structural
PDF optimization through demonstrated compatibility work. These paths are now
implemented in source: Auto and Lossless preserve the format, validate a smaller
result and publish copies. JPEG/WebP and Office package work reuse existing
dependencies; Office package recompression does not require the Convert engine.
Installed 1.5.2 remains unchanged. Combined candidate verification and release
acceptance are recorded separately in the goal; video remains outside this scope.

Version 1.4.8 uses an immediate Technical details disclosure. Its content,
chevron and scroll range update in the first layout without a sliding transition.
The native Expander state and themed ToggleButton retain keyboard interaction,
focus feedback and the remembered preference; window sizing remains unchanged.

Version 1.4.7, installed locally, gives collapsed and expanded Analyze windows the same starting top,
calculated from the expanded 82% opening height. The collapsed window is shorter,
not separately centered. Toggling details changes only height; ordinary movement
and resizing remain unrestricted.

Version 1.4.6, installed locally, adjusts height on explicit Technical details changes. Collapsing
shrinks a normal inspector to at most 570 DIPs; reopening restores its previous
expanded height, fitting below the current top when possible. Position and width
stay unchanged, maximized windows stay maximized, and movement and resizing have
no ongoing geometry corrections. The remembered disclosure state and 82% opening
layout remain.

Version 1.4.5, installed locally, replaces Analyze's temporary bounds with a one-time opening layout.
Expanded inspectors start at 82% of the current monitor's usable height, centered
vertically; remembered collapsed details retain a compact opening height. No
ongoing geometry handlers or maximum-height limit constrain movement, resizing,
snapping, maximizing, monitor changes or later disclosure changes. Long content
scrolls inside the chosen size while footer actions stay accessible.

Version 1.4.4, installed locally, narrows the Analyze taskbar fit to initial automatic layout. Moving,
resizing or maximizing the inspector releases its temporary monitor bounds;
later disclosure changes retain the owner's geometry. It is a starting layout,
not a permanent restriction on where the owner can place the window.
The same update incorporates the owner's further revisions to both triangle
PNGs, preserving the separate small-size artwork and transparent surroundings.

Version 1.4.3, installed locally, refreshes Windows icons from the owner's latest triangle PNGs on
2026-10-16. Preserve source colors, proportions and alpha, using the small artwork
through 48 pixels. It also retains post-layout window bounds checks without
forcing a position correction on every move between monitors.
Installed taskbar resource lookup, three packaged COM activations and all payload
hashes pass. Settings, trial and license data are unchanged by installation.

The 2026-10-16 Analyze refinement in 1.4.2 opens Technical details by default and
remembers subsequent disclosure choices in settings. Expansion fits below the
window's current position within the monitor work area, leaving a small gap above
the taskbar. Type-specific file properties appear before source/classification
information; supporting evidence is last. Duplicate generic file facts are removed.

The 2026-10-16 text-counting update in 1.4.1 reads recognized text in fixed-size
chunks, up to 256 MiB within the existing five-second read budget. Small format
identification remains bounded to 64 KiB. The Analyze window shows an exact line
total when counting completes, or a lower bound and inspected text-byte count
when a limit or invalid tail stops it. Counting runs off the UI thread and keeps
user cancellation and source-change checks. See [Analyzer design](analyzer-design.md)
and the [measured performance follow-up](analyze-performance.md#streaming-text-counts-2026-10-16).

On 2026-10-16 the owner selected `images/icon-tri.png` and
`images/icon-tri-small.png` for Windows branding in 1.3.9. Use the transparent
folded triangle for application, taskbar, package and installer icons. Retain
the emerald-background `icon.png` and `icon-small.png` artwork for online pages.
Keep the external resource index and transparent package backgrounds from 1.3.8.
Version 1.3.9 is installed locally. Generated icon alpha, installed resource lookup,
three packaged COM activations and the installed payload inventory pass; settings,
trial and license data and the online-page source images are unchanged.
The owner's revised triangle PNGs are installed in 1.4.0 on the same date, with
the same icon alpha, installed resource lookup, packaged COM and payload checks.
The version rollover introduces no compatibility change.

2026-10-15 source follow-up: conversion progress/results opens **Files and
details** automatically. Its file table fills the remaining window height;
the selected-file explanation scrolls separately and the bottom actions stay
reachable. Resizing or toggling details retains the chosen window height.
Quiet completion and the separate Analyze inspector retain their existing
lifecycle. This UI change is included in local version 1.3.6, installed on
2026-10-15 with the approved emerald folded-triangle application icon. Windows
uses ten transparent ICO sizes (16 through 256 pixels); package and installer
branding share the same artwork. Explorer action glyphs remain monochrome.

The 1.3.7 local update changes package-level taskbar transparency: all three
identities declare transparent backgrounds and include indexed default, dark and
light target-size assets. Keep the green rounded tile; its outer transparent
corners must not receive Windows' former blue, purple or green package fill.
The 1.3.8 follow-up stages the shared resource index at the external application
location. The 1.3.7 indexes beside registration manifests were not loaded by
Windows; installed resource lookup failed despite correct PNG alpha. Verification
now resolves each tool's icon through its installed package. On 2026-10-16 the
owner confirmed that the gray taskbar square is gone after reopening installed
1.3.8. Resource lookup, alpha, installed payload and packaged COM checks pass.

Analyze UI update, installed locally in 1.2.8 on 2026-10-11: Analyze now uses an independent
read-only inspector window, with an immediate single-file summary and a
multi-file comparison table. Convert and Optimize retain their shared compact
progress/results window, application process and sequential worker. Closing or
cancelling transformation results preserves the inspector and its analysis.
See [analyzer design](analyzer-design.md) for lifecycle and verification limits.
The installed inventory and three shell COM registrations are verified. Settings,
trial and license files remain unchanged; installed UI acceptance remains separate.

The 1.2.9 refinement, installed locally on 2026-10-11, puts text line counts and
encoding alongside file size. Identification evidence remains under Technical
details. Counts from partially inspected files are explicitly lower bounds.

Owner-approved PDF dark-mode conversion, 2026-10-10: add one **PDF (Dark mode)**
destination that creates a local deterministic reading copy, preserving searchable
text, vector geometry and supported document features. Preserve photos; recolor
simple diagrams only under a validated policy. Always keep originals. AI/OCR
reconstruction and cloud processing are excluded. This is an explicit appearance
conversion exception to format-only destinations, not an Optimize action. See the
[implementation goal](pdf-dark-mode-goal.md). The bounded native engine, worker,
direct action and shell target are verified in unsigned
[candidate 1.2.7](pdf-dark-mode-review.md). A subsequent source-only policy update
converts all 50 lecture pages with independent structural and rendered review and
actual-worker validation. It adds local contrast relationships and bounded
transparent-image handling; some preserved images retain light backgrounds.
That update is not packaged or installed; reserved candidate 1.2.7 is unchanged.
The source-only `pdf-dark-3` follow-up adds bounded recoloring of compressed
monochrome and colored diagram images, including the four requested lecture
pages, with contrast checks and preservation of unhandled image panels.
The subsequent `pdf-dark-4` update improves colored-panel ink contrast and
compression handling without changing geometry or the existing admission guards.
The source-only `pdf-dark-5` follow-up preserves colored-ink edge coverage to
reduce pale annotation outlines, retaining the same command and bounded guards.

Owner-approved lecture PDF extension, 2026-10-11: normalize already-dark slide
backgrounds and supported embedded tables to the existing reading palette.
Support validated tagged structure, ordinary links, passive attachments, observed
static layers and wider font metrics while preserving their semantics. Retain one
PDF (Dark mode) command and mandatory copies. The
[lecture support goal](pdf-dark-lecture-support-goal.md) defines the 25-document,
746-page acceptance corpus and implementation sequence. This extension is planned;
installed 1.2.9 remains unchanged.

Owner-approved compatibility expansion, 2026-10-09, in progress: JFIF input,
largest-frame ICO to PNG, all PDF pages to JPEG, all GIF frames to PNG, static
SVG to JPEG, and audio extraction from MP4/MOV/WebM/MKV to WAV/MP3/FLAC. Reuse the
existing destination commands and icons. GIF has no first-frame command or
submenu. Video extraction is the approved narrow extension to the previous
video exclusion; general video conversion remains outside scope. The owner
explicitly added MKV on 2026-10-09; M4V and other containers remain outside scope.
All new conversions keep originals even when overwrite is enabled. Preserve
one batch admission, sequential worker execution, immutable choices and
selective retries. See the [converter design](converter-design.md) for behavior
and current verification. The installed 1.2.5 payload remains the baseline;
these source changes are not installed or packaged as a new release yet.

Owner-approved image expansion, 2026-10-09: ICO and still SDR AVIF output,
classic TIFF to PNG/JPEG/PDF, and static SVG to PNG. Validate the combined icons,
WEM and existing-engine baseline first, then evaluate and implement bounded
converters, distinct destination icons and a combined review candidate. Keep
the direct-command UX and existing publication guarantees. These additions are
implemented in the unsigned [1.2.5 review candidate](image-expansion-review.md).
Native UI, representative fidelity and commercial acceptance remain separate;
local version 1.2.5 is now installed, with the previous 1.2.3 payload retained.

Owner-approved extension, 2026-10-08: [Convert icons and WEM conversion](wem-conversion-goal.md).
Integrate distinct destination icons and add loose WEM/WAV conversion through a
bundled decoder and user-installed Wwise Authoring. Preserve the direct-command
UX; soundbank editing is outside this goal. Both directions now have bounded
real-engine verification in source. Installed/native UI and in-game acceptance
remain separate gates; WEM is not yet an advertised launch capability.
The approved input extension also accepts MP3 and float32 WAV through bounded
PCM16 preparation, with one explicit batch quality decision and no silent loss
of unsupported tags, artwork or out-of-range samples.

Completed UX direction: [context-menu simplification](context-menu-simplification-goal.md)
under [decision 0018](decisions/0018-context-menu-utility-and-output-preference.md).
Remove the general customer workspace and routine planners; retain focused
decision prompts, Analyze, progress/problems, and Settings. Output defaults
to copies, with explicit Settings consent for safe replacement on future commands.
The implementation follows this scope with owner manual acceptance. Additional
accessibility/theme/DPI and installed lifecycle coverage remain release gates.

The free transition removes the License window and all purchase/activation
actions. Settings retains access to [bundled third-party notices](bundled-notices.md)
without an online request. Source delivery and redistribution remain separate.

Previously accepted expansion: [broad file support](broad-file-support-goal.md) under
[decision 0019](decisions/0019-broad-file-analysis-and-media-expansion.md).
Analyze must give useful read-only results for every readable regular file,
including a reviewed offline explanation of common types. Add common audio
analysis/conversion and lossless FLAC optimization, plus PDF/Office/OpenDocument
analysis. Selected launch actions include images-to-PDF, PDF pages-to-images,
PDF optimization and Word/Excel/PowerPoint-to-PDF. The owner-approved
[PDF improvements](pdf-conversion-improvements-goal.md) add best-effort editable
[PDF-to-Word copies](pdf-to-docx.md), white-default PNG pages with optional
transparency, and filename-filtered conversion targets. These changes are
included in local version 1.1.9, which also adds Compact monochrome Explorer icons.
Optional PDF optimization has
direct worker/publication coverage; PDF-to-PNG has direct PNG-command coverage
with per-document summaries and partial-page retries. Renderer release adoption
and visible acceptance remain pending. Optional Convert > PDF now combines images
through a focused order review, one validated copy and whole-document retry.
Bounded legacy DOC/XLS/PPT analysis now supplements package analysis; its
[supported headers and limits](legacy-document-analysis.md) are explicit.
[Optional Office-to-PDF dispatch](office-direct-command.md) now creates separate
document copies, sharing one admission with combined-image PDF work. Spreadsheet
calculation requires a focused choice; retention, fidelity and integrated
acceptance remain open. Retain the simple customer surfaces above. Shared Analyze/fallback,
bounded JSON/XML structure analysis and a 245-entry catalog are implemented with automated
evidence; see [current coverage](file-type-coverage.md). The
[launch capability matrix](launch-capability-matrix.md) separates recognition,
analysis, executable pairs, packaged engines and remaining acceptance. Signing is deferred.
The [combined 1.1.1 candidate](combined-office-candidate.md) packages all selected
engine families for isolated integration; that build does not establish release
adoption or complete Office fidelity and visible acceptance.

On 2026-09-16 the owner added HEIC/AVIF input to JPEG/PNG and AIFF/WMA input
to the existing audio targets to the required local candidate goal. These input
additions are now implemented in the optional
[expanded scratch payload](combined-expanded-inputs.md), with recorded image/audio
worker and copy-publication checks. They are absent from reserved candidate
1.1.1. The [launch matrix](launch-capability-matrix.md) defines admitted variants
and refusals; broader fidelity, human review, engine adoption and final integrated
acceptance remain open. ALAC remains a separate evaluation. The
[current completion plan](broad-file-support-goal.md#current-objective-and-completion-plan)
defines scope freeze, implementation, customer acceptance and combined packaging
milestones, including reviewed commits in both repositories.

Status: shell/process and output-safety foundations implemented; PNG/JPEG/WebP/BMP/TGA
conversion and bounded DDS analysis/conversion have passed local acceptance.
Lossless and bounded lossy PNG optimization are implemented with automated contracts;
broader analysis and live-provider paid activation remain pending. The owner
accepted the simplified image UI; this does not certify all accessibility environments.
See [PNG precision presets](png-lossy-presets.md) for exact scope and remaining checks.


Historical Release Sequence
---------------------------

The original sequence below predates the current implemented scope. Its planned
AVIF, animation, audio, and document statements are historical, not current limits.

The implemented image foundation targets PNG, JPEG, WebP, DDS, TGA,
and BMP. Bounded PNG/JPEG/WebP/BMP/TGA conversion is implemented today. Formats
do not imply support for every variant or operation. A curated general-image
engine is integrated into development packaging. Decision 0011 selects a pinned
CPU DirectXTex bridge; bounded DDS analysis/conversion passes local acceptance. See
[decision 0010](decisions/0010-first-release-formats-and-curated-engine.md).
Retain useful near-term capabilities while excluding unnecessary dependencies;
do not expand product scope merely because upstream bundles a codec.

The intended vertical slices are:

1. Shared settings and output safety, including replacement failure tests.
2. PNG, JPEG, and WebP conversion with transparency and metadata handling.
3. Curated-engine integration and bounded BMP/TGA conversion (implemented), then
   DDS analysis with explicit BC/linear/sRGB conversion and independent validation.
4. Lossless then bounded lossy PNG optimization with representative fixtures.
5. Broader image conversion and analysis, capability by capability.
6. Common audio analysis and conversion.
7. Additional formats supported by demonstrated use cases and reliable tests.

Each slice should work from Explorer through validation before the next format
family substantially expands the product surface.
The 2026-10-09 image expansion selects ICO and still SDR AVIF output, classic
TIFF to PNG/JPEG/PDF and static SVG to PNG, within tested variant boundaries.
GIF and camera RAW remain outside that scope. HEIC/AVIF input to JPEG/PNG is
required by the owner's 2026-09-16 scope decision and implemented in the expanded
scratch payload, subject to the declared variant and acceptance limits above.
Existing HEIF input support does not admit every HEIF-family variant or enable
HEIC output; the separately selected AVIF output remains planned. Decision 0019
supersedes the former image-release-before-audio ordering: broad Analyze, common
audio and agreed document capabilities are now part of the next launch goal.
See [broad file support](broad-file-support-goal.md) for that expansion's sequence;
the image slices above retain their existing implementation and evidence scope.
