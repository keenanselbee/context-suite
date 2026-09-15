Office AppContainer Startup Evaluation
=====================================

Status: embedded Word, Excel and PowerPoint exports pass the authored ordinary /
AppContainer matrix and independent PDF checks. The Windows main-loop lifecycle
and fresh read-only input copies resolve the recorded loading failures. Broader
fidelity, hostile-content/recovery checks, network enforcement and required
customer conversion remain open.

Purpose and boundary
--------------------

The required Office converter needs both an enforced process boundary and an
engine that works inside it. The [native isolation experiment](office-isolation-evaluation.md)
tests file/token/environment behavior with an authored helper. This record starts
with version reporting, then records passive exports and targeted initialization
diagnostics. Each experiment's actual scope is stated below; none establishes
hostile-content handling or required customer conversion.

Preparation and execution
-------------------------

```powershell
.\tools\office-engine\Test-OfficeIsolation.ps1 -CreateDisposableProfile `
  -PreparedOfficeDirectory '<verified repository-local Office evaluation directory>'
```

The registered disposable profile requires the existing explicit owner permission;
the version option cannot run without that profile opt-in. It does not install
Office, register a package or alter Explorer. Do not run this test elevated.

`Prepare-OfficeIsolation.py` verifies the pinned MSI and the complete retained
runtime inventory, then copies only its members into a fresh case's `runtime/office`
directory. The dedicated `runtime` parent contains only that engine and receives
read/execute access, allowing the engine's parent-directory lookup without granting
access to the surrounding staging directory. It verifies source and copied bytes/hashes, rejects reparse paths and
noncanonical/ambiguous inventory names, and rejects existing or out-of-scope
destinations. Earlier generated GUID-named crash dumps beside `soffice` are
recorded and excluded from the copy; they remain untouched in the source.
Missing members and other unlisted files are refused before engine launch.
The copy receipt retains the archive/inventory identity and every expected file.

Only the dedicated runtime boundary receives read/execute access for the disposable profile.
The retained original runtime's ACLs are untouched. Ordinary and AppContainer
controls run the same fixed version command with separate owned UserInstallation
paths, headless flags and the explicit environment. Native critical-error dialog
handling is disabled for these owned launches. The existing 30-second deadline,
diagnostic cap, job memory/process bounds and complete job cleanup apply.

Before the isolated process resumes, the parent verifies its actual AppContainer
token, exact requested package SID and zero capabilities. A successful result
must exit zero and report the exact selected build string. Logs and process/job
results are retained separately for the control and isolated run. This verifies
the launched root token; it does not independently inspect every descendant's
token or prove arbitrary code cannot perform network I/O.

The version experiment runs after the helper access observations and before
profile removal, even when network observations have not established denial.
Its result cannot turn the failed network matrix into a pass. If Office cannot
start, its diagnostic is retained and the owned profile still receives cleanup.

Version-only results (2026-09-14)
--------------------------------

The ordinary and AppContainer commands both return the exact pinned version:
`LibreOffice 26.2.6.3 8221e31b3ac356a1623c672912a3d2b492f7e3d1`. Both exit zero
within the existing deadline and diagnostic limits; their jobs report zero active
processes after cleanup. The isolated root's actual AppContainer SID and zero
capabilities are checked before execution. The helper's environment/storage,
file-denial, lifetime, resource and owner-crash checks also pass.

Evidence is `.codex-temp/office-isolation/d8f28608774f469281ce4f7d8cafa5e5`.
`office-copy.json` records 19,332 pinned files, 1,517,294,910 bytes and eleven
excluded source crash dumps. The first attempt at
`2ce9baaa21d74c65852916644691d917` refused those unlisted diagnostics before
copying or launching Office. The subsequent copy rule preserves them and excludes
only GUID-named `.dmp` files; all other unexpected membership is still refused.
Three path-boundary refusal checks create no destinations. A fourth refuses the
existing runtime destination without changing its directory write time.

The x64 `/W4 /WX` build passes. Logs use `office-isolated-version` and
`office-isolated-version-final` prefixes; path-guard evidence is
`.codex-temp/office-isolated-version-guards.json`. The final receipt is
`.codex-temp/office-isolated-version-verification.json`, covering post-run source
and copied-runtime hashes, both version results and profile cleanup.

The combined isolation test still **fails**, because IPv4/IPv6 attempts reach
observation deadlines without proving denial. This is a version-reporting pass
within a failed broader isolation matrix. No document was opened, no Office
conversion was implemented, and no product payload or installed state changed.


Remaining work
---------------

Evaluate engine/profile initialization, fonts and runtime dependencies, document loading,
rendering policies, independent PDF validation and publication in the same
boundary. Network enforcement evidence remains separately unresolved. Do not
enable the required customer converter merely because version reporting works.


Passive initialization and export experiment
-------------------------------------------

The opt-in `-PassiveExports` switch additionally initializes fresh profiles and
exports the existing authored Word, Excel and PowerPoint fixtures in ordinary
and AppContainer processes. No caller-supplied document or filter is accepted.
Sources/settings have a before-run hash/write-time manifest and read-only access
for the AppContainer. Each profile starts with disabled macros, active content,
Python runtime and update checks; the requested settings are reapplied after
initialization. Each initialization and export has a 60-second deadline under
the same process/memory/diagnostic bounds. Profiles use short owned names.

The fixed PDF recipe preserves image resolution, uses lossless image compression,
excludes notes/hidden slides and hides Word tracked-change markup. The small
Excel fixture's saved formula result agrees with its arithmetic; it cannot decide
the separate saved-values/recalculation policy. Generated outputs stay in scratch,
without customer publication, admission or recycling. Runtime/version controls
still run first. Failure does not skip owned-profile cleanup or become a network
enforcement pass.

Independent inspection is separate so failed native runs retain all observations:

```powershell
.\tools\office-engine\Inspect-OfficeIsolationExports.ps1 -StagingId '<case-guid>' `
  -PdfPreparedDirectory '<verified qpdf evaluation directory>' `
  -PdfiumPreparedDirectory '<verified PDFium evaluation directory>'
```

The inspector verifies the fixed source/settings manifest, checks retained
profile declarations, runs qpdf structure checks and independently extracts text
and renders with PDFium. It requires the authored page counts/geometry and visible
markers, including Word page fields and hidden-sheet/slide exclusions. Normal and
isolated results must match normalized text and exact rendered pixels. Missing
exports and per-case failures remain explicit. This is scoped evidence from tiny
authored documents, not a Microsoft Office baseline or hostile-input certification.


Initialization failure and environment control (2026-09-14)
---------------------------------------------------------

Evidence is `.codex-temp/office-isolation/f2f5447d26614e9f9b2f2e4e4c7c5e86`.
The x64 `/W4 /WX` native build and managed evaluation compilation pass. Both
version commands still match. Each ordinary initialization succeeds and its
family exports a bounded PDF. Independent qpdf/PDFium checks pass on all three
ordinary PDFs: two Word pages, one Excel page and two visible PowerPoint slides,
with expected text, geometry, page fields, profile settings and original hashes.

All three AppContainer initializations fail before document export. Word exits
with code 1 without reaching the launcher deadline; Excel and PowerPoint reach
the 60-second deadline and are terminated by the owned job. Their engine
diagnostic streams are empty. Every completed launch reports zero active job
members after cleanup, and the disposable AppContainer profile is removed.
No isolated PDF exists, so no normal/isolated pixel comparison is counted as a
pass. Both the native matrix and independent inspection remain failed.

A separate ordinary-process control uses the exact redirected eight-variable
environment from this case, a fresh short profile and the same fixed
`--terminate_after_init` command. It succeeds in 1,984 ms and leaves no active
job member. This rules out those redirected paths alone as the explanation;
it does not identify a specific denied API, file or registry key. No access grant
or capability was added to make initialization succeed.

The diagnostic mode is `--office-redirected-control <completed case directory>`
on the newly built native probe. Verify its runtime copy before running it; it
refuses an existing `writable/ec` profile and opens no document or AppContainer
profile. The case's `redirected-build.json` binds the diagnostic binary to its
source hashes. `redirected-control.json` and `.log` retain the actual result.
This is a targeted follow-up, not a replay of the six failed/successful cases.

Native logs use `.codex-temp/office-isolated-exports`; independent inspection logs
use `.codex-temp/office-isolated-export-inspection`. Its retained six-case report
is `inspection-6d1634a352f74c98b023949fa21e5fb5/results.json` under the case's
staging parent. Post-run runtime/source and cleanup reconciliation is
`.codex-temp/office-isolated-exports-verification.json`.

The next integration step is to diagnose actual restricted initialization and
verify a working boundary before admitting customer documents. Network enforcement
is independently unresolved. The successful ordinary exports do not substitute
for isolated Word/Excel/PowerPoint conversion, and no production payload changed.


Owned startup diagnostics (2026-09-14)
------------------------------------

The opt-in `-StartupDiagnostics` mode runs only empty-profile initialization,
once normally and once in the AppContainer. It requires the prepared runtime and
disposable-profile opt-in and cannot be combined with `-PassiveExports`. It skips
the separate access/network matrix; that matrix's earlier failure remains open.
The same job limits, 60-second deadline, disabled-content settings and profile
cleanup apply. No document is opened and no capability is added.

This mode requests `SAL_LOG=+INFO+WARN+TIMESTAMP` and disables OpenCL through
`SAL_DISABLE_OPENCL=1`, in addition to the eight explicit environment variables.
LibreOffice's [logging documentation](https://docs.libreoffice.org/sal/html/sal_log.html)
explains that runtime filters can expose only logging compiled into the build.
Empty diagnostic streams therefore do not identify or rule out a failure cause.

The observer retains handles to observed job members and distinguishes natural
exits from termination during cleanup. It reads window classes, captions and
visibility only for those owned processes, with bounded text requests. It never
clicks or changes windows. Short-lived children and later caption changes can be
missed; these records are observations, not exhaustive process or UI coverage.

Fresh `cas3` through `cas6` roots reuse the verified runtime in the staging parent
above; each disposable profile is removed before the next run. Ordinary controls
all initialize successfully. Restricted initialization remains unsuccessful:

| Case | Ordinary duration | Restricted result | Diagnostic text |
| --- | --- | --- | --- |
| `cas3` | 2,031 ms | Natural exit 1 after 6,125 ms | Empty |
| `cas4` | 1,969 ms | 60-second timeout; observed children still running before cleanup | Empty |
| `cas5` | 2,063 ms | Natural root/engine exit 1 after 27,125 ms | Empty |
| `cas6` | 2,187 ms | 60-second timeout; observed children still running before cleanup | Empty |

In timeout cases, exit code 1 comes from owned-job termination and must not be
reported as an independently observed engine crash. Every recorded job has zero
active members after cleanup. `cas5` exposes a visible `SALFRAME` window titled
`LibreOffice 26.2`, despite headless startup. The subsequent read-only UIAutomation
inspection of the owned `cas6` engine exposes only that window title and no
descendant error text. It does not establish the dialog's cause, usability or
screen-reader delivery. No unrelated application was operated or closed.

`window-build.json` binds the final `/W4 /WX` diagnostic binary to its sources.
Earlier `startup-build.json` and `child-final-build.json` bind the preceding
diagnostic revisions. The initial child-observer build failed on an implicit
character conversion; it was corrected before execution. Each case retains
`startup-*.json`, logs, child/window observations and `profile-cleanup.json`.
The stage also retains `owned-window-accessibility.json` and its stderr file.
Post-run `.codex-temp/office-startup-diagnostics-verification.json` verifies all
19,332 source and copied runtime members, authored fixture hashes, final binary
and source hashes, and absence of the Windows profile folder/registry mapping.
No owned Office process remains. Wrapper syntax, public-source boundaries, theme
policy and documentation checks pass; restricted startup itself still fails.

These diagnostics narrow the failure to full restricted initialization, but do
not establish a denied API or a viable renderer boundary. Further runs should
test a specific new hypothesis or collect different evidence, rather than repeat
the same startup flags. Network enforcement remains a separate unresolved gate;
required customer Office conversion remains unavailable.


Profile-copy hypotheses (2026-09-14)
----------------------------------

The retained ordinary profiles contain fifteen files and mark initial user setup
complete; restricted fresh profiles contain only two files and lack that marker.
The pinned [user-installation source](https://raw.githubusercontent.com/LibreOffice/core/libreoffice-26.2.6.3/desktop/source/app/userinstall.cxx)
copies preset user data before setting `ooSetupInstCompleted`. That difference
motivated two bounded experiments, without broadening access:

- `cas7`: an authored child performs a source read, manual write, `CopyFileW`,
  copy readback and directory creation under the existing grants. Ordinary and
  AppContainer controls return success for all five operations. This eliminates
  a general inability to copy a permitted file, not every operation in Office's
  recursive setup sequence.
- `cas9`: ordinary startup succeeds in 1,937 ms. The parent copies that owned
  completed profile to a fresh restricted profile, retaining the disabled-content
  settings and setup-complete marker. Restricted startup still exits 1 naturally
  after 6,625 ms with empty diagnostics and zero active job members after cleanup.
  All fifteen final profile files match the control byte-for-byte. Pre-initializing
  this profile therefore does not resolve the failure.

The first seeded attempt, `cas8`, failed in test setup because its destination
parent directory did not exist; no restricted Office launch occurred. That
attempt's profile was removed. The corrected experiment creates its own parent
before copying. All three disposable profiles report cleanup, and the Windows
profile folder and registry mapping are independently verified absent.

These are scratch-only variants of the authored probe, not shipping behavior or
new supported invocation modes. Under the existing staging parent, their source,
binary hashes and logs are retained in `copy-diagnostic-c602a8946992481a8286b26c4b1ffe0a`,
`seeded-diagnostic-61d08e3e2cbf4edda704ddedc868a31b` (setup failure) and
`seeded-diagnostic-e63b7ac8bff34d508ec1b396d87fcfca` (completed experiment).
`.codex-temp/office-startup-hypotheses-verification.json` records profile identity,
settings, outcomes and cleanup. Native x64 `/W4 /WX` builds pass. Further Office
work needs a more specific failure diagnostic; neither hypothesis justifies
widening permissions or admitting customer documents.


Runtime lookup and IPC diagnosis (2026-09-14)
-------------------------------------------

A scratch-only debugger launches the owned process with `DEBUG_PROCESS` and
observes its descendants under the existing job, token and environment limits.
It never attaches to an existing application, changes memory/registers or opens
documents. It continues the initial loader breakpoints and passes other exceptions
to the program's handlers. Debug events affect execution timing; they are
diagnostic evidence, not ordinary performance or UI acceptance.

The original layout produces two caught UNO exceptions absent from the ordinary
control: `Exception` reports "Extension Manager: Could not obtain path for
UserInstallation." A later `IllegalArgumentException` concerns an invalid UI
module. Exception types are identified from MSVC metadata; a follow-up reads the
bounded Message field using the pinned generated UNO exception/string layout.
The first debug record included trailing data after debug-string terminators;
the corrected observer stops at the first terminator and drains process-exit
events before moving to the next launch. These scratch diagnostics do not ship.

The pinned [bootstrap implementation](https://raw.githubusercontent.com/LibreOffice/core/libreoffice-26.2.6.3/unotools/source/config/bootstrap.cxx)
checks installation directories through the OSL directory API. Its
[Windows implementation](https://raw.githubusercontent.com/LibreOffice/core/libreoffice-26.2.6.3/sal/osl/w32/file_dirvol.cxx)
uses `FindFirstFileW`. An authored restricted probe finds that the old runtime
root returns error 5 from that API while `GetFileAttributesW` succeeds. Its
children are accessible. Granting only the runtime root leaves the parent lookup
outside the allowed directory.

The preparer now creates `runtime/office` and grants read/execute on the dedicated
`runtime` directory, which must contain only the copied engine. It refuses an
existing boundary, malformed layouts and linked paths. The staging directory
receives no new grant. A post-change probe verifies successful lookup of the
engine root, its program directory and the writable profile, while lookup through
the surrounding stage and the withheld fixture remains denied. Five destination
guard checks pass. Runtime contents and originals remain unchanged.

The corrected layout is staged at
`.codex-temp/office-isolation/1a880d93be994b9a9a3467bb77aa1ba3`.
Ordinary startup succeeds; restricted startup still reaches its 60-second deadline.
A subsequent debug run (`cs13`) no longer observes either C++ exception, but
still times out. This fixes a demonstrated directory-lookup defect, not all
initialization requirements.

The pinned [Office IPC loop](https://raw.githubusercontent.com/LibreOffice/core/libreoffice-26.2.6.3/desktop/source/app/officeipcthread.cxx)
retries pipe creation/opening, and the [Windows pipe implementation](https://raw.githubusercontent.com/LibreOffice/core/libreoffice-26.2.6.3/sal/osl/w32/pipe.cxx)
uses the standard `\\.\pipe\` namespace. In a separate authored test (`cs14`),
ordinary creation succeeds with both standard and `LOCAL` names; AppContainer
creation returns error 5 for the standard name and succeeds with `\\.\pipe\LOCAL\`.
Only unique disposable servers are created and closed; no client connects.
This agrees with Microsoft's [AppContainer pipe requirement](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-connectnamedpipe).
The source and API observations identify an IPC incompatibility consistent with
the later stall; this is not a captured call stack proving its sole cause.

Original-layout debug/path evidence is `cs10` through `cs12` under the preceding
`f2f5447d26614e9f9b2f2e4e4c7c5e86` stage. Corrected-layout startup is `case`, debug
is `cs13`, pipe creation is `cs14`, and directory-boundary verification is `cs15`
under the new stage. Each scratch diagnostic directory retains its authored
source, binary/source hashes and logs. All jobs and disposable profiles are
cleaned up. Reconciliation receipts are `.codex-temp/office-runtime-diagnosis-verification.json`
and `.codex-temp/office-runtime-boundary-verification.json`; destination checks
are `.codex-temp/office-runtime-boundary-guards.json`.

Next evaluate a supported embedded-engine path or a separately reviewed source
integration with compatible IPC. The pinned `mergedlo.dll` exports
`libreofficekit_hook` and `libreofficekit_hook_2`; their presence alone proves
neither usable Windows embedding nor sandbox compatibility. Do not patch vendor
binaries, weaken the process boundary or claim customer conversion from these
results. Network enforcement remains independently unresolved.


Embedded startup (2026-09-14)
-----------------------------

The pinned [embedded initialization implementation](https://raw.githubusercontent.com/LibreOffice/core/libreoffice-26.2.6.3/desktop/source/lib/init.cxx)
enables request handling without creating the desktop IPC thread. This is a
different supported entry point in the same verified runtime, not an edit to a
vendor binary or a weaker process boundary. The independently declared ABI prefix
uses only its size and destroy callback; no document, macro or network method is
invoked. See the pinned [public API declaration](https://raw.githubusercontent.com/LibreOffice/core/libreoffice-26.2.6.3/include/LibreOfficeKit/LibreOfficeKit.h).

```powershell
.\tools\office-engine\Test-OfficeIsolation.ps1 -CreateDisposableProfile `
  -PreparedOfficeDirectory '<verified repository-local Office evaluation directory>' `
  -EmbeddedStartup
```

The mode is mutually exclusive with desktop startup diagnostics and passive
exports. It requires the authorized disposable profile and verified runtime
preparation. It launches two independently built helper aliases with no command
arguments, one ordinary and one restricted. Each receives its own fresh profile
with the existing disabled-content settings. DLL search is restricted to System32
and the copied engine directory. The process calls `libreofficekit_hook_2`, checks
the returned instance/ABI prefix, and invokes its destroy callback. Initialization
and destruction markers, process results and job cleanup are retained. Engine
DLLs stay loaded until that owned child exits.

The original harness (`cs16`) called the internal `osl_setCommandArgs` API and
launched the child with probe-specific switches. Both controls crashed before
returning an engine instance. The debug follow-up (`cs17`) locates the fatal
access violation in `sal3.dll` at offset `0x312ed` in each control. The pinned
[process header](https://raw.githubusercontent.com/LibreOffice/core/libreoffice-26.2.6.3/include/osl/process.h)
marks the argument-reset API internal/deprecated and ineffective on Windows.
The corrected harness omits that call and launches without arguments. These two
changes remove the failed harness behavior together; the individual contribution
of each is not claimed.

The corrected experiment (`cs18`) initializes and destroys both instances:
ordinary 11,219 ms and AppContainer 7,079 ms. Both exit zero within the same
60-second, memory, process and diagnostic limits. Each job has zero remaining
members, and its disposable profile is removed. No document is opened and no
isolated PDF exists from this experiment. This establishes embedded startup and
shutdown only; it does not validate import, rendering, fonts or output fidelity.

These cases are under
`.codex-temp/office-isolation/1a880d93be994b9a9a3467bb77aa1ba3`.
Retained build directories are `kit-diagnostic-a8e68f5d22504b5a9a1247951478e365`,
`kit-debug-8d4dcdc52bd944cc84aebdd4e0e236a1` and
`kit-diagnostic-bad1eb22e22741c587bcfa50a70d47d4` respectively. Their source/binary
hashes bind each observed revision. Failed observations remain retained separately.

The promoted public mode is independently rebuilt and rerun as `cs19`, bound by
`embedded-final-987253c470e44379afd6406f23530b4e/build.json`. Ordinary initialization
and destruction finish in 6,203 ms; restricted initialization and destruction
finish in 5,984 ms. Both exit zero and leave zero active job members. The final
restricted root's AppContainer identity and zero-capability token are checked
before execution. Wrapper syntax and three invalid/conflicting mode checks pass.
`.codex-temp/office-embedded-startup-verification.json` reconciles runtime/source
integrity, retained disabled-content settings, fixture hashes, source/build
identity and removal of the disposable Windows profile and registry mapping.

Next extend this entry point to the existing authored Word, Excel and PowerPoint
fixtures, with explicit load/export policies and independent qpdf/PDFium checks.
Recheck disabled-content settings after initialization and loading, source/output
preservation, resource limits and recovery. Successful embedding does not resolve
the independent network-enforcement gate or enable customer conversion.


Authored embedded exports (2026-09-14)
--------------------------------------

`-EmbeddedExports` extends the API check to the existing three passive authored
fixtures. Each format/control receives its own process, profile and output
folder. The combined initialize/load/export/destroy operation has a 60-second
bound with the same memory, process and diagnostic limits. AppContainer processes
retain the verified zero-capability identity, dedicated runtime read boundary,
read-only fixture directory and owned writable scratch. No arbitrary customer
file is accepted, and no output is published or recycled.

The independently declared stable API prefixes validate their size and required
callbacks. Loading uses `Batch=true,EnableMacrosExecution=false,MacroSecurityLevel=3`.
The pinned [loader implementation](https://raw.githubusercontent.com/LibreOffice/core/libreoffice-26.2.6.3/desktop/source/lib/init.cxx)
otherwise sets macro security to 1. Batch requests silent loading and macros use
`NEVER_EXECUTE`; these settings are not proof of external-resource isolation.
The loader's `UpdateDocMode` assignment is commented out in that source. Do not
claim this API explicitly enforces a no-refresh policy. PDF export uses the
existing fixed JSON settings, including hidden notes/slides and tracked-change
markup exclusions. The pinned export implementation accepts JSON filter options.

The first attempt, `cs20`, initializes all six processes but fails input type
detection. Its Windows-generated file URLs encode the fixture's U+00FC as `%FC`.
The corrected helper encodes UTF-8 bytes (`%C3%BC`), including percent, query and
fragment characters, for its absolute local file URLs. Retained failures are not
overwritten. The corrected `cs21` results are:

| Fixture | Ordinary | AppContainer | Independent output checks |
| --- | --- | --- | --- |
| Word DOCX | Pass, 7,953 ms | Pass, 7,407 ms | Both two-page PDFs pass qpdf, authored text and 612 x 792 point geometry; text and both rendered pages match exactly at 96 DPI |
| Excel XLSX | Loading timeout, 60,125 ms | Loading timeout, 60,110 ms | No output; failure retained |
| PowerPoint PPTX | Loading timeout, 60,125 ms | Loading timeout, 60,125 ms | No output; failure retained |

All four timeout logs reach engine initialization but never return a loaded
document. Each job contains the helper and its console host and has zero members
after cleanup. Both attempts remove their disposable Windows profile. The full
six-case export and inspection commands deliberately return failure. Word's
passing result does not clear the other formats or the required customer action.

Evidence is retained under
`.codex-temp/office-isolation/1a880d93be994b9a9a3467bb77aa1ba3`:
`embedded-exports-d1b11bdd818c4896807de58be29e955e/build.json` binds the first
attempt; `embedded-exports-3db73024406e4672a7fd0a89605002bb/build.json` binds the
corrected native source/build. The independent inspector's
`inspection-8ff9fc922f7642b5b6adc300916d53f2/results.json` records `cs21` explicitly,
including all six outcomes and Word's exact text/pixel comparison. Invoke the
inspector with `-CaseName cs21` for this retained case; its default remains `case`.

`.codex-temp/office-embedded-exports-verification.json` confirms all 19,332
runtime members remain unchanged in both source and copy, exact copied membership,
authored fixture hashes, native source/build identity, all seven disabled-content
profile settings and absence of the disposable Windows profile folder/mapping.
The independent inspector also verifies original fixture timestamps and unchanged
PDFs. Native and managed builds pass with zero warnings/errors; both wrapper
syntax checks, four conflicting/missing-mode refusals and the repository's
138-document/public-boundary/theme checks pass. These infrastructure checks do
not turn the four document-loading failures into passing exports.

The next focused investigation is the embedded loader/main-loop interaction for
Excel and PowerPoint. Both ordinary controls reproduce their restricted failures,
so additional AppContainer permissions are not justified by these observations.
Network enforcement, hostile/active-content cases, broad fidelity, recovery and
production integration remain separate open gates. No visible UI, screen-reader,
installed-shell or customer-conversion acceptance is claimed.


Windows main-loop and input-copy correction (2026-09-14)
--------------------------------------------------------

The focused `cs22` Excel diagnosis samples only the owned root process with
[thread-context snapshots](https://learn.microsoft.com/en-us/windows/win32/api/processsnapshot/nf-processsnapshot-psscapturesnapshot)
and [stack walking](https://learn.microsoft.com/en-us/windows/win32/api/dbghelp/nf-dbghelp-stackwalk64).
No debugger attaches to an existing application, no live registers/memory are
written, and no symbol server or inherited symbol path is used. Two samples,
at approximately 20 and 40 seconds, each take about 140 ms. Both place the
restricted loading thread in `NtUserSetWindowPos`, with VCL window sizing,
widget construction and document loading below it. Another thread runs the Office
main loop through a condition wait. Contexts are snapshots but stack memory is
read live; exported-symbol displacements are retained and are not exact private
function names. These observations identify a Windows UI/thread interaction,
not a proven lock-ownership cycle.

`SAL_LOK_OPTIONS=unipoll` alone (`cs23`) is insufficient: all six processes crash
with access violation `0xC0000005` before loading returns. That experiment never
enters the API's `runLoop`. Calling `runLoop` on the initializing thread and
scheduling document work there (`cs24`) permits all three restricted exports.
Word's ordinary control also passes, while ordinary Excel/PowerPoint return empty
references. Inspection then finds lock files left beside the shared authored
fixtures by `cs21` timeouts. Those files are retained as evidence, not deleted.

`cs25` gives every format/control a fresh read-only input copy under its owned
`allowed` directory. All six exports pass independent qpdf/PDFium structure,
expected text, page count/geometry and exact ordinary/restricted rendered pixels.
The initial two-second scheduling delay is experimental, not a readiness protocol.

The final public mode (`cs26`) replaces that delay with a 100-ms timer that waits
for `Application::IsInExecute`, then verifies `Application::IsMainThread` before
loading. These two independently declared Boolean functions are resolved by exact
export name from the pinned Windows runtime. They are VCL C++ exports, outside the
stable LibreOfficeKit C ABI; missing exports fail closed. The pinned
[implementation](https://raw.githubusercontent.com/LibreOffice/core/libreoffice-26.2.6.3/vcl/source/app/svapp.cxx)
checks actual main-loop state and thread identity. Runtime upgrades must review
this dependency. Document load/export/destruction execute on that thread; engine
shutdown runs on a separate owned thread so the main loop can return. The helper
joins shutdown before reporting completion and remains bounded by its parent job.
`-EmbeddedStartup` retains its separate initialization-only behavior.

| Final authored fixture | Ordinary | AppContainer | Independent comparison |
| --- | --- | --- | --- |
| Word DOCX | 7,594 ms | 6,781 ms | Two pages; expected text and exact pixels |
| Excel XLSX | 6,875 ms | 6,828 ms | One page; expected text and exact pixels |
| PowerPoint PPTX | 6,563 ms | 6,437 ms | Two pages; expected text and exact pixels |

All six initialize, observe loop readiness, load, export, destroy and exit zero.
The inspector additionally verifies each fresh input's hash against the authored
original, its recorded modification time, read-only attribute and absence of
extra files in its input directory. Profile settings, original hashes/timestamps
and output preservation also pass. No declaration here establishes macro/link
blocking or broader document fidelity beyond the specific checks performed.

All cases remain under
`.codex-temp/office-isolation/1a880d93be994b9a9a3467bb77aa1ba3`.
Source/build bindings, in case order `cs22` through `cs26`, are:

- `embedded-load-stacks-822dc756bc544b00b04423a19d79eabf/build.json`
- `embedded-unipoll-ba384ccddd8b4b039b652996fd536f93/build.json`
- `embedded-main-loop-8f81663b92e14cbbb825487102811755/build.json`
- `embedded-fresh-inputs-52e3448dcc3f4266be6391b4afef7f2d/build.json`
- `embedded-loop-final-202990d5e1be45edb058fa36f0ed731d/build.json`

The final independent result is
`inspection-00c46289e4114f98bfcf5bc732cacf37/results.json`; the preceding successful
timing experiment is `inspection-aed19dd762e040999eb5348a3ad87958/results.json`.
`.codex-temp/office-embedded-loop-verification.json` reconciles all five builds,
the final public source, exact runtime membership/hashes, six read-only inputs,
profile settings and removal of all owned Windows profiles/processes. No production
payload changes, installed integration or visible acceptance are implied.

Next exercise broader Word/Excel/PowerPoint fidelity, malformed/active-content
refusal, cancellation/crash recovery and larger-resource bounds through this
lifecycle. Resolve calculation/font policies and the separate network-denial gate
before adopting customer Office conversion. Preserve the earlier failed evidence.
