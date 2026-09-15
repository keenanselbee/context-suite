Office Startup Recovery
=======================

Status: background startup integration implemented; bounded application lifecycle
cases verified separately from native recovery and visible acceptance.

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

Each version-three record must match the expected root/runtime and pass the full
[journal validation](office-ownership-journal.md). A completed profile-deletion
record is left unchanged. Pending records use the existing recovery API, which
requires original-owner death, verifies the native profile and grant identities,
stops the recorded worker job, and then permits permission/profile cleanup.
The scanner does not resume rendering or publish/delete document files.

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

A completed ownership journal proves native profile cleanup only, not that its
PDF was validated or published. Publication reconciliation and retention cleanup
must remain part of the unfinished Office execution/publication integration.
Office admission must await recovery before new Office work shares the runtime;
the customer Office command is not enabled by this change.


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

Full application startup/forwarding/shutdown with a genuinely abandoned native
Office profile, additional themes/DPI, cross-session recovery, production PDF
validation/publication and remaining commerce/installer gates remain open.


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
native profile cleanup through the full application. The separate 99-check crash
run above proves coordinator/native recovery; combining the two remains a gate.
