Office Embedded Image Evaluation
================================

Status: six authored modern Word/Excel/PowerPoint image exports and independent
sample/render/control checks pass, 2026-09-13. This is evaluation evidence, not an
implemented customer Office converter or general document fidelity.

The original passive fixtures established text and simple page geometry. They
did not test embedded image resolution, color or transparency. This experiment
adds one independently authored 1,024-square RGBA PNG per family, drawn at a
nominal two inches square. Its quadrants are opaque red, half-transparent green,
fully transparent magenta and quarter-transparent blue.

Word uses an inline picture on page two, Excel an absolute drawing within an
expanded saved print area, and PowerPoint a picture on the first visible slide.
All relationships are internal. No external resources, macros, reference assets
or customer files are opened. Fixtures follow Microsoft's
[Word picture model](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-insert-a-picture-into-a-word-processing-document),
[spreadsheet anchor](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.spreadsheet.absoluteanchor?view=openxml-3.0.1)
and [presentation picture fill](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.presentation.picture.blipfill?view=openxml-3.0.1).
They are authored XML packages, not Microsoft Office layout baselines.

Each family has two packages with identical part contents. The retained case
uses lossless compression and no downsampling. The positive control changes
only resolution reduction to 150 DPI. Options are recorded per export; controls
must actually reduce dimensions. The existing owned-job launcher and disposable
profiles are used. qpdf checks PDFs and exports their
[decoded JSON streams](https://qpdf.readthedocs.io/en/stable/json.html);
PDFium independently parses and renders all pages at 96 DPI.

The separate `Inspect-OfficeImages.py` launches no engine and reconciles source
and PDF hashes, identical package parts and the embedded PNG CRCs, bounded
inflation and independent quadrant oracle. Retained PDF RGB samples with nonzero
alpha and every soft-mask byte must match the source; fully transparent RGB is
excluded. Rendered solid patch interiors and quadrant order must also agree,
so merely retaining an unused image object cannot pass. It requires reduced
dimensions in the controls and tests changed color/alpha bytes in memory as
negative comparisons. Conversion records must show zero active job members.

Recorded results
----------------

| Family | Retained image | Reduction control | Retained samples and alpha | Rendered image page |
| --- | --- | --- | --- | --- |
| Word DOCX | 1,024 × 1,024 | 300 × 300 | Exact for the stated scope | 2 of 2 |
| Excel XLSX | 1,024 × 1,024 | 300 × 300 | Exact for the stated scope | 1 of 1 |
| PowerPoint PPTX | 1,024 × 1,024 | 300 × 300 | Exact for the stated scope | 1 of 2 |

All six PDFs pass qpdf structure, PDFium page/text/geometry checks and rendered
opaque/translucent patch comparisons. Source hashes remain unchanged. Retained
and control packages contain identical parts, and export settings differ only
in resolution reduction. The six measured export times range from 9,614 to
10,931 ms on this machine, excluding each profile's initialization.

The first inspector stopped because it expected a default soft-mask Decode
array. All six PDFs instead use `[1 0]`, mapping stored mask samples in reverse.
The inspector now applies that declared mapping before comparing alpha, as
specified by the [PDF image Decode entry](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/pdfreference1.6.pdf).
The engine settings and source expectations were unchanged; the first failed
inspection remains retained. Both independent rendered appearance and exact
mapped alpha pass. Other sample mappings are refused by this scoped inspector.

Evidence is under
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-651bb77812a34685a04f5b3ad5f2d16b`:
`office-evaluation.json`, per-case `conversion.json`, `pdf-image-objects.json`,
PDFs and BGRA page renders, and `image-comparison.json`.
The wrapper log is `.codex-temp/office-embedded-images.log` with exit zero;
the final strengthened comparison log is `.codex-temp/office-image-comparison-3.log`.
The 17 existing profile declaration contracts also pass. No human visual review
is claimed for these new rendered pages.

```powershell
.\tools\office-engine\Test-OfficeEvaluation.ps1 `
  -PreparedDirectory '<prepared Office>' `
  -PdfPreparedDirectory '<prepared qpdf>' `
  -PdfiumPreparedDirectory '<prepared PDFium>' -EmbeddedImages
python -B tools/office-engine/Inspect-OfficeImages.py '<printed evaluation directory>'
```

The wrapper verifies the MSI and all recorded Office/qpdf/PDFium identities
before execution. It creates fresh evidence; the inspector rechecks an existing
comparison without overwriting it and refuses changed results. It is not an
untrusted-document inspection service.

Even a passing result leaves JPEG/CMYK/ICC/HDR, cropped/rotated images, vectors,
charts, wrapping, legacy formats and wider layout fidelity open. AppContainer
access denial, hostile-document safety, screen-reader delivery, visible app
acceptance, installation and engine adoption remain separate under the
[Office evaluation](office-engine-evaluation.md) and
[broad-file goal](broad-file-support-goal.md).
