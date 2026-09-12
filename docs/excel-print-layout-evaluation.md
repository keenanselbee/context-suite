Excel Print Layout Evaluation
============================

This experiment evaluates saved workbook print settings for required Excel-to-PDF
conversion. It uses five independently authored passive XLSX fixtures and the
pinned, uninstalled LibreOffice candidate. It does not enable customer conversion
or change the outstanding renderer isolation requirements.


Fixture policy
--------------

Each workbook has a visible worksheet with short literal cell markers and a
hidden worksheet. The visible sheet uses US Letter portrait pages, explicit
margins and column widths, and short rows that fit without an automatic page
break. A marker at row 20 is outside every declared print area. There are no
formulas, external links, macros, date cells or customer documents in these cases.

| Case | Saved settings | Expected PDF page text |
| --- | --- | --- |
| manual-break | 100% scale; manual break before row 5 | `TITLE R02 R03`, then `R05 R06` |
| repeat-title | Same break; row 1 repeats as a print title | `TITLE R02 R03`, then `TITLE R05 R06` |
| fit-one-page | Same break; fit to one page wide and tall | `TITLE R02 R03 R05 R06` on one page |
| disjoint-areas | Separate ranges A1:B2 and A5:B6 | `TITLE R02`, then `R05 R06` |
| hidden-cells | One print area; hidden row 3 and column C | `TITLE R02 R05 R06` on one page |

These expectations derive from authored cell content and documented print
semantics. Microsoft describes [manual breaks and scaling](https://support.microsoft.com/en-us/excel/scale-a-worksheet),
[separate print areas](https://support.microsoft.com/en-us/excel/set-or-clear-a-print-area-on-a-worksheet)
and [repeating title rows](https://support.microsoft.com/en-us/excel/page-setup).
The [OOXML row-break definition](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.rowbreaks?view=openxml-3.0.1)
provides the saved break structure. Primary sources were consulted on 2026-09-11.
No SDK, schema dataset or reference source was imported. These are not captured
Microsoft Excel baseline renders, and exact font/pixel equivalence is not claimed.


Reproduction and evidence checks
--------------------------------

```powershell
.\tools\office-engine\Test-OfficeEvaluation.ps1 `
  -PreparedDirectory '<prepared Office scratch>' `
  -PdfPreparedDirectory '<prepared qpdf scratch>' `
  -PdfiumPreparedDirectory '<prepared PDFium scratch>' `
  -ExcelPrint

python -B tools/office-engine/Inspect-ExcelPrint.py '<printed evaluation directory>'
```

The wrapper verifies pinned Office/qpdf/PDFium payload identities before running.
Each export uses a new repository-local initialized profile and the existing
fixed export settings, including `SinglePageSheets=false`. The child retains
bounded diagnostics and a 60-second deadline. Each PDF must be nonempty and at
most 16 MiB; page count is bounded to 16 before rendering. qpdf checks structure,
and PDFium independently reads text and renders each page at 96 DPI. Page geometry
must remain 612 by 792 points. Source hashes must remain unchanged.

Print mismatches remain observations in `PrintObservation`; a successful export
does not turn them into fidelity passes. Normalized complete page text is checked,
so extra hidden/out-of-area text, missing/repeated markers and wrong page placement
all affect the result. The report retains expected and observed pages separately.

The Python inspector verifies all five cases, source/PDF hashes, original workbook
print areas/title scopes, cell markers, hidden rows/columns, fit settings and manual
breaks. It independently compares retained page text with the documented expected
sequences and writes `independent-print.json` without rerunning Office. It limits
evidence reads and package expansion and rejects linked/outside-scratch paths.


Actual results (2026-09-11)
--------------------------

All **five cases match**, producing eight independently parsed/rendered PDF pages.
The hidden worksheet and out-of-area marker never appear. Repeated titles occur
on both pages, separated print areas occupy their own pages, and fit-to-one-page
overrides the saved manual break for this fixture. All source hashes are unchanged.
qpdf structure checks, page geometry and the 17 profile declaration contracts pass.
The independent inspector confirms all five results and rejects two altered
evidence copies: a forged expected page sequence and a changed workbook hash.

The evaluated engine is LibreOffice 26.2.6.3,
`8221e31b3ac356a1623c672912a3d2b492f7e3d1`. Evidence is retained under
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-1cd01adc101a487e99a08607f40209f7`.
Its `office-evaluation.json`, `independent-print.json`, per-case text, PDFs and
raw 96 DPI renders preserve the observations. The run log is
`.codex-temp/excel-print-evaluation.log`. Negative inspector results are in the
sibling `print-inspector-negative-876720d2342c4876bf5909e2b15c983e` directory.

The Release evaluation host builds with zero warnings/errors. Public-source and
theme-policy checks and 93 documentation files pass. The general foundation,
private-worker and image/audio/PDF regression suites were not rerun for this
evaluation-only change. No fresh production build or engine adoption is claimed.


Remaining acceptance
--------------------

Required next work includes visible multi-sheet order, horizontal pagination and
repeated columns, landscape/paper-size combinations, automatic page breaks,
headers/footers, charts, font-dependent layout, legacy XLS, real Excel baselines
and customer review. Print declarations cannot establish safe rendering or
general document fidelity. Calculation/date/font findings and renderer isolation
remain separate prerequisites for the actual Office converter.

This test-only change does not alter the production payload or claim new shipping
support. No installation, Explorer change, AppContainer profile, live commerce,
native recycling or arbitrary document execution is part of this experiment.
