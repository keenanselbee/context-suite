# Office engine evaluation

These tools evaluate the independently pinned LibreOffice Windows x64 package
using three small, passive fixtures authored in this repository. They do not
enable an Office command or add a production dependency. See the
[evidence and remaining gates](../../docs/office-engine-evaluation.md).

`Test-OfficeEvaluation.ps1 -EmbeddedImages` adds a generated high-resolution
RGBA PNG to Word/Excel/PowerPoint and exports paired no-downsampling/150-DPI
controls. Run `Inspect-OfficeImages.py '<printed evaluation directory>'` to
reconcile package/PDF hashes, full visible image samples and alpha, rendered
patches and the effective resolution settings. See the
[image-preservation evidence and limits](../../docs/office-image-evaluation.md).

`Test-OfficeEvaluation.ps1 -PowerPointSlides` compares saved slide order, hidden
first/last slides and speaker-note exclusion. An identical-input notes-page export
provides a positive control. `Inspect-PowerPointSlides.py '<printed evaluation
directory>'` cross-checks retained declarations, hashes and observations. See
[slide/notes evidence](../../docs/powerpoint-slide-evaluation.md).

`Test-OfficeEvaluation.ps1 -WordRevisions` adds four passive DOCX cases for tracked
insertions/deletions with shown, hidden or omitted revision-display settings and
a clean control. `Inspect-WordRevisions.py '<printed evaluation directory>'`
cross-checks the retained source declarations, hashes and PDF text observations.
See [results and export-policy implications](../../docs/word-revision-evaluation.md).

`Test-OfficeEvaluation.ps1 -WordFinalText` verifies the owner's final-text default
using the same four fixtures with explicit false/true `ExportTrackedChanges`
options. The true variants are positive controls. The same inspector verifies
all eight exports, source hashes/write times, recorded job cleanup and exact
clean-control pixels. Ordinary Writer evaluation exports now explicitly hide
tracked-change markup; the original `-WordRevisions` observation mode stays
available. These remain passive fixtures, not arbitrary-document admission.

`Test-OfficeEvaluation.ps1 -WordRevisionStructures` adds twelve exports across
formatting, table-row and moved-text controls. Run
`Inspect-WordRevisionStructures.py '<printed evaluation directory>'` for independent
declaration/text/pixel reconciliation. The current move case fails its exact-pixel
comparison; see [structural revision evidence](../../docs/word-structural-revisions.md).
Keep that failure visible rather than treating successful export as acceptance.

The separate native isolation preflight now checks reachable IPv4 and IPv6-only
loopback listeners. Its default uses no AppContainer profile and makes no denial
claim. The separately authorized disposable-profile mode requires actual denial
on both endpoints; see [isolation scope and evidence](../../docs/office-isolation-evaluation.md).
The current preflight compares actual fixture reads and distinct output bytes,
preserves inputs during write-access checks, and records token capability counts.
The owner authorized the disposable profile test on 2026-09-14. File/token/content
checks and profile cleanup pass; loopback attempts hit observation deadlines, so
the combined matrix remains failed. Capability diagnostics and before/after
listener controls are recorded without treating timeouts as access denial.

Run from the Context Suite repository with the .NET 10 SDK:

```powershell
.\tools\office-engine\Prepare-OfficeEvaluation.ps1
.\tools\office-engine\Test-OfficeEvaluation.ps1 `
  -PreparedDirectory '<printed office-engine directory>' `
  -PdfPreparedDirectory '<existing verified qpdf directory>' `
  -PdfiumPreparedDirectory '<existing verified PDFium directory>'
```

Use the existing [PDF evaluation preparation](../pdf-engine/README.md) for the
independent validators. Preparation downloads the pinned MSI into a fresh
`.codex-temp/office-engine/<guid>` directory, verifies its SHA256, opens it as a
read-only MSI database, extracts its embedded cabinet and maps INSTALLLOCATION
files. It never executes MSI installation sequences or custom actions. The
optional `-PreparedDirectory` accepts a scratch directory already containing the
pinned `upstream.msi`; unpacking refuses existing `unpacked` or `cabinets` folders.

The runner checks the Office and qpdf inventories and the PDFium bridge/library
identities, creates DOCX/XLSX/PPTX fixtures, and converts each with fixed PDF export
options and a fresh profile. It checks PDF existence, size, qpdf parsing, PDFium
page count/rendering/text, authored page geometry, hidden-content exclusion and
source hashes. An exit code of zero alone is insufficient. A fourth positional
argument to the underlying managed probe selects `Word`, `Excel` or `PowerPoint`
for targeted fixture diagnosis; it does not accept arbitrary input documents.

The Word fixture now includes separate first-page/default headers and a shared
footer with PAGE/NUMPAGES fields deliberately cached as `99`. Its PDF checks
require the correct header on each page, `Page 1 of 2` / `Page 2 of 2`, no stale
`99`, and the existing two-page text/table content. This tests only local pagination
fields; it does not authorize external field updates or establish all field behavior.

Retained evidence includes the MSI, extracted cabinet and raw members, mapped
inventory, generated originals/PDFs, BGRA renders, text and execution JSON. Each
evaluation's `profiles.txt` lists its disposable profiles under
`.codex-temp/office-profile-<guid>`. These shorter sibling paths avoid a failure
observed with deeper profiles; controlled tests below isolate a length dependency. No
automatic evidence cleanup runs. Allow several GB of scratch space.

Add `-ProfileMatrix` to the test wrapper to repeat the PowerPoint fixture across
short/long and ASCII/Unicode profile paths while holding source/output paths and
export options fixed. This mode records missing-PDF observations and finishes the
matrix; completing the matrix does not mean every conversion passed. On this
machine both 90-character profiles passed, including Unicode, and both
154-character profiles returned exit zero without a PDF. `-ProfileLengths` instead
uses a fixed ASCII parent/depth and profile lengths of 90/110/130/150/170 characters.
The first three passed, while the last two returned zero without a PDF. The
profile also determines temporary/data directories, so the responsible internal
path and exact cutoff are still unknown. Select only one experiment switch at a
time; see the evidence record.

`-EnvironmentPaths` separates the profile from the four environment variables.
It runs the same generated PowerPoint input with a short control, all paths long,
then only UserInstallation, TEMP, TMP, APPDATA or LOCALAPPDATA long. The profile
roots are 90 or 170 characters; environment directories append their variable
name. Source/output paths and PDF options stay fixed, and each completed PDF
passes the same independent checks. `conversion.json` records the exact profile
and environment paths. A missing PDF is recorded as a failed conversion, even
when the process exits zero. This diagnostic mode does not establish a customer
path policy or arbitrary-document isolation.

Add `-LegacyAnalysis` to generate disposable DOC/XLS/PPT copies with the fixed
Word 97, Excel 97 and PowerPoint 97 export filters, then inspect their headers
using the production Core legacy analyzer. It requires likely content identities,
unchanged source/copy hashes and bounded copies, retaining conversion/analysis
JSON. This tests parser interoperability with LibreOffice-produced files, not
Microsoft Office fidelity or a customer legacy conversion command. Only one
experiment switch may be selected. See [analysis scope](../../docs/legacy-document-analysis.md).

`-LegacyPdf` also generates those legacy copies, then converts both modern and
legacy versions to PDF with the same fixed export options and fresh profiles.
Every PDF must pass the existing qpdf/PDFium page, authored text, geometry and
original-preservation checks. The runner compares corresponding 96-DPI BGRA
renders and normalized extracted text, retaining `legacy-pdf-comparison.json`.
Pixel equality is exact, includes alpha and excludes stride padding. Different
pixel dimensions are recorded as incomparable, never rescaled into equality.
Differences are observations requiring review; no visual-loss tolerance is
silently accepted. These same-engine roundtrips are not Microsoft Office reference
baselines, customer legacy-file coverage or general fidelity acceptance.

After a retained `-LegacyPdf` run, use:

```powershell
python -B tools/office-engine/Inspect-OfficePdfComparison.py `
  '<completed authored legacy evaluation directory>' '<retained qpdf directory>'
```

This reads the four generated Excel/PowerPoint PDFs through the pinned qpdf
runtime, retaining decoded page-stream diffs, page boxes, font dictionaries,
embedded TrueType hashes and structure-role counts in fresh scratch. It verifies
the complete qpdf archive/payload membership and source PDF hashes, and refuses
incomplete or mismatched comparison records. It does not run Office or alter PDFs.
It is a diagnostic for these authored fixtures: its subprocess has a 20-second
deadline and post-run output-size checks, not a production hostile-PDF sandbox.
Neither matching fonts nor matching text proves visual or accessibility fidelity.

The [owned-job launcher](../../docs/office-process-lifetime.md) now assigns each
child to a bounded Windows job during process creation. It enforces a 60-second
deadline and 64 KiB per diagnostic stream, terminates descendants on root exit
or cancellation, and verifies zero active job members. Run
`Test-OfficeProcesses.ps1` for sixteen helper-only lifetime/diagnostic contracts.
`Test-OfficeEngineLifetime.ps1 -PreparedDirectory '<retained Office directory>'`
additionally verifies real engine startup cancellation, owner-crash cleanup and
reuse of the same interrupted profiles. It opens no documents and verifies exact
job membership before treating a process as the owned engine. Full evidence and
remaining active-document/isolation gates are in the linked lifetime record.
Add `-DuringExport -PdfPreparedDirectory '<retained qpdf directory>'` to test
the generated 96-page Word export control, cancellation and owner failure after
observed temporary PDF growth, and same-profile two-page recovery. Select
`-ExportFamily Excel,PowerPoint` for the equivalent 96-sheet/slide controls and
same-family recovery (one Excel page or two PowerPoint pages). Several families
run sequentially after one complete payload verification, each with fresh evidence
and profiles. Word remains the default. See the
[export-interruption evidence and limits](../../docs/office-export-interruption.md).
The probe's `--export-fixtures <fresh directory> Excel` (or `PowerPoint`) mode
only generates the large passive package. Its
`--export-file-release <fresh directory>` mode checks delayed file release,
a persistent lock and a missing file without starting Office. Use repository
`.codex-temp` directories for both. Recovery waits at most five seconds for
sharing violations to clear and records the wait; other file errors fail directly.
Its profile disables macros, active content, Python
runtime and automatic update checks. **It is not a filesystem/network sandbox**,
and neither those settings nor hostile-content isolation have been accepted by
testing. Use only these authored passive fixtures. Extracted fonts and VC runtime
files outside INSTALLLOCATION are excluded; the current machine's existing fonts
and runtime can affect results. Package completeness and redistribution remain
unresolved.

`Test-OfficeEvaluation.ps1 -FontSubstitution` uses the same three prepared-directory
arguments to compare authored Word/Excel/PowerPoint pairs requesting Arial or
`ContextSuiteAbsentFont9361`. It retains PDF font-name declarations, text and
96 DPI renders. Run `python -B tools/office-engine/Inspect-OfficeFontComparison.py
'<printed evaluation directory>'` to verify retained source/PDF hashes, check that
the paired document parts differ only by requested font name, and report exact
pixel differences. These are substitution observations, not font/layout acceptance
or a production missing-font detector.

`Test-OfficeEvaluation.ps1 -ExcelDates` exports three authored XLSX fixtures:
omitted/explicit 1900 date systems and an explicit 1904 date system. Numeric cells
cover early serials, the 1900 leap-day compatibility anomaly, a modern date,
fractional days and elapsed hours. ISO-style formats avoid relying on the current
regional short-date display. No formulas are present in the printed worksheet.
The report records each expected Excel display and actual extracted PDF value;
completing the exports does not accept differences as fidelity passes.
Use `python -B tools/office-engine/Inspect-ExcelDates.py '<evaluation directory>'`
to independently check retained package values/styles/date-system declarations,
source/PDF hashes and per-cell text observations. This is a passive fixture
experiment, not customer conversion or general date/locale compatibility.

`Test-OfficeEvaluation.ps1 -ExcelPrint` exports five passive XLSX fixtures covering
manual page breaks, repeated title rows, fit-to-one-page, disjoint print areas and
hidden rows/columns. It records exact normalized text per page as a fidelity
observation, independently checks PDF structure/page geometry, and retains 96 DPI
renders. Use `python -B tools/office-engine/Inspect-ExcelPrint.py '<evaluation directory>'`
to verify original print declarations, source/PDF hashes and recorded observations.
See [print layout scope and results](../../docs/excel-print-layout-evaluation.md).
Completion alone does not imply all five print policies matched.

Retained `FontSubstitution` and `LegacyPdf` exports can be inspected without
running Office using
`python -B tools/office-engine/Inspect-OfficePdfComparison.py '<evaluation directory>' '<prepared qpdf directory>'`.
The script verifies the pinned runtime and export hashes, then records embedded
TrueType program/table hashes and selected internal names in fresh scratch.
`python -B tools/office-engine/Test-OfficeFontInspection.py` exercises bounded
in-memory name/directory cases without loading or installing fonts. Read the
[font-program evidence and limits](../../docs/office-font-program-inspection.md).

The separate native isolation experiment is:

```powershell
.\tools\office-engine\Test-OfficeIsolation.ps1
```

It builds an independently authored Windows x64 probe, generates scratch file and
loopback-listener controls, and tests job ownership, timeout and diagnostic limits.
It also tests per-process commit, aggregate commit and process-creation limits
against matching positive controls, recording actual private-commit deltas, helper
handles and full job accounting. Every job must reach zero active processes after
cleanup. Test memory budgets are 32-256 MiB; no machine-wide exhaustion is needed.
An additional crash case verifies the unique executable path and creation time
of a live child/grandchild, forcibly terminates only their job owner, and requires
both descendants to exit without graceful launcher cleanup. Process handles stay
open for observation; no observer job handle keeps the crashed owner's job alive.
The default runs only preflight. It does not create an AppContainer profile or
claim that access restrictions passed. No Office engine or customer document runs.

`-CreateDisposableProfile` is a separate explicit opt-in for the prepared
AppContainer file/network matrix. **Obtain owner authorization before using it:**
Windows creates per-user profile files beneath `%LOCALAPPDATA%\Packages` and
profile registry metadata outside the repository. The moniker is
`ContextSuite.Office.Evaluation.<scratch-guid>`, recorded in `case/profile-name.txt`.
The probe refuses a collision, requests no capabilities, grants read/execute only
to its owned fixture/executable directory and modification only to its owned
output directory, and removes only the profile it successfully created. Failed
cleanup prints a recovery warning; preserve the recorded profile name. It never
installs/registers a Context Suite package or changes Explorer registration.

The opt-in path has been executed after authorization; the file checks pass but
network denial remains unverified. The earlier unregistered
AppContainer attempt failed at process creation with Windows error 2. The probe
therefore does not fall back to unrestricted execution for an isolated case.
Read the [isolation evidence](../../docs/office-isolation-evaluation.md) for exact
scope, remaining tests and authorization requirements.

Access-test children now receive an explicit eight-variable Unicode environment.
Known Windows AppContainer redirection of local/temp paths is checked against
exact owned scratch locations. Actual file writes/readback through all five
storage variables pass, with three changed-environment controls. Unknown variables
are counted but their values are never logged. The build receipt includes
`Environment.h`; environment checks do not imply renderer compatibility or resolve
the failed network-denial matrix. See the linked isolation record for retained
observations and profile cleanup.

Add `-PreparedOfficeDirectory '<verified Office evaluation directory>'` together
with `-CreateDisposableProfile` for the fixed `--version` viability experiment.
It creates and verifies a separate runtime copy, grants read/execute only on that
copy, and compares ordinary/AppContainer startup under owned jobs and explicit
environment paths. It opens no document. Read the
[startup scope and results](../../docs/office-isolated-startup.md); neither
version reporting nor successful cleanup resolves network enforcement.

Add `-PassiveExports` to that command to initialize six fresh profiles and export
the existing authored Word/Excel/PowerPoint fixtures, once normally and once in
the AppContainer. Macros/active content remain disabled; no arbitrary document is
accepted. Initialization and export each have a 60-second bound. File creation
alone is not a fidelity pass. After the run removes its disposable profile, use
`Inspect-OfficeIsolationExports.ps1 -StagingId <case-guid>` with
`-PdfPreparedDirectory` and `-PdfiumPreparedDirectory` to check retained PDFs with
the pinned independent engines and compare normal/isolated text and pixels.
Missing exports and mismatches remain failures in the retained result.

Use `-StartupDiagnostics` instead of `-PassiveExports` for a focused pair of
empty-profile initialization attempts. This requests compiled engine logging,
disables OpenCL and records observed owned-child exits and window captions under
the same job bounds. It opens no document and skips the separate access/network
matrix. The current ordinary control passes while restricted initialization
fails; see the [diagnostic results and limits](../../docs/office-isolated-startup.md#owned-startup-diagnostics-2026-09-14).

`Read-OfficeNetworkEvents.ps1 -StagingId <scratch-guid> -PlanOnly` prepares four
read-only WFP queries for the retained probe's IPv4/IPv6 loopback events and
filters. It verifies the probe hash and profile name and rejects reparse paths.
The dry run writes nothing. Actual queries require a separately authorized
administrator window; run them promptly after a fresh non-elevated isolation
test because the event window is ten minutes. Evidence stays in that case's
new diagnostic directory. The reader neither enables tracing nor changes firewall
rules or loopback exemptions. Empty events, matching filters alone and successful
queries do not prove network denial.
