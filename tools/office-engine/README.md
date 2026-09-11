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

Retained evidence includes the MSI, extracted cabinet and raw members, mapped
inventory, generated originals/PDFs, BGRA renders, text and execution JSON. Each
evaluation's `profiles.txt` lists its disposable profiles under
`.codex-temp/office-profile-<guid>`. These shorter sibling paths avoid a failure
observed with deeper Unicode profiles; the exact cause remains unresolved. No
automatic evidence cleanup runs. Allow several GB of scratch space.

The child wrapper provides a 60-second deadline, bounded diagnostics and an owned
process-tree kill attempt. Its profile disables macros, active content, Python
runtime and automatic update checks. **It is not a filesystem/network sandbox**,
and neither those settings nor hostile-content isolation have been accepted by
testing. Use only these authored passive fixtures. Extracted fonts and VC runtime
files outside INSTALLLOCATION are excluded; the current machine's existing fonts
and runtime can affect results. Package completeness and redistribution remain
unresolved.
