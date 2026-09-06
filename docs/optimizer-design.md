Context Optimizer Design
========================

Status: initial design; PNG is the first planned implementation.


Purpose
-------

Context Optimizer reduces file size while preserving the current format. It
makes the loss policy explicit, measures the result, and never treats a smaller
file as successful if it violates the selected quality or capability contract.


Explorer Experience
-------------------

**Optimize** is a top-level Explorer command. Its submenu shows only policies
supported by the complete selection. Initial PNG actions are:

- **Lossless** — pixels decode identically.
- **Balanced** — bounded visual loss under a documented quality floor.
- **Smallest** — stronger, clearly identified lossy reduction under its own
  quality floor.
- **More options...** — opens comparison, metadata, destination, and replacement
  controls.

The default output is a collision-safe sibling file. Replacing the source is an
explicit option and must use recoverable transactional publication.


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
- A cancelled or failed operation cannot damage or replace its source.


PNG Version 1
-------------

The initial PNG engine design uses independently selected and pinned tools:

- Lossless recompression through `oxipng` or an equivalently verified engine.
- Lossy palette quantization through `pngquant` or an equivalently verified
  engine, followed by lossless structural cleanup.

The production implementation must not resolve tools from `reference/`. Each
engine requires a pinned version, integrity hash, packaging location, supported
operation contract, and deterministic fixtures.

Lossless validation should decode source and output into a canonical pixel
representation and compare dimensions and pixels. Metadata equality is governed
separately because removing optional metadata may be part of the selected policy.


Preset Semantics
----------------

Each preset is a versioned policy, not a label over an open-ended retry ladder.
It defines:

- Whether pixel changes are allowed.
- Hard minimum quality or quantization limits.
- Dithering behavior where relevant.
- Metadata preservation or removal.
- Required preservation of alpha, animation, color information, and dimensions.
- A maximum acceptable output-size relationship.
- The exact validation contract.

Content-oriented variants such as **Crisp UI** and **Smooth gradients** may be
added after representative visual fixtures demonstrate that users can understand
and benefit from the distinction.


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
- A direct preset is offered only when every selected file supports its semantic
  contract. A mixed-format selection may be partitioned only in **More
  options...**, where the proposed groups are visible before execution.
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


Planned Image And Audio Expansion
---------------------------------

Later image optimization may include JPEG and WebP only after generation-loss
behavior and metadata handling are explicit. Re-encoding an already lossy file
must never be described as harmless.

Audio optimization should begin with lossless operations whose invariants are
clear, such as verified FLAC recompression. Lowering the bitrate of MP3, AAC, or
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
