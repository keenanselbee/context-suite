Quiet-First UX And Best-Effort Optimization
==========================================

Status: broad everyday-image implementation in progress; local automated evidence, human acceptance pending
Date: 2026-09-08

Objective
---------

Deliver an understandable, context-menu-first utility: direct Optimize presets,
Auto first, best-effort processing within the chosen loss budget, quiet success,
and a compact accessible UI only when useful. Follow
[decision 0015](decisions/0015-simple-context-menu-workflows.md).

Deliver this as one integrated goal covering PNG compatibility, direct Convert,
compact UI, workflow resilience, representative image tests and an updated internal
installer. No additional codecs, payments or signing are included.

Direct Optimize and safe common Convert requests no longer open a mandatory
planner. Convert falls back to a preselected planner for meaningful decisions;
the advanced Optimize picker retains its planner. The checks below are bounded
local evidence, not complete visual/accessibility or commercial acceptance.

Current expansion (2026-09-08)
-----------------------------

- Advanced Optimize now follows the compact planner layout: two-column file/plan
  list, readable selected status, collapsed file/output details and separately
  visible replacement consent. The body scrolls while confirmation stays outside
  it; the existing Lossless planner default and copy/replacement gates are retained.
- Changing task/plan/preview/settings messages use a shared status control that
  explicitly raises polite live-region events while visible. It coalesces one
  dispatcher turn, clears queued announcements on unload and never shows a hidden
  window to announce quick work. Actual screen-reader delivery remains unverified.
- `tools/Test-ViewContracts.ps1 -Configuration Release` passes 34 non-interactive
  checks on the compiled production windows, without desktop input, workers or
  trial admission. It checks view loading, bounded layout calculation, accessible
  status text and secondary-panel/confirmation wiring, not visual acceptance.
  Foundation contracts also pass after correcting replacement-specific destination
  wording; that final wording change was checked without rerunning media integration.
  Quiet Optimize smoke `8fea218478c045f48ffafe2615864754` passes all four actions
  after the status-control changes, with no early window and unchanged source.

- Retry now considers the latest attempt per source/tool (Windows case-insensitive
  paths), preserves its original menu action and captures current settings for the
  new batch. Repeated clicks cannot queue the same retry twice; committed outcomes
  and superseded failures are excluded. Missing files are reported without retrying
  successful files. Earlier attempt details remain available in session history.
- Direct Convert treats an already-matching supported format as a quiet no-op,
  not an error or same-format re-encode. It creates no redundant copy; the advanced
  planner still directs intentional same-format processing to Optimize.
- A restricted user/session handoff mutex serializes launcher forwarding with
  automatic idle shutdown, while media work remains outside the lock. Acquisition
  polls asynchronously on the WPF dispatcher (15-second bound); a terminated owner
  is recoverable through normal mutex abandonment, without persistent lock cleanup.
  Ordinary window-close cancellation and abrupt app crashes remain distinct from
  automatic idle handoff; this is not a durable cross-crash exactly-once queue.
- Stress evidence: 40 Optimize requests spaced 150 ms (`c0d49d658a264f019d4d274f96743e99`)
  and 20 Convert requests spaced 250 ms (`5acd3b061a8d475790bedcfb4c11205b`) pass under
  `.codex-temp/quiet-smoke/`, with per-action counts, distinct existing outputs,
  unchanged sources, no early windows and automatic exit. The Optimize run spans
  two sequential owners (18 and 22 requests). Earlier runs timed out or completed
  only 39/40 before this guard; failures remain retained as diagnostic evidence.
  Use `-Rounds` and `-SpacingMilliseconds` with `tools/Test-QuietOptimization.ps1`
  for bounded stress; per-owner intermediate traces and stderr are retained.

- Adam7 PNG lossless recompression now preserves interlacing, exact metadata,
  palette/transparency and all sample precision. Empty passes are handled; packed
  padding is excluded from the sample digest without changing filter history.
  Auto/Balanced/Smallest use lossless fallback for interlaced images.
- 817 private engine/adapter contracts pass, including 75 independently generated
  interlaced combinations covering all admitted depth/color pairs and dimensions
  1x1, 2x3, 4x1, 5x7 and 17x19. A second decoder compares pixels. Evidence:
  `.codex-temp/image-tests/engine-bad977ff230342019dad317161afe12e`.
- Convert exposes PNG, JPEG, WebP (lossless), BMP, TGA, More options and Settings.
  Safe copies use full dimensions, preserved metadata and JPEG quality 90.
  Unknown/unsupported metadata, matte, precision, lossy re-encoding and DDS
  decisions still require review. Informational loss-not-restored and untagged
  sRGB notices do not themselves require a decision. Shell COM/host tests pass
  for every direct target and the advanced entry with complete selections.
- Settings has an output-folder picker. Conversion size/quality and technical
  file details are secondary; previews debounce changes automatically. Desktop
  visual and interaction verification of this expansion is pending.
- 585 foundation/integration contracts pass, including selected Optimize details,
  retry deduplication,
  handoff contention/cancellation/abandonment recovery, same-format quiet no-op,
  automatic latest-preview refresh, direct conversion copy
  safety, duplicate suppression, mixed failure, explicit matte handoff and the
  informational-versus-consent policy. Both quiet smoke modes pass: four Optimize
  requests (`8f39b59d55e8485f9fa6275a0d66db69`) and two Convert requests
  (`d21b5341c1c94b9ebbd9f77986f915f8`), beneath `.codex-temp/quiet-smoke/`.
- The initial quiet Convert smoke (`61a73567cb6b4b8787f28cd42d310406`) timed out
  because resolution-only normalization unnecessarily opened a planner. The
  corrected policy retains resolution and still gates EXIF/XMP normalization.
- Five hash-verified, untouched Afterburner screenshot copies spanning five games
  were checked through the production adapter, without research metadata stripping.
  All five were rejected for `fdEC`: this is a compatibility finding, not a passed
  optimization corpus. Evidence: `.codex-temp/png-quantization/production-4e458b905629446fa6b1f57f120f1537/results.json`.
  `tools/Test-PngCorpus.ps1 -Corpus <copied folder>` records production outcomes
  and, for admitted inputs, validates independent pixel/alpha/metadata/quality
  screens and elapsed time across all four presets. Inputs must be copied beneath
  `.codex-temp/png-quantization`; original media is never an evaluation output.
- Installed package health and packaged COM activation pass for all three existing
  identities. No installation or registration was changed; this does not prove
  visible Explorer menu order or the latest DLL loaded in a running Explorer.
- Updated internal payload: `artifacts/release-candidates/f0abfbc1ec394472a496d7b3b77cc564/ContextSuite`.
  Unsigned Inno build: `artifacts/installer-candidates/d981d701cc6f4f06b53fefd49eba6adb/ContextSuite-0.1.0.0-win-x64-internal.exe`.
  It records dirty development provenance and uses distinct unsigned test identities.
  All 25 mocked registration/input-rejection contracts pass against this candidate.
  This artifact includes the retry/no-op/handoff fixes and revised planners/status
  controls, including replacement-specific destination wording. Its unsigned identities
  are compile-test inputs and are rejected by the installer at installation time.
  No installation, signing or distribution approval; native upgrade admission
  remains closed. This is packaging evidence, not installed workflow acceptance.

Decisions awaiting owner response
--------------------------------

- Add an explicit per-batch, copy-only option to omit unsupported PNG metadata?
  Recommended: yes, off by default, naming what will be omitted and preserving
  the original. No such removal is currently implemented or implicitly authorized.
- An idle keyboard/mouse window for the opt-in interactive desktop test. The
  updated UIA harness compiles but has not run against the new layout. Automatic
  preview behavior has a focused real-worker contract; this is not visual review.

Implemented Slice And Evidence
------------------------------

- Native Auto/Lossless/Balanced/Smallest, separator and Settings; complete
  selections and distinct stable IDs validated through actual COM invocation
  and the native host. No Explorer package was installed by this change.
- Quick copy-only plans bypass the planner, retain immutable settings, and use
  the existing trial gate and validated transactional publisher. Mixed failures
  do not prevent supported files or later batches from completing.
- Auto v1 compares lossless with reviewed RGB7, using the Balanced quality floor
  and at least 5% extra savings against the smaller source/lossless baseline.
  Balanced v2 compares lossless/RGB7; Smallest v2 adds RGB6. Equal sizes keep the
  gentler candidate. Lossless eligibility is independent of lossy eligibility.
- Results distinguish requested preset, actual method and fallback reason.
  Protected PNG metadata errors identify the chunk (including fdEC), without
  silently removing it; interlaced PNG now uses lossless fallback.
- Hidden startup, delayed non-activating progress after two seconds, optional
  Windows chime per wholly successful quick batch, mute captured per batch,
  problem visibility, and idle app/worker cleanup. Settings-only activation does
  not open the task window. User-opened windows are retained.
- Main task window now has a concise summary, activity indicator, cancel-work
  control and expandable file/outcome/savings details. Recovery paths and engine
  data remain available in selected-file details. Convert planner and advanced
  settings layout need further UX work.

Local checks on 2026-09-08:

- Production managed/native build and packaging checks pass (development only).
- `tools/Test-ShellPrototype.ps1 -Configuration Release` passes, including all
  four actual preset invocations with complete multi-file selections.
- `tools/Test-Foundation.ps1 -Configuration Release -Integration`: 552 contracts
  passed, including preset admission, copy-only execution, mixed outcomes,
  duplicate requests, mutable-preference isolation, Auto worker publication and
  repeated no-size-gain results. Quiet timing/mute/notification policy is tested
  separately from actual sound playback.
- `tools/Test-ImageConversion.ps1 -Configuration Release`: 667 adapter/engine
  contracts passed, including independent Auto quality/benefit decisions across
  synthetic fixtures, lossless/gentler fallback and preserved metadata/alpha.
- New `tools/Test-QuietOptimization.ps1 -Configuration Release` compiles the real
  app into the isolated test host and submits four concurrent direct actions.
  The run `f33ad8d8810a4accb5c27c02cc3cb61c` under `.codex-temp/quiet-smoke/`
  completed all four, exercised publication, showed no early window, left the
  original unchanged, consumed requests and exited with no owned worker left.
  The earlier transparent fixture also exercised four Unchanged completions
  without any shown windows. The test uses muted isolated settings/trial data;
  it does not prove audible sound or manual Explorer appearance.

Remaining acceptance before calling this UX finished:

- Installed classic/modern menu and actual delayed progress/problem presentation;
  keyboard, focus, scaling, contrast, screen reader and audible/missing chime checks.
- Wider startup/shutdown-boundary stress (including disconnected clients),
  long-running batch cancellation and user-owned-window interaction review.
- Held-out real-image Auto calibration; synthetic quality thresholds and earlier
  visual review of RGB precision are not proof of ideal Auto choices for all images.
- Broader protected metadata handling (the later [fdEC exception](optimizer-design.md#png-fdec-compatibility-exception)
  now creates warning copies), further planner
  simplification and desktop acceptance of the new direct Convert actions.
- Durable optional completion history for people who mute sound is not added;
  successful quick jobs leave their named output copies, with no resident process.

Implementation Sequence
-----------------------

1. Best-effort policy and truthful outcomes.
   - Add an immutable Auto policy and preserve the existing named presets.
   - Compare lossless and supported precision candidates. Balanced never exceeds
     its reviewed loss budget; Smallest may select a gentler candidate if smaller.
   - For Auto, initially compare lossless with the reviewed RGB7 candidate. A
     proposed starting rule requires the Balanced quality floor and at least 5%
     additional savings versus lossless before accepting pixel changes. Validate
     this conservative benefit rule on held-out images and freeze it in the
     versioned policy; it is a design starting point, not measured market preference.
   - Keep a useful lossless result when a lossy candidate is unsuitable, fails
     its quality screen or is larger. Genuine corrupt source, cancellation,
     worker death and invalid encoder output remain distinct failures; do not
     conceal failures as a successful fallback.
   - Report requested preset separately from actual method, fallback reason,
     bytes saved and No smaller result. Never invent savings or claim global optimality.

2. Direct shell actions and quiet application lifecycle.
   - Add Auto/Lossless/Balanced/Smallest with stable action IDs and explicit order
     to native shell, native host validation and managed activation together.
   - No mandatory planner for quick actions; confirm the internally validated
     copy-only plan from the explicit menu choice. Retain trial admission.
   - Start hidden without taskbar/foreground flashes; forward requests without
     showing an existing window. Handle repeated/concurrent activations and
     shutdown races without losing or duplicating batches.
   - Play one Windows chime on full completion. Add a mute setting; missing sound
     is harmless. Keep sounds outside worker and Explorer enumeration.
   - Proposed progress rule: show after two seconds of ongoing work, including
     probing, rather than immediately because a batch contains multiple files.
     Ensure large selections have accessible progress and cancellation.
   - Surface failures/necessary decisions once per batch, not one modal per file.
     Close only automatically opened progress; preserve user-opened surfaces.

3. Compact task UI overhaul.
   - Replace the always-visible engineering grid with task/progress, counts,
     saved space and Cancel while running. Reveal per-file detail on demand.
   - On problems, lead with affected files and useful actions: Retry failed,
     Open folder, change the necessary setting, or Details. Never retry successes.
   - Keep settings grouped by everyday intent. Technical codec, path and engine
     information stays secondary. Rework planners to show only relevant choices.
   - Preserve theme, DPI, contrast, keyboard and screen-reader behavior. Document
     what a user with sound disabled can inspect after intentional app launch.

4. Real-world PNG compatibility, then direct Convert targets.
   - Inventory rejection reasons on authorized copies, including Afterburner
     fdEC, interlacing, indexed/grayscale, transparency keys and ICC profiles.
   - First improve precise errors and lossless eligibility/fallback; then extend
     variant support with independent validation and metadata policy tests.
   - Do not silently strip metadata to make the success rate look better.
   - Apply quiet/direct interaction to conversion targets once their required
     decisions and warnings can be represented safely. Keep Analyze read-only.

Verification And Exit Criteria
-------------------------------

- Native menu contracts prove exact order, labels, preset IDs and complete
  selections. Malformed action/schema combinations remain rejected.
- Policy tests prove Auto's quality/benefit rule, lossless fallback, gentler
  candidate selection, deterministic ties, unsupported variants and no-size-gain.
- Real worker tests verify pixels/alpha/metadata, safe publication, trial gates,
  failures/cancellation and no stronger-than-requested loss across mixed batches.
- Application tests prove no planner/window flash on fast quick actions, delayed
  progress, no focus theft, one completion signal per batch and no success sound
  on partial failures/cancellation. Test with injected sound/timing boundaries;
  verify the real Windows sound path separately without bundling its contents.
- Verify repeated activation, startup/shutdown races and quiet-process cleanup.
  No hidden resident app or orphaned worker after the final batch.
- Use an isolated test host and disposable files/trial settings. Do not consume
  the real trial, modify original media, restart Explorer or install packages as
  part of automated tests. Installed-menu verification requires explicit opt-in.
- Human review confirms the actual menu and compact UI are understandable,
  uncluttered and usable at supported DPI/contrast settings.
- Keep a held-out real corpus and publish only measured compatibility claims;
  neither a large synthetic test count nor one working screenshot proves broad support.

Scope Limits
------------

No new codecs, AI image classification, arbitrary engine controls, website
accounts, payment implementation, signing or publishing in this UX effort. Do
not add permanent tray infrastructure or a custom notification platform. Keep the
existing process and source-safety architecture unless a demonstrated blocker
requires a separately reviewed change.
