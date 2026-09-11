# Office engine evaluation

These tools evaluate the independently pinned LibreOffice Windows x64 package
using three small, passive fixtures authored in this repository. They do not
enable an Office command or add a production dependency. See the
[evidence and remaining gates](../../docs/office-engine-evaluation.md).

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

The child wrapper provides a 60-second deadline, bounded diagnostics and an owned
process-tree kill attempt. Its profile disables macros, active content, Python
runtime and automatic update checks. **It is not a filesystem/network sandbox**,
and neither those settings nor hostile-content isolation have been accepted by
testing. Use only these authored passive fixtures. Extracted fonts and VC runtime
files outside INSTALLLOCATION are excluded; the current machine's existing fonts
and runtime can affect results. Package completeness and redistribution remain
unresolved.

The separate native isolation experiment is:

```powershell
.\tools\office-engine\Test-OfficeIsolation.ps1
```

It builds an independently authored Windows x64 probe, generates scratch file and
loopback-listener controls, and tests job ownership, timeout and diagnostic limits.
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

The opt-in path is prepared but has not been executed. The earlier unregistered
AppContainer attempt failed at process creation with Windows error 2. The probe
therefore does not fall back to unrestricted execution for an isolated case.
Read the [isolation evidence](../../docs/office-isolation-evaluation.md) for exact
scope, remaining tests and authorization requirements.
