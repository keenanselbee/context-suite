Office Worker Lifetime Verification
==================================

Status: application-owned worker job and corrected interruption boundary verified;
Office context recovery, publication and customer command remain incomplete

The later [child-access recovery follow-up](office-profile-ownership.md#residual-child-access-recovery)
adds descendant permission checks to the profile owner, with 72 ownership checks
and 39 real-worker export checks. The interruption evidence below belongs to this
earlier checkpoint and was not rerun for that Office-only cleanup change.

The [profile cleanup follow-up](office-profile-ownership.md) passes 56 focused
ownership checks, including bounded sharing-conflict retries and retained
ownership after exhaustion. Real Office interruption tests subsequently completed
all nine stop/following-export cells across three recorded runs. Independent PDF
inspection passed for all nine following exports. Those results exposed a further
application lifetime gap and do not establish completed interruption acceptance.


Observed return-boundary failure
--------------------------------

All three worker-only crash cases returned from `WorkerClient` before the retained
native host handle was signaled. The harness then waited for that exact host before
cleaning its profile. Cancellation and deadline cases observed the native handle
already signaled at client return. The old assertion required eventual process
exit, so it did not fail the three early returns; final evidence reconciliation
correctly rejected them.

The retained mode runs beneath `.codex-temp/office-worker` are:

| Mode | Worker evidence | Independent inspection beneath `contracts/` |
| --- | --- | --- |
| Cancellation | `556f332dfd514885bd3e00575a710fa4` | `inspection-f07ed84c8bea43eaa13afdc534f49bba` |
| Worker-only crash | `ccfb22fe4f7047cd86f8fdfeaec8b3c2` | `inspection-0ef4f89a8ab64465acd352abe97067ca` |
| Controlled deadline | `707610755f264466845046ea67624efb` | `inspection-778d58af7aab4751b22f0d3ccc98d789` |

The cancellation run also retains a later process-observer exception; its completed
mode report is distinct from that failed overall run. The observer now tolerates
unavailable main-module information during startup and preserves executable/SID
identity requirements. Focused mode selection avoids repeating an already
completed mode solely because a later mode failed.

`.codex-temp/office-interruption-cleanup-verification.json` independently verifies
all nine candidate/source hashes, the nine inspected PDFs, removal of all 19
involved profile folders/mappings and absence of their SIDs from 27,241 runtime
and context ACLs. It explicitly records `WorkerReturnBoundaryPassed: false`.
The stronger final verifier remains failed; cleanup evidence does not override it.


Application-owned lifetime job
-------------------------------

The client now creates an unnamed, non-inheritable Windows job for each worker
and assigns the worker before sending its first command. The trusted worker's
startup waits for that command before starting an engine. The application retains
the job handle when the worker exits. Stopping the client terminates remaining job
processes and checks active-process accounting until empty, with a five-second
observation deadline. It cleans worker scratch only after successful process
cleanup; failures propagate and retain scratch evidence.

The job sets kill-on-close without breakaway flags or additional resource limits.
Private engine jobs retain their existing memory/process/security restrictions.
Microsoft documents [nested jobs, child membership and accounting](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
and the [assignment contract](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject).
This component targets Windows x64 and uses that platform's job-information layout.
Active Office engine cleanup after application loss and persisted profile recovery
still require their own tests. The PDF publication-crash regressions below cover
existing PDF journal boundaries, not Office profile recovery.

The real-worker harness now requires the retained host handle to be signaled
**when the client returns**, before its own fallback wait. Its worker-loss
replay is `.codex-temp/office-worker/28bfbb3d66154d04837420490f154b5e`.
The prepared worker receipt is
`ce6f4c03432d41cb836192e492ac22d2/retry-38ae46bcbcd440dcab4630b47454dbe5/build.json`.
The native host and engine inventory remain unchanged.

All **3,023 foundation contracts** pass after the client change; the retained log
is `.codex-temp/office-worker-job-foundation-8f70119ba0d54423bd96059fda2d8561.log`.
Application, worker and private-harness Release builds pass without warnings or
errors. Reserved production candidate 1.1.0 is unchanged.


Corrected matrix and broader regressions
----------------------------------------

All three modes now pass against matching product and harness source hashes:
**90 checks across nine stop/recovery cells**, in three focused runs. Each uses
the original generated 96-page DOCX/XLSX/PPTX interruption fixtures and an ordinary
following export through the same client. Every native host is already signaled
at client return. The deadline case fires the recorded 180-second application
timer after PDF growth; it does not wait 180 wall-clock seconds.

| Mode | Corrected worker evidence | Independent inspection beneath `contracts/` |
| --- | --- | --- |
| Worker-only crash | `28bfbb3d66154d04837420490f154b5e` | `inspection-795b578a75c2421186ca64ebeaa3733c` |
| Cancellation | `c982fd3a2ccf4818a47840f277ee6500` | `inspection-82255ad1878644db846eca901642dbb5` |
| Controlled deadline | `84937e3d7f284c13af6fbf5ffc92778e` | `inspection-3ec4c3684d68460da3ad016d06c5fe45` |

All nine recovery PDFs pass independent qpdf structure, authored-text, page-geometry
and exact PDFium control-pixel comparisons. Final reconciliation at
`.codex-temp/office-worker-job-final-verification.json` verifies current source and
worker identities, all original/copied-source and candidate hashes, removal of
18 profile folders/mappings, and absence of their SIDs from 27,202 runtime/context
ACLs. These PDFs are test candidates, not application-published Office outputs.

Broader checks use the same changed client:

- **3,223 foundation contracts including real image-worker integration** pass at
  `.codex-temp/worker-job-integration-d485ffb3e8b6424fa03fe3030caf29d0`. This includes
  the 3,023 base contracts; the two totals must not be added together.
- At `.codex-temp/worker-job-media-dd5b4898cf944b89afc78637ab15070d`, **96 audio
  interruption**, **24 FLAC optimization interruption**, **51 PDF failure**,
  **20 PDF-page interruption** and **56 combined-PDF workflow** checks pass.
  These include existing source/publication preservation and PDF publication-crash
  checks. They do not establish Office publication or active-Office app-loss recovery.

The first audio/PDF attempt at `worker-job-media-e2cd1e336cdb40cab852713311b2cb57`
used the retained candidate's older worker with the current client and failed at
the initial IPC exchange. The retained Core binary predates the new `OfficeWork`
field, and framed deserialization rejects unmapped properties. The successful
follow-up uses matching current managed worker files with the independently
verified retained audio/PDF engines in a fresh scratch directory. It is not a
production package. The test verifies the 116-file reserved payload and its
inventory unchanged before and after execution.

Next, construct and journal production Office contexts, implement independent
PDF validation and transactional copy publication, and verify application-loss
recovery through those actual boundaries. Broader admission, fonts, calculation,
network enforcement, policy-disabled Windows and runtime adoption remain open.
No new UI, installed lifecycle, screen-reader, theme/DPI or commerce acceptance is
implied by these automated checks.


Recoverable named job component
------------------------------

`WorkerProcessJob.CreateRecoverable` now creates a fresh, session-local named
job with kill-on-close and no breakaway. Its non-inherited handle uses an explicit
DACL granting access only to the current user and SYSTEM. Creation refuses an
existing job or another kernel object with the same name before changing limits.
The identity records the random job name, creator PID/start time and Windows
session. At this component checkpoint, ordinary `WorkerClient` calls still used
the existing unnamed constructor; the named component was not yet connected to
Office requests or journals. The subsequent dispatch binding is described below.

`StopRecordedAsync` requires a validated identity in the original session and
refuses while the recorded creator is alive. Failure to query the creator remains
a failure. Once the creator has exited, it opens only the named job with query
and terminate access, verifies the expected lifetime flags, terminates the job
and observes zero active processes before returning. Only the specific
file-not-found result reports that the object is absent. The caller must establish
confirmed job creation and assignment from trusted durable evidence before using
that absence to authorize any cleanup.

Opening a job keeps a handle alive, so recovery cannot rely solely on the
kill-on-last-close flag after opening it. Windows documents the
[job lifetime and nested process behavior](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects),
[creation collision result](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-createjobobjectw)
and [query/terminate access rights](https://learn.microsoft.com/en-us/windows/win32/procthread/job-object-security-and-access-rights).
The random name and restricted DACL do not authenticate a journal against another
full-trust process acting as the same user.

All **29 new lifetime checks** pass within **3,152 foundation contracts** at
`.codex-temp/worker-lifetime-foundation-da44dca806ef4aa1817ba8d04e938ed5`, with
matching source hashes. Two actual disposable owner/worker/descendant trees test
owner-only crashes. Without another job handle, both descendants exit. With a
retained observer handle, both remain live until recorded recovery explicitly
terminates them; both have exited when recovery returns. The named object is
then absent. Native membership queries verify the worker and descendant belong
to the expected job before each crash.

Additional checks verify the actual DACL, live-owner and session refusals, job
and event-name collisions, and refusal of an unexpected-limit job without
terminating its disposable process or changing its limits. All owned processes
are stopped. The Release application builds with zero warnings/errors. The
earlier 25-check run is retained separately at
`.codex-temp/worker-lifetime-foundation-8795009ddd8c4b3d9074b29ad2332dc7`.

The subsequent [version-three dispatch binding](office-ownership-journal.md#worker-lifetime-binding-before-dispatch)
records the assigned job identity before Office request bytes are sent and checks
failure before dispatch. Actual Office owner-loss recovery using that record
remains unfinished. These component tests use generated process trees; they create no Windows
profiles and run no Office engine. AppContainer access denial to the named job,
cross-session recovery and hostile same-user object substitution are not verified.
The older real-Office and broad media regressions were not rerun for this component.


Actual Office owner-loss acceptance
----------------------------------

The [journal recovery acceptance](office-ownership-journal.md#application-loss-during-office-rendering)
now kills only a disposable application owner while its DOCX/XLSX/PPTX renderer
is live and its PDF is growing. Reopened journal recovery confirms worker/host
exit before permission cleanup. All 81 checks pass, including three subsequent
ordinary exports with independent PDF inspection. Six recorded named jobs and
six profiles are absent after recovery and following exports. This uses the
private test owner; production restart orchestration and publication remain open.


Paired source-built interruption replay, 2026-09-23
-------------------------------------------------

The current paired application worker at
`.codex-temp/office-execution/bbd046b4e4054d63b8330d7d444ff101/worker` passes
**90 safety checks across nine active-PDF stop/recovery cells** at
`.codex-temp/office-worker/db678ec111fa4a5c8fecb2caf42fef08`.
Cancellation, worker-only loss and the controlled 180-second application deadline
all stop the exact native host before client return, preserve sources and permit
a following export through the same client. Measured stop times are 159-364 ms;
these are diagnostic timings, not the final performance benchmark. The deadline
is triggered after observed PDF growth, not after waiting 180 wall-clock seconds.

The generated Word/PowerPoint stress files still contain 96 pages. Excel uses the
unchanged, previously accepted 48-sheet capacity fixture. The first replay,
`f6d0658c92c84f249504c35318c7afd8`, stopped after Word because the old 96-sheet
workbook has 194 relationship parts, exceeding the existing chart-inspection
limit of 128. Diagnostic `9bde0efb8cfe4db5beadd05d345dab70` confirms this preflight
refusal and a successful ordinary workbook export. The replacement stress input
has 98 relationship parts. Its copy/source hashes and the other two original
stress fixtures are bound in `.codex-temp/office-isolation/paired-native-stop-01/preparation.json`.
No product admission limit or test stop condition changed.

The postverification receipt in the successful run checks source/worker/engine
identities, all originals and copied sources, and 23 profile folders/mappings
removed across the matrix, failed first attempt and diagnostic. All 6,098 owned
runtime/context ACLs are free of these test SIDs. This is active-PDF interruption;
it does not establish interruption before a calculated workbook ZIP is complete,
application-owner loss, network denial or final-candidate acceptance.

Independent PDF acceptance remains **failed**. The first inspector requires
updater settings that this compiled engine does not contain. Its failed receipt
is retained. The isolated inspector then links the existing updater-free profile
verifier, requiring the pinned build configuration, absent updater library and
registration, and all retained macro/active-content/Python/security/OpenCL and
calculation controls. Its 25 profile contracts pass; the shared verifier and PDF
oracle are unchanged. The second inspector only adds retained per-family
comparison details before the same exact-pixel refusal. Inputs and both source
variations are recorded under `.codex-temp/paired-stop-inspector-01` and `-02`.

`independent-replay-02/result.json` in the successful run records three failed
mode inspections. All nine PDFs pass qpdf structure, authored text and page
geometry; the six Word/Excel PDFs also match historical pixels exactly.
PowerPoint loses the authored `DDEEFF` light-blue text-box fill on both visible
slides, differing by 123,622 and 123,587 pixels from the earlier control. This is
not a small typography tolerance issue. The three recovery outputs agree with
each other. `prior-powerpoint-comparison/result.json` also verifies identical
OOXML package-part contents and exact pixels against the earlier uninterrupted
paired application publication at `be096d79feb741d3a3834d004c77c700`: the loss
predates this interruption replay. No tolerance was relaxed and that earlier
publication success does not establish fill fidelity.

Keep the native safety result separate from this rendering failure. The later
[shape data packaging repair](office-source-build.md#ooxml-shape-data-packaging-repair)
identifies omitted preset geometry files and restores exact historical pixels
through the restricted host without recompiling any native binary. The later
[application replay](office-runtime-verification.md#reconstructed-selection-with-shape-resources-2026-09-23)
selects that payload and passes all three ordinary PDF control-pixel comparisons.
Repeat the interruption matrix against this identity before accepting recovery
fidelity; the failed receipts above retain their original meaning.


Reconstructed runtime interruption replay, 2026-09-23
----------------------------------------------------

Runtime04 and the worker from
`.codex-temp/office-execution/45172c40ea494afabd97ab556b49910e` pass the same
**90 safety checks across nine active-PDF interruption cells** at
`.codex-temp/office-worker/45bfa7f9b7234457b0d7695a112c0d8c`. Cancellation,
worker-only loss and the controlled deadline each stop the exact native host
before returning and permit a following conversion through the same client.
The 96-page Word/PowerPoint and 48-sheet Excel fixtures are unchanged. Stop
observations are 144-325 ms; this remains diagnostic timing, not final performance
acceptance or a wall-clock wait for the 180-second deadline.

`independent-replay-01/result.json` now passes all nine following PDFs against
historical structure, authored text, page geometry and exact control pixels,
including both PowerPoint text-box fills. The retained inspector binary and PDF
oracle are unchanged. Its updater-free profile checks use the reconstructed
build configuration and runtime registration, preserving the 25 policy controls.
This closes the missing-fill failure for these recovery outputs; the old failed
receipts are not relabeled.

`postverification.json` verifies unchanged source/worker/runtime and original
identities, all 18 profiles and mappings absent, and all 5,809 owned runtime/context
ACL entries free of the test SIDs. The later recovery-harness version assertion
correction does not alter this run's product implementation or PDF oracle.
These results do not prove interruption of an incomplete workbook ZIP, native
network denial, final packaged bytes or owner review. Actual application startup
recovery is recorded in the [separate replay](office-startup-recovery.md#reconstructed-runtime-application-recovery-2026-09-23).

The subsequent [controlled unfinished-workbook replay](excel-calculated-date-guard.md#controlled-unfinished-workbook-interruption-2026-09-23)
passes all three stop modes at an actual incomplete ZIP boundary. Its test-only
breakpoint is removed and debugger detached before the existing lifetime shutdown
runs. Production shutdown code and native engine bytes are unchanged. The later
[managed guard tests](excel-calculated-date-guard.md#managed-validation-interruption-2026-09-23)
cover active hash/worksheet-read cancellation; broader resource/security and exact
final-candidate checks remain.


Exact-candidate interruption replay, 2026-09-24
---------------------------------------------

The original unsigned 1.1.2 worker now passes all 90 checks across nine active
PDF stop/recovery cells at
`.codex-temp/office-worker/784605ee737b4fc0b44e5e21f055a57f`.
Cancellation, worker loss and controlled deadline expiry each observe a growing
native PDF, stop the retained native process before return and permit a following
export. No native or managed rebuild was needed.

The completed verification receipt is
`.codex-temp/candidate-112-office-interruptions-70100b6c17d44a5e94989bc0755455d5/verification-02/results.json`.
All nine following PDFs pass independent structure, authored text, page geometry
and exact control-pixel comparisons. All eighteen profiles and registry mappings
are absent; 5,809 owned runtime/context ACL entries contain none of the test SIDs.
Captured candidate files, sources, original/snapshot inputs and membership remain
unchanged. The contract host uses the shipped Core and Private DLLs.

The initial independent inspection selected the generic inspector, which requires
an updater setting absent from this reconstructed runtime. That failed receipt
is retained at the parent evidence directory. The already-verified
`PairedStopInspector02` checks the selected updater-free build configuration,
runtime registration and all 25 profile controls, then applies the unchanged PDF
oracle. Its binary/source identities match the earlier runtime04 receipt. Only
inspection and cleanup verification were resumed; the 90-check export matrix
was not rerun. No production setting or PDF tolerance was changed.

Actual application startup recovery, unfinished-workbook interruption and
affirmative network evidence retain their separately recorded scopes.
