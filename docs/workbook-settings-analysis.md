Workbook Settings Analysis
==========================

Analyzer `workbook-settings-1` reports saved date and calculation declarations
from recognized OOXML spreadsheets. This provides read-only evidence for the
Office fidelity work after the [date-system experiment](excel-date-system-evaluation.md)
and [calculation experiment](excel-calculation-evaluation.md). It does not select
a rendering policy, evaluate formulas or enable Office-to-PDF conversion.


Reported declarations
---------------------

The reader uses the main workbook XML already loaded for package identification.
It handles the existing Transitional and Strict spreadsheet namespaces and the
existing workbook/template/macro-enabled content-type mapping. No additional
package reads, engine startup or installed Office dependency are introduced.

| XML attribute | Reported value |
| --- | --- |
| workbookPr/date1904 | Boolean declaration of the 1904 date base |
| workbookPr/dateCompatibility | Separate Boolean date-compatibility declaration |
| calcPr/calcMode | Automatic, Manual or Automatic except data tables |
| calcPr/fullCalcOnLoad | Boolean full-calculation-on-load request |
| calcPr/forceFullCalc | Boolean forced-full-calculation request |
| calcPr/calcOnSave | Boolean calculation-on-save request |
| calcPr/iterate | Boolean iterative-calculation declaration |
| calcPr/fullPrecision | Boolean full-precision-calculation declaration |

These are explicit declarations, not an effective configuration or a promise
that a renderer honors them. Missing attributes remain `NotEncoded`, with no
invented false value or automatic mode. This distinction also applies when the
entire optional element is absent. The collapsed technical details explain that
defaults, cell dates, formulas, cached results, sheet settings and renderer
behavior were not checked. Boolean facts retain their typed values.

Microsoft documents the interaction between
[date1904 and dateCompatibility](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.workbookproperties?view=openxml-3.0.1):
dateCompatibility=false makes date1904 inapplicable. The analyzer reports both
saved flags rather than deriving a possibly misleading effective date system.
The [calculation properties](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.calculationproperties?view=openxml-3.0.1)
and [mode values](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.calculatemodevalues?view=openxml-3.0.1)
provide the other field identities. Primary documentation was read on 2026-09-11;
no SDK, schema dataset or reference implementation was imported.


Parsing and failure limits
--------------------------

Existing package budgets remain unchanged: 256 KiB per selected XML part,
1 MiB aggregate expanded content, 4 MiB managed package reads, XML depth 32,
prohibited DTDs and no resolver. The application retains its read-only lease
and content deadline. Cancellation propagates through the declaration scan.

Each settings element must be a unique direct child in the workbook namespace.
Duplicate, nested or foreign-namespace lookalikes, qualified lookalike attributes,
child elements and non-whitespace element content make that group unavailable.
Boolean values accept XML true/false/1/0 and surrounding XML whitespace; unknown
or invalid values are not echoed into the report. Unknown unrelated attributes
are outside this scan, so it does not claim complete schema validation.

Date and calculation groups are independent. A late malformed flag discards its
whole group's tentative facts, adds an explanation, and preserves the other
group and basic spreadsheet identity. Malformed main XML or an exceeded main-part
budget still uses the established ZIP/basic-file fallback.

Markup-compatibility elements and directives other than `mc:Ignorable` leave both
groups unavailable because this reader does not choose alternate content or
process extension wrappers. An Ignorable attribute alone does not alter the
literal standard declarations being reported. Namespace declarations alone are
not setting attributes. This limited handling is not a compatibility processor.


Verification and remaining work
-------------------------------

All **2,243 foundation contracts pass**, including **61 new checks** for both
namespace families, Boolean forms, all three modes, omissions versus false,
interacting date declarations, invalid values, group isolation, ambiguous XML,
compatibility handling, non-spreadsheet scope and cancellation. A real application
reader case recognizes a workbook named PNG and preserves its hash/timestamp.
Log: `.codex-temp/workbook-settings-foundation-final.log`.
The initial run caught an await/span error in the new test's hash comparison;
the corrected final run passes without changing production behavior for that fix.

The same reader inspected seven retained date/calculation experiment workbooks.
Independent Python inspection compares all eight reported attributes against
their source XML and verifies source hashes. All seven match; originals retain
their bytes and write times. Evidence is in `.codex-temp/workbook-settings-probe/`.
Cached, stale and missing-cache automatic fixtures correctly share an automatic
declaration: these facts cannot establish cache freshness or formula equivalence.

Fresh combined Release staging is
`artifacts/production-staging/c2a73562db084b148f407c62d74c82d4`, with zero build
warnings/errors and passing curated image/audio/PDF candidate, notice, dependency
and file-inventory checks. Log:
`.codex-temp/workbook-settings-production-c2a73562db084b148f407c62d74c82d4.log`.
The build uses `-SkipShell`; no new native, installed, visible, keyboard or
assistive-technology acceptance is claimed. No Office engine is shipped by this
change. Catalog revision remains 2026-09-11.6 and report schema remains 1.

Required next work includes calculation policy, cell/style-aware date handling,
missing-font handling, renderer isolation, actual Office conversion and its
fidelity matrix. These declarations are evidence for that work, not permission
to treat a workbook as safe to render or to remove those launch requirements.
