Project Implementation History
==============================

Status: historical checkpoints extracted from the project README on 2026-10-30.
Installed versions, test totals, trial/licensing behavior, and pending work below
belong to their original checkpoints. Undated paragraphs retain that historical
scope; they are not current requirements or executable setup instructions.

See [current project status](../README.md#project-status), [development](development.md),
and [the free transition](free-access-goal.md) for current source and release work.
The original scope is retained to explain the subsequent expansion, not to exclude
implemented audio and document capabilities.


Local Milestone History
-----------------------

Local version **1.4.7** is installed as of 2026-10-16. Analyze now streams text
line counts, opens Technical details by default, remembers disclosure choices,
prioritizes file-specific facts and opens expanded at 82% of the current monitor's
usable height with balanced vertical spacing. This placement runs once; normal
moving, resizing, snapping and maximizing remain free of ongoing corrections.
Collapsing Technical details shrinks the normal window; reopening restores its
previous expanded height where space permits, preserving position and width.
Maximized windows keep their geometry. Remembered collapsed details open compactly
at the same starting top as the expanded window, without separate centering.
The installed build uses the owner's latest transparent triangle PNGs for
ten Windows icon sizes from 16 to 256 pixels and preserves movement between
monitors. The emerald-background artwork remains reserved for online pages.
The complete bundled engine selection, lecture PDF compatibility and expanding
conversion file-details UI are retained. Default, dark and light taskbar resources
remain indexed at the external application location with transparent backgrounds.
The Analyze update passed 4,925 foundation contracts and 951 Analyze checks;
the shared-starting-position follow-up passed 21 checks in each theme. Broader visible lifecycle
acceptance remains separate. The installed payload and packaged resource lookup
checks pass; settings, trial and license files were preserved during installation.
Previous installed payloads are retained for rollback.
See the [icon conventions](style-guide.md#shell-integration) and
[lecture support evidence](pdf-dark-lecture-support-goal.md).
The owner confirmed on 2026-10-16 that the gray taskbar square is gone after
reopening 1.3.8. Broader visual/accessibility acceptance remains separate.
The milestone records below retain their historical package and acceptance scope.

The `pdf-dark-6` [lecture extension](pdf-dark-lecture-support-goal.md) now
normalizes all 25 topic PDFs (746 pages), including already-dark navy slides,
tables and eligible diagrams. Real-worker copies, repeat no-ops, preservation
checks and rendered review pass. The earlier 50-page lecture also passes.
The isolated 1.3.1 candidate passes exact-worker and payload verification. Installed 1.2.9
remains unchanged. Protected/interactive or ambiguous PDFs remain unsupported;
photos and uncertain image regions retain their original appearance.
Native desktop/accessibility and owner acceptance remain separate.

The `pdf-dark-7` COSC 310 follow-up adds rounded panels, safer overlapping
text and image handling, readable screenshot fallbacks and page-specific failures.
Five original lectures (212 pages) pass native conversion and repeat checks;
three originals still refuse with documented reasons. Candidate 1.3.2 passes
actual-worker, preservation and payload checks; see the same lecture support goal.
It is not installed.

The owner-approved image expansion adds ICO and AVIF output from the
five ordinary raster inputs, classic TIFF to PNG/JPEG/PDF, and static SVG to PNG.
All four are implemented in the unsigned **1.2.5** review candidate, including
[bounded SVG rendering](converter-design.md#svg-input-checkpoint-2026-10-09).
The archive passes fresh-extraction and payload verification; its exact staged
worker passes 5,064 foundation checks and the recorded existing-feature replay.
See the [candidate evidence](launch-capability-matrix.md#image-expansion-candidate-125-2026-10-09)
and [interactive icon/output review](image-expansion-review.md).
Native Explorer, accessibility, representative-content fidelity and commercial
acceptance remain open. Preserved combined baseline 1.2.4
passes the recorded automated workflow and payload checks, including destination
icons and WEM. See the [baseline checkpoint](launch-capability-matrix.md#image-expansion-baseline-2026-10-09).
At that checkpoint the installed local application was **1.2.5**; the previous
1.2.3 payload was retained for rollback. Earlier milestone records retain their
original evidence and do not establish completion of this expansion.

The older implementation milestones below retain their original package and
acceptance scope; the 1.2.5 candidate record describes that integration checkpoint.

The preceding [broad file support goal](broad-file-support-goal.md) covers
useful Analyze results for every readable regular file, an offline common-type
catalog, common audio, and PDF/document support. The first Analyze source slice
now handles unknown/empty files, initial header identification and existing DDS
details, bounded JSON/XML structure analysis, WAVE/FLAC header facts,
bounded OOXML/OpenDocument package and legacy DOC/XLS/PPT analysis, and a
245-entry offline catalog.
The [versioned launch matrix](launch-capability-matrix.md) separates current
analysis, exact conversion pairs, optimization, packaged engines and remaining
acceptance. See [analysis coverage](file-type-coverage.md) for individual
facts and source-review limits.
Audio conversion now has isolated worker/publication coverage for all 30
cross-format pairs, direct Convert commands and a compact prompt for required
quality changes. Production audio engine adoption and visible acceptance remain
pending. [MP3 output cover preservation](mp3-output-artwork.md) has isolated
evidence for supported FLAC, Ogg and M4A inputs. [M4A output cover preservation](m4a-output-artwork.md)
also supports representable FLAC, MP3 and Ogg pictures. [WAV source artwork](wave-id3-artwork.md)
now uses the same preserved output paths. [WAV output artwork](wave-output-artwork.md)
also retains representable covers from the other five audio formats.
[WAV Unicode tags](wave-text-conversion.md) preserve supported descriptive
values in explicitly encoded ID3 alongside ordinary ASCII INFO fields. An explicit
[isolated production build option](audio-distribution.md) packages the
reviewed audio runtime and notices; default release packaging
still excludes it. Broader analysis remains in progress. Selected document launch actions are
images-to-PDF, PDF pages-to-images, PDF optimization and Word/Excel/PowerPoint-to-PDF.
A structural PDF optimization candidate now passes isolated worker, paid-access
and copy-publication checks, plus independent two-page rendering comparison.
Auto/Lossless dispatch now includes PDFs in the same batch as PNG and FLAC, with
mandatory PDF copies. Broader fidelity, engine adoption and the other document
actions remain pending. **Convert > PNG** now includes PDFs when the optional
renderer is present, with numbered page copies, one document result and retries
that skip completed pages. Partial failure/cancellation remain visible; normal
packaging still excludes the renderer. **Convert > PDF** now combines supported
images into one validated copy when the optional validator is present. Several
images require page-order review; retries keep that order and preserve originals.
An explicit [combined production candidate](pdf-production-payload.md) now
packages these PDF engines and notices alongside images/audio. Default release
adoption and broader document fidelity remain pending. Office conversion is
implemented separately in isolated builds and is absent from that package.
Normal packaging still excludes this validator. [Optional Office PDF commands](office-direct-command.md)
now connect DOCX/XLSX/PPTX documents to separate validated copies, with a focused
spreadsheet calculation choice. Integrated acceptance remains pending.
The fresh [combined 1.1.1 candidate](combined-office-candidate.md)
now packages Office alongside image/audio/PDF engines for isolated integration
checks; fidelity, redistribution and release adoption remain open.
The later [expanded scratch payload](combined-expanded-inputs.md) adds
HEIC/AVIF input to JPEG/PNG and AIFF/WMA input to the six existing audio targets.
Its exact worker passes the recorded 659 HEIF interoperability and 211 expanded
audio direct-command checks, including validated output copies and preserved
originals. These additions follow the variant limits in the launch matrix and
are absent from reserved 1.1.1. Human review, Office fidelity/security, engine
adoption and one final versioned candidate remain required.
The [goal record](broad-file-support-goal.md)
identifies the latest isolated staging and its limited acceptance scope.
The owner accepted the image utility's simplified UI; screen-reader, additional
themes/DPI and installed lifecycle coverage remain separate gates. Signing is
deferred. The following records describe the existing image foundation.

Product design and technical discovery are in progress. A native x64 shell
prototype now registers **Analyze**, **Convert**, and **Optimize** as independent
Windows 11 Explorer commands and hands the complete selection to a separate host
through a bounded, versioned request file. The standalone native prototype host
only confirms activation; production WPF activation supports the implemented
analysis/conversion workflows below.

Shared image-output safety and settings are implemented and locally verified.
The [goal evidence](image-output-safety-goal.md) records the tested scope and
replacement restrictions. The completed PNG/JPEG/WebP conversion slice has tested
adapter/worker execution, a system-themed conversion planner, previews and
trial-gated safe publication and a passing bounded media/failure matrix.
The curated native engine is now integrated into production development builds,
with bounded BMP/TGA conversion bringing the catalog to twenty cross-format pairs.
The completed bounded [DDS slice](dds-conversion-goal.md) adds public header analysis,
pinned CPU DirectXTex conversion, mip/color policies and safe publication: 27
conditional conversion pairs total. Earlier focused image/DDS desktop checks
passed before the current simplification; final customer-surface acceptance
remains open.
Bounded [lossless PNG optimization](png-optimization-goal.md) now has a
direct commands, a private encoder, independent sample/metadata validation,
trial-gated smaller-only publication and automated failure tests.
[Balanced and Smallest](png-lossy-presets.md) add bounded RGB precision and dark-protected palette reduction
for supported 8-bit RGB/RGBA PNGs, with exact alpha and preserved accepted metadata. Interactive
optimization/licensing UI acceptance and live paid activation remain pending; this is not release-ready.
[Fixed optimization recipes](decisions/0016-fixed-optimization-recipes.md)
are now implemented: predictable presets, at most one safety fallback and
gradient-protected Smallest. See [integration evidence](fixed-preset-integration.md)
for policy limits, measured latency and the scope of visual approval.
An Inno Setup offline installer candidate now compiles. The
[installer lifecycle integration](installer-recovery.md) now wires versioned
staging, active-release launch/uninstall and recovery, with automated failure
tests. Native upgrade admission remains closed pending signed Windows lifecycle
acceptance; trusted signing and clean-machine checks remain pending. See the
[packaging goal](release-packaging-goal.md),
[integration evidence](bmp-tga-and-engine-integration.md)
and [remaining redistribution gates](release-redistribution.md).
See the [conversion goal](image-conversion-goal.md) for verified scope.


Initial Scope
-------------

The first release path is intentionally narrow:

1. Shared settings, safe copies, and optional replacement: implemented and tested.
2. PNG/JPEG/WebP/BMP/TGA conversion and curated-engine integration: implemented
   and tested for explicitly bounded variants.
3. DDS analysis and bounded 2D conversion: implemented and locally verified
   under [decision 0011](decisions/0011-dds-engine-and-texture-policies.md).
4. Optimize PNG files with explicit lossless and bounded lossy policies, then
   expand image coverage through tested capabilities.
5. Add common audio analysis and conversion after the image workflow is
   reliable.

Video, documents, cloud processing, AI editing, CD ripping, and arbitrary media
engine commands are not part of the initial scope.

The latest source changes add [best-effort PDF-to-Word conversion](pdf-to-docx.md),
white PDF page backgrounds with an optional transparent setting, and
selection-aware conversion targets. Installed local version 1.2.3 adds transparent
figure backgrounds and closer answer-font spacing to these
changes, native Word rules, grouped prose, heading styles, repeated footers and
Compact monochrome Explorer icons. Previous installed payloads and
reserved candidates are preserved.

The [icons and WEM goal](wem-conversion-goal.md) has distinct Convert
destination icons and both loose WEM-to-WAV and installed-Wwise audio-to-WEM paths
in source. Native shell, real-engine worker/publication, setup recovery and
representative BG3 decoding checks pass. The goal records the bounded input
matrix and remaining native UI/game acceptance; the installed application is
unchanged.

The subsequent WEM input extension adds MP3 and float32 WAV preparation in the
same pending-batch workflow, with one quality-consent prompt. It reuses the pinned
MP3 decoder and installed Wwise, keeps rate/channels and supported WAV loops, and
removes intermediate audio after use. The installed application remains unchanged.
