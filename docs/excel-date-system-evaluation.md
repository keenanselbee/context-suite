Excel Date-System Evaluation
===========================

The 2026-09-11 passive XLSX-to-PDF experiment identifies an early-1900 date
compatibility gap in the pinned LibreOffice 26.2.6.3 renderer. Three exports
complete with unchanged originals, but only 15 of 21 displayed values match the
independently authored Excel expectations. This is a fidelity finding, not an
accepted customer conversion matrix.

Current preflight now refuses a workbook when the bounded stored-date inspection
positively identifies this early-1900 risk. The application, context preparation
and private export path share that refusal. This prevents the demonstrated
incorrect output; it does not correct the renderer or settle whether refusal is
acceptable for launch. Formula results after recalculation and unsupported
formatting/inspection cases still require a complete date policy.

The later formula experiment below demonstrates that this is an actual bypass
of the known-date check: modern saved values pass preflight, while recalculation
produces six incorrect early-date displays. The rendering policy remains
unresolved; successful preflight is not proof of date fidelity.


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
row/nondefault-column format inheritance, conditional formatting, ambiguous declarations,
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

The six retained PDF date mismatches remain unresolved. The later known-date
refusal below prevents those positively identified workbooks from entering new
exports. Next work must complete date policy for formula and unsupported-format
cases and resolve the required launch behavior; detection and refusal do not
establish corrected rendering.


Known-date conversion refusal
-----------------------------

`OfficeSourcePreflight` retains the XLSX identity and stored-date evidence while
returning an explanation when `EarlyDateCells` is positive. The application
reports the document as unsupported before asking about calculation or requesting
paid admission. Context preparation repeats the check on its source lease before
creating any root, journal or snapshot. The private adapter repeats it on the
fingerprinted read-only snapshot before opening the native runtime and returns
the same explanation. Both cached-value and recalculation modes are affected;
retry cannot bypass the check. The user is directed to export from Excel, and
Analyze remains available.

The check uses the existing declared worksheet scan, including hidden or unused
declared parts, without trying to infer which cells will be printed. A modern
date, ordinary number, elapsed duration or the 1904 date system does not trigger
this particular refusal. That absence is not a fidelity certificate. Unavailable
inspection counts remain unavailable; this change neither claims those cases
are safe nor resolves their pending rendering/publication policy.

All **3,656 foundation contracts** pass, including application command/retry and
preparation checks, at
`.codex-temp/office-date-refusal-674a449ccc8c403db0d851997200513c`.
All **24 private adapter checks** pass at
`.codex-temp/office-date-refusal-85ce699330e144de8978650f492e42e5`.
The private checks cover both calculation modes on the three retained authored
workbooks. Affected workbooks fail at source preflight; the 1904 control reaches
runtime lookup. The test supplies no native host and creates no native profile
or PDF. Original bytes/timestamps and recorded relevant source inputs are
unchanged. Both contract projects build in Release without warnings/errors.

The first private run reached the correct missing-runtime boundary for the 1904
control but failed because its assertion caught managed file exceptions only;
the native directory lease returns Windows error 2. The corrected assertion
accepts only missing-file/directory errors 2/3 at that boundary. This test-only
correction followed the passing foundation run; its production and public test
sources remain unchanged. The failed run is retained with the foundation evidence.

To rerun the focused private check after building its project:

```powershell
dotnet run --project proprietary/tests/ContextSuite.Pdf.ContractTests -c Release -- `
  --office-date-refusal '<retained Excel date fixtures directory>' `
  '<new .codex-temp/office-date-refusal-GUID directory>'
```


Formula results after recalculation (2026-09-16)
-----------------------------------------------

`Test-OfficeEvaluation.ps1 -ExcelFormulaDates` generates three variants with the
same date-system declarations and number formats as the numeric matrix. Each
of the seven numeric cells becomes an explicit arithmetic formula (`0+` followed
by its original numeric value). The first four saved results are deliberately
set to 40729; the remaining caches retain their correct values. There are no
external references or volatile functions. This separates calculation policy
from the date display issue without using customer documents.

Each workbook is exported under explicit always-recalculate and never-recalculate
profiles. Both settings are verified after the engine exits. The current shared
source preflight is run before each export and returns ordinary XLSX, no refusal,
complete stored-date inspection, zero early stored dates and six calendar-formatted
formula cells. The elapsed-duration formula is not counted as a calendar date.
The preflight does not evaluate any formula.

| Date-system declaration | Recalculate | Use saved values |
| --- | --- | --- |
| Omitted (1900) | 4/7 displays match; serials 1, 59 and 60 remain wrong | 7/7 match the deliberately saved values |
| Explicit false (1900) | 4/7 displays match; the same three errors | 7/7 match the deliberately saved values |
| Explicit true (1904) | 7/7 match | 7/7 match the deliberately saved values |

Overall **36 of 42 displays match; fidelity fails**. Saved-value matches do not
make those stale values mathematically correct. They verify the explicitly chosen
saved-value behavior only. Recalculated modern dates, date/time and elapsed-hour
controls retain their expected displays. Changing a whole workbook's date base
cannot fix only the affected values. The current implementation is not changed
to rewrite cells, force saved values or silently omit formula support.

All six exports complete with structural PDF checks, independent PDFium
text/rendering, one-page geometry, unchanged original hashes/write times and no
active child processes after job cleanup. These are command-line evaluation
exports using short repository-local profiles, not production app/worker
publication or AppContainer network-isolation acceptance. No Windows profile
registration, installation, Explorer changes or live commerce occurs.

Evidence is retained at
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-ede461f4630a4338a9375a56d602fbf7`.
`office-evaluation.json` records all six cases and preflight observations;
`independent-formula-dates.json` derives expectations from the source values,
arithmetic, formats and chosen calculation policy, and explicitly records
`FidelityPassed: false`. The inspection uses authored Excel-format expectations,
not Microsoft Excel-rendered reference PDFs.

The wrapper log and source/exit receipts use `.codex-temp/excel-formula-dates`.
The Release probe build has zero warnings/errors; all 17 profile declaration
contracts pass. Eight negative controls reject altered mode, missing cases,
PDF identity, observations, preflight counts, profile paths, arithmetic and
caches. The inspector also rechecks a fresh copy of the original numeric matrix,
retaining its 15/21 matches. These records are in
`.codex-temp/excel-formula-dates-inspection.json`. After execution, only the
inspector's path guard was corrected to match the existing profile-name suffix;
all renderer, fixture and Core source inputs remain unchanged. Repository
boundary, theme and 165-document checks pass. General foundation/native suites
were not rerun for this evaluation-only change.

Next work must handle dates produced by recalculation, as well as stored dates
and formats outside the scan's scope. The existing known-date refusal cannot
close this gate. No refusal-only scope reduction or date-fidelity acceptance is
inferred from the experiment.


Exported cache inspection (2026-09-16)
--------------------------------------

`Test-OfficeEvaluation.ps1 -ExcelDateSnapshots` exports the same three authored
formula workbooks to separate XLSX copies under both explicit calculation modes.
All **42 exported numeric caches match** the chosen saved-value or recalculation
policy. Originals retain their hashes and write times, six jobs end with no active
children, and all 17 profile declaration checks pass. These exports produce no
PDFs and use command-line evaluation profiles, not registered Windows profiles.

The original bounded date reader returned unavailable for every copy. Inspection
identified the known LibreOffice workbook calculation-syntax extension and an
explicit column style of zero. The reader now accepts the exact reviewed
extension shape and the default column style, without evaluating formulas or
changing the input. Its complete read of all six retained copies reports three
early dates for each recalculated 1900 variant, zero for the saved-value and 1904
controls, and six date-formatted formula cells in every case.

Microsoft documents [column styles](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.column?view=openxml-3.0.1)
as defaults. Index zero agrees with the reader's existing fallback for cells
without an explicit style; other column inheritance and all row inheritance
remain unavailable. LibreOffice's [extension writer](https://raw.githubusercontent.com/LibreOffice/core/master/sc/source/filter/excel/xeextlst.cxx)
identifies the calculation extension's URI and five string-reference conventions.
The reader accepts those values only in a single, exact workbook extension with
one empty payload element and the expected attribute. Unknown values, namespaces,
attributes, nested content, repeated/relocated extensions and worksheet copies
remain unavailable. This is interpretation of stored values and formats only;
the syntax marker does not authorize formula evaluation. The upstream source is
research, not a claim that its bytes match the pinned runtime.

Evidence is retained at
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-9023afb44fbc45acb75646f596c11cc6`.
The independent [copy inspector](../tools/office-engine/Inspect-ExcelDateSnapshots.py)
checks authored formulas, source caches, formats, date declarations, exported
numeric caches, hashes, calculation profiles and job completion.
`independent-date-snapshots.json` records the output declarations and observations.
The later `stored-snapshot-inspection-6b406b2169084ba98d5bb119de2034f1.json`
records the improved reader's six successful checks with unchanged copies.
The original unavailable observations remain in `office-evaluation.json`.

Recheck the generated copies without launching an engine:

```powershell
dotnet run --project tools/office-engine/Probe/Office.Evaluation.csproj -c Release -- `
  --inspect-excel-date-snapshots '<completed ExcelDateSnapshots directory>'
```

The native-generation log and receipts use `.codex-temp/excel-date-snapshots`.
The reader/probe changes were made after generation; the retained files were then
read again rather than regenerating them. Source data is never normalized or
rewritten to make the inspection pass. Release probe builds have zero warnings
or errors. The existing date guard can now inspect this additional workbook
shape, but no production snapshot-export or post-calculation guard is added.

All **4,058 foundation contracts pass**, including 38 new reader-boundary checks.
The canonical Release log and unchanged-input receipts use
`.codex-temp/excel-snapshot-foundation`. Seven independent-inspector controls pass
in `.codex-temp/excel-snapshot-inspector-checks.json`, including an altered numeric
cache with a correspondingly updated hash. The first scratch mutation changed
a shared-string index instead of the intended numeric cell and correctly left
the cache comparison unchanged; the corrected control selects cell B2 explicitly.
Both disposable copies are retained. Repository boundary, theme and 165-document
checks pass. Formal packaging and visible acceptance were not rerun.

LibreOfficeKit documents [copy-save semantics](https://github.com/LibreOffice/core/blob/master/include/LibreOfficeKit/LibreOfficeKit.hxx)
that retain the loaded document's identity. Separate command-line exports do not
establish correspondence with a PDF from the same instance. The native experiment
below checks that correspondence for these authored workbooks; production
integration, interrupted-save handling, early-date rendering and the required
launch date policy remain open.


Native same-document copy experiment (2026-09-16)
------------------------------------------------

The evaluation-only [native probe](../tools/office-engine/DateProbe/Probe.cpp)
loads each authored formula workbook once. On that same document pointer it
saves a PDF, saves an XLSX copy without `TakeOwnership`, and saves a second PDF.
The [managed harness](../tools/office-engine/Probe/ExcelNativeDateEvaluation.cs)
uses the existing bounded job and fresh short profile for each case. These are
ordinary processes operating on passive generated fixtures, not filesystem or
network isolation acceptance. No production host, transport or packaging changes
are part of this experiment.

All six date-system/calculation cases complete. The twelve PDFs pass qpdf and
independent PDFium parsing/rendering. Each before/after pair has identical raw
text, Letter page geometry and 96-DPI BGRA pixels. All 42 exported numeric caches
match the selected saved-values or recalculation policy. The bounded reader
reports three early dates for both recalculated 1900 cases, zero for the other
four controls and six date-formatted formulas in every copy. Originals retain
their bytes and last-write timestamps, and completed jobs contain no processes.

The independent inspector records `PdfUnchanged: true` and
`ExpectedCachesMatch: true`, but **`DateFidelityPassed: false`**. Only 36 of 42
date/duration displays match Excel expectations, before and after copy-save.
The same six early-1900 display errors remain. Identical wrong PDFs prove that
this copy-save did not change these fixtures; they do not prove correct rendering
or establish equivalence for arbitrary workbooks.

Evidence:
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-63a69667cfcd4512b2e607d4034839ab`.
It includes the twelve PDFs, six workbook copies, per-case progress, profile/job
records, text, renders and `independent-date-snapshots.json`. Source-bound log and
unchanged-input receipts use `.codex-temp/excel-native-dates-inherited`.
The native executable is identified by
`.codex-temp/office-date-native/f13dc52e87fa440f868fe8834d4f5e70/build.json`;
the wrapper checks that executable and its C++/CMake sources before execution.

The initial run reached its 60-second deadline without producing a PDF. Its
failed log and unchanged-input receipt remain under `.codex-temp/excel-native-dates`,
with evidence directory `evaluation-421445804fc94cc3931f37625f1c52ea` beneath
the same prepared Office root. Its owned process was terminated. The completed
run supplies `SAL_LOK_OPTIONS=unipoll` before native process creation, matching
the established embedded evaluation, and adds flushed phase diagnostics. It
does not extend the deadline or count the timed-out run as successful.

Build and run with independently prepared payloads:

```powershell
.\tools\office-engine\Build-ExcelDateProbe.ps1
.\tools\office-engine\Test-OfficeEvaluation.ps1 `
  -PreparedDirectory '<prepared Office directory>' `
  -PdfPreparedDirectory '<prepared qpdf directory>' `
  -PdfiumPreparedDirectory '<prepared PDFium directory>' `
  -ExcelNativeDateSnapshots -NativeDateProbeDirectory '<new native probe directory>'
python -B tools/office-engine/Inspect-ExcelDateSnapshots.py '<completed evaluation directory>'
```

Native and managed Release builds pass; all 17 profile declaration checks pass.
Thirteen independent-inspector rejection controls cover incomplete cases,
changed sources/copies/PDFs/date observations, mismatched profiles, failed scans,
changed/truncated pixels and unfinished jobs. Their receipt is
`.codex-temp/excel-native-date-inspector-checks.json`. The seven existing CLI
snapshot-inspector controls also pass with the extended inspector, including an
altered numeric cache with an updated hash; their new receipt is
`.codex-temp/excel-native-date-cli-inspector-checks.json`. Foundation contracts are
unchanged and were not rerun for this evaluation-only change.

The subsequent [calculated-date guard](excel-calculated-date-guard.md) carries
bounded workbook evidence from the isolated production host through validation
and withholds the demonstrated bad exports. Active copy-save interruption and
broader deadline/cancellation coverage remain open. This experiment alone
neither adds that publication guard nor repairs required date fidelity. Broader workbook
formatting, native network isolation, current combined packaging and manual
acceptance remain separate open work.


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
