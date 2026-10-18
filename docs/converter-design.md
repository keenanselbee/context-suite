Context Converter Design
========================

The [lecture PDF support goal](pdf-dark-lecture-support-goal.md), approved
2026-10-11, implements validated tagged, linked, passively attached and statically
layered lecture PDFs, actual font/glyph bounds and palette normalization within
already-dark pages. Policy `pdf-dark-6` passes the 25-document, 746-page corpus
and repeat no-op checks. It retains one PDF (Dark mode) destination, mandatory
copies and document semantics. Candidate 1.3.1 passes exact-worker and payload verification;
installed 1.2.9 / `pdf-dark-5` remains unchanged. Historical milestones below
retain their original scope and do not describe the current policy.

The `pdf-dark-7` follow-up adds bounded rounded backgrounds, proved text/outline
overlap handling, legacy direct links, local image compositing and screenshot
readability fallbacks. It also reports static failure categories and affected
pages. Five COSC 310 originals / 212 pages pass native and actual-worker checks
in isolated candidate 1.3.2. Remaining refusals and repair limits are recorded
in the same lecture support goal.

PDF (Dark mode), approved 2026-10-10, is implemented in source under the
[dark-mode conversion goal](pdf-dark-mode-goal.md). It creates mandatory
PDF copies through a distinct appearance-conversion action, preserving text and
vector geometry within tested feature bounds. Unsigned
[candidate 1.2.7](pdf-dark-mode-review.md) passes combined verification and fresh
extraction. The subsequent source-only `pdf-dark-2` update converts the complete
50-page lecture with actual-worker and rendered validation. A further source-only
`pdf-dark-3` update improves compressed monochrome and colored embedded diagrams,
with local text/image and colored-shape contrast guards. Its bounded layout
and transparency policy is documented in the goal; retained images may remain
light. It is not packaged or installed. Native UI and owner acceptance are
separate. This action is not in the older candidate 1.2.6.
The source-only `pdf-dark-4` follow-up strengthens small marks within colored
panels and reduces compression fringes, with no added destination or setting.
The source-only `pdf-dark-5` update reduces pale outlines around colored
annotation edges without changing the direct-action UI.

Compatibility expansion, approved 2026-10-09
-------------------------------------------

This expansion is implemented in unsigned candidate 1.2.6, verified 2026-10-10.
Exact combined-candidate regressions and fresh extraction passed; see the
[candidate review](compatibility-expansion-review.md) for identities, evidence
and remaining human acceptance. The installed 1.2.5 baseline below is unchanged.
Retain existing labels, destination icons and order. Explorer checks
filenames only; content validation stays in the worker. All new paths preserve
originals regardless of the overwrite preference.

- JFIF is a JPEG input alias, including mixed-case extensions, with validated
  JPEG content and existing JPEG-source targets. There is no rename action or
  JFIF destination. Reuse orientation, color and metadata policies.
- ICO to PNG extracts the largest embedded image by pixel area, then bit depth,
  then original directory order. Preserve alpha and never enlarge. The current
  bounded reader admits up-to-eight-bit PNG entries with supported color data
  and 40-byte BI_RGB DIB entries at 1/4/8/24/32 bits with complete AND masks.
  Limit input to 128 MiB and 256 entries, each at most 256 by 256. Unsupported
  selected entries fail explicitly; do not substitute a smaller image. Embedded
  ICC color is normalized to sRGB on an Automatic copy. Unsupported metadata,
  DIB headers/compression and higher-depth PNG remain explicit refusals. For
  32-bit alpha icons, follow [Windows drawing semantics](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-drawiconex)
  and ignore the legacy mask; all-zero alpha falls back to that mask. Refuse
  background-dependent legacy XOR effects that PNG cannot represent.
- PDF to JPEG converts every page at 150 DPI, quality 90, on white paper.
  Reuse numbered page copies, bounded geometry and retry of unfinished pages.
  Keep PDF-to-PNG and Word behavior.
- GIF to PNG exports every complete displayed frame, including duplicates, in
  order. Animated inputs create one collision-safe folder with numbered PNGs;
  static inputs create one PNG. Handle frame offsets, palettes, transparency
  and disposal, with bounded source/frame/pixel/output/time costs. Explain that
  PNGs preserve frame order, not playback timing. No first-frame option, extra
  submenu, timing manifest or rebuilding. Retain validated frames on partial
  failure/cancellation; retry only missing frames in the same output folder and
  refuse a changed source or substituted output folder. The implemented bounds
  are 128 MiB per source, 16,384 pixels per side, 40 million pixels per canvas,
  4,096 frames and 512 million displayed pixels per batch, 128 MiB per PNG,
  2 GiB of saved PNGs per batch and ten minutes of extraction per attempt.
  Earlier recovery-record links remain visible after retry. The bounded reader
  handles GIF87a/89a palettes, interlacing and disposal methods 0-3; unsupported
  rendering extensions, user-input frames and non-square pixel aspect ratios
  are refused rather than silently flattened.
- SVG to JPEG reuses Automatic/custom proportional sizing and font notices.
  The same compact size dialog includes an explicit white/black/custom RGB
  background choice for JPEG. Apply that choice to rendered transparency at
  quality 90, tag sRGB and validate the decoded output. Remember target, width
  and background on retry. Retain bounded local rendering and external-resource
  refusals; PNG retains alpha.
- MP4/MOV/WebM/MKV to WAV/MP3/FLAC reuses audio output commands and policies. Select
  a single track directly; show a compact language/name/channel picker when
  multiple audio tracks exist. Bind explicit stream identity through retries.
  Report no-audio/unsupported-codec inputs per file. Verify exact container and
  audio-codec variants, timestamps, delay/priming, duration, metadata/chapters,
  large WAV limits and malformed inputs. Preserve supported layouts and require
  consent for channel reduction and quality loss. Lossless output does not
  restore quality lost in the source. Video pixels are not converted. The current
  source implementation retains an absolute selected stream index and full
  source digest through retries. WAV/FLAC preserve supported surround layouts;
  MP3 requires explicit consent to mix surround audio to stereo. Chapters and
  attached artwork remain in the original and require omission consent. Standalone
  audio starts at the selected track's first presented sample; the video's start
  offset is not synthesized as silence. Decoder padding, packet continuity and
  exact presented extent are checked before publication. MOV-family inputs
  admit AAC, MP3, FLAC and PCM16; Matroska admits those codecs plus Opus and
  Vorbis (WebM's ordinary audio codecs). AC-3, E-AC-3, DTS and TrueHD remain
  unsupported. MKV was explicitly added by the owner on 2026-10-09. Inputs stay
  bounded to 512 MiB, selected packet/frame inventories to 65,536 entries and
  16 MiB of JSON each, and decoded reference audio to 1 GiB. Timestamp clocks
  must resolve at most 256 samples per tick. Each decoded frame is checked
  against the cumulative sample clock, avoiding accumulated rounding error on
  long Matroska recordings while refusing real gaps/overlaps. Container-declared
  sample-aligned duration trims at most one codec frame of terminal padding;
  without it, the verified decoded extent is retained. Ten-minute mono 8 kHz
  Vorbis/WebM and PCM16/MKV fixtures pass, not arbitrary-length movie support.

Exact-candidate evidence: JFIF adapter (28 checks), ICO input adapter (43),
mixed JFIF/ICO app publication with an independent Windows decoder (5), PDF
page workflows (33), PDF direct commands including partial JPEG retry (21),
private PDF raster (41), SVG PNG/JPEG adapter (115),
SVG worker/application including JPEG retry (18), GIF parser/compositor (31),
GIF adapter (38), GIF model and actual-worker publication/retry (31), audio and
video models/picker/IPC (154), foundation including integration (5,122), shell availability (663),
and compiled destination icons (336 frames). GIF workflow
checks include cancellation, worker termination, selective retry in the same
folder, source/folder substitution refusal, independent Windows PNG decoding,
and a mixed GIF/raster batch with one admission. Builds of the actual application
and worker pass. The corrected combined audio engine includes the timestamp
normalization filter and preserves all five earlier audio repairs. Fresh uniquely
named smoke outputs pass independent decoding for four video containers and six
audio codecs, twelve target outputs, four timestamp-filtered outputs and two
ten-minute recordings. This replaces the earlier invalid cross-container evidence
from colliding output filenames. Video adapter checks (71) cover selected-track
conversion, all admitted codec variants, long recordings, source changes and
timeline bounds, surround preservation/downmix, and chapter/artwork omission
consent; actual app/worker checks (30) cover selection, one admission,
mandatory copies, no audio, publication failure, cancellation, worker termination
and immutable video retry choices. Ordinary Office retry output preferences
retain their existing behavior.
The final replay uses staged Core/Private assemblies, the staged worker and
application, and pinned engines. All 40 replay suites pass, together with focused
PDF, AIFF/WMA/Voice and packaging checks recorded in the candidate review.
SVG model/application (50) remains reused development evidence; its JPEG worker
workflow, adapter, native renderer and interruption checks were rerun. Prompt
renders reuse the unchanged window source; copied output examples come from
the final candidate. These checks do not establish unrestricted movie support,
representative customer-media fidelity or installed native UI acceptance.

The final candidate includes independently pinned dependency payloads,
notices and required source/build materials, honest dirty-source provenance,
fresh-extraction verification, and applicable image/DDS/audio/WEM/PDF/Office,
shell/icon and publication regressions. Preserve reserved version payloads and
the installed 1.2.5 baseline. Installation, registration, signing, publication,
commits and unrelated cleanup are not part of this implementation authorization.


Earlier image expansion baseline
--------------------------------

Owner-approved image expansion, 2026-10-09: add ICO and still SDR AVIF output
from supported PNG/JPEG/WebP/BMP/TGA, classic TIFF to PNG/JPEG/PDF, and static
SVG to PNG. All four are implemented in unsigned review candidate 1.2.5. Only ICO and
AVIF add destination commands; TIFF and SVG reuse existing commands. TIFF keeps
every page as numbered images or one ordered PDF per source, always on copies.
ICO fits without distortion and pads transparently; SVG uses bounded local
rendering with a compact sizing choice and no external resources. Retain the
existing batch admission, cancellation, transactional publication and necessary
matte decisions. All four source implementations now have isolated adapter,
worker/publication and native shell evidence below. The combined archive passes
fresh-extraction and payload verification. See the
[exact-candidate evidence](launch-capability-matrix.md#image-expansion-candidate-125-2026-10-09)
and [owner review handoff](image-expansion-review.md). Native UI, representative
fidelity and commercial acceptance remain open. The owner subsequently requested
local installation of the verified 1.2.5 payload; see the review handoff.

The owner-approved [icons and WEM goal](wem-conversion-goal.md) adds distinct
destination icons and loose WEM-to-WAV / WAV-to-WEM paths. WEM encoding
uses a compatible user-installed Wwise Authoring tool with detection and a
Locate Wwise recovery prompt. Keep game/reference choices compact and preserve
the existing batch/publication boundaries. Both directions pass the actual
worker/application publication route with an optional pinned decoder payload;
the installed application is unchanged. Initial WEM admission is little-endian
PCM16/Vorbis, mono/stereo at 44.1/48 kHz with supported forward loops. Encoding
accepts PCM16/float32 WAV and MP3 at those rates/layouts, with bounded preparation
and no unsupported metadata. Float32 and decoded MP3 are prepared as PCM16 in
owned scratch after batch-level precision consent; MP3-to-Vorbis also requires
lossy-transcoding consent. The combined quality prompt appears once per batch.
Supported WAV forward loops survive preparation. Refuse nonfinite or out-of-range
samples rather than clipping them. Temporary audio is removed on completion or
cancellation. MP3 uses the pinned audio engine; WAV needs no FFmpeg installation.

WEM remains visible for WAV/WAVE/MP3 selections without querying Wwise in Explorer.
The focused setup prompt detects/locates and remembers WwiseConsole, then resumes
the original batch. Match original WEM selects the observable codec and requires
the WAV's rate/channels to agree; it does not copy original quality, encoder build
or bank settings. Advanced offers explicit PCM16 or high-quality Vorbis. No
unverified game presets are presented. Wwise 2023.1.10.8659 is the tested tool;
discovering another installation does not establish game compatibility.

Current scope: [decision 0018](decisions/0018-context-menu-utility-and-output-preference.md)
removes the manual conversion workspace and routine planner in favor of direct
format commands and focused necessary prompts. Copies stay the default; a new
explicit Settings choice applies safe replacement to future commands, subject
to mandatory copy exceptions. See the
[completed simplification goal](context-menu-simplification-goal.md) for verification.

The preceding expansion follows [decision 0019](decisions/0019-broad-file-analysis-and-media-expansion.md)
and the [broad file support goal](broad-file-support-goal.md): common audio through
an explicit tested container/codec input/output matrix. Images-to-PDF, PDF
pages-to-images and Word/Excel/PowerPoint-to-PDF are selected for launch; exact
variants, engines and fidelity follow the [document design](document-design.md).
The later owner-approved [PDF-to-Word workflow](pdf-to-docx.md) adds best-effort
editable DOCX copies for supported text PDFs; complex figures remain images.
Neither expansion is implemented by the existing image adapters.

The 2026-09-16 goal revision additionally requires HEIC and AVIF input to the
existing JPEG/PNG commands and AIFF/WMA input to the existing six audio targets.
HEIC/AVIF now have isolated adapter and actual-worker JPEG/PNG evidence, including
the existing direct-command workflow. Production capability adoption and packaging
remain pending. AIFF/WMA now have isolated worker and
[direct-command evidence](expanded-audio-direct-verification.md); formal adoption
remains pending. Their admitted variants,
color/HDR/alpha or audio precision/layout policies, metadata preservation and
engine packaging must be verified before capability registration. They do not
add output formats, Optimize presets or a general conversion planner. ALAC
remains a separate evaluation; see the current broad file support goal.

The isolated HEIF path now preserves ICC-only and bitstream color information,
uses declared conservative defaults for unspecified color on mandatory copies,
and refuses hidden HDR or inconsistent alpha transforms. Follow the
[color policy and evidence](heif-color-fallback.md) before production adoption.

Status: PNG/JPEG/WebP/BMP/TGA conversion, planner, preview and trial-gated publication
implemented and verified locally for the bounded slice; release checks remain open.

Direct PNG/JPEG/WebP/BMP/TGA menu targets run without a planner
when no meaningful decision is needed. Defaults are full size, automatic metadata,
JPEG quality 90 and lossless WebP. Copies are the default. Explicit schema-two
Overwrite originals settings authorize future replacement; old permission-only
settings migrate to copies. Automatic preserves normalized EXIF/XMP,
resolution and ICC where supported; unsupported extras and stale thumbnails may
be omitted quietly from the new copy. BMP/TGA receive sRGB pixels, not a discarded
color transform. Automatic omission forces a copy even when replacement was requested.
The optimizer's separately approved `fdEC` exception is unchanged.

Choosing a format accepts its ordinary encoding/precision limits on a copy; no
routine metadata acknowledgement or warning sound is required. Transparency
backgrounds and DDS still require explicit choices and preview. Unknown critical
PNG chunks, unsupported HDR/color interpretation and animation remain guarded.
An unsupported file does not block independent valid files; a mixed selection
needing a transparency background still opens the choices window.

The focused conversion dialog keeps its target fixed to the selected command,
with optional file details and preview. Only necessary transparency or DDS choices
are visible; DDS retains explicit descriptive-information handling. Preview work
starts when requested or needed, then refreshes after a 300 ms edit pause.
Expired access offers License and retry of the same selected command/files.
Standalone launch opens Settings with Explorer instructions and License.
See [current UX verification](quiet-first-ux-goal.md) for pending desktop acceptance.


Purpose
-------

Context Converter changes image and audio formats safely. It serves users who
know where a file needs to work but may not know which format, codec, quality
setting, or compatibility tradeoff best fits that destination.

Its promise is:

> Change media formats without hidden quality, transparency, metadata, or
> compatibility surprises.


Explorer Experience
-------------------

**Convert** is a top-level Explorer command. Its submenu shows only destinations
with a supported filename hint in the current selection. Hide the parent command
when no destination applies; Settings alone does not make it available. Mixed
selections retain destinations useful for at least one file, with independent
content validation and per-file unsupported results outside Explorer. Enumeration
checks only bounded selection metadata and filenames, never media contents,
engine availability or licensing.

A child action may begin immediately only when its plan is complete and safe.
If conversion requires a meaningful choice, Converter opens a planning surface.
Examples include choosing a matte for transparent-to-JPEG conversion, resolving
mixed animation support, or confirming lossy-to-lossy transcoding.

The submenu should not reproduce a large preset tree. Destination-oriented
recommendations and advanced settings belong in the planning surface.

A separated **Settings...** item comes last and opens the Convert section of the
shared settings window without starting a media operation.


Primary Jobs
------------

1. Convert an unsupported image into a broadly compatible format.
2. Convert a photographic PNG into JPEG or WebP intentionally.
3. Convert a transparent image to an opaque format with an explicit background.
4. Convert lossless audio into a smaller distribution format.
5. Decode compressed audio into an editing-compatible format without implying
   quality restoration.
6. Convert a batch with consistent settings and collision-safe output names.


Initial Image Scope
-------------------

The first image-release target is PNG, JPEG, WebP, DDS, TGA, and BMP. Only
bounded variants are supported. PNG/JPEG/WebP/BMP/TGA conversion has completed
its local acceptance, as has the bounded DDS implementation. Follow
[decision 0010](decisions/0010-first-release-formats-and-curated-engine.md) for
the curated general-image engine and separately selected DDS toolchain.

Magick.NET-Q16-x64 14.17.1 is pinned and tested for the first slice; broader
runtime/failure coverage and redistribution checks remain gates. See
[decision 0009](decisions/0009-first-image-engine-and-trial.md) for image/trial
policies and the [implementation goal](image-conversion-goal.md) for coverage.
DDS uses a pinned CPU DirectXTex bridge under
[decision 0011](decisions/0011-dds-engine-and-texture-policies.md). Its native
dependency is confined to the sequential worker. Current support and evidence
are recorded in the [DDS goal](dds-conversion-goal.md).

Primary conversions are:

- PNG to JPEG for smaller photographic uploads and broad compatibility.
- PNG to WebP for web delivery with optional transparency.
- JPEG to PNG for workflows requiring lossless subsequent edits, with a notice
  that existing JPEG loss is not restored.
- JPEG to WebP for web delivery.
- WebP to JPEG or PNG for software that does not accept WebP.

BMP/TGA input and output are implemented across all twenty cross-format pairs.
BMP accepts 24-bit uncompressed Windows INFOHEADER files; TGA accepts bounded
24/32-bit true-color raw/RLE files. BMP outputs require an explicit matte for
transparency; TGA retains alpha. Both outputs are eight-bit untagged sRGB with
automatic copy metadata handling and sRGB transformation. See the exact variant boundaries and
[verification evidence](bmp-tga-and-engine-integration.md). Engine coder
availability alone is not support.

ICO and still SDR AVIF output, classic TIFF input and static SVG rasterization
are selected by the 2026-10-09 scope above; only ICO currently has an isolated
implementation. HEIC/AVIF
input already has a separate bounded path. GIF, camera RAW and HEIC output
remain outside the selected expansion. Engine availability does not establish
tested behavior or redistribution readiness.


DDS targets cover ordinary 2D BC1/DXT1, BC2/DXT3, BC3/DXT5, BC4/BC5 UNORM/SNORM,
BC7, R8, RG8 and RGBA8/BGRA8. BC6H/HDR, typeless, premultiplied-alpha and
cube/array/volume conversion are deferred. Export is an explicitly selected
color mip to 8-bit sRGB PNG; numeric export needs a separate range policy. Linear/sRGB
interpretation must be explicit where supported, with pixel conversion distinct
from tag-only reinterpretation. Do not offer nonexistent sRGB variants for
BC4/BC5/BC6H or apply color transfer to alpha/data textures. Preserve supported
mips, faces, layers, and slices; reject unsupported structures instead of silently
flattening them. Exact encoding, mip-generation, and HDR policies belong in the
dedicated DDS implementation brief before advertising these capabilities.

An explicit representation change is conversion even when both filenames end in
`.dds`; an unchanged extension is not sufficient reason to skip it.


Image Safety Rules
------------------

- Detect alpha before targeting a format without transparency.
- Require an explicit matte color for transparent-to-JPEG conversion.
- Preview the matte against the image before conversion.
- Apply orientation before removing orientation metadata.
- Do not enlarge an image unless explicitly requested.
- Preserve color behavior where possible and warn when conversion changes bit
  depth, HDR, animation, or color capabilities.
- Treat animated inputs as unsupported until frame behavior is designed and
  tested.
- Never overwrite the source by default.


Planned Audio Scope
-------------------

The [fixed audio policy and worker integration](audio-conversion-policy.md) now
implement typed target/consent decisions and application-owned publication through
the real worker. All 30 cross-format pairs pass generated-fixture copy workflows;
six same-format cases remain unchanged without admission. Direct WAV, FLAC, MP3,
M4A (AAC), Ogg Vorbis and Opus menu actions now use fixed settings. Routine work
stays quiet; required lossy-transcoding, resampling or precision changes open one
compact decision for the batch. Closing License refreshes access without changing
the target, files or captured settings. Metadata completeness, visible acceptance,
independent fidelity/listening and production payload acceptance remain pending.
The scope below is still the
launch requirement.

The first audio release should analyze and accept WAV, FLAC, MP3, M4A/AAC, and
Ogg/Opus. Outputs may include MP3, M4A/AAC, FLAC, WAV, and Ogg/Opus only where
the selected engine and packaging support them reliably.

Destination contexts may include:

- **Play anywhere** — broadly compatible output.
- **Speech sharing** — compact, intelligible speech.
- **Music sharing** — compressed stereo with supported tags.
- **Editing master** — uncompressed or lossless output for tool compatibility.
- **Lossless archive** — lossless encoding without false quality claims.
- **Game sound effect** — editing- and engine-friendly output.
- **Looping game music** — avoids unsuitable padding behavior when seamless
  playback matters.


Audio Safety Rules
------------------

- Identify lossy-to-lossy transcoding before it occurs.
- Preserve tags and artwork by default when the target supports them.
- Explain channel, sample-rate, bit-depth, and bitrate changes.
- Do not default stereo music to mono.
- Do not describe decoded MP3-to-WAV output as higher quality.
- Treat a target file size as a constraint, not a promise of inaudible change.
- Keep loudness normalization, silence trimming, and other signal processing
  outside basic conversion.


Conversion Plan
---------------

Every operation should produce a typed plan before execution. The plan includes:

- Detected source properties and target format.
- Selected destination context or explicit settings.
- Expected capability changes and warnings.
- Metadata, orientation, transparency, animation, and color policies.
- Output directory, filename, collision behavior, and replacement policy.
- Engine and adapter selected for the conversion.
- Decisions still required from the user.

An incomplete or contradictory plan cannot execute.


Batch Contract
--------------

Selecting multiple files and invoking Convert creates one coordinated batch.

- One target format and shared policy apply to the batch unless the planning
  surface explicitly shows and confirms compatible subgroups.
- A direct **To ...** child is offered only when every selected file supports
  that target safely.
- The planner analyzes every file before execution and groups file-specific
  consequences, such as only some images requiring a matte.
- One queue, progress surface, and cancellation control represent the batch.
- Work uses bounded concurrency rather than one uncoordinated process or window
  per selected file.
- Each file receives its own reserved output, validation, and result.
- One failure does not discard validated outputs from unrelated files. The final
  summary distinguishes succeeded, failed, cancelled, and unsupported items.
- Cancelling stops pending work and requests cancellation of running work;
  already published outputs remain intact and are reported accurately.


Necessary Decision Dialog
-------------------------

The focused conversion dialog contains:

- The selected format and a concise selection/status summary.
- Fixed full-size/quality defaults; output follows captured Settings.
- Collapsed file details and an on-demand preview.
- Visible transparency-background controls only when the selection needs them.
- Explicit advanced DDS controls when working with textures.
- Meaningful decision acknowledgement; no repeated replacement-consent checkbox.
- Fixed Convert/Cancel actions and an activation action when access is blocked.

No manual file picker or drag-and-drop workspace is exposed. The main window
shows compact progress, problems and per-file results only when needed.
Legacy choose-format requests retain a focused target-choice compatibility path;
the current menu offers direct targets, including a dedicated DDS action.

Image conversions should support a zoomable before-and-after preview. Audio
conversions should first provide source and proposed-output summaries; A/B audio
playback can follow after engine behavior is reliable.


Execution And Results
---------------------

- Run batches with bounded concurrency and cancellation.
- Isolate per-file failures and continue files that can safely complete.
- Write to a unique temporary output and validate it before publication.
- Preserve the source and use a collision-safe sibling name by default.
- Treat explicit replacement as a recoverable transaction.
- Use `name - Converted.ext` or a meaningful DDS suffix such as
  `name - BC7-sRGB.dds`, numbering collisions from `(2)`. Preserve existing source
  suffixes. Follow [decision 0008](decisions/0008-output-naming-settings-and-replacement.md)
  for naming and replacement/recycling failure handling;
  [decision 0018](decisions/0018-context-menu-utility-and-output-preference.md)
  defines saved replacement consent and migration.
- Report actual output format, properties, size, warnings, and path.
- Offer useful next actions such as opening the folder, copying the output,
  retrying failures, or revising the plan.


Required First Image Release
----------------------------

- PNG, JPEG, WebP, TGA, and BMP conversion for explicitly tested variants/pairs.
- DDS representation conversion under the separately accepted texture policy;
  the target does not promise every BC format or texture structure at launch.
- Explorer activation of the complete selection.
- Multiple-file queue.
- Output recommendation with a concise explanation.
- Fixed reviewed quality defaults and original dimensions.
- Transparency detection and matte preview.
- Automatic metadata handling by default; strict preservation or descriptive
  removal remains a secondary option. Automatic is not a privacy scrub: supported
  EXIF/XMP (including location) is retained.
- Collision-safe naming and atomic publication.
- Cancellation and per-file failure handling.
- Completed-output validation.
- Before-and-after preview and result summary.


Non-Goals
---------

Converter does not own same-representation size optimization, video conversion,
cloud processing, AI editing, CD ripping, arbitrary engine commands, unrequested
source removal, permanent-delete fallback, or dozens of obscure formats without
demonstrated demand.


ICO Output Checkpoint, 2026-10-09
--------------------------------

PNG/JPEG/WebP/BMP/TGA to ICO reuses the existing image probe, immutable plan,
sequential worker and application publication path. The ICO command is appended
after existing destinations without changing their order. It always creates a
copy, including when Settings permits replacement, and needs no routine sizing,
quality or transparency prompt. ICO input conversion is not admitted.

Frames use 16, 24, 32, 48, 64, 128 and 256 pixel square canvases, stopping at the
first standard size at least as large as the source's longer side, or 256.
Each frame is aspect-fitted independently with centered transparent padding;
artwork is never enlarged. A 20 by 10 source therefore produces 16 and 24 pixel
frames, with the original 20 by 10 artwork centered in the 24 pixel canvas.
Orientation is applied once before sizing. Color profiles are transformed to
sRGB before eight-bit output; ICO stores untagged sRGB straight-alpha pixels.
Automatic metadata handling keeps the source while omitting unsupported
descriptive metadata and resolution. Strict preservation refuses those extras.

The output-only writer emits 32-bit uncompressed DIB frames with complete
one-bit masks; it adds no third-party coder or runtime dependency. A separate
bounded output reader checks every directory entry, frame header, pixel, mask,
offset and the complete extent before returning a publication hash. Limits are
the existing 128 MiB input, 16,384-pixel side and 40-megapixel image ceilings,
at most seven frames, a 512 KiB ICO artifact ceiling and the existing 120-second
conversion deadline. Native image memory/disk/thread limits remain unchanged.
Cancellation is checked during source reading, native processing, frame
construction, writing and validation; worker lifetime remains application-owned.

Isolated `ico-adapter-02.log` records 119 checks across all five sources, small
and large aspect ratios, exact alpha/padding, eight orientations, tagged 16-bit
color, preview, changed source, cancellation, corrupted output and nonuniform
downsampling with hidden-color/alpha-edge halo checks. The public
`ico-workflow-02.log` records 65 actual-worker checks, including independent
Windows decoding of every frame, direct quiet copies, mixed unsupported input,
dispatch cancellation, worker death, changed source and selective replay.
The first workflow run exposed WPF's preferred-size frame ordering; the test
now compares frames by size, retaining separate file-directory order checks.
Native shell checks record 320 decoded glyph frames and 368 filename/target
availability checks, plus selection transfer and missing-asset fallback.
The broader `ico-foundation-01.log` records 4,959 passing foundation contracts
against the isolated worker, including existing image/DDS/PNG native interruption
checks and the new ICO planner/UI/workflow contracts. After that run, a test-only
addition in `ico-active-01.log` passes 13 ICO cancellation, process-death and
deadline checks during an observed active large-image request; it verifies CPU
activity, not a specific native instruction boundary. App, worker and test
projects compile without warnings. The private image test lockfile was refreshed
to reflect its existing PDF dependency before a locked build passed.
These local artifacts are under `.codex-temp`; final combined packaging,
installed Explorer and owner visual acceptance remain open.


TIFF Input Checkpoint, 2026-10-09
-------------------------------

Classic TIFF now uses the existing PNG, JPEG and PDF destinations. PNG and JPEG
create a numbered copy for every supported page; PDF creates one ordered copy
per TIFF, retaining source document boundaries even in mixed selections.
Transparent JPEG pages require a background choice. No routine TIFF settings
dialog or new destination command is added. Page results retain committed copies
across cancellation and retry; a changed source requires a fresh command.

The decoder is Windows' explicitly selected built-in TIFF WIC codec, preceded by
a bounded independent directory and strip-extent reader. Both classic TIFF byte
orders, bilevel and eight-bit grayscale/RGB/RGBA are admitted. The tested codecs
are uncompressed, LZW, ZIP and PackBits, plus CCITT Group 3/4 for bilevel pages.
Independent fixtures additionally verify white/black grayscale interpretation,
reversed bilevel bit order, associated/unassociated alpha, multiple strips and
horizontal prediction with LZW/ZIP. BigTIFF, tiled/separate-plane images,
sub-images, palette/CMYK, floating-point and higher-precision samples remain
explicitly unsupported. Unsupported pages can leave useful numbered copies;
an incomplete TIFF never produces a partial PDF disguised as the full document.

Orientation is applied once. RGB and gray ICC profiles are transformed to sRGB,
then samples are materialized as eight-bit pixels consistently for all three
targets. PNG retains exact normalized pixels and alpha; JPEG uses quality 90
and the chosen opaque background. PDF stores lossless ordered sRGB samples and
alpha masks. Physical resolution follows orientation, including swapped axes;
PDF uses 96 DPI for absent resolution and preserves pixel aspect for unitless
resolution. PDF physical-size limits are checked before admission. Outputs are
mandatory copies; descriptive TIFF metadata and auxiliary representations are
not carried into the new format. Original bytes remain available in the TIFF.

Limits are 128 MiB per source, 128 pages, 16 megapixels per page, a 16,384-pixel
side, 128 megapixels per document and 512 megapixels/4,096 pages per batch.
Artifacts are bounded to 128 MiB each and 2 GiB of published TIFF output per
execution. The sequential worker enforces a 1.5 GiB process-commit limit during
TIFF operations; no additional TIFF decoder package is shipped. Probe/conversion
requests have a 120-second deadline and previews 30 seconds. Cancellation checks
surround native work and each decoded row band; application-owned worker
termination remains the fallback for an unresponsive native call.

The isolated `tiff-adapter-09.log` records 653 passing checks. Completed PDFs are
parsed independently by the pinned qpdf validator against both writer samples
and an independently authored fixture oracle for page order, pixels, alpha,
profile and physical dimensions. Color-profile fixtures additionally compare
against independently prepared color transforms. Refusal tests cover truncated
directories/data, cycles, BigTIFF, oversized dimensions, unsupported precision
and profiles, changed sources, cancellation and occupied output reservations.
An earlier run found fractional Q16 color-transform samples despite an eight-bit
depth declaration; explicit sample materialization fixed the grayscale-profile
PNG mismatch. Review also moved PDF physical geometry checks ahead of admission
and made unsupported ICC failures page-specific.

The public logs record 13 plan/transport/retry checks, 19 actual-worker/app
workflow checks and 13 active-worker interruption checks. Cancellation, worker
death and an injected deadline occur after observing worker CPU activity, not
at a claimed native instruction boundary. They verify that no partial output
is published and that a later request starts a fresh worker. Native shell
contracts pass the expanded 400 availability checks and isolated COM activation.
The current isolated app/worker/validator composition passes 4,973 foundation
contracts in `tiff-foundation-final-20261009.log`. An earlier test-folder copy
overwrote the worker's current private assembly with an older app-side DLL;
that failed run is retained separately as `tiff-foundation-20261009.log`.
Restoring the correct assembly fixed startup before the successful rerun.
The generic raster-PDF refactor also passes the existing 133 image-PDF candidate
and 45 native validator checks, plus 13 focused reuse checks. The local
`.codex-temp/tiff-engineering-checkpoint.json` binds source, binary and evidence
hashes without creating or modifying a reserved payload. Final combined
packaging and owner/native UI acceptance remain open.


AVIF Output Checkpoint, 2026-10-09
--------------------------------

The isolated source implementation adds still SDR AVIF copies from PNG, JPEG,
WebP, BMP and TGA. The command follows ICO without changing earlier menu order.
Its Compact glyph is an octagon with a diamond cutout, distinct from the other
image targets. Explorer checks filenames only; application availability requires
both the encoder and existing HEIF decoder. Missing engines produce a per-file
result without publication. Routine conversion retains original dimensions and
uses the existing automatic metadata policy, immutable batch settings, one
admission, sequential worker and application-owned transactional publication.
AVIF is copy-only even when Settings permits overwriting originals. Existing
HEIC/AVIF input continues to target PNG/JPEG; this adds no AVIF re-encoding command.

The independently sourced encoder is libavif 1.4.2 with AOM 3.15.1, statically
linked into the private x64 bridge. The
[build recipe and notices](../tools/avif-engine/README.md) identify exact source
archives and reproduce a fresh source build without reference-project assets.
The selected DLL SHA-256 is
`D31E4A28AE6C9CD73E6CF96A86805BFC22915329D600A73D420E991239C6B77B`.
Build evidence lives in `.codex-temp/avif-native-agent/full-build-quality95`.
Separate build paths need not produce identical DLL hashes; the runtime pins
this concrete build. Shipping source materials and final payload integration
remain part of the combined packaging milestone.

The fixed policy is quality 95, speed 6, at most two encoding threads, eight-bit
YUV444/full-range sRGB and lossless straight alpha. ICC transforms occur before
eight-bit quantization; orientation is applied once. Supported EXIF/XMP and
resolution survive normalized preservation, with stale color declarations
changed to sRGB. Descriptive removal retains the output color profile. Higher
source precision is disclosed as a copy conversion limit. The separate existing
HEIF reader validates every result's geometry, color declarations, exact ICC,
normalized EXIF/XMP, exact alpha and visible RGB RMSE at most 6 on the 0-255 scale.
This is a numerical quality gate, not a claim of perceptual or lossless RGB
equivalence. The HEIF reader has its own pinned payload and shares AOM codec
lineage; it is not claimed to be an unrelated AV1 implementation.

Limits retain the 128 MiB source/output cap, 16,384-pixel side and 40-million-pixel
image ceiling. Each metadata payload is bounded to 4 MiB. Conversion and preview
use a 3 GiB process memory limit; the sequential worker enforces its 120-second
conversion deadline and active cancellation. Native encoding itself polls
cancellation before/after its blocking call, so the worker process supplies
active interruption. The shared memory-scope refactor retains TIFF's 1.5 GiB
budget and passes all 653 TIFF adapter checks again.

Current procedural fixture measurements, including preparation and independent
output validation within the conversion call, are:

| Fixture | Conversion | Output bytes | Visible RGB RMSE |
| --- | --- | --- | --- |
| 4000 x 3000 scene | 5.77 s | 4,820,289 | 1.063 |
| 4000 x 3000 random noise | 15.94 s | 25,947,299 | 3.059 |
| 1024 x 1024 transparent line art | 1.28 s | 63,347 | 0.889 |
| 8000 x 5000 scene, pixel ceiling | 17.90 s | 15,946,660 | 1.053 |

All four retain exact alpha and unchanged source bytes. The largest test process
peaks at 1,944,248,320 committed bytes, including fixture generation and later
decoding; this is not a native-encoder-only measurement. These authored patterns
do not substitute for owner review of representative photographs or artwork.
The earlier quality-90 noise run failed the selected RMSE gate at 6.797. Raising
quality fixes that case rather than relaxing the limit, with larger output as
the tradeoff. An earlier unfiltered 40 MP PNG exceeded the compressed source cap;
PNG Sub filtering keeps the same authored pixels within that unchanged cap.

Current logs under `.codex-temp` record 108 adapter checks, 20 native ABI checks,
11 loader checks across valid/missing/tampered/extra/preloaded engines, 87 public
planning/UI checks, 33 actual-worker workflow checks, 14 active interruption
checks and 137 existing HEIF interoperability checks with 20 outputs. The adapter
matrix covers all five sources, all eight orientations, descriptive metadata,
resolution, RGB/grayscale ICC, higher precision, resize, tiny/odd geometry,
changed sources and pre-cancellation. Testing exposed the existing HEIF input
path's missing EXIF identifier: it now parses normalized tags and refuses a
conflicting orientation, with a dedicated regression. Worker tests cover mixed
selection, single admission, copy-only Settings behavior, selective retry and
missing engines. Active faults follow observed CPU activity without claiming a
particular native instruction boundary.

The broader `avif-foundation-95.log` records 4,998 passing checks before the final
application-startup test encounters an inaccessible same-name process during
module enumeration. The harness now tolerates inaccessible/exited unrelated
processes while still requiring discovery and parent-exit cleanup of the exact
owned worker before its deadline. The focused rerun in `avif-app-smoke-95.log`
passes actual WPF startup, repeated activation, request consumption and worker
cleanup. These are separate results, not a claimed uninterrupted foundation pass.
The current source/runtime/evidence inventory is retained in
`.codex-temp/avif-engineering-checkpoint.json`; it also records the failed runs
and the test-only harness change after the broader run.

Native shell checks pass 425 availability assertions, isolated COM activation
and all 336 icon frames. The rendered light/dark icon comparison was inspected;
installed Explorer, contrast/DPI/keyboard behavior and owner fidelity acceptance
remain open. At this AVIF checkpoint SVG implementation, final combined packaging
and regression/review handoff were still required for the full goal. Installed 1.2.3 and reserved 1.2.4
remain unchanged.


SVG Input Checkpoint, 2026-10-09
------------------------------

Static SVG now targets the existing PNG command. Explorer examines filenames
only. The application probes each source, then offers one compact Automatic or
Custom width choice for the selection, with necessary font substitutions shown
before admission. Height follows each source's aspect ratio. Automatic keeps
ordinary dimensions, reduces oversized artwork to the processing bounds, and
produces at least one pixel per side. Integer rounding uses uniform rendering
and transparent residual canvas space. SVG always produces a copy, including
when Settings permits replacing originals. Changed source or font facts require
a new command; retries retain the selected width and skip completed copies.

The private renderer uses independently sourced resvg 0.48.1 and locked Rust
dependencies. Its [build recipe](../tools/svg-engine/README.md) checks 63 official
crate archive hashes, 2,336 extracted source files, the Windows x64 Rust 1.98.1
host, static C runtime and 122 dependency license texts. The selected executable
SHA-256 is `1348469816E870C68857423BBBEAB045FAB45BA848E085079A866A358004B090`.
Matching source-verification and build-provenance receipts are retained under
`.codex-temp/svg-engine-build/dc414c0757e94b5f87212f86ed0f06c7`. Build paths affect
Rust executable bytes, so a fresh rebuild need not have the same executable
hash; runtime verification binds this exact adopted artifact. No reference
project binary, code or asset is used.

Supported bounded content includes shapes, paths, static CSS, local reuse,
linear/radial gradients, clipping, masks, patterns, text, selected filters and
embedded eight-bit PNG/JPEG. DTDs, unsafe XML, scripts, animation, foreign HTML,
external files/network references, cycles, malformed geometry, missing glyphs
and renderer warnings are refused. Embedded images with unsupported animation,
precision or color metadata are refused. Every embedded image is decoded and
bounded before rendering; images in definitions and paint resources do not
have a one-for-one top-level tree count. Independent pixel checks cover their
admitted reuse, mask, pattern and clipping cases. This is a documented static
subset, not general browser SVG support.

Input is limited to 1 MiB, 2,000 elements and XML depth 64. Up to eight embedded
images are admitted, at most 384 KiB each, one megapixel each and two megapixels
combined. Ordinary output is limited to 4,096 pixels per side and 16 megapixels;
filtered output to 1,024 per side and one megapixel. The source viewport may be
larger so Automatic can reduce it. Batches admit at most 1,024 SVGs and 512
megapixels of planned output. PNG output is capped at 128 MiB. The managed
adapter uses a 1.5 GiB process limit, a bounded child process and a 90-second
native deadline inside the sequential worker's 120-second deadline.

Only a fixed set of Arial, Segoe UI, Times New Roman and Courier New files is
loaded from Windows. Actual font bytes are fingerprinted; no fonts are shipped.
The child receives source bytes through a read-only inherited handle and returns
straight RGBA8 pixels. The adapter encodes eight-bit sRGB PNG, retains the ICC
profile explicitly, checks every decoded RGBA byte and the encoded geometry,
then returns a validated temporary result for application-owned publication.
The child lifetime/job arrangement is not an operating-system filesystem or
network sandbox; external resource access is disabled by the renderer boundary.

The adopted engine passes 50 native semantic/adversarial checks. Isolated
managed evidence passes 83 adapter checks, 15 engine-loader checks across five
scenarios, 44 public model/application checks and 11 actual-worker workflow
checks. These include color, transparency, font disclosure, custom sizing,
changed facts, occupied reservations, malformed/oversized input, mixed raster
and SVG selection, one decision/admission, cancellation before conversion and
selective retry after a publication fault. Shell checks pass 476 filename-only
availability assertions, isolated COM activation and all 336 icon frames.
Another 16 actual-worker interruption checks observe the renderer by exact path
and worker parent PID, then confirm its exit after cancellation, worker-only
termination and deadline expiry. Each fault preserves the source, publishes no
output and permits a fresh worker for the next request.

Maximum-canvas fixtures pass independent PNG pixel checks: a 4000 x 4000
gradient converts in 1.50 seconds to 76,621 bytes; a filtered 1000 x 1000 square
in 0.23 seconds to 10,806 bytes. The adapter test process peaks at 557,625,344
working-set bytes across both cases, including encoding and validation; this
does not measure aggregate native-child memory. These simple procedural
fixtures are not representative-artwork fidelity acceptance.

This checkpoint records the isolated SVG evidence. The subsequent
[combined 1.2.5 candidate](launch-capability-matrix.md#image-expansion-candidate-125-2026-10-09)
and [visual/keyboard/DPI/fidelity handoff](image-expansion-review.md) bind the
integrated results separately. Native UI and owner acceptance are not claimed.
