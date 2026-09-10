Context Optimizer Design
========================

Next scope follows [decision 0019](decisions/0019-broad-file-analysis-and-media-expansion.md)
and the [broad file support goal](broad-file-support-goal.md). Preserve the verified
PNG recipes and add bounded lossless FLAC recompression with decoded-sample and
metadata validation. PDF optimization is selected for launch; engine choice and
preservation policies remain pending under the [document design](document-design.md).
These additions are planned; format recognition alone does not enable Optimize.

The first FLAC core step now inventories bounded metadata and reconciles original
descriptive blocks with the newly encoded STREAMINFO. Independent byte comparison
and decoded-sample tests protect against an engine's metadata rewrite. Application
blocks, unknown block types and seek tables still require handlers before
recompression; the feature is not exposed as a customer action. See
[current experiment and preservation evidence](audio-engine-evaluation.md).

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

Accepted next direction: [decision 0015](decisions/0015-simple-context-menu-workflows.md)
and the [quiet-first UX plan](quiet-first-ux-goal.md) replace mandatory planning
with direct Auto-first presets and best-effort processing. This includes
saving a useful permitted fallback. The initial slice is implemented
with local policy, worker, shell and isolated quiet-launch evidence; human
acceptance and wider PNG compatibility remain pending.

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

**Optimize** is a top-level Explorer command. Its submenu shows only policies
supported by the complete selection. Direct preset IDs capture the saved output choice;
the bounded shell currently validates selection count, while content support is
checked outside Explorer and inapplicable files get per-file explanations.
No manual picker or routine preset planner is exposed. Expired access appears
as an actionable problem with License and Retry failed; retry retains the
selected file and preset. Legacy choose-preset activation uses conservative
Lossless directly. Direct Explorer Auto remains first.
Implemented direct PNG actions are:

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
