PowerPoint Layout Acceptance
============================

Status: application publication/cleanup and independent D09 inspection pass.
On 2026-09-24 the owner approved a fixture-only 0.015-point font-size allowance;
reinspection of the unchanged candidate PDF passes all three negative-control
groups. Historical failures under the original 0.01-point limit remain below.
This covers the image, diagram/table and mixed-typography group in the
[local acceptance contract](local-candidate-acceptance.md). It complements
existing slide-order/visibility/notes checks. It is not real-document or human
page acceptance. The later [candidate record](expanded-candidate-1.1.2.md)
contains the repaired Excel charts and current D09 replay; the earlier attempts
below retain their original observations.


Fixed fixture and expectations
-------------------------------

The independently authored passive PPTX has three visible 720 by 405 point slides.
It uses ordinary DrawingML picture, shape, connector, table and text-run objects,
following Microsoft's [slide structure](https://learn.microsoft.com/en-us/office/open-xml/presentation/working-with-presentation-slides)
and [graphic-frame model](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.presentation.graphicframe).
There are no macros, external links, embedded Office objects, notes or hidden slides.

- Slide one places a 64-by-64-pixel BMP at (72,108), sized 216 by 144 points.
  Its four equal quadrants are red, green, blue and yellow in reading order.
  A separate caption sits below it.
- Slide two has labeled blue/green start/end boxes and a horizontal black arrow.
  Its native two-column, three-row table starts at (72,180); columns are 216
  points wide and rows 36 points high. Rows contain ITEM/VALUE, ALPHA/10 and BETA/20.
- Slide three has separate Arial regular 20-point, Arial bold 24-point,
  Times New Roman italic 22-point and Courier New regular 18-point lines.
  All slides have an explicit Arial bold 24-point heading.

Before the first export, the oracle requires exact text tokens on the correct
pages, no extra labels and the authored three-page geometry under the existing
0.1-point page-size tolerance. PDFium reads each
non-whitespace character's ink bounds, size, font name/flags, embedding status and
embedded-program hash. Ink must remain inside its authored box within one point;
its left edge must start within four points of the declared text origin. Font
sizes originally allowed 0.01-point numerical variation; the owner-approved
allowance is now 0.015 points for this D09 fixture only. Expected families/bold/italic names
must match, fonts must be embedded and the typography slide must contain four
distinct font programs. Font names alone are not complete font-program fidelity;
the recorded programs and rendered pages remain available for human review.

Independent 96-DPI raster checks require all four image colors to occupy their
declared rectangles, with at most two pixels of edge variation and at least 97%
matching area. Color channels allow eight code values of rendering variation.
Fixed interior regions check the two diagram fills, black connector and table
fill. Glyph checks preserve the association between table labels and values.
The verifier must reject extra text, moved glyphs, wrong font names, out-of-limit
font sizes and removed image/diagram pixels. Expected bounds and limits are not
derived from the first output; the later explicit tolerance decision is recorded
separately from the original failed exports.


Execution and inspection
-------------------------

```powershell
python -B tools/office-engine/Test-OfficeExecution.py --create-disposable-profiles `
  --powerpoint-layout --retained-worker '<verified scratch worker>/ContextSuite.Worker.exe'
python -B tools/office-engine/Inspect-WordFontStyles.py --powerpoint-layout `
  --execution-id '<printed execution ID>' --pdf-prepared '<pinned qpdf preparation>' `
  --pdfium-prepared '<pinned PDFium preparation>'
```

The first command retains the existing explicit disposable-profile authorization
boundary. It uses the actual application command, validated PDF copy publication,
original hash/time checks and native profile/context cleanup evidence. A retained
worker must match the fresh managed build; pinned engines remain unchanged.
The second command verifies independent qpdf/PDFium identities, regenerates and
compares every authored package part, checks the PDF and stores raster/text/font
observations. Each phase retains source/binary identities and failed attempts.
No installation, registration, live licensing, source overwrite or playback occurs.


Retained attempts
------------------

Attempt `2daa124908cb4ba8beb55d72ce37547f` stops before profile creation because
the prior scratch worker's managed bytes no longer match the fresh build. Both
build logs remain; no application contracts directory was created. The guard
was retained, and the next attempt prepares a fresh worker with the pinned
engines from immutable combined 1.1.1. No existing worker is overwritten.

Fresh execution `3e3ed6933e7d42b19a020aca5737313c` passes seven application checks:
one validated PDF copy, unchanged original hashes/write times, no remaining
worker/publication/context and the disposable native profile plus registry
mapping removed. The installed-font deck needs no font-substitution prompt.
The exit receipt reports zero and unchanged captured source/binary/engine inputs.

The first independent inspection, receipt `5f635818231b4be5814f10b19bcf03df`,
fails a verifier assumption: the export's 405.014-point height is within the
existing 0.1-point geometry tolerance, but the rasterizer correctly rounds up
to 541 rows rather than the verifier's hard-coded 540. Inspection now derives
raster dimensions using the probe's existing ceiling rule and maps pixel/glyph
observations using the measured page dimensions. The geometry tolerance did not
change. The earlier failed artifacts remain.

Inspection `9dcd10d5543b4f168de9d10067f51386` also exposes an incorrect table-fill
sample spanning two black row borders. The verifier now samples inside each
authored cell, retaining the same color/coverage limits. All original text,
position, color and font-size expectations remain fixed.

After these verifier corrections, image and diagram/table pages pass. Exact text,
placement and expected embedded font-family/style declarations pass across the
deck. The typography page still fails because its authored 20-point Arial line
is reported as 20.01300048828125 points, exceeding the predeclared 0.01-point size
tolerance. Other recorded sizes are 18, 21.996999740600586 and
24.009000778198242 points. The current aggregate fidelity result remains false.
An explicit owner decision on a proposed 0.015-point rounding allowance is
pending; do not silently loosen the limit or rewrite the failed observations.
The final recorded inspection receipt is `9ab17e30a7ee4f0d9661879cf5e4a576`;
its source/binary identities remain unchanged and its nonzero exit is expected
for this unresolved acceptance failure. Both first pages also pass their missing
text/glyph, extra text, moved glyph, wrong font/size and removed-raster controls.
The typography page's control aggregate stays false because its baseline fails.

Assistant visual inspection of losslessly encoded PDFium rasters shows all three
slides, intact color quadrants, a right-pointing arrow, correctly associated table
values and distinct expected typography without apparent clipping. This is
limited artifact inspection, not owner acceptance, a real PPTX comparison or
screen-reader/theme/DPI testing. Derived PNGs remain under the second inspection's
`visual-check-59301d417bf2464c97c45d03f0418978` directory.

The existing three slide-order/hidden/notes publications were independently
reinspected with the updated inspector. Receipt `6afd9bf71bd64e3ab94815f9be28663c`
under execution `d99391e39ffb46649b8f7cf05653408d` passes all three PDFs and four
negative text controls, with unchanged captured inputs. It does not rerun the
earlier application exports. Worker, contract and inspector builds have zero
warnings/errors. Office chart loss, network enforcement and final-candidate
acceptance remain separate unresolved gates.

All 4,384 foundation contracts pass with exit zero in
`.codex-temp/powerpoint-layout-foundation-6a7832c0537948eeb40839f0b760789e`.
Six command-line authorization/scenario guards pass in
`.codex-temp/powerpoint-layout-guards-bcec9a4db2a04575825dc212811924ce`.
Repository boundary/theme policy and 191 documentation files pass. These green
regression checks do not change the explicitly failed D09 fidelity result.


Reconstructed application replay, 2026-09-23
-------------------------------------------

Runtime04 with the complete shape resources repeats the authored deck through
the actual application at
`.codex-temp/office-execution/c2be527e3f764f6f8e2128eef772e986`, using worker
`45172c40ea494afabd97ab556b49910e`. All **seven application checks** pass:
validated publication, original preservation, no font prompt and complete
worker/profile/context cleanup. Captured execution inputs are unchanged.

Independent receipt `powerpoint-layout-inspection-receipt-2e180ef58fc0407284bbf4657d5bf02d.json`
retains **failed aggregate fidelity**. Image and diagram/table pages pass all
text, placement, font, pixel and negative controls. The typography page preserves
text, placement and embedded font families/styles but again reports the 20-point
Arial line as 20.01300048828125 points. The other three font sizes match the
previous observations above. No tolerance or oracle changed; the pending owner
decision remains separate from the now-repaired ordinary PowerPoint fill defect.
`prior-layout-comparison.json` confirms all three rasters, text, render geometry
and glyph observations exactly match the historical D09 result. Inspection
inputs are unchanged and all builds have zero warnings/errors.
This is not final-candidate or human acceptance.


Candidate rounding diagnosis, 2026-09-24
--------------------------------------

The exact 1.1.2 candidate inspection at
`.codex-temp/office-execution/104e37134a0c4653af3f350bea74d45d/powerpoint-layout-inspection-ac9048868391402b96151f64640776e0/results.json`
retains the same four typography sizes and failed font oracle. A read-only
diagnostic at
`.codex-temp/candidate-112-powerpoint-rounding-ed20c249b0c843618cf45faa5aa5c529`
binds that report, the unchanged oracle and five native source/header files by
hash. It runs no renderer and changes no output, source or tolerance.

The reconstructed source sets the drawing document pool metric to hundredths of
a millimetre. DrawingML imports character heights as points; the font-height
item's setter rounds to twips and, for its non-twip representation, converts to
integer hundredths of a millimetre. The inspected locations are
`sd/source/core/drawdoc.cxx`,
`oox/source/drawingml/textcharacterproperties.cxx`,
`editeng/source/items/textitem.cxx`, `include/tools/UnitConversion.hxx` and
`include/o3tl/unit_conversion.hxx` in the retained source-kit replay tree.

Converting each authored size through that storage unit, then rounding to three
decimal places and observing it as a 32-bit float, reproduces every measured
size exactly:

| Authored points | Integer hundredths of a millimetre | Predicted decimal points | Observed PDFium points |
| --- | --- | --- | --- |
| 18 | 635 | 18.000 | 18 |
| 20 | 706 | 20.013 | 20.01300048828125 |
| 22 | 776 | 21.997 | 21.996999740600586 |
| 24 | 847 | 24.009 | 24.009000778198242 |

This supports unit quantization as the explanation, rather than an arbitrary
allowance fitted to one failing number. It is an inference from source and
observations, not a traced proof of every runtime conversion. It does not prove
other font sizes or documents acceptable. The proposed fixture-only 0.015-point
allowance remains an owner decision; the existing 0.01-point assertion and
failed aggregate are unchanged. A precision repair would require investigating
the renderer's storage/export path and regression impact, not merely rebuilding
the same native inputs.


Approved candidate reinspection, 2026-09-24
-----------------------------------------

The owner explicitly approved the proposed 0.015-point font-size allowance for
this PowerPoint fixture. Only `PowerPointLayoutOracle.FontMatches` changes; all
text, page geometry, glyph placement, font family/style/embedding, raster and
negative-control rules remain. This is not a general Office tolerance change.

Receipt
`.codex-temp/candidate-112-powerpoint-approved-258c5e2bf0ea41e4ba69a17798addb14/results.json`
records independent reinspection of the original 1.1.2 PDF, using the pinned
qpdf/PDFium tools and a fresh managed inspector. Its build has zero warnings and
errors. All three pages and all three negative-control groups pass. Glyph/text
observations, source/PDF identities and candidate inventory are unchanged.
The new inspection is `powerpoint-layout-inspection-43dcb0245e4c43adad66032ab65f476d`;
the prior failed inspection remains intact. No Office export or native rebuild
was needed. The [current worksheet](document-review.md) includes this result;
human review and real-deck acceptance remain separate.
