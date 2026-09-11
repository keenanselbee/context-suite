Combined Image PDF Candidate
============================

Status: ordered plan and private writer evaluated; customer command, production
validation and publication are not implemented for this action.

Owner decision and page-order policy
-----------------------------------

On 2026-09-10 the owner selected **one combined PDF** for several images and
required an explicit page-order policy. This supersedes the earlier experiment's
one-PDF-per-image proposal. The intended Convert > PDF behavior is:

- One selected image produces a one-page PDF without an order prompt.
- Several images open one focused **Page order** review. Start with the order
  captured in the request; do not claim that Explorer supplied its displayed
  sort order. Show a numbered list, filenames and enough path information to
  distinguish duplicate names. The top image is page 1. Provide Move up/Move down
  actions accessible by keyboard, plus Convert and Cancel/Escape.
- The reviewed order becomes immutable before admission/execution. Never
  silently sort or reorder it during encoding, retries or publication. Settings
  changes must not reset it. Cancel creates no output and starts no paid batch.
- One failed/unsupported image prevents publication of the combined document;
  never silently omit pages. A failed attempt can be retried as a whole. There
  are no partial page publications for this many-to-one action.
- Always create a PDF copy. The proposed name is the first reviewed image's
  stem plus ` - Combined.pdf` for multiple images, or ` - Converted.pdf` for one.
  Use the saved Convert output folder or that first image's folder and show the
  destination in the order review. Existing-name collisions use `(2)`, `(3)`, etc.
  Overwrite originals cannot recycle any member of a combined selection.

Only immutable plan/order enforcement is implemented in this checkpoint. The
focused UI, command, names and publication above are the next integration work,
not existing customer behavior or visual acceptance.

Fixed image representation
--------------------------

Candidate policy `images-combined-pdf-1` retains full-resolution decoded samples,
applies orientation once and places each image on a page matching its physical
dimensions. Respect valid directional density, swapping it with rotated axes.
Without density, use 96 DPI. For aspect-only density, use 96 DPI horizontally
and retain the declared pixel aspect ratio. No resizing, paper-margin insertion,
JPEG re-encoding or lossy recompression is performed. A PDF copy cannot restore
detail already lost in a JPEG or lossy WebP source.

The fixed raster-only PDF 1.7 writer uses Flate image streams, explicit ICCBased
color and an optional grayscale soft mask for alpha. It preserves 8/16-bit
samples, exact supported RGB/grayscale profiles and invisible color samples.
Unprofiled supported nonlinear RGB/gray images use explicit sRGB; unprofiled
linear samples are refused pending a reviewed color interpretation. Orientation,
ICC and physical density affect the output. Other source descriptive metadata
stays in the untouched originals; it is not copied into PDF metadata. There is no
text layer, OCR, accessibility tagging, active content, attachment or original-file
embedding. Do not market these raster pages as accessible/tagged documents.

The writer is independently authored managed code using the existing .NET
compression library. It does not enable ImageMagick's PDF coder, add a PDF writer
dependency, run reference software or introduce a general PDF parser. Its format
structure follows the image, ICCBased and soft-mask definitions in
[ISO 32000-1](https://developer.adobe.com/document-services/docs/assets/35e4369068f86065372c18787171a17e/PDF_ISO_32000-1.pdf).

Bounds and source handling
-------------------------

Current candidate inputs: supported static PNG, JPEG, WebP, BMP and TGA images.
DDS, HDR, animation and unsupported color interpretations remain excluded.
Limits: 128 MiB per source, 512 MiB across the selection, 16 million pixels per
page, 128 million pixels total, 4,096 pages, 16,384 pixels per dimension and
positive physical page dimensions from 0.01 to 14,400 points. The output stream
is capped at 128 MiB. Large-image memory/output-limit acceptance remains pending;
these bounds are refusals, not promises to complete every file at the limit.

The writer takes checked read-only, single-link source leases for **all** inputs
before decoding the first image. It rechecks exact source facts while decoding,
then every handle identity and source hash before returning bytes. Leases deny
write/delete sharing and are released on success, refusal or cancellation.
It returns candidate bytes only; there is no final naming, output reservation,
published result, trial admission or claim of validated publication yet.

Production integration must independently validate the generated PDF against
the ordered plan and retain/recheck every original through final publication.
The current publisher's first-source fingerprint alone cannot approve an entire
combined selection. Use existing transactional copy/journal behavior for the
single output after this group-specific safety work. A 150-DPI renderer limit
must not cause low-density, large physical pages to be silently shrunk; validate
under bounds suitable for their actual image dimensions.

Evidence (2026-09-10)
---------------------

`tools/pdf-engine/Test-ImagePdfCandidate.ps1` uses the independently acquired
qpdf 12.4.1 and non-V8/non-XFA PDFium 8044 evaluations. It checks prepared runtime
inventories and probe source identity and creates only disposable repository-local
fixtures/evidence. No installation, registration, native recycling or Polar call
is involved. No new native dependency is added to normal production staging.

The candidate passes **133 checks** on a combined 23-page document:

- qpdf accepts its structure without warnings/repair. Its independently decoded
  image/soft-mask streams match expected samples, precision and exact ICC bytes.
- PDFium renders all pages with the expected geometry. The separate probe is
  limited to 16 pages per call, so qpdf produces bounded subsets from the actual
  combined PDF for rendering; the combined file's hash stays unchanged.
- Sample cases include 8/16-bit RGB/gray with/without alpha, sRGB and Adobe RGB,
  unprofiled RGB, all eight independently mapped EXIF orientations, JPEG, lossless
  WebP, BMP and TGA. Rendered appearance agrees with independent ICC/sample
  conversion within four premultiplied 8-bit channel levels; this is a bounded
  generated fixture comparison, not broad viewer/color-management acceptance.
- Repeated serialization is deterministic. Changed source digest, pre-cancel,
  cancellation after acquiring all source leases, attempted writes/renames during
  work, original hashes and lease release are checked. A local Unicode path past
  MAX_PATH also succeeds. UNC and long engine paths are not established.

Evidence: `.codex-temp/pdfium-engine/cee300f69505476e87893226200b171e/`
`image-pdf-695e3acf9f504d88922edaeb3c8dc2e5/image-pdf.json`.
The foundation suite passes **1,587** checks, including 33 new order/geometry/
budget/copy-policy contracts. Logs: `.codex-temp/image-pdf-candidate.log` and
`image-pdf-foundation.log`. Earlier failed generated runs are retained: the test
initially exceeded the separate probe's 16-page limit, then attempted an excluded
solid-color image coder while preparing a cancellation fixture. The harness now
uses page subsets and resizes an existing PNG; production policies were not relaxed.

Isolated Release stage `artifacts/production-staging/40139f709ace4db582beae9042db63ef`
builds with zero warnings/errors and passes curated engine, dependency/notice and
file-allowlist checks. It used `-SkipShell`; no native shell change or installation
was tested. Existing image-worker, raster/optimization, audio and hidden UI suites
were not rerun for this unconnected writer slice; their earlier evidence retains
its original scope. No desktop or assistive-technology test ran.

Still required: production semantic validation, worker protocol/access/deadlines,
all-source publication/recovery, order dialog and direct command, wider fidelity,
resource-limit benchmarking, crash/timeout/failed-publication tests, reader
compatibility, actual visual/keyboard/screen-reader/theme/DPI review, and the
remaining [broad-file goal](broad-file-support-goal.md). This checkpoint does not
complete images-to-PDF or authorize commercial release.
