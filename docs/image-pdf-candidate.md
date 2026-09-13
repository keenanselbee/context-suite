Combined Image PDF Candidate
============================

Status: direct Convert > PDF, focused page-order review, validated worker output
and all-source copy publication implemented with automated evidence. Optional
engine adoption and visible acceptance remain pending.

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
copy publication, the focused order dialog and menu command are implemented when
the optional validator is present. Isolated execution and hidden-view tests do
not establish visible acceptance or production engine adoption.

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
is capped at 128 MiB. The later resource checkpoint covers large opaque BMP pages
and output-cap refusal. Other precision/transparency/container combinations and
whole-worker memory acceptance remain open. These bounds are refusals, not
promises to complete every file at the limit.

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

The subsequent checkpoints add worker/access/validation/publication, the order
dialog and direct command below. Still required: wider fidelity,
resource-limit benchmarking, native crash/timeout tests, reader
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

The following checkpoint adds the focused page-order review. Direct Convert > PDF,
integrated quiet results and retry, broader native interruption/resource/fidelity/
reader acceptance and engine adoption remain open. All required Office conversions
and remaining broad-file and commercial-release gates stay open.

Focused page-order review checkpoint (2026-09-10)
------------------------------------------------

`ImagePdfOrderWindow` and its view model implement a numbered, single-selection
list with filenames and full parent folders, Move up/down buttons, Alt+Up/Down
bindings and Cancel/Escape. The first image determines the proposed name and
default folder; a saved output folder still takes precedence. Existing-name
collision numbering is explained. Overlong output names block Convert with a
remedy: move another image to page 1 or cancel and shorten the filename.

The model preserves exact source facts and the captured settings, starts in
request order, and moves the selected image without losing selection. It returns
one immutable ordered confirmation, prevents double submission and further moves,
and never admits work. Access refresh preserves the order, updates the actual
Convert button and provides an activation action when blocked. Older concurrent
access replies cannot override a newer status; closing cancels access checks.
The application presenter makes License a child of the order review, refreshes
access on closing License and suppresses delayed progress while reviewing.
The orchestration helper bypasses review for one image and disposes cancelled
multi-image reviews. The direct dispatch/combined-result retry path does not call
this helper yet; no new customer menu command is enabled by this checkpoint.

The list virtualizes up to 4,096 rows and has a bounded height. The remaining
content scrolls at the minimum window size while status and action buttons stay
outside the scroll area. Initial keyboard focus targets the first page. Hidden
tests verify selection movement, accessible row names, keyboard bindings, actual
button enablement, long filenames, last-page selection and minimum-size bounds.
They caught and fixed a missing command notification that left Move down visibly
enabled at the last page. These are automated WPF properties/layout checks, not
proof of actual focus, key delivery, screen-reader announcements, theme contrast,
other DPI levels or visual usability.

Fresh evidence: **1,669 foundation checks** (18 new order/model/orchestration
checks), **102 hidden-view checks** (16 new including window instantiation/live
text), and **10 isolated licensing-harness checks**. Release staging
`artifacts/production-staging/04a6c35e61904d87b21b19f93eb4003d` builds with zero
warnings/errors and passes curated-engine, dependency, notice and allowlist
verification using `-SkipShell`. Logs are `.codex-temp/image-pdf-order-foundation.log`,
`image-pdf-order-views.log`, `image-pdf-order-license.log` and
`image-pdf-order-staging-build.log`. The earlier worker/native suites were not
rerun for this UI-only slice. Optional native engines remain excluded from normal
staging. No app window, installer, registration, recycling or live provider ran.

Build deviation: the initial build omitted `-StagingId` and refreshed the ignored
development payload at `artifacts/production/Release`; this was reported to the
owner. It did not run installation or registration commands. The subsequent build
used the fresh isolated staging path above. Do not treat the initial build as an
isolated payload or claim the development payload remained unchanged.

The next checkpoint below connects Convert > PDF to this review and the worker.
Required Office transformations and the full broad-file/release gates remain open.

Direct combined-PDF command checkpoint (2026-09-10)
-------------------------------------------------

The native Convert submenu and host/request validation now accept **PDF** as a
distinct target. Existing action positions and identities are retained. The
shell only hands off the selection; it never loads an engine. The app declines
this action before probing/admission when the optional validator is absent.
Supported static images form one job and one result row, with every original
listed in details. Several images invoke the focused order review; one image
converts directly. Every source must probe and fit the per-page/aggregate bounds.
An unsupported, unreadable or duplicate member prevents the entire document;
no selected image is silently omitted. Aggregate input budgets are checked during
preflight, and the session queue counts all selected images despite combined rows.

One ordinary paid/trial admission covers execution. Success follows the existing
quiet-completion policy; failure remains actionable. The result shows the reviewed
page order and aggregate source bytes; the output name follows the reviewed first
image even when it differs from the request's original first path. All outputs
are copies, including with Overwrite originals selected.

Retry recreates the entire failed/cancelled document, skips completed output and
retains reviewed order without prompting again. It uses current Convert output
settings, as other direct retries do. A cancelled review without confirmation
must still be reviewed on retry. Every source is re-probed against its reviewed
hash/length before a new admission; changed input requires a new Convert > PDF
command. Separate selections remain separate retry jobs even if they overlap or
start with the same image. Repeated retry clicks queue one attempt per document.
All selected paths must still exist before a retry can be queued.

Fresh isolated workflow evidence passes **20 direct image-PDF checks** and the
existing **56 worker/publication/crash checks**, with real image/PDF engines and
generated disposable fixtures. Cases include reviewed reverse order, one row and
quiet completion, direct single-image output, publication failure, current-folder
retry, cancelled review, changed secondary input, overlapping groups, duplicates,
unsupported members, expired/reactivated admission and missing-engine refusal.
All original hashes remain unchanged. Evidence is under `.codex-temp/pdf-engine/`
`3edb2e8361e04782a91ef8364bd3a537/image-pdf-worker-5b363597ac1644eea3c0c9c2a4278b36`,
including `direct/image-pdf-direct.json` and `results/app-crashes`.

The shared regression passes **1,869 foundation/image-worker checks** (1,669
foundation plus 200 existing real image-worker checks). Existing PDF page
publication and direct workflows pass **25 + 18 checks**, with evidence under
the prepared PDFium directory at `page-worker-9b01087f069c407993c6bf84c8338a5f`.
The **10 isolated licensing-harness checks** also pass. Logs are
`.codex-temp/image-pdf-direct-regression.log`, `image-pdf-direct-page-regression.log`
and `image-pdf-direct-license.log`. Native writer/validator algorithms and audio
engines were not changed; their separate suites were not rerun in this slice.

The full isolated Release stage is
`artifacts/production-staging/ce26f4b0eb00464ba9c49f2782227db4`, built with fresh
native shell/host output and zero warnings/errors. Curated payload, dependency,
notice and allowlist checks pass. Native shell COM/host contracts pass for the PDF
action and full selection handoff; no registration is needed for these tests.
Its files match the workflow-tested `9142d42355b94bb48bf7763ff4f48900` stage except
the inventory and shell DLL (updated PDF tooltip); native contracts use the final
shell DLL. Logs: `.codex-temp/image-pdf-direct-workflow.log`,
`image-pdf-direct-final-build.log`, `image-pdf-direct-shell.log` and
`image-pdf-direct-views.log`. The **102 hidden-view checks** pass; actual desktop,
keyboard focus/input, screen-reader, theme/DPI and installed acceptance remain
unverified for this command. Engine adoption, wider native interruption/resource/
reader/fidelity acceptance, Office-to-PDF and all remaining broad-file/release
requirements remain open. No install, registration, recycling or live provider ran.


Large-image and output-budget evidence (2026-09-12)
--------------------------------------------------

Five resource cases now exercise the real image adapter, managed PDF writer and
pinned independent validator. The fixtures are authored top-down, 24-bit BMPs
with deterministic noise. An RGB digest is calculated while generating each
source, independently of image decoding and PDF serialization. Successful output
must match that digest through the writer's sample inventory and the independent
validator's decoded PDF streams.

| Case | Observed result | Elapsed seconds |
| --- | --- | --- |
| 4,000 x 4,000 pixels, the 16-million-pixel page limit | Exact RGB samples; validated 48,018,667-byte PDF | 3.05 |
| 16,384 x 1 pixels, the width limit | Exact RGB samples; validated 53,180-byte PDF | 0.17 |
| Three distinct 4,000 x 4,000 source files | Writer refuses the 128 MiB output cap; no candidate returned | 4.66 |
| 4,001 x 4,000 pixels | Plan refuses the per-page pixel budget | 0.90 |
| 32 x 24 pixels after both refusals | Same adapter and validator produce a valid 6,306-byte PDF | 0.58 |

Every original retains its SHA-256 and write timestamp. Exclusive read access
after each case proves the source leases were released. The independent
validator leaves no owned snapshots, and image scratch contains only its intended
configuration directory. All created PDF files remain test artifacts in repository
scratch; this suite does not invoke application publication or native recycling.

These are single-run timings, including source checks, not percentile benchmarks.
The host's cumulative peak working set reaches 857,714,688 bytes (about 818 MiB).
It includes preceding cases and excludes native validator children; it is not a
per-case or whole-worker memory ceiling. This result does not establish every
large alpha/16-bit/profile/container combination, all aggregate input limits,
native allocation failure, deadlines, publication recovery or visible acceptance.
No production limits or implementation changed.

Reproduce with the parent-owned wrapper:

```powershell
.\tools\pdf-engine\Test-ImagePdfResources.ps1 -ProductionStage '<isolated stage with the PDF candidate>'
```

The wrapper verifies the stage's complete PDF payload before and after the run,
builds the private contract host and creates fresh repository evidence. The tested
stage is `artifacts/production-staging/4921457d351a4daf85270a7f6672b542`.
Final results are
`.codex-temp/image-pdf-resources-0e87f0b918544e2c85330372e4888a4b/image-pdf-resources.json`,
with `.codex-temp/image-pdf-resources-final.log` and recorded exit code 0.
The initial host build has zero warnings/errors; its log is
`.codex-temp/image-pdf-resources-build.log`. Earlier passing evidence at
`.codex-temp/image-pdf-resources-0a12d8180c5348278506820ddf8f6280` predates the
explicit image-scratch cleanup assertion and the reproducible wrapper.


Packaged resource workflow and limit guidance (2026-09-12)
----------------------------------------------------------

The [worker resource wrapper](../tools/pdf-engine/Test-ImagePdfResourceWorker.ps1)
now uses those generated fixtures through the actual staged worker and application
publisher. The 16-million-pixel page publishes a named copy byte-identical to the
independently sample-validated writer output. Three noisy pages exceed the output
cap without publishing a partial document. A small conversion then succeeds in
the same worker. All originals retain their hashes and timestamps; exclusive
reads verify source-lease release. Reservations and journals are removed after
each workflow, and disposing the worker removes its scratch and ends the process.
Overwrite preference remains unable to recycle any original.

This exposed a generic failure message at the output limit. The writer now emits
the existing typed `ResourceLimit` category instead of reporting unsupported input.
The executor gives a specific next action: "PDF conversion reached a processing
limit. Select fewer or smaller images and try again. All originals were kept."
No raw engine diagnostic crosses IPC, no new protocol field is added, and the
128 MiB cap and output validation remain unchanged. The direct writer resource
test also requires the typed limit failure.

Fresh combined stage
`artifacts/production-staging/a27c34ab662a44ee9ece421d94b49c19` includes this change
and the native shell build. Build output reports zero warnings/errors; engine,
dependency, notice, file-allowlist and inventory checks pass. The final three
workflows pass at
`.codex-temp/image-pdf-resource-worker-1db220c2df48417b924262b290cc7b8b/image-pdf-resource-worker.json`,
with log `.codex-temp/image-pdf-resource-worker-final.log` and recorded exit code 0.
The production build log is `.codex-temp/image-pdf-resource-production.log`.
The typed writer/validator regression passes all five cases at
`.codex-temp/image-pdf-resources-4bafa1d0a2974acdb09278fb2dc42807`, with log
`.codex-temp/image-pdf-resources-typed.log` and exit code 0. The foundation run
initially exposed the [rejected test-oplock cleanup bug](analyze-io-cancellation.md).
After that test-only fix, all 2,391 current-worktree foundation contracts pass;
separate trial-policy edits in that worktree are not part of the staged PDF
payload or this PDF checkpoint. Broader image/audio and hidden-view suites were
not rerun for the resource error-category/message change.

One live validator was observed by exact executable path and worker parent PID.
The worker's cumulative peak working set was 707,121,152 bytes; the observed
validator peak was 104,255,488 bytes. Concurrent 20 ms sampling reached
690,421,760 bytes. The concurrent sample is not the sum of per-process high-water
marks and can miss transient peaks or short-lived children. The application host
is excluded. These are measurements for these opaque BMP workflows, not a memory
ceiling, native-allocation-failure test or broad input acceptance.

```powershell
.\tools\pdf-engine\Test-ImagePdfResourceWorker.ps1 -ProductionStage '<isolated combined stage>' -FixtureDirectory '<generated image-pdf-resources directory>'
```

The wrapper checks the full staged file inventory before and after execution.
The earlier baseline at
`.codex-temp/image-pdf-resource-worker-e269c89836614374b2e077c87a65c5f0` passes safe
publication/refusal/reuse on the previous stage, with the old generic wording;
it is not evidence for the new message. The timed results do not establish
visible status delivery, keyboard, screen-reader, theme/DPI or installed behavior.
