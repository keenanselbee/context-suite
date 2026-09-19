PowerPoint Layout Acceptance
============================

Status: application publication/cleanup passes; image and diagram/table checks
pass, but typography exceeds the predeclared font-size rounding tolerance.
This covers the image, diagram/table and mixed-typography group in the
[local acceptance contract](local-candidate-acceptance.md). It complements
existing slide-order/visibility/notes checks. It is not real-document or human
page acceptance, and does not resolve the separate Excel chart-loss blocker.


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
sizes allow 0.01-point numerical variation. Expected families/bold/italic names
must match, fonts must be embedded and the typography slide must contain four
distinct font programs. Font names alone are not complete font-program fidelity;
the recorded programs and rendered pages remain available for human review.

Independent 96-DPI raster checks require all four image colors to occupy their
declared rectangles, with at most two pixels of edge variation and at least 97%
matching area. Color channels allow eight code values of rendering variation.
Fixed interior regions check the two diagram fills, black connector and table
fill. Glyph checks preserve the association between table labels and values.
The verifier must reject extra text, moved glyphs, wrong font names, changed
font sizes and removed image/diagram pixels. Expected bounds and limits are not
derived from the first output or loosened after a failed export.


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
