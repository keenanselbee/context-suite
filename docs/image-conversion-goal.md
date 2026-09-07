PNG, JPEG, And WebP Conversion Goal
===================================

Status: completed locally for the bounded PNG/JPEG/WebP slice; commercial release gates remain
Date: 2026-09-06


Objective
---------

Deliver real, local PNG/JPEG/WebP batch conversion from Explorer through the
system-themed WPF planner, existing sequential worker, semantic validation, and
application-owned safe publication. Include minimal production trial admission
and automated media/failure tests. Follow accepted
[decision 0009](decisions/0009-first-image-engine-and-trial.md),
[Converter design](converter-design.md), and the completed
[output-safety foundation](image-output-safety-goal.md).

This is a bounded Milestone 6 vertical slice with the necessary Milestone 2 media
execution work. It does not complete all image formats, licensing, or release work.


Current Evidence
----------------

- Exact Magick.NET-Q16-x64/Core 14.17.1 dependencies restored and locked in the
  private repo; current NuGet audit reported no known vulnerable packages.
- `tools/Test-ImageConversion.ps1` now passes **235** engine/adapter contracts:
  all six cross-format pairs, independent PNG pixels, exact lossless RGBA,
  independently mapped pixels for all eight EXIF orientations, ICC retention,
  explicit matte, bounded preview, resize, and source/linked-output protection.
  The expanded matrix adds independent RGB/grayscale/alpha 8/16-bit PNGs,
  lossy/lossless WebP, progressive JPEG, ICC and location removal, exact fractional
  physical/aspect resolution round trips, real animated WebP, invalid profile/CRC,
  compressed-profile limits, post-scan JPEG metadata and 512-pixel preview bounds.
  Directional resolution swaps with rotated axes; conflicting resolutions fail.
  Active native memory/disk/thread/profile/dimension ceilings and deterministic
  cache exhaustion/recovery are also checked.
  XMP-only orientation now has independent pixel expectations for all eight
  values under both metadata policies. Conflicting EXIF/XMP or duplicate XMP
  orientations and misplaced JPEG profiles are rejected. Real embedded JPEG
  thumbnails in EXIF IFD1 and XMP are removed under both metadata policies;
  preserve mode explicitly warns about stale-thumbnail removal.
  XMP-only fractional resolution is reconciled with EXIF/container values and
  normalized after rotation; conflicting/incomplete values fail. Grayscale ICC
  tone retention is tested, and colored JPEG mattes convert grayscale profiles
  to sRGB with an explicit warning. CMYK and known HDR/gain-map markers are rejected.
  Evidence: `.codex-temp/image-tests/engine-b67a4e1fbf7f4c25852e04365ac6e55f`.
  The runner has a 120-second process limit. Coverage is the bounded matrix below,
  not a guarantee for every file variant or a security certification.
- Native Windows DLL and unchanged upstream notices are now delivered beside the
  worker. Detailed payload hashes and dependency-license observations live in
  `proprietary/docs/image-engine-verification.md`. LGPL-covered bundled dependencies
  require redistribution review; a coder allowlist is not license clearance.
- Public typed image facts/options/plans and exact-plan confirmation now have
  focused contracts: explicit matte, metadata/loss warnings, orientation/resize,
  no enlargement, mixed applicability and stale/forged plan rejection.
- The application-owned local trial store is implemented and independently
  tested with isolated clocks/files: atomic first start, concurrent admission,
  72-hour boundary, rollback/forward-time handling, invalid storage, restart and
  admitted-batch completion. The application executor now uses it before output
  reservation; the production conversion UI is now connected to that executor.
  Status reads do not create storage; tests never touch the real default path.
- The latest `Test-Foundation.ps1 -Configuration Release -Integration` passes
  **280** contracts, with a clean production build. This supersedes the earlier
  bootstrap-stage foundation count.
- The real deployed worker now probes, converts and returns bounded raw BGRA
  previews over typed IPC. Calls are serialized, with 30-second probe/preview and
  120-second conversion deadlines. A damaged item returns a bounded failure and
  does not prevent the next item from running. The worker cannot publish outputs.
- App-owned integration tests publish actual validated JPEG/WebP copies, reject
  stale plans, preserve original bytes, and exercise trial admission/expiry with
  isolated storage and clocks. No real trial data was created or modified.
- Boundary-fault tests inject partial reserved output and kill the owned worker
  just before dispatch: that item fails once, its temporary output is abandoned,
  pending items restart and convert, and earlier published files remain unchanged.
  Cancellation at dispatch retains completed results and cancels pending items.
- Active-encode interruption tests now use an independently generated 4096-square
  noise PNG and the real deployed worker. They observe CPU activity while the
  encoder holds its output handle, then cancel, kill the owned worker, or expire
  the injected deadline clock. All three cases abandon temporary output, preserve
  source hashes and start a fresh worker for a subsequent request. The deadline
  assertion checks the production 120-second policy without waiting two minutes;
  production always uses the system clock, with no environment/command bypass.
- Worker IPC now carries stable failure categories instead of native diagnostic
  strings. Tests distinguish unsupported signatures, invalid files, missing files,
  changed sources, worker termination and deadlines. Unsupported input is shown
  separately from failed input in application results.
- `Test-PublicationWindows.ps1 -Recycle -Images` passes both real-codec replacement
  modes on the existing verified Windows/NTFS platform. It decodes the published
  JPEGs and hashes the actual recycled originals. Same-path coverage uses genuine
  PNG contents in a mislabeled `.jpg` source, not same-format optimization.
  Evidence: `.codex-temp/publication-windows/image-recycle-a8a1fef473da49cc8006ba651251b6da`.
  Only two new disposable originals were recycled; no trial state or prior Recycle
  Bin contents were changed.
- `Test-DesktopSmoke.ps1 -Images -Configuration Release` passes **26** checks
  against the real app sources in an isolated test host, with the deployed worker:
  keyboard navigation, explicit target/matte, stale-consent reset, real preview,
  cancellation without trial start, mixed-file applicability, queued activations,
  actual WebP/JPEG copies, independently decoded resize, native file selection,
  source-preserving retry, real cross-window OLE file drop, and output-folder
  opening after expiry. Transparent JPEG confirmation requires a current matte
  preview; changed options cannot reuse it.
  Source hashes are unchanged. Evidence: `.codex-temp/desktop-smoke/images-0c283a615a3b4b4084482e43826f3aab`.
  A transient missing-control failure after planner closure was fixed in the
  harness's bounded polling; its failed evidence remains separate from this pass.
- Production WPF startup/forwarding/settings/keyboard/resize/reopen tests also
  pass using deliberately invalid fixtures (no real trial admission).
  Evidence: `.codex-temp/desktop-smoke/80196b5371a24f1098edb245a6525fe2`.
  The harness now focuses an actual button before keyboard navigation instead
  of assuming the DataGrid container owns keyboard focus. Dark
  conversion and results captures were reviewed. Live theme transitions,
  contrast themes and full accessibility remain separate manual checks.
- File picker, drag/drop, source retry, proposed output paths and full selected
  result details are implemented. The WPF dependency manifest no longer includes
  private/engine assemblies; the worker alone links them. Both production builds
  still fail clearly without the private checkout. The test host is never shipped.
- The capability catalog now advertises exactly six tested cross-format pairs;
  worker reuse/restart contracts verify the same set, with no Analyze/Optimize/DDS
  entries. Every file still requires content probing and a valid explicit plan.
  The completion audit below records the bounded requirement coverage.
  No Explorer packages were installed, and no real trial data was changed.


Completion Audit
----------------

The current source, generated outputs and successful runs were checked against
each acceptance group, not just the aggregate test count:

| Requirement | Evidence and boundary |
| --- | --- |
| Six cross-format pairs and semantic output validation | Private `AdapterContracts` and `ImageMatrixContracts`: real codec outputs, independent PNG pixels/signatures, reopened dimensions/depth/alpha and profile bytes; exact lossless and tolerant lossy comparisons |
| Matte, precision, no-upscale resize and metadata/color policy | Public image-plan and view-model contracts plus the private matrix: all eight EXIF/XMP orientations, RGB/grayscale/alpha 8/16-bit sources, ICC retention/acknowledged matte transform, EXIF/XMP thumbnail/location removal, fractional resolution and conflicts |
| Unsupported and hostile inputs; resource bounds | Actual animation/CMYK and known HDR/gain-map fixtures, invalid signatures/CRC/profiles, huge dimensions and compressed-profile limits; disabled coders/delegates; live resource ceiling/exhaustion tests and bounded previews |
| Typed plans, IPC and sequential ownership | Public plan/framing/queue contracts and real-worker integration verify exact item/plan identity, stale-source rejection, serialization, bounded messages and one application owner |
| Safe copies and recoverable replacement | `PublicationContracts` cover collisions/races, Unicode/long paths, failed validation, injected I/O failure, locks and source changes; real-codec native Windows evidence above proves both restricted replacement modes |
| Cancellation, crash and timeout | `ImageWorkerContracts` and `ImageInterruptionContracts` interrupt actual owned workers; no incomplete publication, no automatic failed-item retry, pending work restarts, prior successes remain intact |
| Production 72-hour admission | `TrialContracts` plus application orchestration: atomic/concurrent first start, failed writes, UTC/rollback/forward time, expiry, malformed records, restart and admitted-batch completion; all test clocks/storage are isolated |
| Usable real planner and result actions | 26 isolated real-window checks cover normal activation, keyboard use, preview/matte/consent gates, mixed/queued batches, picker, OLE drop, retry, actual outputs and post-expiry folder/settings actions; separate production-executable smoke passes |
| Pinned private engine and public/private boundaries | Locked 14.17.1 package/Core, matching native DLL/notice hashes, clean production build, expected missing-private failures, engine-free WPF dependency manifest; public Core/contracts/test-host output inventories contain no private or native-engine artifacts |
| Documentation and scoped delivery | Source/theme/document checks and both worktree whitespace checks pass. No commits, pushes, Explorer installation, live Polar changes or real trial access. No source archive or release package was published |

Native failure and image-policy coverage is finite, not a sandbox proof or a
promise to accept all PNG/JPEG/WebP variants. Malformed, unsupported or
unrepresentable contents fail visibly; metadata removal does not bypass HDR checks.
The OLE test uses a disposable external drag source, not Explorer menu enumeration.
Its failed foreground-handling attempts remain separate diagnostics. Test artifacts
remain under `.codex-temp`; the output-folder tests leave their own Explorer tabs
open rather than risk closing a user's other tabs.

Release gates remain: native dependency redistribution obligations, paid Polar
activation/offline policy, installer/signing/update design, hosted CI, modern
Explorer-menu verification, live theme transitions and full accessibility review.
Those are not claimed complete by this bounded conversion implementation.


Implementation Order And Ownership
----------------------------------

1. Verify/pin Magick.NET-Q16-x64 14.17.1 in the existing private project. Record
   dependency resolution, package/native versions, redistribution notices and
   security audit results. Prove real worker decoding/encoding on owned fixtures.
   No engine dependency in public core, Explorer, or the WPF UI process.
2. Add public typed source facts, immutable conversion plans, consequences,
   validation evidence and bounded IPC contracts. Worker owns actual probing,
   thumbnail/preview rendering and codecs; app owns planning, admission and
   publication. Verify caller-supplied paths and avoid decoding previews in UI.
3. Implement isolated, versioned local trial storage and admission with the
   decision's 72-hour start, rollback, expiry, failure and in-flight rules. Keep
   clocks and access doubles in tests; never consume/reset the user's real trial
   in automated checks. Preserve public/private composition and build failures.
4. Build the Convert planner in the existing single-app flow: target, quality,
   WebP lossless choice, optional maximum dimension, metadata policy, matte
   selection/preview, warnings, actual output plan, and explicit Convert action.
   Keep grouped batches coherent when new Explorer activations arrive; never
   change a confirmed batch's settings. Add file picking and drag/drop as input
   alternatives without recursive folder import.
5. Encode sequentially to app-reserved temporary files. Reopen and validate the
   actual result before publication. Carry matching plan/item IDs and exact
   validated bytes to the publisher; reject stale inputs, evidence or settings.
   Integrate progress, cancellation, worker crash recovery and per-file results.
6. Verify actual output paths, retained backups/warnings, mixed-batch totals,
   opening the output folder, and source-preserving retry. Integrate bounded
   before/after preview; preview artifacts are private temporary data, never
   user-visible converted outputs or a trial-expiry bypass.
7. Extend existing public contracts, private-adapter integration and opt-in
   desktop tests. Keep the public test build independent of private code. Update
   capability docs only for combinations actually passing real fixtures.

Public source owns contracts, planner/UI, queue, output safety, trial policy
contracts and applicable tests. The separate `proprietary/` repo owns the real
media adapter and production commercial composition, with its own tests/docs
where needed. This goal authorizes scoped implementation edits in both repos;
it does not authorize staging/committing either one, pushing, installing packages
into Explorer, modifying Windows settings, purchases, or live Polar changes.


Acceptance Matrix
-----------------

- All six cross-format pairs produce files with independently checked signatures
  and reopened semantic properties. Test RGB/grayscale, 8/16-bit PNG, transparent
  and opaque files, JPEG and lossy/lossless WebP. Do not rely solely on encoder
  exit codes or the same metadata object used to write output.
- Explicit JPEG matte preview matches compositing; PNG/WebP alpha survives within
  the chosen encoding policy. Cover fully transparent and partly transparent
  pixels, lossy warnings, precision-reduction acknowledgement, and no-upscale resize.
- Cover every EXIF orientation, ICC/color handling, retained/removal metadata,
  embedded thumbnails/location, unsupported color models, and malformed profiles.
  No silent metadata/capability loss. Use semantic or tolerance-based comparisons
  for lossy media, exact pixel checks where lossless preservation is promised.
- Reject animation, unsupported HDR, wrong signatures, truncation, corrupt data,
  huge dimensions/decompression bombs and disabled coders/delegates. Verify finite
  time/memory/disk/thread/preview limits; failures do not affect unrelated files.
- Mixed supported/unsupported/failed selections remain one coordinated batch.
  Repeated activation preserves single ownership, per-batch settings and truthful
  progress/results. Same-format items are clearly not applicable to this slice.
- Source hashes remain unchanged in default-copy tests. Reuse publication tests
  for collisions, existing destination races, Unicode/long paths, failed validation,
  cancellation, disk/permission failures and changed source. Add real-codec
  integration through both allowed replacement modes without weakening safety gates.
- Worker cancellation/crash/timeout never publishes an incomplete output. Restart
  only for pending work; do not automatically retry the failed item. Already
  published results remain completed even if later cleanup or a batch fails.
- Trial tests cover not-started preview/invalid plans, first confirmation, atomic
  start failure, concurrent admission, 72-hour boundary, time-zone independence,
  rollback/forward jumps, malformed/unknown records, restart and in-flight expiry.
  No reset flag, fake paid provider or real trial-state modification in tests.
- Test the actual planner via normal activation with isolated test composition
  and owned fixtures, including keyboard access, warning/matte gates, preview,
  output policy, results and app reuse. No test-only access route in shipping
  binaries. Record separately which real production UI flows were checked.
- Pin and report actual tested dependencies. Preserve required third-party
  notices, private-source exclusion, absence of private symbols in public
  artifacts, and clear missing-private failures.

Fixtures must be authored/generated or have recorded redistribution permission.
Small committed fixtures and golden expectations should not all be generated by
the encoder under test. Test-only generators and controlled outputs are not
production capabilities. Keep scratch files inside `.codex-temp`; native recycle
checks remain opt-in and never purge the Recycle Bin.


Verification And Completion
---------------------------

Extend `Test-Foundation.ps1`, `Test-DesktopSmoke.ps1` and existing integration
scripts rather than adding a second test framework. Add a focused real-image
integration command if needed. Run affected public contracts, real private
adapter/worker integration, production build, private-boundary and repository
checks; native shell tests only when that surface changes. Record commands,
counts, fixture coverage, runtime/native versions and residual manual checks.

Completion requires real supported conversions, the usable planner, enforced
production trial admission and passing source-preservation/failure contracts.
Unsupported variants must be visible and documented, not faked. If package or
policy verification exposes a material conflict, report the evidence and exact
decision needed; do not substitute a new engine, broader formats, unsafe color
behavior, a paid access bypass, or unverified replacement support silently.

Deferred: DDS BC1-BC7 and linear/sRGB/mips/arrays/cubemaps, optimization, additional
formats, audio, live Polar activation, paid offline policy, installers/signing/
updates, custom accounts, and anti-piracy machinery. DDS is the next dedicated
media goal, not an implied capability of this Magick.NET slice.
