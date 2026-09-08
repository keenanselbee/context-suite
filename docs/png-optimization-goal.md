Lossless PNG Optimization Goal
==============================

Status: implemented locally; automated acceptance recorded below. Interactive
optimization-window/Explorer acceptance and commercial release checks remain pending.
Date: 2026-09-08

This records the original lossless slice. The subsequent [precision preset
integration](png-lossy-presets.md) adds Balanced/Smallest to the current planner.

Scope
-----

Deliver a complete bounded lossless PNG workflow using the existing application,
worker, settings, trial and output-safety foundations. Follow
[decision 0013](decisions/0013-lossless-png-optimization.md). Do not implement
lossy presets, add paid activation, change installed Explorer packages, install
software, sign, publish or enable native installer upgrades as part of this goal.

Implemented
-----------

- Pinned oxipng development staging, build guards, private CI provisioning,
  runtime executable verification and production allowlist/notice checks.
- Independent unfilter/sample validation and original non-IDAT chunk restoration.
  Embedded sRGB ICC is explicitly retained during decoder admission instead of
  being replaced with an implicit sRGB declaration.
- Public immutable PNG plans and confirmation, separate optimization worker
  commands, private adapter and a conditional PNG-to-PNG optimization capability.
- One WPF planner per selection with fixed lossless/preserve policy, per-file
  support and proposed output names, trial status and explicit replacement consent.
- Existing Explorer Choose preset activation, an in-app Optimize PNG picker,
  operation-preserving retry, per-file size/savings and aggregate saved bytes and
  percentage. Results include the engine version and policy identifier.
- Publish only smaller semantically verified output. Equal/larger output is
  Unchanged; sources are preserved by default. Cancellation does not undo prior
  publications, and an admitted batch can finish after trial expiry.
- Bounded child process pipes, timeouts and a native kill-on-close job; an abrupt
  worker-only crash cannot leave the encoder running.

Automated Acceptance
--------------------

Commands run from the public repository root:

```powershell
./tools/png-engine/Stage-PngEngine.ps1
./tools/Test-ImageConversion.ps1 -Configuration Release
./tools/Test-Foundation.ps1 -Configuration Release -Integration
./tools/curated-engine/Test-ProductionPackaging.ps1
./tools/Test-Repository.ps1
```

The full foundation command uses isolated generated inputs/trial data and tests
the real worker. It does not register packages or manipulate user media.

- Image/adapter matrix: 457 checks passed, including 118 new PNG checks across
  fifteen color/depth combinations, Unicode, transparency/hidden RGB, ICC/EXIF/XMP,
  all five PNG row filters, malformed/truncated/excess data, rejected variants,
  tampered policy/digest, nonempty reservations and original preservation.
- Foundation/integration: 464 checks passed, including PNG planning, trial,
  immutable confirmation, naming/collisions, unchanged results, mixed-batch WPF
  view-model handoff, per-file/aggregate reporting and expired/cancelled admission.
- Real PNG interruption checks cover cancellation, encoder crash, worker-only
  crash and application timeout. They observe the actual owned encoder doing
  work, verify its exit, retain original hashes and allow subsequent work.
- Production packaging now includes adversarial checks for a modified optimizer
  executable, identity and license: 12 packaging contracts passed. Public-source
  boundary, system-theme policy and all 40 documentation files passed validation.
- The isolated WPF test host also builds with the new window. All 121 DDS codec
  contracts pass after the shared decoder preservation change.
- A repeated interruption run exposed a broken-stdin race on encoder death.
  Pipe failures are now classified as encoder failures (or cancellation/timeout
  when appropriate), not as source-file permission problems. The final complete
  foundation run passed all 464 checks with this correction.

Final image evidence: `.codex-temp/image-tests/engine-6525d3b0e55f4aa9ac1ea06da01a438f`.
Final foundation transcript: `.codex-temp/png-foundation-verified.log`.
PNG interruption evidence and publication journals are generated under
`.codex-temp/foundation-tests/png-interruption-*` and `png-worker-*`.
All fixtures and output are disposable, repo-local material; no real customer
data or production trial reset is used.

Remaining Acceptance And Release Limits
--------------------------------------

- Verify the actual Optimization window from classic and modern Explorer and
  the in-app picker: keyboard/focus, light/dark/high contrast, scaling, long
  paths, all-unsupported selections, cancel, mixed results, retry and details.
  Compiled XAML and view-model tests are not interactive UI acceptance.
- Direct Auto/Lossless/Balanced/Smallest actions are now implemented under the
  [quiet-first slice](quiet-first-ux-goal.md); installed-menu acceptance remains open.
- Replacement reuses the existing restricted-platform publisher and tested
  recovery contracts; no additional live Recycle Bin test was performed here.
- Independently review the selected upstream binary's transitive components,
  notices/source provenance, build flags and runtime requirements before adding
  it to a commercially cleared signed release. Hosted CI remains unverified.
- Signed installer, Polar activation and the other release gates are unchanged.
