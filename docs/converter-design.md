Context Converter Design
========================

Current scope: [decision 0018](decisions/0018-context-menu-utility-and-output-preference.md)
removes the manual conversion workspace and routine planner in favor of direct
format commands and focused necessary prompts. Copies stay the default; a new
explicit Settings choice applies safe replacement to future commands, subject
to mandatory copy exceptions. See the
[completed simplification goal](context-menu-simplification-goal.md) for verification.

Next scope follows [decision 0019](decisions/0019-broad-file-analysis-and-media-expansion.md)
and the [broad file support goal](broad-file-support-goal.md): common audio through
an explicit tested container/codec input/output matrix. Images-to-PDF, PDF
pages-to-images and Word/Excel/PowerPoint-to-PDF are selected for launch; exact
variants, engines and fidelity follow the [document design](document-design.md).
Neither expansion is implemented by the existing image adapters.

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

**Convert** is a top-level Explorer command. Its submenu shows only output
formats compatible with the complete current selection, using plain labels such
as **To JPEG** and **To WebP**.

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

Later candidates include TIFF, GIF, ICO, and AVIF input/output. HEIC/HEIF input,
camera RAW, and SVG rasterization need separate scope and redistribution
decisions. Add formats for demonstrated use cases and tested behavior rather
than format-count marketing.

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
