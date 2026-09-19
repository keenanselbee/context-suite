Excel Layout Acceptance
=======================

Status: application checks pass; D07 fidelity fails because the current security
policy suppresses charts. A paired diagnostic isolates the policy conflict.
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

The current `OfficeSourcePreflight` checks document identity/variant and selected
date/font/slide facts; it explicitly is not an active-content safety scan. Its
successful result cannot justify enabling embedded content. A repair must retain
or replace the existing enforcement with verified protection, rather than infer
that an ordinary DOCX/XLSX/PPTX suffix makes the whole package passive.

To reproduce only the policy diagnostic, add `--chart-policy-control` to the
inspection command above. It generates its own passive workbook and keeps both
local profiles and outputs as evidence; it accepts no customer document path.
