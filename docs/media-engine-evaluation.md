Media Engine Evaluation
=======================

Review date: 2026-09-09. Status: source-based comparison plus an
[isolated audio experiment](audio-engine-evaluation.md) and
[qpdf inspection/recompression experiment](pdf-engine-evaluation.md). No new production
dependency or document-engine acceptance yet.

The owner selected images-to-PDF, PDF pages-to-images, PDF optimization and
Word/Excel/PowerPoint-to-PDF for launch. These accompany common audio conversion
and FLAC optimization in the [active goal](broad-file-support-goal.md).
This comparison selects the next experiments, not a reduced launch scope.

Candidate comparison
--------------------

| Component | Relevant capability and evidence | Main integration cost | Evaluation direction |
| --- | --- | --- | --- |
| FFmpeg / ffprobe | Structured stream probing and audio encoding, including native AAC/FLAC and wrappers for LAME, libvorbis and libopus; actual availability depends on build configuration | Pin complete build and dependent libraries; constrain formats/protocols; verify tags, sample fidelity and precise codec/container pairs | Evaluate an independently sourced audio-focused build first |
| Separate audio tools | A narrower codec-specific executable could handle one target | Multiple process adapters, probes, metadata mappings and packaging/update paths would still be needed for the required matrix | Retain as fallback for an actual FFmpeg gap, especially FLAC metadata preservation |
| qpdf | Structural PDF transformations; not a renderer or a content-layout engine | Bound subprocesses and output; validate preserved document structure independently | First candidate for structural PDF optimization and auxiliary inspection |
| PDFium | PDF parsing and page rasterization, with build switches disabling JavaScript and XFA | Native build/dependency inventory, private wrapper and bitmap limits; visible forms/annotations need explicit handling | First candidate for PDF facts and PDF-to-image rendering with JavaScript/XFA disabled |
| PDFsharp | PDF construction and drawing JPEG/PNG/BMP images | Another dependency; evaluate page geometry, ICC/orientation and alpha fidelity | Candidate for images-to-PDF; compare with PDFium image-page creation before adding a separate writer |
| LibreOffice | Headless conversion and configurable PDF export | Full rendering payload, fonts, isolated profiles, external-link/macro restrictions and document layout differences | First standalone Office-to-PDF candidate; requires actual offline/fidelity and unpacked-payload experiments |
| Installed Microsoft Office automation | Reuses a user's installed document renderer | Version/licensing/environment dependency, potentially interactive dialogs and customer application state | Do not make it the default dependency; desktop automation is distinct from Microsoft's unsupported server automation scenario |

Capability sources: [FFmpeg codecs](https://ffmpeg.org/ffmpeg-codecs.html),
[ffprobe output controls](https://ffmpeg.org/ffprobe.html),
[qpdf scope](https://qpdf.readthedocs.io/en/stable/overview.html),
[PDFium build and rendering](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/README.md),
[PDFsharp bitmap handling](https://docs.pdfsharp.net/PDFsharp/Topics/Bitmap-Images/Drawing.html),
[LibreOffice startup parameters](https://help.libreoffice.org/latest/en-US/text/shared/guide/start_parameters.html),
[LibreOffice export controls](https://help.libreoffice.org/latest/en-US/text/shared/guide/pdf_params.html),
[Microsoft unattended automation considerations](https://learn.microsoft.com/en-us/office/client-developer/integration/considerations-unattended-automation-office-microsoft-365-for-unattended-rpa).

The evaluation directions are engineering judgments from those capabilities.
The audio experiment now records a narrow 36-pair fixture matrix, process timing,
working set and payload size, plus metadata failures that constrain integration.
The qpdf experiment records a narrow structural rewrite, unchanged decoded streams,
timing/payload sizes and a signature-summary omission. Other document candidates
remain unmeasured. Do not replace measurements with upstream
marketing or a reference application's installed size. Rendering and structural optimization
need different acceptance oracles even if one package eventually supplies both.

Redistribution review
---------------------

Upstream licensing is only the starting inventory: FFmpeg configuration can
change its applicable license, qpdf identifies Apache-2.0, PDFsharp identifies
MIT, PDFium has a BSD-style top-level license plus dependencies, and LibreOffice
publishes its source and bundled-library licensing information. Record exact
versions, source archives/hashes, build configuration, transitive components,
notices, source-delivery obligations and updates for the actual payload before
production adoption. Subprocess separation alone does not settle distribution
obligations. Avoid claiming that any combination is commercially cleared.
[FFmpeg terms](https://www.ffmpeg.org/legal.html),
[qpdf license](https://qpdf.readthedocs.io/en/12.1/license.html),
[PDFsharp license](https://docs.pdfsharp.net/General/License/License.html),
[PDFium license](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/LICENSE),
[LibreOffice licenses](https://www.libreoffice.org/licenses/).

First document experiments
--------------------------

| Selected action | Initial fixed policy to evaluate | Evidence required before enabling |
| --- | --- | --- |
| Images to PDF | Owner selected one combined PDF on 2026-09-10. Multiple images require a focused page-order review; one image goes directly to one page. Preserve oriented physical dimensions, full samples, ICC and alpha on a mandatory copy; see [candidate and ordering policy](image-pdf-candidate.md) | Page size/count, orientation and ICC/color checks; rendered comparison for opaque/transparent inputs; reliable Unicode names; all-source validated copy publication |
| PDF pages to images | All pages to PNG at a fixed 150 DPI trial preset; stable page-number suffixes. Account for crop/rotation and page geometry; no implicit JPEG loss | Reference render comparisons, small text/vector lines, mixed page sizes, rotation, transparent content, annotations and stored form appearances; page/pixel/memory limits; all-output staging and cancellation recovery |
| PDF optimization | Structural recompression only initially; no downsampling, JPEG re-encoding, font substitution, page deletion or rasterization. Publish only a validated smaller copy | Page render equality plus structural inventories for links, forms, bookmarks, attachments and accessibility tags; reject signed inputs for rewriting; explicitly handle encryption without removing it silently |
| Word to PDF | Begin with DOCX and DOC fixtures; preserve authored pagination, headers/footers, tables, fields and images. No macros or external updates | Native/reference baseline PDF comparisons, missing-font behavior, tracked changes/markup policy, bidirectional text and font embedding restrictions |
| Excel to PDF | Begin with XLSX and XLS fixtures; honor authored print areas, sheet visibility, paper size, scaling and page breaks | Formulas with cached values, charts, wide sheets, mixed print areas, hidden sheets, page headers, external workbooks and printer-independent layout |
| PowerPoint to PDF | Begin with PPTX and PPT fixtures; printable visible slides with authored slide size | Text/shapes/images/chart fidelity, theme fonts, grouped/rotated objects, notes/hidden-slide policy and a clear static-output limitation for animation/media |

These are starting fixture variants and proposed presets, not an accepted final
capability matrix. Do not remove a named Office family when a candidate struggles.
Evaluate XLSB, templates and macro-enabled variants separately; never execute their
active content. OpenDocument analysis remains required, while conversion is not
in the owner-selected transformation set.

For qpdf, first investigate object-stream generation and lossless stream
recompression, with image optimization disabled. A successful exit or structural
check is not proof that an entire document remains visually/semantically equal.
The tool documents content-preserving structural options separately from lossy
image recompression. [qpdf options](https://qpdf.readthedocs.io/en/stable/cli.html).

The [initial LibreOffice evaluation](office-engine-evaluation.md) records the
26.2.6 pin, read-only MSI extraction, payload footprint, three passive modern
Office fixture results and an unresolved profile-path failure. It does not adopt
the engine or establish arbitrary-document isolation and broad layout fidelity.

For LibreOffice, explicitly set image compression, resolution, tagging, form,
notes/hidden-slide and sheet export options. Do not inherit defaults silently.
In particular, the documented single-page-sheets option ignores print ranges and
hidden state, so it is unsuitable as an ordinary export default. Build disposable
profiles per job; a profile switch and headless flag are not a security sandbox.
[PDF export options](https://help.libreoffice.org/latest/en-US/text/shared/guide/pdf_params.html).

Audio experiment matrix
-----------------------

The first input corpus must include PCM WAVE (integer/float and extensible), FLAC,
MP3, M4A/AAC, Ogg Vorbis and Opus. Cross these with every selected output below;
record each actual pair as passed, restricted or failed rather than a broad
"audio supported" flag. ALAC, AIFF, WMA and APE remain additional input evaluations.

| Target | Starting encoder/preset experiment | Main evidence |
| --- | --- | --- |
| WAVE | PCM representation preserving decoded precision and layout | Exact samples for lossless sources; no integer truncation or float clipping |
| FLAC | Native FLAC, fixed compression level after timing tests | Exact samples and meaningful metadata; unsupported floating-point/precision requires a decision, not hidden quantization |
| MP3 | LAME, fixed high-quality VBR candidate | Listening and decoded-signal checks, delay/padding/gapless behavior, channel/rate restrictions and tag/artwork roundtrip |
| M4A/AAC | Native AAC-LC in MP4/M4A, fixed reviewed rate | Channel/rate-specific quality, duration/padding, cover art and tags; do not equate M4A with a codec |
| Ogg Vorbis | libvorbis, fixed quality candidate | Rate/layout, tags, artwork, decoded duration and listening |
| Opus | libopus in Ogg, fixed music preset candidate | Sample-rate conversion consequences, pre-skip/end trim, channel mapping, tags and listening |

For FLAC Optimize, compare decoded samples plus required metadata and retain
originals whenever the result is not smaller or preservation fails. Copying a
generic engine's metadata options is insufficient for cuesheets, application
blocks and artwork. Lossy-to-lossy conversion retains the required focused consent.

Execution and measurement gates
-------------------------------

1. Acquire independently from upstream or a reviewed build supplier into
   repository-local isolated staging; record hashes, source, version, build flags,
   dependency licenses and exact file inventory. Never reuse reference binaries.
2. Run a disposable smoke corpus without installed-app or shell changes. Measure
   startup, per-file time, peak memory and payload size on this machine. Label
   measurements with the exact binary and fixture hashes.
3. Implement worker-owned bounded subprocess lifetime, kill-on-cancel, memory/CPU
   and output limits. Analyze must remain available without licensing admission;
   transformations reuse existing paid-access rules. Disable script/macro execution
   and external resource retrieval and verify attempted access with hostile inputs.
4. Stage only job-owned inputs/outputs. Do not let a rendering engine modify user
   source files, a customer's existing Office/LibreOffice profile or network files.
   Application-owned validation and publication remain authoritative.
5. Add independent semantic/render/sample validation and mixed-batch recovery,
   then fixed action definitions and the minimal necessary prompts. Separate
   operational restrictions from catalog descriptions.
6. Refresh isolated production staging only after the chosen implementations
   pass their matrix. Record visible and keyboard acceptance separately.

No experiment has passed merely because its requirements are listed here.
Failure of one candidate means evaluating another approach while preserving the
selected launch scope. Signing, installation, live commerce and publication remain
separate authorization/release gates.
