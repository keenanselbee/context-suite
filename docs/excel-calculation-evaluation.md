Excel Formula Calculation Evaluation
====================================

This is an isolated Office-to-PDF experiment, not a customer conversion feature
or acceptance of a spreadsheet calculation policy.

The earlier workbook fixture had a correct cached formula value. Its successful
PDF could not distinguish using that saved value from actually calculating the
formula. The `-ExcelCalculation` mode in
[Test-OfficeEvaluation.ps1](../tools/office-engine/Test-OfficeEvaluation.ps1)
addresses that gap with four independently authored passive workbook variants.
All use `SUM(A2:B2)` with inputs 100 and 157:

| Case | Workbook calculation mode | Saved formula result |
| --- | --- | --- |
| Correct-cache control | Automatic | 257 |
| Stale automatic | Automatic | 918273 |
| Stale manual | Manual | 918273 |
| Missing cache | Automatic | No value element |

Each keeps `calcId=191029`, `fullCalcOnLoad=0` and `forceFullCalc=0`. The experiment
does not modify user files, add external references, use macros or volatile
functions, or change a person's installed Office profile. Variants are created
from the harness's own tiny XLSX, with a hidden sheet and a print-area sentinel.

Each variant runs with three fresh short profiles: the unmodified evaluation
default, requested `OOXMLRecalcMode=0` (always), and requested value 1 (never).
Only this experiment sets that property; requested settings are not proof of
effective behavior. The retained 26.2.6.3 payload's
`share/registry/main.xcd` declares the property under
`org.openoffice.Office.Calc/Formula/Load`, with default value 1.
[LibreOffice's current calculation enum](https://raw.githubusercontent.com/LibreOffice/core/master/sc/inc/calcconfig.hxx)
documents the numeric mapping; the actual candidate behavior is tested separately.
[LibreOffice formula help](https://help.libreoffice.org/latest/en-US/text/shared/optionen/01060900.html)
describes the import recalculation choices, and
[Microsoft's calculation-mode documentation](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.calculationproperties.calculationmode?view=openxml-3.0.1)
identifies the workbook attribute. These references were read on 2026-09-11;
the current upstream source/help are not a claim of exact candidate source identity.

The wrapper verifies retained engine inventories before launch. Each conversion
must produce a bounded PDF, pass qpdf checking and independent PDFium page/text
inspection/rendering, retain the authored page size and hidden/print-area policy,
and leave the workbook hash unchanged. A correct-cache match is explicitly not
proof of recalculation. Observed stale values are reported as such; completing
the experiment does not mean they are acceptable launch output.

Initial default-only evidence
-----------------------------

The initial four-case run retained both stale cached values, including in the
automatic workbook. The missing-cache case produced 257. All four exported
one-page PDFs with preserved originals and print policy. Evidence:
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-3388203b44fd4885bad39af8a89f369e`;
log `.codex-temp/office-excel-calculation.log`.

The first twelve-case comparison produced the same values for all three
requested profiles. The recalculation property was absent from every resulting
profile's `registrymodifications.xcu`. This does not prove that an effective
always-recalculate setting preserves stale values: applying the override itself
was not established. Evidence is `evaluation-823b5180f24940898ad11c6c67a70b76`
under the same prepared directory, with log
`.codex-temp/office-excel-calculation-matrix.log`. An independent Python inspection
verified the workbook XML values/modes, source/PDF hashes and saved profile
properties in `independent-calculation-verification.json`.

The final experiment initializes each disposable profile with
[`--terminate_after_init`](https://help.libreoffice.org/latest/en-US/text/shared/guide/start_parameters.html)
before requesting the calculation setting. It retains initialization diagnostics
and a copy of the requested settings before document execution. Initialization
does not load a document or establish an isolation boundary.

Initialized-profile results
---------------------------

All twelve final exports passed bounded PDF checking, independent page/text
inspection/rendering, page size, print-area/hidden-sheet checks and original
preservation. The observed formula values were:

| Workbook | Default | Requested always (0) | Requested never (1) |
| --- | --- | --- | --- |
| Correct cache | 257 | 257 | 257 |
| Stale automatic | 918273 | 257 | 918273 |
| Stale manual | 918273 | 257 | 918273 |
| Missing cache | 257 | 257 | 257 |

An independent inspection confirms the exact source formula/inputs/caches,
workbook modes and source/PDF hashes. The requested setting persisted as 0 or 1
in all eight explicit profiles after export. This demonstrates a working
recalculation override for these fixtures after initialization. It does not
establish the internal cause of the earlier discarded settings.

Two policy implications matter: forced recalculation also changes a workbook
marked manual, and "never" does not guarantee that no calculation occurs when
cached values are absent. Do not use that setting as an execution-isolation
control or promise it will always reproduce saved values.

Evidence is under
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-70d65fd762754f07b1a0faf272881b9c`:
`office-evaluation.json`, PDFs, page renders/text and
`independent-calculation-verification.json`. Each recorded profile retains
`initialization.json`; explicit profiles also retain `requested-settings.xcu`.
The log is `.codex-temp/office-excel-calculation-initialized.log`; the independent
inspection script is `.codex-temp/VerifyExcelCalculation.py`.

The Release evaluation host builds with zero warnings/errors. Repository
boundary/theme and 77-document validation pass. No production build or foundation
rerun is claimed for this evaluation-only change. No Office processes remained
after the final run.

Remaining acceptance
--------------------

The later [workbook settings analyzer](workbook-settings-analysis.md) reports
saved calculation mode and selected flags without supplying missing defaults.
Retained correct-cache, stale-cache and missing-cache automatic fixtures all
report Automatic as declared; this cannot certify the freshness of their values.

The launch implementation must choose and document an explicit calculation
policy; neither a successful export nor a cached-value match proves fresh or
Excel-equivalent results. Broader formulas, dates/locales, missing fonts,
charts, external dependencies, manual calculation and legacy XLS need separate
coverage. Arbitrary-document isolation and production integration remain open.
Other profile settings also require verification after initialization: a retained
profile showed `AutoCheckEnabled=true` despite the initial false seed. This
experiment did not trace network activity or establish effective update disabling.
The subsequent [profile-settings correction](office-profile-settings.md) tracks
reapplication and saved-declaration verification separately from enforcement.
No installer, live service, AppContainer profile, visible UI or screen-reader
acceptance is established here.
