Context Converter Design
========================

Status: PNG/JPEG/WebP/BMP/TGA conversion, planner, preview and trial-gated publication
implemented and verified locally for the bounded slice; release checks remain open.

Direct PNG/JPEG/WebP/BMP/TGA menu targets now create safe copies without a planner
when no meaningful decision is needed. Defaults are full size, preserved metadata,
JPEG quality 90 and lossless WebP. Existing replacement preferences never authorize
a quick action to remove an original. Transparency backgrounds, metadata changes,
precision reduction, lossy-to-lossy processing and DDS still require the preselected
planner. Informational notices about lost detail not being restored or untagged
BMP/TGA sRGB are not extra confirmation gates. Resolution-only normalization also
proceeds quietly; EXIF/XMP normalization still requires review because it can remove
embedded thumbnails. The planner refreshes previews after
a 300 ms pause in edits; size/quality and technical file details are secondary.
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
explicit metadata/color-loss policy. See the exact variant boundaries and
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


Full Planning Surface
---------------------

The Converter window should contain:

- File and folder input plus drag-and-drop.
- A queue with source properties, intended output, status, and warnings.
- A destination-context or output-format selector.
- A concise conversion-plan summary.
- Image quality and maximum-dimension controls where relevant.
- Transparency, metadata, and output naming controls.
- An Advanced section for technical settings.
- A primary Convert action.
- A result view with output size, important property changes, failures, and next
  actions.

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
  for settings, per-batch consent, and replacement/recycling failure handling.
- Report actual output format, properties, size, warnings, and path.
- Offer useful next actions such as opening the folder, copying the output,
  retrying failures, or revising the plan.


Required First Image Release
----------------------------

- PNG, JPEG, WebP, TGA, and BMP conversion for explicitly tested variants/pairs.
- DDS representation conversion under the separately accepted texture policy;
  the target does not promise every BC format or texture structure at launch.
- Explorer activation, drag-and-drop, and file selection.
- Multiple-file queue.
- Output recommendation with a concise explanation.
- Image quality and maximum-dimension controls.
- Transparency detection and matte preview.
- Explicit metadata preservation or removal.
- Collision-safe naming and atomic publication.
- Cancellation and per-file failure handling.
- Completed-output validation.
- Before-and-after preview and result summary.


Non-Goals
---------

Converter does not own same-representation size optimization, video, documents,
cloud processing, AI editing, CD ripping, arbitrary engine commands, unrequested
source removal, permanent-delete fallback, or dozens of obscure formats without
demonstrated demand.
