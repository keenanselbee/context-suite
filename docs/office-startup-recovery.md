Office Startup Recovery
=======================

Status: background startup integration and actual-app recovery of abandoned
native work verified; customer Office execution and visible acceptance remain open.

The application starts `OfficeRecoveryCoordinator` after it owns the activation
router. It runs off the dispatcher, so recovery does not hold up activation or
ordinary Analyze/image work. The context root is `OfficeContexts` beneath the
configured worker scratch directory. Missing storage remains absent and quiet;
the scanner creates neither directories nor Windows profiles.

The coordinator leases the ordinary local NTFS root and reads only direct
children. It limits enumeration to 512 entries, processes at most 256 journals,
and checks a 30-second elapsed budget between records. These are scan bounds,
not a hard deadline that interrupts a native cleanup call. Existing per-record
journal, directory-tree, job-stop and sharing-retry bounds still apply.

Each version-three or version-four record must match the expected root/runtime and pass the full
[journal validation](office-ownership-journal.md). A completed profile-deletion
record is left unchanged. Pending records use the existing recovery API, which
requires original-owner death, verifies the native profile and grant identities,
stops the recorded worker job, and then permits permission/profile cleanup.
The scanner does not resume rendering or publish outputs. Version-four retirement
intent permits the completed temporary-file cleanup described below.

Legacy, corrupt, torn, live-owner, aliased or changed records remain for review.
An `office-<identity>` preparation directory without its corresponding journal
also requires review; the scanner never walks or deletes it. Exhausted scan or
sharing bounds remain visible, rather than being treated as completed cleanup.

If native cleanup fails after ownership reconstruction, `ReleaseRecoveryLeases`
releases only the reconstructed handles. It preserves pending permissions and
the durable journal so a later attempt can reconstruct them again. Original
owners cannot call this method to bypass their cleanup obligations. Missing or
changed native profiles, failed confirmation writes and ambiguous deletion remain
review cases; this does not add an unjournaled deletion fallback.

Cancellation is checked before the next record. A native cleanup already in
progress finishes its bounded attempt first. Quiet application exit waits for
the recovery task; explicit close requests cancellation and awaits that attempt.
The result updates the existing recovery notice without replacing publication
recovery information or batch state. Recovered interruptions offer Convert again;
unresolved records give their retained location. Missing/completed-only storage
does not open a window.

A profile-deleted entry proves native profile cleanup only, not that its PDF was
validated or published. Only the later version-four retirement intent permits
removing generated context files. Publication reconciliation remains separate.
Office admission must await recovery before new Office work shares the runtime;
the customer Office command is not enabled by this change.


Completed-context retirement
-----------------------------

New application-prepared contexts use a version-four journal with the measured
identities of all five generated directories. `RetirementIntent` is a terminal
record written after native profile cleanup and after the caller has
finished using the context. This separates interrupted temporary cleanup from an
interrupted export whose source snapshot and partial result should remain for
review.

The coordinator can now resume that recorded retirement after the exact writer
has exited. It verifies native profile folder/mapping absence, stops any recorded
worker job, and reuses bounded handle-based tree inspection/deletion. Remaining
directories must match their durable identities. It accepts missing owned
directories and a journal whose whole tree is already absent, permitting retry
after deletion was interrupted. Replacements, hard links, live owners and newly
present profiles stop cleanup with the evidence retained. Completed retirement
leaves the context root empty and the next scan quiet.

Unstarted version-four preparation is a separate supported path: a sole profile
intent, verified owner death, absent native profile folder/mapping and all five
matching generated directories permit recording retirement intent before cleanup.
This removes empty or partially copied snapshots and reports interrupted work.
Locked files and substituted directories remain for retry or review.

This does not delete version-three retained contexts, preparation without a
complete record, or interrupted exports lacking retirement intent. Those records keep their
existing review/profile-recovery behavior. It does not resume conversion or
reconcile publication records. Physical power-loss durability, preparation that
failed before its journal, and visible application acceptance remain separate.

`tools/office-engine/Test-OfficeRetirement.py --create-disposable-profiles`
accepts one generated DOCX fixture under repository scratch. It exercises the
actual native profile/grant owner and kills only its disposable writer after
durable retirement intent, then invokes the same coordinator used by startup.
The ordinary foundation variant uses authored native-lifecycle records and
creates no Windows profile. No Office renderer is needed for either variant.

All **3,434 foundation contracts** pass in
`.codex-temp/office-preparation-foundation-58b8a886029945c8850becc935f666bf`.
The additions cover version-four binding validation and round-trip persistence,
worker-lifetime retention, terminal intent ordering, six killed-writer recovery
cases and ordinary preparation. Repeated/missing/unknown binding fields and
legacy records with invented bindings are refused. Version-three writes omit the
new optional field entirely, preserving their original schema for older readers.
The six recovery cases use
complete, partly deleted, replaced child/root, linked-file and journal-only states.
The partial deletion and journal-only boundaries are constructed fixture states;
the writer is actually killed after intent and before file deletion.

All **40 native retirement checks** pass in
`.codex-temp/office-retirement/fb8a8fe708e341cda9baada87c938720`.
Seven cases create and clean real Windows profiles/grants. The additional case
recreates one profile after recorded cleanup and verifies that recovery leaves it
and the context untouched until its actual new owner removes it. Thus the final
run makes eight profile creations across seven names. It starts no Office renderer
and performs no conversion. All original bytes/times and the runtime sentinel
remain unchanged. The wrapper records exit zero and unchanged inputs; the contract
and application test hosts build in Release without warnings/errors.

The preceding native runs `7a669b4a4def45f8bdfbb2ecb70960b4`,
`a8e87324e34c4345bc38b32601209a1f` and `f186d1e06cd949efac4db80e6919cbbf`
also passed 40 checks before the final journal regression and version-three
serialization compatibility correction. Final verification in
`.codex-temp/office-restart-retirement-verification.json` checks the final sources
and confirms all 28 distinct profile folders/mappings and context trees from the
four runs are absent, with no test owner, worker or Office host remaining.

The earlier 43 real-export checks, independent PDF comparisons and actual WPF
startup lifecycle runs were not rerun for this change. The new tests invoke the
same recovery coordinator from disposable test processes. Actual-app restart
acceptance with version-four contexts and customer Office command integration
remain open, along with incomplete-preparation retention and broader packaging.
No reserved production payload, installed state or visible acceptance changed.


Verification
------------

All **3,238 foundation contracts** pass at
`.codex-temp/office-preparation-foundation-e80fac30a81b4aabb4602effb265f081`,
including 17 new coordinator checks. These cover absent storage, complete and
uncertain schema fixtures, live-owner refusal, corrupt/multiply linked journals,
orphan preparation, cancellation before scanning, mismatched runtime roots, scan
limits and view-model notification. They create no native Windows profiles.

All **140 native ownership checks** pass at
`.codex-temp/office-owner/96f7f90eacba410ebe05732b755a1278`, with matching fixture
results under `.codex-temp/office-isolation`. The added retry case releases
reconstructed leases while permissions remain, reopens the unchanged journal,
reconstructs again without duplicating cleanup intent, and completes cleanup.
The initial replay at `cae435c48b1b4ffeb6d8be0ba836e1c9` passed its preceding
138 cases; a mismatched scenario name prevented the two new checks from running.
The corrected final replay above executes both.

All **104 hidden view contracts** pass, including two new recovery-notice binding
checks. The first new view check read before WPF processed its data bindings; the
fixture now drains that dispatcher priority before checking updates. No window
was shown and no native profile, licensing provider or worker was invoked by the
view tests. These checks do not prove visible usability or screen-reader delivery.

All **99 actual worker checks** pass at
`.codex-temp/office-worker/759ff3015c9c491dbac137542ddbc2d9` with runner mode
`startup-recovery`. Three owner-only crashes occur during observed PDF growth,
one each for Word, Excel and PowerPoint. The coordinator discovers the abandoned
journal, stops the recorded process group and removes its native profile. A
second scan leaves the completed journal and interrupted PDF unchanged. The
following exports pass 66 checks, including refusal to abandon original ownership
through the recovery-only lease API. All three completed PDFs pass independent
qpdf/PDFium inspection under
`contracts/inspection-75f40dd9094d4c498d7dad73adc755f4` in that stage.

The first attempt at `a265cffd4ab64c52adde6dbfdd5c2018` timed out before the
renderer readiness evidence appeared; it is not a passed crash case. Its fallback
cleanup completed. The fixture's outer setup/readiness deadline now permits five
minutes, while the separate 100-second renderer-observation bound is unchanged.
Added timing records show 4.2-4.9 seconds of preparation in the successful run;
they do not establish the precise cause or worst-case duration of the first delay.
No production timeout changed.

The retained `.codex-temp/office-startup-all-worker-verification.json` reconciles
current source/binary identities, 189 journal frames, 35 grant-directory
identities, seven unchanged source snapshots and all seven absent profiles/jobs,
including the failed attempt. Its scan of 23,082 ACL entries finds no test SIDs.
Three interrupted PDFs remain as evidence, without being accepted as outputs.
The separate `.codex-temp/office-startup-native-verification.json` accounts for
both native ownership replays: twelve absent profiles, 244 journal frames and
44 grant-directory identities. The application Release build passes with zero
warnings/errors; its log is `.codex-temp/office-startup-application-build.log`.

The actual-app recovery follow-up below now covers abandoned native profiles.
Additional themes/DPI, cross-session recovery, customer Office execution,
production PDF validation/publication and remaining commerce/installer gates
remain open.


Application lifecycle checks
----------------------------

`tools/Test-OfficeAppLifecycle.ps1` now drives the actual application dispatcher,
startup code, activation router and shutdown in separate test-host processes.
It uses generated BMP files, fresh local-trial/settings storage and authored
ownership journals under `.codex-temp`. It creates no native Office profiles or
permissions, invokes no licensing provider and performs no installation or
Explorer registration. Only its owned test processes are closed.

The five cases pass **42 checks**:

- Missing storage: a direct TGA conversion publishes a named copy, preserves its
  original and exits without showing a window, creating Office storage or adding
  a recovery notice.
- Completed journal: ordinary conversion and exit succeed, with no window or
  recovery notice and unchanged journal bytes.
- Unconfirmed ownership: the application shows the retained-work notice. A
  second test application forwards Analyze through the actual router and exits;
  analysis succeeds without losing the notice.
- Locked journal during forwarding: Analyze completes while startup recovery is
  still pending. Recovery subsequently reports the retained work.
- Locked journal during close: the real window close handler awaits the active
  bounded sharing retry. Exit occurs about five seconds after close was requested,
  with the journal and original fixture unchanged.

Final results are under
`.codex-temp/office-app-lifecycle/4c892015c2a54e35b4b18781378ccb33`.
The matching runner receipt is
`.codex-temp/office-app-lifecycle-run-d114e1d03df44988a5d57ea05029408e/verification.json`;
it verifies source, worker and retained build-receipt hashes before and after the
run. The test host builds with zero warnings/errors, and all 104 hidden view
contracts pass again after the entry-point addition. Earlier successful fixture
runs are retained separately and are not added to the final check count.

The first runner attempt omitted optional audio/PDF payload switches and stopped
at allowlist verification before launching an app. Its corrected invocation then
exposed a wrapper bug: launching a GUI executable did not wait for its exit. That
wrapper's success message was invalid. The final wrapper explicitly awaits the
process and captures its exit code, stdout and stderr. The first child attempt
left no completed lifecycle receipt; a later captured run reports that direct
conversion failed with the old 1.1.0 worker. The current protocol has added Office
fields, and its strict JSON
reader rejects unknown fields; the test host must use a matching worker build.

The final runs use the retained worker at
`.codex-temp/office-worker/ce6f4c03432d41cb836192e492ac22d2/worker`, verified against
its `retry-38ae46bcbcd440dcab4630b47454dbe5/build.json` receipt. Supply
`-RetainedBuildReceipt` with `-WorkerPath` for that mode. Production-staged workers
instead use the normal payload verifier, with explicit `-AllowAudioCandidate`
and `-AllowPdfCandidate` switches when needed. Candidate 1.1.0 remains unchanged;
this is current-source test-host evidence, not a new packaged candidate.

These tests observe window visibility and programmatically request close, but do
not inspect rendered layout, keyboard behavior, screen-reader delivery or other
themes/DPI. Locked authored journals test application waiting/forwarding, not
native profile cleanup through the full application. The subsequent run below
connects the actual application to the native crash fixture.


Actual application recovery of native work
-----------------------------------------

The opt-in `app-recovery` mode of `tools/office-engine/Test-OfficeWorkerStop.py`
now prepares its contexts under the application's actual
`WorkerScratch/OfficeContexts` layout. The harness owner uses application source
preparation, grants a disposable native profile and dispatches the real renderer.
Only after observing PDF growth and the exact live worker/host identities does
the harness kill that owner. A fresh WPF test application then uses ordinary
`App.OnStartup` recovery, shows the retained-work guidance and closes through the
real close handler. It does not resume rendering or publish the interrupted PDF.

All **99 worker checks** and **18 application lifecycle checks** pass at
`.codex-temp/office-worker/0573faec0473408693fdf7123b27156a`. Word, Excel and
PowerPoint each pass six actual-app checks. The 99-check harness total includes
the subsequent 66 ordinary export checks; do not count those twice. Three
completed PDFs pass independent structure, authored text, geometry and exact
control-pixel checks under
`contracts/inspection-39e45cdc13974ca0bab82a1db3e8f04f`.

The first run at `0ff27f4e804848ef8f0156f386d39966` passed Word but exceeded the
two-minute application observation deadline on Excel. It is a failed run, even
though fallback cleanup succeeded. The test now records dispatcher heartbeat,
journal byte count, CPU/memory and recovery task status, with a three-minute
observation bound. No production deadline changed. The successful replay's app
cases finish in 5.7-6.2 seconds and show journal progress; this does not establish
the cause of the first delay or worst-case recovery latency. That performance
uncertainty remains open.

`.codex-temp/office-native-app-verification.json` reconciles the final source and
application/worker binaries, all eight profiles/jobs across both attempts, 216
journal frames, 40 grant-directory identities and eight unchanged source
snapshots. Its 23,729-entry ACL scan finds no test SIDs. The three final interrupted
PDFs are retained with checked hashes. The earlier failed run also has a separate
`.codex-temp/office-native-app-failure-cleanup.json` receipt; cleanup is explicitly
distinguished from test success. Both test hosts build with zero warnings/errors.
All 42 existing application lifecycle checks also pass again at
`.codex-temp/office-app-lifecycle/b6bd4cee86d64fb2a0ca2ab6cea3e512`, with the input
receipt under `.codex-temp/office-app-lifecycle-run-58a8ddc3fbcf42dd8560ab8505f0b2a3`.
The 3,238 foundation contracts were not rerun; their recorded source hashes still
match the unchanged product implementation.

The recovering app is real, but the original crashing owner is the isolated
renderer harness. Customer Office admission/execution/publication is still absent,
so this is not an end-to-end customer conversion or new packaged release. The
existing candidate remains unchanged. Rendered layout, keyboard behavior,
screen-reader delivery, other themes/DPI and installed-shell acceptance were not
tested by these programmatic lifecycle checks.
