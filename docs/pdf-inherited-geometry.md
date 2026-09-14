PDF Inherited Geometry And Page Scale
=====================================

Candidate 1.0.2 fixes a measured PDF-to-PNG sizing error. The previous renderer
treated a page's coordinate units as points even when its leaf dictionary declared
a different `/UserUnit`. A 72 by 36 coordinate-unit page produced 150 by 75 pixels
at 150 DPI for units 1, 2 and 4 alike. The latter two require 300 by 150 and
600 by 300 pixels. The revised native probe returns those expected dimensions.

The PDF reference's [page dictionary](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/pdfreference1.7old.pdf),
table 3.27, defines UserUnit as the size of a coordinate unit in multiples of
1/72 inch, with a default of 1. It is not designated inheritable. MediaBox,
CropBox and Rotate have separate inheritance rules. The indexed primary reference
supplied these declarations after direct retrieval exceeded the browsing tool's
size limit. No reference implementation or source prose is copied into the code.


Implementation and bounds
-------------------------

The pinned PDFium 8044 API returns cropped, rotated dimensions without applying
UserUnit. The measured behavior also agrees with the inspected upstream
[page dimension implementation](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/core/fpdfapi/page/cpdf_page.cpp).
That upstream source is supporting context, not a substitute for the actual
pinned-binary measurements.

The authored renderer now uses the already-selected qpdf 12.4.1 library to read
each leaf page dictionary from the same bounded in-memory input. qpdf resolves
indirect numeric objects; there is no byte-pattern search or new handwritten PDF
parser. Its page count must agree with PDFium, recovery is disabled and parsing
warnings cause refusal. The scale follows page-tree order, not object numbers.
No page or source is rewritten and no document actions are invoked.

Missing or null UserUnit uses 1. Finite positive numeric values up to 75,000 are
subject to the existing physical-size, dimension and pixel-area limits after
scaling. Malformed values are invalid input; excessive scales or resulting page
sizes produce the existing typed resource-limit result. Both inspection and
rendering read the actual scale, so a forged or stale planning inventory cannot
remove the native limit. Rendering fills the correspondingly sized bitmap at
the existing 150 DPI, retaining rotation, alpha, sRGB and validated copy output.

The renderer adds qpdf30.dll and its complete pinned Microsoft runtime dependency
set. The managed adapter leases and hashes those files before native execution.
Full qpdf LICENSE.txt and NOTICE.md accompany this third qpdf runtime copy.
The selected PDF payload grows from 45 to 53 files; the complete combined payload
contains 116 files. The renderer is 27,648 bytes, SHA-256
`4D58C3D9DFFCC8973391C93A0612C7DF97D516519BB726F6FEB2A29836485913`.
PDFium, qpdf and audio dependency versions remain unchanged.


Actual acceptance
-----------------

- **206 geometry/worker/publication checks pass** on candidate 1.0.2. The prior
  59-check matrix is extended with two-level inherited boxes/rotation, nearest
  ancestor and leaf overrides, explicit zero rotation, default crop, negative
  origin and crop/media intersection. Authored patterns establish complete pixel
  expectations independently of another PDF renderer.
- Scale cases include 0.5, 1, 2 and 4, rotated cropping, indirect numeric values,
  null defaults and non-inheritance. A three-page document deliberately orders
  its page objects differently from their object numbers and uses different
  scales and rotation. The scaled 16-million-pixel boundary passes; malformed
  and oversized values publish nothing. A forged bounded plan for an oversized
  scaled page is refused during rendering. Sources retain hashes/timestamps and
  the test recycler throws if called.
- **29 private raster checks and 12 comparison checks pass**. Existing form,
  annotation, alpha/profile, density, protected/malformed input, long-path and
  source-change behavior remains covered. New checks reject changed/missing
  qpdf geometry dependencies and verify active dependency leases.
- The updated isolated page-worker wrapper passes **25 workflow, 20 interruption
  and 18 direct-command checks** using a disposable evaluation payload made from
  candidate 1.0.2 and the freshly built renderer. This verifies its added runtime
  copying as well as cancellation, recovery and direct-command behavior.
- **21 payload checks pass**, including qpdf geometry dependency corruption and
  absence, complete native import closure, notices and default-release refusal.
  **2,696 foundation contracts pass**. Release builds report zero warnings/errors.

These are distinct scoped suites. No all-feature release regression, independent
PDF-renderer comparison of the new scaled fixtures, visible/keyboard, screen-reader,
theme/DPI or installed-shell acceptance is claimed. Native allocation failures,
the actual adapter deadline, other hostile/fidelity variants, redistribution and
release adoption remain open. Office-to-PDF remains a separate required feature.


Evidence and reproduction
-------------------------

The versioned combined stage is
`artifacts/production-staging/76ffc1a76c854a6896e4d5ab832f40bc`.
Its reserved 1.0.2 receipt records inventory SHA-256
`9C03FF7CA002B5C3BBBADD63A8913A6100B94F998F6C2E1E3B6147C3E736BF9B`.
The previous 1.0.1 stage and receipt are retained unchanged.

```powershell
.\tools\pdf-engine\Build-PdfiumEvaluation.ps1 -PreparedDirectory '<fresh verified PDFium directory>' -Renderer -QpdfPreparedDirectory '<verified qpdf directory>'
.\tools\pdf-engine\Test-PdfPageGeometry.ps1 -ProductionStage '<existing combined 1.0.2 stage>'
```

Native preparation: `.codex-temp/pdfium-engine/44820d5e04b34ceb81ff3d666fbdb622`.
The main geometry receipt is
`.codex-temp/pdf-page-geometry-509eec349d9841e9bab11dc17b242258/pdf-page-geometry.json`.
Private/comparison receipts are its preparation directory's
`raster-adapter-5522116a15204b91b115078a665d65ae` and
`raster-reference-0f8f13bed6fb426aab0872f9b20eec1c` directories. Wrapper results are
in `page-worker-6850fd27c0aa4488a165b9b75397056d`; the payload receipt is
`.codex-temp/pdf-payload-tests-c1337e718aac4855949abd3e7ea96896/results.json`.

Logs use `.codex-temp/pdf-userunit-` with suffixes `native-build.log`,
`native-fixed.log`, `raster.log`, `foundation-final.log`, `production.log`,
`geometry.log`, `worker.log` and `payload.log`. The initial source measurement is
`.codex-temp/pdf-userunit-discovery-284b97963b9144c0bd2820b27d3721e4/userunit-discovery.json`;
the corrected native measurement is in
`.codex-temp/pdf-userunit-discovery-051233f7ba1741f39a7ca0743a4b5ae9`.
The first test-host compile used Length on a read-only list; its failed log is
retained as `pdf-userunit-foundation.log`. The corrected run above passes.

No installation, registration, native recycling, live licensing or publishing
was performed. This checkpoint is local implementation and acceptance evidence,
not commercial release clearance.
