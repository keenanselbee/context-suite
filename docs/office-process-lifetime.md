Office Evaluation Process Lifetime
==================================

Status: evaluation launcher and authored-process contracts pass, with three
passive Office exports verified on 2026-09-13. Required customer Office-to-PDF
conversion and filesystem/network isolation remain incomplete.

The evaluation harness previously used `Process.Start` and a process-tree kill
attempt. A launcher could exit while a descendant retained diagnostic handles;
a failed diagnostic reader could also wait for the entire enclosing deadline.
`OfficeEvaluationProcess` now owns Office initialization/export and independent
qpdf/PDFium child invocations through a separate unnamed Windows job per run.

The process is created suspended with an explicit list of three inherited pipe
handles and `PROC_THREAD_ATTRIBUTE_JOB_LIST`. Windows assigns the job during
process creation, before any child code runs; there is no separate unowned
creation-to-assignment interval. The main thread resumes after the managed
process handle and readers are ready. Microsoft's
[process attribute documentation](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)
defines the handle and job lists. The job handle is not inherited.

Each job requests kill-on-close, eight active processes, 512 MiB per-process
commit and 1 GiB aggregate commit. These are configured evaluation limits, not
measured Office memory acceptance or a universal ceiling on Windows helpers.
The deadline is 60 seconds. Each diagnostic stream is capped at 64 KiB before
text decoding; UTF-8 multibyte output cannot increase that byte allowance.
Input is an empty pipe. Arguments are passed directly without a shell and the
existing explicit profile environment is retained.

Cancellation and failed readers terminate the owned job. Root-process exit also
terminates remaining members before waiting for diagnostic EOF. Cleanup checks
actual active-process accounting until zero, with a separate five-second limit.
Successful reports retain root PID, total job processes and elapsed time; the
final report schema additionally records zero active processes after cleanup.
Owner death closes the sole job handle, independently of graceful cleanup.

Reproduce the helper contracts from the repository:

```powershell
.\tools\office-engine\Test-OfficeProcesses.ps1
```

This builds the evaluation host and runs only its own disposable helper modes.
Thirteen checks pass for literal empty/spaced/quoted/Unicode arguments, explicit
environment, empty stdin, separate diagnostics, successful completion, nonzero
exit, ASCII and multibyte diagnostic overflow, timeout, root-exit descendants,
cancellation, abrupt owner death and subsequent successful work. Tree checks
hold actual process handles and verify executable path and creation time before
observing exit. The owner-crash test kills only its created owner, not its tree;
the descendant exit assertions therefore depend on job ownership.

Evidence is `.codex-temp/office-process-ce61ca2463b341edaf81a727912cd4d9/process-contracts.json`,
with `.codex-temp/office-process-final.log` and its matching zero exit record.
ASCII/multibyte overflow cleanup took 86/84 ms; the requested two-second timeout
took 2,028 ms. These are one-run observations, not performance guarantees.
The first attempt's assertion required the exact cancellation exception type and
rejected .NET's derived `TaskCanceledException`. The corrected assertion accepts
the expected exception family. That failed run remains in
`.codex-temp/office-process-contracts.log`; no launcher failure was hidden by it.

The standard verified-payload Office wrapper also passes all seventeen profile
declaration checks and the three generated exports: Word two pages (9,885 ms),
Excel one page (8,848 ms), PowerPoint two pages (9,234 ms). qpdf structure and
independent PDFium text/geometry/render checks pass. Sources, PDFs and retained
profile-verification records were separately reconciled. Each conversion reports
four total job processes. No Office processes remained after completion.

This evidence is under
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-3451ba55b086488482d62cfe299142b6`,
including `office-evaluation.json`, per-conversion records and
`job-source-output-reconciliation.json`. The wrapper log is
`.codex-temp/office-job-evaluation.log`, exit zero. The explicit zero-active field
was added after these exports; the final helper run verifies that reporting
addition, while the same zero-active cleanup check already guarded the exports.

The Release host builds without warnings/errors. Repository boundaries, theme
policy, documentation and whitespace validation are checked separately. No
production implementation or payload changes; broader foundation/media suites
were not rerun for this evaluation-only work.

These jobs control lifetime and resource requests, not access to files, registry
or networks. The [AppContainer experiment](office-isolation-evaluation.md) still
awaits its separately requested authorization. Arbitrary documents, active-document
cancellation/crashes, hostile-content denial, memory-pressure behavior, rendering
policies, production integration and visible acceptance remain required. Only
the authored passive fixtures may use this evaluation launcher.

Actual engine startup interruption (2026-09-13)
----------------------------------------------

`Test-OfficeEngineLifetime.ps1 -PreparedDirectory '<retained Office directory>'`
now verifies the MSI and complete recorded payload before running two bounded
engine-lifetime cases. It accepts no document path. Each case initializes a new
short repository-local profile, reapplies/verifies the existing profile settings,
then starts headless LibreOffice without a document or termination argument.

The launcher exposes a scoped observer that can test membership in its exact
job using Microsoft's [IsProcessInJob API](https://learn.microsoft.com/en-us/windows/win32/api/jobapi/nf-jobapi-isprocessinjob).
The observer requires both `soffice.com` and the pinned `soffice.bin` to be members
of that job, checks their executable paths and retains process handles, creation
times and a 300 ms live control interval. Looking up an engine process by name
alone never authorizes observation as this test's owned engine or termination.

Both cases pass:

| Case | Observed stop | Same-profile recovery |
| --- | --- | --- |
| Cancellation after live engine control | Launcher and engine handles signal exit 71; cleanup 23 ms | Initialization completes in 2,211 ms; settings verify; zero active job processes |
| Abrupt evaluation-owner death | Only owner is killed, exit -1; retained launcher and engine both signal exit 0; cleanup 19 ms | Initialization completes in 2,136 ms; settings verify; zero active job processes |

Exit zero after job-handle closure is an observed exit code, not proof of graceful
engine shutdown. The owner-crash observer independently checks the reported PIDs,
creation times and executable paths, and verifies another live interval before
killing its created owner. It holds process handles, not the owner's job handle.
No tree kill or unrelated-process termination can satisfy these assertions.

After each interruption the settings file opens with exclusive sharing and the
same profile restarts with `--terminate_after_init`. No fresh replacement profile
hides recovery failure; all profile files and evidence are retained. No documents
were opened, and there is no partial-PDF or active-document recovery claim.

Evidence is under the pinned prepared directory at
`lifetime-e786667dfa594244b749f1d6e4c88868/engine-lifetime.json`, with per-case
`profile.json` and `stopped.json`. The wrapper log is
`.codex-temp/office-engine-lifetime.log`, matching exit record zero. No Office or
evaluation processes remained after completion.

Sixteen helper contracts now pass, including three added checks for exact-job
membership, observer failure cleanup and canceling an unfinished observer when
its root exits. The last case prevents observation from delaying cleanup until
the full deadline. Its evidence is
`.codex-temp/office-process-f0c5511554f54f068daf11f652a228e5/process-contracts.json`
and `.codex-temp/office-engine-observer-final.log`, exit zero. The earlier thirteen
helper checks also passed before this observer extension; they are not additional
independent cases. The Release host builds without warnings/errors.

The engine cases exercise startup interruption and profile reuse, not interruption
during Word/Excel/PowerPoint rendering, full descendant inventory after owner
death, external-access denial, memory pressure or commercial release acceptance.
The AppContainer authorization, rendering policies and required production Office
converter remain open. Foundation/media suites and production staging were not
rerun for these evaluation-only changes.

The later [Word export-interruption experiment](office-export-interruption.md)
adds a 96-page complete control plus cancellation and owner failure after actual
temporary PDF growth. Both interrupted profiles subsequently export a valid
two-page document. It also records an initial engine restart and follows only
its replacement in the same owned job. The startup counts above remain separate
evidence; Excel/PowerPoint interruption and isolation are still open.
