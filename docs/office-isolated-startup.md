Office AppContainer Startup Evaluation
=====================================

Status: pinned version reporting passes inside an actual AppContainer; network
denial, document initialization and required customer conversion remain unverified.

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
