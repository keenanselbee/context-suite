Office AppContainer Startup Evaluation
=====================================

Status: pinned version reporting passes inside an actual AppContainer, but full
initialization fails. Ordinary authored exports pass independent PDF checks.
Network denial and required customer conversion remain unresolved.

Purpose and boundary
--------------------

The required Office converter needs both an enforced process boundary and an
engine that works inside it. The [native isolation experiment](office-isolation-evaluation.md)
tests file/token/environment behavior with an authored helper. This follow-up
tries only the pinned Office engine's `--version` command in the same boundary.
It opens no document and does not establish font discovery, import, export,
hostile-content handling or customer conversion.

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
runtime inventory, then copies only its members into a fresh case's `office`
directory. It verifies source and copied bytes/hashes, rejects reparse paths and
noncanonical/ambiguous inventory names, and rejects existing or out-of-scope
destinations. Earlier generated GUID-named crash dumps beside `soffice` are
recorded and excluded from the copy; they remain untouched in the source.
Missing members and other unlisted files are refused before engine launch.
The copy receipt retains the archive/inventory identity and every expected file.

Only the copied runtime receives read/execute access for the disposable profile.
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
