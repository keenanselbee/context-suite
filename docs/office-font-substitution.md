Office Font Substitution Evaluation
==================================

The 2026-09-11 passive evaluation demonstrates different font substitutions in
Word, Excel and PowerPoint exports. All six exports pass the existing page,
visible-text and print-policy checks, while four of the five paired pages have
different pixels. Successful export and matching text are insufficient evidence
of layout fidelity.


Authored comparison
-------------------

`Test-OfficeEvaluation.ps1 -FontSubstitution` creates two copies of each existing
passive modern-format fixture. The control requests Arial; the other requests
the synthetic family `ContextSuiteAbsentFont9361`. The test installs no fonts.
Word changes the explicit title-run font, leaving other runs unchanged.
PowerPoint changes its explicit/theme font declarations. Excel receives an
explicit default Arial or synthetic font stylesheet, replacing its previous
dependence on an unspecified default.

The independent Python inspection verifies that corresponding ZIP parts are
byte-identical after substituting the one requested family name back to Arial.
It also verifies original/PDF hashes and the recorded PDF font-name observations
against retained qpdf JSON. Each export uses a new initialized profile with the
previously verified saved settings and the existing fixed PDF options.

The pinned LibreOffice 26.2.6.3 engine produced these PDF BaseFont declarations
(subset prefixes omitted here only for readability):

| Family | Control | Synthetic-family case | Changed pixels per page at 96 DPI |
| --- | --- | --- | --- |
| Word | Calibri; Arial-BoldMT | Calibri; BodoniMTBlack | 6,268; 0 |
| Excel | ArialMT | DejaVuSans | 2,806 |
| PowerPoint | ArialMT | DejaVuSans | 11,648; 11,618 |

All pairs have equal normalized extracted text and identical page dimensions.
The pixel comparison uses complete BGRA buffers, without rescaling or tolerance.
Word's unchanged second page is a useful control because its font declaration
was not modified. Every export returned zero with empty stderr; the retained
stdout contains the normal conversion message, not a font-substitution warning.

These are observations on this machine's font environment. PDF name declarations
are not independent font-program identity or per-character fallback validation.
The test does not establish which substitution another machine will choose,
whether all glyphs match, or how long documents will repaginate. No visual review,
screen-reader delivery or acceptable typography tolerance is claimed.


Implementation consequence
--------------------------

Office-to-PDF must handle missing fonts explicitly before routine quiet success.
Do not assume an empty diagnostic stream means all requested fonts were used.
The next implementation must account for styles/themes, aliases, embedded-font
rights, per-script glyph fallback and the renderer's actual font environment.
Comparing PDF BaseFont strings alone is not an adequate production detector.
Preserve copies and use a compact necessary decision when fidelity cannot be
maintained, within the existing context-menu UX; a general planner is unnecessary.
The detector and exact customer flow are still unimplemented.
The subsequent [read-only declaration scan](document-font-references.md) reports
requested names and unresolved themes within fixed limits; it does not establish
installed availability or determine the fonts actually used by the renderer.

This finding adds to the unresolved calculation policy and isolated-engine gates;
it does not enable customer Office conversion or replace that requirement with
analysis. AppContainer access testing remains pending its separate authorization.


Evidence
--------

Six bounded exports, qpdf checks, PDFium text/renders, original preservation and
17 profile declaration contracts pass. The probe builds in Release with zero
warnings/errors. The wrapper verifies the retained Office/qpdf inventories and
PDFium binaries before launch. No Office processes remained after completion.

Evaluation root:
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-6e80c05370d2453f9dcf9a40a7b60285`.
It retains `office-evaluation.json`, fixtures, PDFs, font-object JSON, text,
renders, profile paths and `font-comparison.json`. Log:
`.codex-temp/office-font-substitution.log`.

Run the retained-result inspection with
`python -B tools/office-engine/Inspect-OfficeFontComparison.py '<evaluation root>'`.
It refuses existing result-file reuse. Four negative cases reject the wrong
evaluation mode, a changed PDF, changed font observations and a path escaping
the Office scratch directory; evidence is
`.codex-temp/office-engine/font-guards-0135a2477c5948f684c51a7f5175a24b/results.json`.
No production build, full foundation rerun, installed changes or hostile-document
isolation acceptance is implied by this evaluation-only checkpoint.
