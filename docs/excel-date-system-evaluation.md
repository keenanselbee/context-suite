Excel Date-System Evaluation
===========================

The 2026-09-11 passive XLSX-to-PDF experiment identifies an early-1900 date
compatibility gap in the pinned LibreOffice 26.2.6.3 renderer. Three exports
complete with unchanged originals, but only 15 of 21 displayed values match the
independently authored Excel expectations. This is a fidelity finding, not an
accepted customer conversion matrix.


Fixture and independent expectations
------------------------------------

`Test-OfficeEvaluation.ps1 -ExcelDates` creates three copies of the existing
passive workbook: omitted `date1904`, explicit false, and explicit true. The
printed sheet contains only numeric date/time values and row labels, with no
formulas. Fixed `yyyy-mm-dd`, `yyyy-mm-dd hh:mm:ss` and `[h]:mm:ss` formats
separate date-system behavior from localized short-date formatting. Hidden-sheet,
print-area and one-page geometry checks remain active.

Microsoft documents the two [Excel date systems](https://support.microsoft.com/en-au/excel/date-systems-in-excel),
including the July 5, 2011 serials 40729 and 39267, and the
[1900 leap-year compatibility behavior](https://learn.microsoft.com/en-us/troubleshoot/microsoft-365-apps/excel/wrongly-assumes-1900-is-leap-year).
The authored workbook uses the [date1904 declaration](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.workbookproperties.date1904?view=openxml-3.0.1)
and [custom number formats](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.numberingformat?view=openxml-3.0.1).
No Office SDK, reference implementation or source document is imported.

The independent Python inspector reads retained source XML, checks exact numeric
values, row labels, number formats/style mappings and date-system declarations,
and derives expected dates using Gregorian arithmetic with Excel's fictitious
1900 leap day handled separately. It verifies source/PDF hashes, extracted-text
records and the C# probe's per-cell observations. It does not launch an engine.
These are documented-format expectations, not Microsoft Excel-produced PDFs or
an independently rendered Microsoft Office baseline.


Observed values
---------------

Omitted and explicit-false 1900 declarations produce identical displayed values:

| Numeric cell | Expected Excel display | Candidate PDF display |
| --- | --- | --- |
| 1 | 1900-01-01 | **1899-12-31** |
| 59 | 1900-02-28 | **1900-02-27** |
| 60 | 1900-02-29 | **1900-02-28** |
| 61 | 1900-03-01 | 1900-03-01 |
| 40729 | 2011-07-05 | 2011-07-05 |
| 40729.5 | 2011-07-05 12:00:00 | 2011-07-05 12:00:00 |
| 1.5, elapsed hours | 36:00:00 | 36:00:00 |

The 1904 case matches all seven expectations: 1904-01-02, 1904-02-29,
1904-03-01, 1904-03-02, 2015-07-06, 2015-07-06 12:00:00 and 36:00:00.
Every export returns zero with empty stderr. Structural qpdf validation,
independent PDFium text extraction/rendering, expected page geometry and original
hash checks pass. Those checks do not make the six differing values acceptable.

The eventual converter needs date-aware compatibility handling or a necessary
user decision for affected values before claiming fidelity. Never shift all
numeric cells or all dates: modern dates, 1904 dates and elapsed durations have
passing controls here. Formats, formulas and cell meaning must be distinguished;
the current observation code is not a production detector or repair. Calculation
policy, arbitrary-document isolation and customer integration remain separate
required work. No launch requirement is removed by this finding.

The later [workbook settings analyzer](workbook-settings-analysis.md) reports
saved date1904 and dateCompatibility flags, keeping omissions distinct from false.
It does not yet identify affected cells, derive their effective date system or
repair the rendering mismatch.


Stored-value inspection (2026-09-16)
-----------------------------------

The [conversion preflight](../src/ContextSuite.Core/Analysis/OfficeSourcePreflight.cs)
now returns separate [stored-date evidence](../src/ContextSuite.Core/Analysis/ExcelStoredDateInspection.cs)
for identified ordinary XLSX packages. It does not change admission or accept
the renderer's incorrect dates. The owner has been asked whether launch may
decline affected workbooks or must convert those dates exactly; that decision
is still pending. Exact date fidelity remains required until scope is resolved.

LibreOffice's [calculation documentation](https://help.libreoffice.org/latest/en-US/text/shared/optionen/01060500.html)
explicitly distinguishes its pre-March-1900 calendar from Excel's. The current
upstream [workbook importer](https://raw.githubusercontent.com/LibreOffice/core/master/sc/source/filter/oox/workbooksettings.cxx)
selects only the 1899-12-30 or 1904-01-01 base and describes the remaining early-date
difference. This is current upstream research, not a claim that its source bytes
match the pinned binary. The exact version-tag URL could not be retrieved.
Changing every cell or the workbook's date base would also move the already
correct modern dates and durations; no such change is made.

The new reader counts numeric cells with stored values from zero inclusive to
61 exclusive and a supported direct calendar number format in the Transitional
1900 system. It separately counts date-formatted formula cells; it never evaluates
formulas or treats a cached result as the result of future recalculation. The
1904 system retains zero for this particular risk. Literal numbers, ordinary time
formats and elapsed durations are distinguished from calendar dates using
Microsoft's [number-format definitions](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.numberingformat?view=openxml-3.0.1).
No cell values, formulas, sheet names or cell addresses are returned in the result.

The scope is the worksheet parts explicitly declared in content-type overrides,
including hidden or potentially unused parts. It is not an effective print-area
or whole-workbook semantic check. ISO date-string cells, chart labels and formulas
which acquire a date only after evaluation are outside the numeric-value count.
Zero is not a certificate of correct rendering. `Complete` means this stored-value
scan completed within its supported declaration scope, not that Excel fidelity
is complete.

Locale-dependent built-ins, multi-section/conditional/localized custom formats,
row/column format inheritance, conditional formatting, ambiguous declarations,
Strict/compatibility date semantics and budget failures return unavailable counts.
Incomplete inspection never becomes zero. XML DTDs/resolvers are disabled, depth
is limited to 32, and the existing ZIP reader bounds entries to 4,096, each part
to 256 KiB, total expanded parts to 1 MiB and reads to 4 MiB. The date scan also
limits worksheet parts to 64, cell formats to 4,096 and cells to 65,536. It uses
the caller's cancellation token and restores the source position. General Analyze
does not start this additional cell scan.

All **3,643 foundation contracts** pass, including the new stored-date cases,
with unchanged recorded source inputs. Evidence:
`.codex-temp/excel-stored-dates-35c2dd0b5d15481d85db0531593bbad4`.
The three retained failing/control workbooks independently produce expected
counts **3, 3, 0** with no formula cells and unchanged hashes/write times, in
`stored-date-inspection-545260301d8f4f108d929f20873cbb2c.json` under the original
evaluation directory. This read launches no Office engine and creates no native
profile. Contract and inspection builds complete without warnings/errors.

Reproduce the retained-fixture read with:

```powershell
dotnet run --project tools/office-engine/Probe/Office.Evaluation.csproj -c Release -- `
  --inspect-excel-stored-dates '<original Excel date evaluation directory>'
```

The six PDF date mismatches remain unresolved. Next work must connect a complete
date policy to execution/publication, including formula and unsupported-format
cases; this inspection component alone does not satisfy that requirement.


Evidence and limits
-------------------

Retained evidence:
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-cd2fc101d2d64109b7f290837fbf48cf`.
It contains generated workbooks/PDFs, conversion/profile records, extracted text,
96-DPI renders, `office-evaluation.json` and `independent-dates.json`.
The full log is `.codex-temp/office-excel-dates.log`.

Recheck a completed run with
`python -B tools/office-engine/Inspect-ExcelDates.py '<evaluation directory>'`.
It refuses to overwrite an existing independent report. Four negative controls
reject the wrong evaluation mode, a changed PDF, changed observation and a path
outside Office scratch before writing a report. Evidence:
`.codex-temp/office-engine/date-guards-d4b56ff07c5b431aba2eee15c3246651/results.json`.

The probe builds with zero warnings/errors and all 17 profile declaration
contracts pass. The existing wrapper verifies the retained Office/qpdf/PDFium
payload identities before launch and uses fresh short profiles. The retained log
predates a console-only clarification that labels date observations as not a
fidelity pass; the structured observations already distinguish every mismatch.

This matrix does not establish localized formats, formula calculation, time zones,
negative dates, legacy XLS, arbitrary-document isolation, visual acceptance or
screen-reader delivery. No production runtime, installed state, Explorer
registration, AppContainer profile or live licensing changes are involved.
