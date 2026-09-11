Combined Image PDF Candidate
============================

Status: ordered plan, writer, independent validator and isolated worker/publication
implemented with automated evidence; order UI and customer command remain pending.

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
- Always create a PDF copy. The implemented name is the first reviewed image's
  stem plus ` - Combined.pdf` for multiple images, or ` - Converted.pdf` for one.
  Use the saved Convert output folder or that first image's folder and show the
  destination in the order review. Existing-name collisions use `(2)`, `(3)`, etc.
  Overwrite originals cannot recycle any member of a combined selection.

Immutable plan/order enforcement, writing, independent validation and transactional
copy publication are implemented. The focused UI and menu command are next;
isolated execution tests do not establish existing customer UI or visual acceptance.

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
The writer itself returns candidate bytes and digests computed from original
decoded samples before PDF encoding. The worker/publication checkpoint below
connects this to access admission, a checked output reservation and final naming.

The application now uses the independent validator below and records/checks every
original through final publication. A first-source fingerprint alone cannot
approve an entire combined selection. The group-specific source leases extend
the existing transactional copy/journal workflow. A 150-DPI renderer limit
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

The subsequent checkpoints add worker/access/validation/publication below.
Still required: order dialog and direct command, wider fidelity,
resource-limit benchmarking, crash/timeout/failed-publication tests, reader
compatibility, actual visual/keyboard/screen-reader/theme/DPI review, and the
remaining [broad-file goal](broad-file-support-goal.md). This checkpoint does not
complete images-to-PDF or authorize commercial release.

Independent validation checkpoint (2026-09-10)
---------------------------------------------

The optional `ContextSuite.ImagePdfValidator.exe` uses the already acquired,
pinned qpdf 12.4.1 library in an independently authored native reader. The managed
writer and qpdf reader do not share PDF parsing/serialization code. The reader
accepts only this candidate's fixed raster-only schema: one flat ordered page
tree, one full-page image operation per page, exact allowed dictionaries, RGB or
gray ICC streams, optional gray alpha and Flate image data. It refuses extra or
unreferenced objects, actions, unexpected resources/content operations, external
streams, encryption, revision links, nonstandard filters and all qpdf warnings
or repair. This intentionally declines other valid PDFs; it is not a general
document validator, signature verifier or active-content sanitizer.

The reader decodes each image/alpha stream through a bounded hashing pipeline.
It checks exact decoded byte counts and returns SHA-256 digests for color, alpha
and ICC, plus ordered page/sample geometry. It never allocates a full inflated
image, creates a JSON/base64 copy of all pixels or renders at a chosen DPI.
The managed protocol checks those facts against the confirmed plan and the
pre-encoding original-sample digests. A success exit alone is insufficient.
See the [qpdf library](https://qpdf.readthedocs.io/en/stable/library.html) and
[stream pipeline model](https://qpdf.readthedocs.io/en/stable/design.html);
implementation uses the pinned SDK headers as its API authority.

Only a read-only seekable candidate handle crosses into the native child. The
shared launcher starts it suspended and assigns the 1 GiB kill-on-close job
before work, with a restricted handle list and no customer-path arguments.
Input is at most 128 MiB; replies are at most 655,376 bytes. Per-stream inflation
stops at the expected sample count, with the existing 16-million-pixel page and
128-million-pixel document limits. The wrapper uses a 60-second whole-validation
deadline, verifies and leases the host/runtime hashes, checks an owned snapshot
before/after parsing, checks candidate memory again and removes only its own
snapshot. This is bounded process containment, not an OS security-sandbox claim.

Build with `tools/pdf-engine/Build-ImagePdfValidator.ps1 -PreparedDirectory
'<prepared qpdf directory>'`, then add `-Validate` to `Test-ImagePdfCandidate.ps1`.
The native host builds with zero warnings/errors. Its reviewed SHA-256 is
`4BD57D54B4B6BD7C0A56170EEA26CA8DFB7080B429821D513E69829DCD91E073`;
`image-validator-build.json` records source/build hashes and the actual compiler
instance. Neither this host nor qpdf is added to normal production staging.

The fresh run passes **45 native validation checks** and all **133 writer checks**.
The full **1,628 foundation checks** include 41 new reply/expectation contracts.
Cases include reordered/duplicate pages, changed transforms/geometry, extra
actions/resources/content, inflated/wrong sample declarations, ICC/alpha changes,
external/unapproved streams, altered expectations, truncation, unreferenced
objects, source-memory mutation, runtime leases and modified-host refusal.
Test mutations rebuild xref offsets and raw-stream lengths so rejection is not
credited merely to stale fixture offsets. A low-density page succeeds at original
sample dimensions even though 150-DPI rendering would exceed the raster budget.
Pre-cancel, cancellation after snapshot creation, cleanup and long local Unicode
snapshot paths pass. This does not yet prove cancellation during a confirmed-live
native decode, forced native crash/timeout recovery or UNC/long engine paths.

Evidence: `.codex-temp/pdfium-engine/cee300f69505476e87893226200b171e/`
`image-pdf-74123da8a7c74c039f909e62c9994c71/validation/image-pdf-validation.json`,
with the companion writer `image-pdf.json`. Logs: `.codex-temp/`
`image-pdf-validation.log`, `image-pdf-validation-foundation.log`,
`image-pdf-validator-build.log` and `image-pdf-validation-production.log`.
Release stage `artifacts/production-staging/7c787dc3faa24b4f813e0dd43951b67c`
passes payload, dependency and notice checks with zero warnings/errors using
`-SkipShell`. No unrelated engine/UI suite, desktop acceptance, installation,
Explorer registration, native recycling or live commerce was run for this slice.

The next checkpoint below adds typed worker execution and all-source publication.
The focused order dialog/direct command, production engine adoption, wider
fidelity/resource/viewer acceptance, required Office-to-PDF and the complete
broad-file goal remain open.

Worker and all-source publication checkpoint (2026-09-10)
-------------------------------------------------------

Typed `images-to-pdf` requests carry the confirmed ordered plan, explicit order
review state, policy and one app-created output reservation. The worker returns
only bounded validation, ordered source hashes and output facts. Protocol checks
reject unrelated payloads, missing review, foreign output identities and source
paths used as output. Plans cap source-description text at one million characters
and validate output-folder paths so JSON stays under the existing bounded IPC
frame. The client uses a 180-second conversion deadline around the validator's
60-second native deadline. Normal staging still excludes the optional validator;
the executor declines missing-engine work before starting trial or reservation.

One normal trial/paid admission covers the combined document. Expiry does not
interrupt admitted work, and expired paid access cannot fall back to a new trial.
Every image must succeed; the action does not silently omit failed pages. A retry
recreates the whole combined document. The order dialog and direct queue retry
surface are not connected yet; the executor's retry behavior is independently
tested rather than presented as existing UI.

Before reservation, the app opens a checked read-only lease for every original
and compares its length/hash with the reviewed source facts. Those leases deny
write and rename sharing until publication or abandonment. Ordered item IDs,
paths and volume/file-ID/hash fingerprints are stored in the publication record.
The worker independently holds its own source leases through encoding, semantic
validation and writing the checked empty single-link reservation; it rechecks
every source and the written candidate digest before replying. The app verifies
all held source fingerprints and current paths again at the final publication
boundary, after the Publishing checkpoint and before the rename.

The existing atomic copy/journal path publishes one ` - Combined.pdf`, or
` - Converted.pdf` for a single image, with normal `(2)` collision numbering.
Source-byte totals include every input. No image is overwritten, renamed or
recycled, including with Overwrite originals selected. Successful publication
cleans its journal. Failure before the final rename saves no PDF; a failure
after the rename retains the completed copy and its recovery warning/record.
Abrupt exit preserves the recorded state and originals; discovery of records
does not implement automatic restoration or establish visible recovery usability.

Verification passes **56 isolated combined-PDF workflow checks**. The real worker
combines PNG/JPEG/WebP/BMP/TGA, 16-bit RGB/gray, ICC and alpha under one admission.
Checks cover collisions, trial expiry, cancellation after reservation, secondary
source changes, native refusal of nonempty/hard-linked reservations, worker death
and whole-document retry. Every original denies write/rename at the final move
boundary. Before/after-move failures retain the full ordered journal. Five actual
test-app exits at Prepared, Validated, Publishing, immediately after the move and
Committed verify original/candidate fingerprints, record rediscovery and worker
exit with its parent. They leave disposable crash evidence intentionally retained.

The full regression passes **1,851 checks**: 1,651 foundation (23 new protocol,
naming and paid-access checks) plus 200 existing real image-worker checks. Existing
PDF page publication/direct workflows pass **25 + 18** checks. The private 133
writer/45 native-validator suites from the preceding checkpoint were not rerun;
the new file adapter is exercised through the real worker. No native algorithm
changed. Audio/structural-PDF engine suites and hidden/visible UI were not rerun.

Combined workflow evidence: `.codex-temp/pdf-engine/3edb2e8361e04782a91ef8364bd3a537/`
`image-pdf-worker-caa52727b907406ba736e66c45903ceb/results/image-pdf-worker.json`,
including its `app-crashes` directories. PDF-page regression evidence ends in
`page-worker-f33d8026c33c4c818801c2deeba7db41` under the prepared PDFium directory.
Logs: `.codex-temp/image-pdf-worker.log`, `image-pdf-worker-foundation.log`,
`image-pdf-worker-regression.log`, `image-pdf-page-regression.log` and
`image-pdf-worker-final-build.log`. Fresh isolated Release stage
`artifacts/production-staging/2cdfc9bd64a242f4ae0cd4321797a4c3` builds with zero
warnings/errors and passes payload/dependency/notice checks with `-SkipShell`.
Its staged files match the tested `34a2404c14e047e4a0347141f44d37f5` payload
byte-for-byte, excluding the stage-specific inventory. No installed change,
recycling, live Polar, signing purchase or publishing ran.

Next: focused page-order review and direct Convert > PDF, integrated quiet results
and retry, then broader native interruption/resource/fidelity/reader acceptance
and engine adoption. All required Office conversions and remaining broad-file
and commercial-release gates stay open.
