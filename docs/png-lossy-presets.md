PNG Lossy Presets: Implementation Evidence
=========================================

Date: 2026-09-08
Status: bounded production implementation; automated checks passed locally.
Interactive window and commercial release acceptance remain pending.

Current implementation: [fixed recipes and one safety fallback](decisions/0016-fixed-optimization-recipes.md)
for Auto/Lossless/Balanced/Smallest. Auto alone routes between conservative recipes;
Smallest uses one dark-protected ExoQuant recipe rather than a palette search.
See [integration evidence](fixed-preset-integration.md) for current limits, measured
timing, visual approval and verification. The
[preset research evidence](png-palette-comparison.md#preset-candidate-validation)
records earlier experiments, not current implementation of the new defaults.

Historical v1 evidence below is superseded for candidate selection by decision
0016 and for Explorer presentation by the [quiet-first implementation](quiet-first-ux-goal.md). The advanced planner
remains available inside the application, not through Choose preset in Explorer.

The user's visual approval followed the [expanded research](png-quantization-improvements.md).
[Decision 0014](decisions/0014-png-precision-presets.md) specifies production policy
and its narrower admission boundary. No original screenshots were edited or added
as distributable fixtures during integration.

Historical Precision Implementation
-----------------------------------

- Lossless/Balanced/Smallest selector in the system-themed WPF batch planner,
  explicit lossy/banding warnings and per-file unsupported explanations.
- Lossless default on every open, immutable versioned snapshots, and cleared
  replacement consent when presets change. No new saved settings.
- Private RGB7/RGB6 transform, exact alpha and hidden RGB, independent sample
  validation, native reopen, metadata/header retention and unchanged quality floors.
- Strictly smaller than source AND lossless baseline; otherwise no publication.
- Existing trial gate, safe publication, collision naming, aggregate savings and
  result-policy reporting, plus cancellation in managed work.

Reproduce
---------

```powershell
./tools/Test-ImageConversion.ps1 -Configuration Release
./tools/Test-Foundation.ps1 -Configuration Release -Integration
./tools/Test-Repository.ps1
```

The private adapter suite compares production samples against a separate research
oracle. Tests cover gradients, icons, text, texture, noise, transparency and hidden
RGB; both presets exercise smaller output and Unchanged. Independent metrics
check quality. Header/metadata retention, RGB/alpha corruption, eligibility,
unknown fdEC and pre-cancelled work have focused assertions.

Real-worker tests exercise both policies through IPC and application publication.
Interruption tests exercise cancellation, worker death, child death and forced
timeout for every preset, plus second-encoding cancellation for lossy work. They
check source integrity, no publication/orphan and subsequent worker use.

Local verification: 633 image/adapter contracts passed, including unchanged
conversion/lossless regression coverage. Evidence is retained under
`.codex-temp/image-tests/engine-d5232f8116464c7286c21d32461f2812/`.
The production WPF/worker build passed without warnings or errors and retained
the existing curated engine, DDS and PNG payload identity checks. These are local
engineering checks, not hosted CI or commercial clearance.
All 530 foundation/integration contracts passed, including repeated lossy
Unchanged results, refreshed replacement consent and second-encoder cancellation.
Documentation/source-boundary validation passed for 44 documents, and both
repository diffs passed whitespace checks. Interactive selector acceptance was
not run; no Explorer registrations, original media, real trial records, commits
or release publication were changed by this implementation.

Remaining Acceptance
--------------------

- Inspect the actual Optimize > Choose preset window: selector, warnings,
  keyboard use, narrow sizing and system light/dark/contrast appearance.
- Try supported RGB/RGBA and unsupported ICC/16-bit/indexed files; switching to
  Lossless must restore its broader eligible rows.
- Run disposable mixed batches for both presets; inspect output, savings, retry
  and Lossless default on reopening. Preset changes require fresh replacement
  consent; use only disposable inputs for replacement checks.
- Retain held-out real images and inspect more dark gradients/UI text before
  commercial quality claims. The three approved research screenshots are not
  representative acceptance data. The later [fdEC removal exception](optimizer-design.md#png-fdec-compatibility-exception)
  admits that chunk for optimization under normal copy/replacement selection.
- Large-batch memory/performance and clean-machine interactive release testing
  remain open. These tests do not close installer/signing, redistribution, paid
  licensing or broader Milestone 5 comparison-UI work.
