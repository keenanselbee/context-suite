Excel Layout Acceptance
=======================

Status: D07 fidelity remains failed because the current security policy suppresses
charts. The current shared preflight refuses affected XLSX files as an interim
protection. Earlier publication checks and the paired diagnostic remain evidence
of the unresolved renderer defect, not current permission to publish these files.
This is one part of the
[local acceptance contract](local-candidate-acceptance.md), not general Excel
fidelity or human acceptance.


Fixed authored cases
--------------------

Four passive XLSX packages cover Letter portrait and landscape, each using the
saved-values and recalculate choices. They contain a seven-column merged heading,
a merged cell with two explicitly wrapped lines, a three-row value table, a
column chart and a final print-area marker. The chart references the worksheet
values 10, 20 and 30 with matching caches. These cases do not replace the separate
formula/date controls or the existing 48-sheet capacity evidence.

The fixtures are independently authored Open XML, without macros, external
relationships or customer data. The package structure follows Microsoft's
[spreadsheet chart documentation](https://learn.microsoft.com/en-us/office/open-xml/spreadsheet/how-to-insert-a-chart-into-a-spreadsheet).
Expected content and geometry are declared before inspecting renderer output.

Application checks require four PDF copies through direct Convert commands,
the expected calculation choices, no unexpected missing-font review, unchanged
original bytes/timestamps, and complete worker/profile/context/publication cleanup.
Overwrite is enabled deliberately; Office still preserves originals.

Independent inspection regenerates and compares every source ZIP part, checks
PDF structure with qpdf, and reads text, character boxes and rendered pixels with
PDFium. It requires:

- One page with the requested Letter orientation, within 0.1 PDF point.
- Complete expected text, including table/chart labels, values and end marker.
- A complete merged heading, separate aligned wrapped lines and text inside
  the declared page margins.
- Exactly three solid red chart bars in category order, with equal widths and
  a common baseline; heights in the authored 1:2:3 ratio within three raster
  pixels at 96 DPI. Bar interiors must be at least 95 percent filled.
- Each chart category centered beneath its corresponding bar.
- Negative controls rejecting missing/extra/changed text, reordered or missing
  bars, a changed value ratio and an incomplete bar interior.

No tolerance may be enlarged merely to make a failed output pass. Source and
output hashes/timestamps, native reader identities, executable/source identities
and all failed attempts remain in repository-local evidence.


Commands
--------

The execution command creates disposable AppContainer profiles and requires the
owner's existing explicit test authorization. Use a fresh worker with verified
Office, qpdf and PDFium engines, or a retained scratch worker matching the current
build:

```powershell
python -B tools/office-engine/Test-OfficeExecution.py --create-disposable-profiles `
  --excel-layout --retained-worker '<current scratch worker executable>'

python -B tools/office-engine/Inspect-WordFontStyles.py --excel-layout `
  --execution-id '<completed execution GUID>' `
  --pdf-prepared '<verified qpdf preparation directory>' `
  --pdfium-prepared '<verified PDFium preparation directory>'
```

The second command does not create Office profiles or execute Office documents.
It only checks the retained authored packages and published PDFs. Human page
review, the final combined candidate replay and other Office review groups remain
separate acceptance work.


Recorded results: 2026-09-19
----------------------------

Application execution
`.codex-temp/office-execution/0535981a6bfa4ff9a87bedf52b0143f6`
passes fourteen checks. All four PDFs publish as copies, both calculation choices
are retained, no font review appears, originals retain bytes/timestamps, and all
four AppContainer profile folders/mappings and contexts are removed. The wrapper
verifies unchanged sources, binaries and engines. Release builds have zero
warnings/errors. The application harness takes 95.13 seconds for all four cases;
this includes fixture/check overhead and is not the required performance sample.

Independent `layout-inspection-9214da3c96294c75b49161b99f1484cc` **fails all four
outputs**. qpdf structure, page geometry, merged heading and wrapped text pass.
Every chart is absent: zero red bars and no chart categories or axis labels.
The negative controls pass. The failed inspection and its receipt
`layout-inspection-receipt-5900d84724f74d908589f40def1538c0.json` remain intact.
Successful publication therefore does not establish fidelity.

The separate paired diagnostic uses the same verified runtime and newly authored
passive workbook in ordinary bounded jobs, not AppContainers. It changes only
`DisableActiveContent` between two fresh repository-local profiles; macros and
Python remain disabled, and update checks stay off. The disabled case repeats
the missing chart. The enabled control contains all three bars and passes the
unchanged text, position, proportion and label checks. Both process trees exit;
source and runtime hashes are unchanged. The receipt is
`chart-policy-inspection-receipt-528d7c19ff5a4e1f9abe5dfa1fd341aa.json` under the
same execution directory. Its passing diagnostic means the policy difference is
demonstrated, not that product fidelity or isolation is accepted.
An independent read of both profiles before and after export verifies all 32
required setting declarations, including retained macro/Python restrictions.
This verifies declarations, not enforcement. The full foundation suite passes
4,384 contracts; repository checks pass for 188 documentation files. Neither
result clears the failed chart-fidelity matrix.

This agrees with the renderer's [embedded-object factory](https://github.com/LibreOffice/core/blob/master/embeddedobj/source/commonembedding/xfactory.cxx),
which refuses embedded-object creation under that setting, and its
[chart import path](https://github.com/LibreOffice/core/blob/master/oox/source/drawingml/shape.cxx),
which obtains the chart model from an embedded object. The runtime comparison is
the evidence for the pinned binary; upstream source explains the likely mechanism.

Do not simply enable active content in production or remove charts from required
fidelity acceptance. Resolve passive-chart rendering with explicit external-link,
OLE/DDE, macro and process/network/file enforcement evidence, then replay this
matrix against the corrected isolated host. Other Office chart families need
corresponding coverage. The existing profile policy and native host are unchanged.
The unresolved Office network-enforcement gate remains independent and relevant.
The [targeted renderer proposal](office-chart-renderer-repair.md) now contains
a pinned draft patch that keeps the active-content policy enabled and narrowly
permits new internal charts to run. Static checks pass; native compilation,
enforcement and fidelity remain unverified. The production candidate is unchanged.

The current `OfficeSourcePreflight` checks document identity/variant, selected
date/font/slide facts and the interim Excel chart restriction; it explicitly is
not an active-content safety scan. Its
successful result cannot justify enabling embedded content. A repair must retain
or replace the existing enforcement with verified protection, rather than infer
that an ordinary DOCX/XLSX/PPTX suffix makes the whole package passive.

To reproduce only the policy diagnostic, add `--chart-policy-control` to the
inspection command above. It generates its own passive workbook and keeps both
local profiles and outputs as evidence; it accepts no customer document path.


Interim chart refusal: 2026-09-19
--------------------------------

`ExcelChartInspection` uses the existing bounded ZIP reader to inspect content
types and relationship parts. Ordinary DrawingML and newer chartEx content
declarations, transitional/strict chart relationships and chartEx relationships
refuse conversion, including hidden, external or unused declarations. Filenames
alone do not identify charts. The guard does not open external targets or extract
parts. It stops after positive chart evidence; otherwise it must finish inspection
within 128 relationship parts, 256 KiB per part, 1 MiB aggregate expanded XML and
the existing ZIP read/directory limits. Malformed, ambiguous, DTD-bearing,
over-depth or over-budget inspection refuses conversion instead of asserting
that the workbook is chart-free. Cancellation propagates and source leases remain
open and unchanged. This is a scoped corruption guard, not a complete package or
embedded-content audit; other Office chart/embedded-object families still need
separate evidence.

The application, context preparation and private worker all use the shared
preflight. Direct commands explain that charts cannot currently be preserved,
suggest exporting from Excel, and retain Analyze. Refusal precedes calculation
prompts, access admission and publication. Neither calculation mode bypasses it.
Existing chart-free preflight controls retain their earlier behavior.

Run the dedicated real-worker refusal check from the repository:

```powershell
python .\tools\office-engine\Test-OfficeChartRefusal.py
```

It builds fresh scratch managed binaries without a native Office runtime and
submits the four original D07 fixtures in both calculation modes. Evidence
`.codex-temp/office-chart-refusal/6b52265351be48cea739b8bb9f29972c` passes
28 checks: eight worker refusals, unchanged originals/snapshots, no output/native
profile creation, worker reuse and cleanup. A chart-free control passes preflight
and reaches the deliberately missing-runtime failure, distinguishing chart
refusal from an unrelated broken context. Binary hashes remain unchanged.
Both Release builds have zero warnings/errors. Foundation evidence
`.codex-temp/office-chart-foundation-bde7e0e2568d49e5865422ec3f59b48c` passes
4,439 contracts, including direct application refusal, independent preparation
refusal, declaration/relationship variants and malformed/limit controls.

The original positive `--office-excel-layout` suite and independent chart oracle
are unchanged. They cannot pass under this interim restriction. D07 remains
required: restore passive-chart rendering with verified enforcement, then remove
or narrow this guard based on new fidelity evidence and replay the original
acceptance. No reserved candidate, installation or native security policy was
changed by this checkpoint. Final combined-candidate replay remains outstanding.


Isolated renderer repair evidence (2026-09-22)
---------------------------------------------

The [matching rebuilt renderer](office-chart-renderer-repair.md#matching-runtime-and-passive-pdf-exports-2026-09-22)
now passes all four authored PDF comparisons with `DisableActiveContent=true`.
Portrait/landscape and saved-values/recalculated exports retain the expected
text, page geometry, merged/wrapped cells, three bars and category associations
under the unchanged independent oracle. Originals and inventoried inputs remain
unchanged, and all conversion jobs end with zero active processes.

Evidence is `G:\ContextSuite-OfficeBuild\evidence\chart-pdf-export-03` with bound
preparation/launch receipts and retained earlier failures. This uses ordinary
bounded jobs and fresh profiles on passive generated workbooks. It is not the
actual application publication path or restricted-host acceptance; D07 and the
shipping chart guard remain open until those required checks pass. The renderer
record also identifies the updater-free evaluation configuration and its separate
profile verification, which does not alter shipping controls.

The subsequent [restricted-host replay](office-chart-renderer-repair.md#restricted-host-chart-exports-2026-09-22)
passes the same four comparisons through the unchanged actual AppContainer
launcher and profile owner. Evidence is
`G:\ContextSuite-OfficeBuild\evidence\chart-host-export-01`, with matching
preparation and postverification receipts. Every original and inventoried input
is unchanged; all four disposable profiles and registry mappings are removed.
Only the scratch native host's library mapping changes to match the unmerged
runtime. This advances restricted-host passive fidelity, but the shipping chart
guard and D07 application-publication gate remain. Whole-document security,
network enforcement, adoption and final-candidate replay are separate requirements.
