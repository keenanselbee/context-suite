PDF Page Geometry And Size Acceptance
=====================================

The later [typed page-limit checkpoint](pdf-page-limits.md) closes the generic
invalid-input wording gap identified here and expands this suite to 59 checks.
The original 50-check results below retain their own stage and scope.

The isolated staged PDF-to-PNG path passes **50 checks** against independently
authored page geometry and pixel expectations. This verifies a previously open
page-size/fidelity slice; native allocation failure and broader PDF fidelity
remain separate obligations.

Authored cases
--------------

- Four cropped rectangular pages declare rotations 0, 90, 180 and 270 degrees.
  A nonzero CropBox selects the center of a larger MediaBox. Red, blue, green
  and transparent quadrants make their orientation observable without fonts,
  images or a second renderer as the expected-output oracle.
- A 1,920-point-square page reaches exactly 4,000 by 4,000 pixels at 150 DPI:
  the existing 16-million-pixel page limit.
- Thin solid pages reach exactly 16,384 by 1 and 1 by 16,384 pixels. Their long
  physical dimension is deliberately just below the pixel edge to account for
  PDFium's float geometry and the protocol's ceiling operation. The admitted
  pixel limit is exercised; no exact real-number round-trip claim is made.
- Three valid PDF containers declare pages beyond the pixel-area, width or
  height bounds. Inspection refuses each before publication.
- Another valid page then completes through the same worker client.

The crop and clockwise rotation expectations follow the page dictionary and
default user-space rules in Adobe's
[PDF reference](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/pdfreference1.3.pdf).
These fixtures use explicit page entries; inherited boxes, UserUnit and other
page-boundary variants are not established by this matrix.

Every accepted page passes native inspection, actual worker rendering, validation
and application-owned copy publication beside the original, even with overwrite
preference set and no alternate output folder selected.
The test decodes the published PNG with Windows' WPF decoder and compares its
complete BGRA SHA-256 to rows calculated from the authored pattern. This compares
all color and alpha bytes, not just dimensions or two copies of a renderer result.
It does not provide an independent PDF renderer comparison or visual acceptance.

All eleven originals retain their SHA-256 and write timestamp. Rejected inspections
add no outputs or publication journals. Reservations and journals are empty after
completion, and the recycler throws if invoked. Test results also record elapsed
time and the worker's cumulative peak working set as observations, not release
performance thresholds or native-child allocation measurements.

Evidence and reproduction
-------------------------

The current test uses unchanged combined stage
`artifacts/production-staging/747958a4173e4bf4b16ad92e9fd9c7a2`.
The complete pinned PDF payload is verified before and after execution. No
production code or payload was changed, and previous image/audio/foundation
suite counts are not repeated as new evidence. The Release host builds without
warnings/errors.

The initial 42-check direct run is retained at
`.codex-temp/pdf-page-geometry-e8da000aec8141f4a23ad29703cb6c08/pdf-page-geometry.json`,
with log `.codex-temp/pdf-page-geometry.log`. The new standalone wrapper also
passes all 42 checks and both payload verifications, retaining
`.codex-temp/pdf-page-geometry-57a41bf77ced45ea9f4755cdc4d5b762/pdf-page-geometry.json`
and `.codex-temp/pdf-page-geometry-wrapper.log`. These are repeat executions of
one 42-check suite, not 84 distinct checks. The final run removes the alternate
output directory and adds eight explicit source-folder copy assertions, passing
**50 checks**. It retains
`.codex-temp/pdf-page-geometry-68c2932dc2c0441d83b23c9010efe768/pdf-page-geometry.json`
and `.codex-temp/pdf-page-geometry-copy-policy.log`, with both payload checks
passing. The broader combined-production
wrapper was updated and reviewed, but its other suites were not rerun here.

```powershell
.\tools\pdf-engine\Test-PdfPageGeometry.ps1 -ProductionStage '<existing isolated combined stage>'
```

The command writes generated PDFs, published PNGs and `pdf-page-geometry.json`
under a fresh repository `.codex-temp/pdf-page-geometry-*` directory. The combined
`Test-PdfProduction.ps1` wrapper includes the same contract. No customer files,
reference assets, installation, registration, recycling or live licensing are used.

The first test-host compile used a nonexistent `DecodeFailed` enum member. It
was corrected to assert the actual current `InvalidInput` category. The successful
run does not make that wording suitable: page-limit failures still need a distinct,
readable size-limit outcome. Keep that UX issue open alongside native allocation
failure, actual adapter deadline, hostile decoders, forms/fonts/transparency
variants, inherited geometry and manual/assistive acceptance.
