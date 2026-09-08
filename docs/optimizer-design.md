Context Optimizer Design
========================

Status: bounded lossless PNG recompression implemented under
[decision 0013](decisions/0013-lossless-png-optimization.md). See
[goal evidence](png-optimization-goal.md). Bounded [Balanced/Smallest presets](png-lossy-presets.md)
are implemented under [decision 0014](decisions/0014-png-precision-presets.md).
Direct Auto/Lossless/Balanced/Smallest quick actions and lossless fallback are
implemented. Interactive release acceptance and selectable metadata policies remain planned.

Accepted next direction: [decision 0015](decisions/0015-simple-context-menu-workflows.md)
and the [quiet-first UX plan](quiet-first-ux-goal.md) replace mandatory planning
with direct Auto-first presets and best-effort candidate selection. This includes
saving a useful lossless fallback when it wins. The initial slice is implemented
with local policy, worker, shell and isolated quiet-launch evidence; human
acceptance and wider PNG compatibility remain pending.


Purpose
-------

Context Optimizer reduces file size while preserving the current format. It
makes the loss policy explicit, measures the result, and never treats a smaller
file as successful if it violates the selected quality or capability contract.


Explorer Experience
-------------------

**Optimize** is a top-level Explorer command. Its submenu shows only policies
supported by the complete selection. Direct preset IDs execute copy-only batches;
the bounded shell currently validates selection count, while content support is
checked outside Explorer and inapplicable files get per-file explanations.
The in-app picker retains advanced planning and replacement confirmation.
Its planner shows preset, policy explanation and a two-column file/plan list.
The selected file's full status stays readable below the list; dimensions and
proposed paths are under File details. Output/replacement controls are collapsed
initially, and replacement consent remains separately visible when requested.
The body scrolls independently of the confirmation buttons. The advanced planner
retains its conservative Lossless default; direct Explorer Auto remains first.
Implemented direct PNG actions are:

- **Auto** — the first/recommended action; a conservative, image-specific choice
  among validated candidates, with worthwhile savings and bounded visual loss.
- **Lossless** — pixels decode identically.
- **Balanced** — bounded visual loss under a documented quality floor.
- **Smallest** — stronger, clearly identified lossy reduction under its own
  quality floor.
- Advanced comparison, metadata, destination and replacement controls belong
  inside the application; they are not a mandatory menu/planner detour.
- **Settings...** — after a separator at the bottom, opens the Optimize section
  of shared settings without starting work.

Direct presets should complete without a success window: one optional Windows
chime per successful batch, including No smaller result. Show compact cancellable
progress only when useful; problems and necessary decisions get actionable UI.

The default output is a collision-safe sibling file. Replacing the source is an
explicit option and must use recoverable transactional publication.

Use `name - Optimized.ext`, then `name - Optimized (2).ext` for collisions.
Preserve the source basename including existing suffixes. Replacement is disabled
by default, requires per-batch confirmation, and recycles the uniquely named
original backup only after publication succeeds. If recycling fails, keep the
backup and show its location. Quick actions remain copy-only. Follow
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


PNG Version 1
-------------

The PNG engine design uses independently selected and pinned tools:

- Implemented lossless recompression through pinned `oxipng` 10.1.0, with no
  representation reductions and original metadata chunks retained exactly.
- Implemented bounded lossy RGB precision reduction followed by the same cleanup,
  with curated Magick.NET admission/reopen validation. No paid quantizer or
  additional runtime dependency is introduced.

The user-approved [expanded evaluation](png-quantization-improvements.md) selected
7-bit/6-bit RGB precision policies for Balanced/Smallest instead of a fixed
256/128-color palette. These remain ordinary 8-bit PNGs with bounded RGB sample
changes and exact alpha. Production retains admitted metadata and representation;
ICC/indexed/grayscale and unsupported chunks remain outside lossy admission.
Each policy retains the smallest eligible result, including lossless fallback.
Auto tests RGB7 with the Balanced quality floor and requires at least 5% additional
savings against the smaller of source and lossless baseline before pixel changes.
Balanced compares lossless/RGB7; Smallest compares lossless/RGB7/RGB6. Ties retain
the gentler result. Lossy-ineligible but lossless-supported files use lossless.
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

Approved next PNG research direction: retain **Auto, Lossless, Balanced, Smallest**
in that order. Presets describe quality limits, not fixed colour counts. Auto
favours near-original appearance and worthwhile savings; Balanced allows small
changes with stricter gradient/detail protection; Smallest allows stronger loss
without abandoning its floor. Select from a small bounded candidate set per image,
keep lossless fallback for every preset, and never force additional degradation
merely because Smallest was selected. Metadata and transparency preservation do
not vary implicitly with the preset. No extra colour-count or dithering menu is
needed for everyday users.

The [512-colour/light-dither experiment](png-palette-comparison.md) is approved for
broader testing, initially as a Smallest candidate. It is not an approved production
replacement, a universal default or permission to loosen Balanced's limits.
Current shipping-candidate behaviour remains documented above until integration
and broader gradient/detail acceptance are complete.

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
