Context Converter Design
========================

Status: migrated and adapted from the original Context Converter product design;
no implementation exists yet.


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

The first Converter release should accept and produce PNG, JPEG, and WebP.

Primary conversions are:

- PNG to JPEG for smaller photographic uploads and broad compatibility.
- PNG to WebP for web delivery with optional transparency.
- JPEG to PNG for workflows requiring lossless subsequent edits, with a notice
  that existing JPEG loss is not restored.
- JPEG to WebP for web delivery.
- WebP to JPEG or PNG for software that does not accept WebP.

Later candidates include HEIC input, AVIF input and output, TIFF, BMP, GIF, SVG
rasterization, and ICO. Each format should be added from demonstrated use cases
and tested behavior rather than format-count marketing.


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
- Report actual output format, properties, size, warnings, and path.
- Offer useful next actions such as opening the folder, copying the output,
  retrying failures, or revising the plan.


Required First Image Release
----------------------------

- PNG, JPEG, and WebP conversion.
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

Converter does not own same-format optimization, video, documents, cloud
processing, AI editing, CD ripping, arbitrary engine commands, automatic source
deletion, or dozens of obscure formats without demonstrated demand.
