Office Worker Lifetime Verification
==================================

Status: application-owned worker job and corrected interruption boundary verified;
Office context recovery, publication and customer command remain incomplete

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
