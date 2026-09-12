Document Support Design
=======================

Status: bounded package analysis and optional direct PDF optimization, PDF-to-PNG and combined image-to-PDF implemented with automated evidence; engine adoption, Office transformations and launch acceptance pending

Boundary
--------

[Decision 0019](decisions/0019-broad-file-analysis-and-media-expansion.md) adds
PDF and Office/OpenDocument identification and read-only analysis to the next
implementation goal. On 2026-09-09 the owner selected PDF tools plus
Word/Excel/PowerPoint-to-PDF for launch. Analysis alone does not satisfy this scope.

Required analysis
-----------------

Identify PDF, OOXML Word/Excel/PowerPoint, OpenDocument text/spreadsheet/presentation
and common legacy Office families using appropriate signature/container evidence.
ZIP and OLE alone do not establish an application-specific format. Distinguish
catalog descriptions from document properties that were actually parsed.

Report basic file facts and typical uses even when detailed parsing is unavailable.
For PDF, aim to expose available version, page count and encryption state. For
Office/OpenDocument, expose safely available package properties and content family.
Do not guess page counts for reflowable documents, confuse sheet/slide counts with
pages, bypass encryption, execute active content, resolve external references or
require installed Office simply to explain a file.

The [Word revision declaration reader](word-revision-analysis.md) adds scoped
insertion/deletion/move-marker counts from the main XML without additional reads.
It reports stored declarations independently of display settings; zero is not a
whole-document revision scan or conversion-admission result.

The [PowerPoint visibility reader](powerpoint-slide-analysis.md) adds visible,
hidden and default-setting counts from referenced slide parts within shared
limits. Unsupported or incomplete scans leave those counts unavailable while
retaining declared totals and identity. Custom shows, notes and rendered PDF
page counts remain outside this inspection.


Workbook setting declarations (2026-09-11)
------------------------------------------

The [workbook settings reader](workbook-settings-analysis.md) adds optional typed
date-base and calculation declarations from the already inspected main XML.
Omissions remain distinct from false/default values. Ambiguous settings leave
their group unavailable while retaining basic identity. No cells are evaluated,
and these declarations do not establish cache freshness or rendering fidelity.


External relationship analysis (2026-09-11)
------------------------------------------

Analyzer `relationships-1` now counts declared external links in recognized
OOXML Word/Excel/PowerPoint packages. It reads package-root and part-level
relationship files, including unreferenced relationship files, under the existing
ZIP/XML/read/deadline limits. Both Strict and Transitional document families use
this inspection. An external target may be absolute or relative; the declared
`TargetMode` determines the count, not whether a URL looks remote. Microsoft's
[package relationship documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.packaging.package.createrelationship?view=net-10.0)
describes this distinction.

Analyze reports the count and the number of relationship files inspected. It
does not open targets, resolve URLs, extract payloads or run an engine. The shown
scope explicitly excludes document fields and embedded-content scanning. A zero
count therefore means zero external declarations in the inspected relationship
files, not zero possible external activity or proof that a document is safe.
No target address is copied into the displayed facts. ODF and legacy documents
do not receive a zero count for a scan that was not performed.

Malformed, duplicate-ID, unsupported or over-budget relationship files leave the
link count unavailable and retain the already identified document family and
basic facts. Partial counts are not presented as complete. Cancellation still
propagates, including after family identification during an optional-part read.
This inspection is not complete relationship/schema validation or authority to
admit an Office conversion. Required rendering isolation remains separate.

Twenty-two added contracts cover six family/namespace combinations, relative and
absolute external declarations, root/orphan relationship files, zero-count scope,
partial failures, DTD refusal, part/aggregate budgets, ambiguous case, ODF scope
and mid-scan cancellation. The real application file-reader check now reports a
generated external file reference while preserving the source bytes/timestamp.
All 1,790 foundation contracts pass; the final log is
`.codex-temp/document-relationship-foundation-cancel.log`.

Fresh Release staging is
`artifacts/production-staging/8b3f443a25974f8aba523bea131182dc`. It builds with zero
warnings/errors and passes engine/notice/dependency/file-allowlist checks. This
run used `-SkipShell`; it does not establish new native shell, installation,
visible/keyboard, theme/DPI or screen-reader acceptance. No Office engine was
added. The build log is
`.codex-temp/document-relationship-production-8b3f443a25974f8aba523bea131182dc.log`.

The first attempted redirected foundation command stopped when PowerShell
treated an expected malformed-client stderr diagnostic as an error. It was not
counted as a pass. Subsequent runs used process-level log capture; the complete
final suite passes without suppressing the diagnostic or changing the tests.

Implemented package analysis (2026-09-09)
-----------------------------------------

Analyzer version `package-1` reads the ZIP directory and selected declaration
parts under the application's existing read-only lease and five-second content
deadline. It skips unrelated entry payloads, even in large files. There is no
extraction, renderer, engine dependency, reference resolution or paid admission.

- OOXML checks the root office-document relationship, an explicit main-part
  content-type override and the matching Word, Excel or PowerPoint XML root.
  Transitional and Strict namespaces are recognized. Template and macro-enabled
  type declarations map to the same catalog families; a macro-enabled type is
  not evidence that a VBA project exists or that a file is safe. Report declared
  sheet/slide-list counts; their target parts are not validated. Word page count
  remains unavailable without rendering.
- OpenDocument checks `mimetype`, the manifest root/content entries and the
  matching text, spreadsheet or presentation body. Report direct sheet/slide
  elements. If the manifest declares encrypted main content, identify only from
  agreeing package declarations and leave content facts unavailable.
- Identity stays **likely**. Unsupported, conflicting, malformed and over-budget
  packages retain useful ZIP/basic facts. ZIP or OLE alone never establishes an
  Office family. Bounded [legacy Office analysis](legacy-document-analysis.md)
  now checks root stream names and binary declarations separately. Optional PDF probing
  now supplies structural facts under the separate integration described below.

Limits: ZIP32, one disk, at most 4,096 entries and a 1 MiB directory; stored or
Deflate selected parts of at most 256 KiB each in both compressed and expanded
form; at most 1 MiB expanded and 4 MiB additional reads per analysis. Check local
and central declarations and CRC32 for selected parts. XML has a 256 KiB character
budget, depth 32, prohibited DTDs and no resolver. Duplicate/case-ambiguous entry
names are declined. ZIP64, unsupported compression, ambiguous declarations and
noncanonical relationship targets retain fallback; these are implementation
limits, not assertions that such documents are invalid. This is not full ZIP,
document schema, encryption, signature or active-content validation.

The 47 independently authored document contracts cover six families, Strict and
Transitional OOXML, renamed files, macro/encryption declarations, mismatches,
duplicates, DTD/depth/size limits, corrupted CRCs, excess inflation, cancellation,
100 deterministic mutations, skipping an unrelated 8 MiB member and real-reader
source preservation. The complete foundation run passes 991 contracts. These
minimal fixtures are not rendered Office documents or conversion acceptance.
Broader application-produced documents, templates, signed packages, ZIP variants,
partial main-part analysis and visual acceptance remain to be tested.

The isolated Release build at
`artifacts/production-staging/6d4dba1a92b74eec9e12e50a68fbb4f7` includes this parser
and passes payload/notice/dependency checks with zero build warnings or errors.
It used `-SkipShell`; no Explorer registration, installer lifecycle or new visible
acceptance is implied. No audio engine was added to this normal payload.

Implementation research uses the
[Open XML package model](https://learn.microsoft.com/en-us/office/open-xml/general/how-to-create-a-package),
[content types and relationships](https://learn.microsoft.com/en-us/office/open-xml/about-the-open-xml-sdk),
[Word macro types](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-offmacro2/ec97c690-7a7e-422f-8af0-d5baa7c9e385),
[PowerPoint macro types](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-offmacro2/18138244-588d-4c8f-bd82-1b9a4aa2af38),
[OpenDocument package specification](https://docs.oasis-open.org/office/OpenDocument/v1.3/OpenDocument-v1.3-part2-packages.html)
and [ZIP specification](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT).
Implementation and fixtures are independently authored; no reference binaries or
document contents are copied.

Selected launch transformations
-------------------------------

An explicit [isolated production packaging option](pdf-production-payload.md)
now assembles the pinned qpdf/PDFium runtimes, authored hosts and notices for the
three implemented PDF actions below. Default release adoption, broader fidelity
and required Office transformations remain pending.

- Images to one combined PDF, with a focused page-order review for multiple
  images. The [ordered writer and worker](image-pdf-candidate.md) now have isolated
  validated publication and all-source recovery evidence. The direct command
  invokes the order dialog for several images and publishes one PDF result;
  retries retain the reviewed order with current Convert output settings.
- PDF pages to images with fixed reviewed resolution and predictable naming.
- PDF optimization with declared preservation of text/vector/document features.
- Word, Excel and PowerPoint to PDF with evaluated font/layout fidelity and dependencies.

OpenDocument analysis remains required; OpenDocument-to-PDF has not been selected.
Excel PDF export also requires an explicit calculation policy. The
[formula-cache experiment](excel-calculation-evaluation.md) shows that the
candidate's default can retain stale saved results even in an automatic workbook.
Do not infer calculation fidelity from correct-cache fixtures or successful export.
The [date-system experiment](excel-date-system-evaluation.md) also finds incorrect
early-1900 date displays despite successful export. Passing modern-date and
1904-system controls rule out applying an unconditional numeric/date adjustment.
Exact legacy/modern input variants, presets and engine choices remain to be
evaluated. No rendering engine is adopted or installed by this scope decision.

The [Word revision experiment](word-revision-evaluation.md) shows that deleted
draft text can enter a PDF when revisions are displayed or the display setting
is omitted. Explicitly hidden revisions exclude that marker in the tested export
while retaining it in the source. Required Word conversion needs an explicit
revision policy; successful export does not mean a clean accepted-text document.

The [PowerPoint slide/notes experiment](powerpoint-slide-evaluation.md) tests
saved slide order, hidden first/last slides and speaker-note exclusion, with a
positive notes-export control. Its observed results inform the export policy;
simple slide text does not establish complete presentation fidelity.

The [legacy PDF experiment](office-engine-evaluation.md) now covers passive
generated DOC/XLS/PPT roundtrips through the candidate. Page/text assertions pass;
Excel/PowerPoint rendering differs from their modern baselines. This is evidence
to resolve before adopting a fidelity policy, not an accepted legacy input matrix.

The [engine comparison and experiment matrix](media-engine-evaluation.md) records
candidate capabilities, proposed fixed policies and independent acceptance oracles.
The first [qpdf experiment](pdf-engine-evaluation.md) passes 13 narrow generated-file
checks and records a signature-summary omission that constrains rewrite admission.
Optional private adapter/worker/Analyze integration adds pages, encryption and
reported document inventories using complete snapshots up to 16 MiB. It passes
16 private and 11 app-to-worker checks; the engine remains evaluation-only.
Independent rendering/fidelity and the other engine experiments remain pending.

Structural PDF optimization candidate (2026-09-10)
--------------------------------------------------

The private `PdfProbeAdapter.OptimizeAsync` now evaluates fixed policy
`pdf-structural-1` on owned snapshots. It recompresses supported streams at level 9
without image downsampling, rasterization or a format-version increase. Existing
object streams and unreferenced objects/resources are preserved. A result must
be strictly smaller; otherwise the source bytes are returned unchanged.

The public `PdfRewriteInventory` reads complete qpdf JSON v2. It inspects all
object dictionaries before decoding streams, refusing encryption, signature-related
information, external streams and uninspected revision history. The field summary
is never an admission authority. Complete inline-stream inventories then compare
the rooted document graph, ordinary unreferenced components, original document
identifier when present and PDF version. Object numbering and reviewed physical
storage fields may change. Unknown extra storage metadata remains compared.

The file-based worker and application executor now use this candidate through
normal trial/paid admission and transactional copy publication. A source read
handle stays open while the worker validates snapshots and fills the app's
checked empty reservation; final naming remains application-owned. PDF work
always requests copies even when the saved Optimize preference selects overwrite.
The publisher discards unchanged results without creating duplicates. Summary
facts can exclude known protected files, but cannot approve preservation; the
complete inventory remains the execution admission authority.

Direct Auto/Lossless dispatch now includes PDFs alongside PNG/FLAC in one
admitted batch with combined progress, cancellation and quiet completion. The
PDF result remains copy-only. Balanced/Smallest requests explain the PNG-only
policy before admission; missing PDF engines report unavailable before launch.
Known encryption is excluded during planning, while complete-object signature
checks remain authoritative during execution. Activation retries keep the menu
action/files and capture current Settings. Normal staging still excludes qpdf;
this dispatch is verified with the isolated evaluation payload.

The [dated evidence](pdf-engine-evaluation.md)
includes 32 rewrite contracts, 19 new plan/access contracts, 27 private adapter
checks, 17 real optimization workflow checks, 19 direct mixed-family checks and independent PDFium comparison
of two generated published pages. Another 51 generated checks now cover native
cancellation/client timeout/worker death, publication move errors, application
exit at five publication checkpoints and long local paths. Restart discovers and
preserves recovery records; these checks do not implement automatic restoration
or establish visible recovery usability. Larger document/feature coverage,
UNC/long engine-install paths and production engine adoption remain required.
Incremental/linearized inputs with revision links need a history
handler; declining them is a current limitation, not the final launch scope.
The later [combined image PDF](image-pdf-candidate.md) and direct PDF-to-PNG
checkpoints below implement those optional candidates. Word/Excel/PowerPoint-to-PDF
remains required and unimplemented; none of these candidates has launch clearance.

For each selected action, document source/target variants, rendering requirements,
metadata/accessibility/signature consequences, cancellation, resource limits,
engine packaging and validation. PDF rasterization is not a substitute for
document-preserving optimization. Reject or explain unsupported signed/encrypted
inputs; do not silently remove protections or alter signed content under a
preservation claim.

Output and acceptance
---------------------

Reuse application-owned validated publication and copies by default. Multi-page
and many-to-one actions need explicit batch grouping, page ordering, collision
handling, partial-result and recovery policies. Do not extend overwrite to these
actions until its meaning and safety are verified. A forced copy is preferable
to exposing an unsafe override, but must be recorded in the capability matrix.

Test text and scanned PDFs, vectors, fonts, rotation, large pages, multiple pages,
encryption/signatures, malformed objects, compressed content, missing fonts,
macros/external links and cancellation. Compare rendered appearance for rendering
actions and preserved structure for structural operations. Detail current evidence
in the [broad file support goal](broad-file-support-goal.md); do not inherit image
or reference-program acceptance results.

PDF page renderer candidate (2026-09-10)
--------------------------------------

The optional private renderer returns page geometry and one PNG page at a time.
The first checkpoint covered the adapter; worker/publication follow below.
Policy `pdf-pages-png-150-1` selects 150 DPI, straight BGRA8 rendering on transparent
backing, visible annotations and stored form appearances. PNG encoding retains
exact rendered pixels and an explicit sRGB profile, and records physical density
within one pixel per metre of 150 DPI. It excludes generated date/time metadata.
This raster copy cannot carry document interactivity, attachments or verifiable
signatures. Originals must remain intact. Signed documents may supply visual
copies; the candidate neither validates nor preserves their signatures. Protected
PDFs, including empty-user-password protection, and XFA forms are refused.

The native host uses independently acquired, pinned PDFium 8044 with V8/XFA
absent. It receives a read-only seekable source handle, fixed operation arguments
and a bounded binary stdout protocol. No customer paths or output paths are sent
to the native host. No document actions, JavaScript platform, navigation, file or
network callbacks are invoked. The shared media launcher creates the process
suspended, restricts inherited handles and assigns the existing 1 GiB kill-on-close
job before resuming. This is process containment, not a claim that native parsing
is a security sandbox. Each native call has a 60-second deadline.

Input is limited to 128 MiB, 4,096 pages, 16 million pixels per page and 16,384
pixels per dimension. All page geometry is inspected before rendering: positive
finite dimensions at most 14,400 points, rotation 0–3 and exact rounded-up 150-DPI
pixel geometry. The managed protocol rejects malformed, truncated, reordered,
over-budget or inconsistent replies. The adapter checks source length/hash before
work and after rendering, then independently decodes each encoded PNG to verify
pixels and color profile. The qpdf optimizer retains its separate 16 MiB limit.

The native host builds with zero warnings/errors. Verification passes 1,535 public
foundation contracts (28 new protocol checks), 24 private raster checks, 12 fresh
separate PDFium evaluation checks, 292 audio checks after sharing the launcher,
and 27 existing structural PDF adapter checks. Raster checks include both authored
pages, alpha, form appearances through reference pixels, physical density,
protected/malformed/oversized source refusal, changed-source refusal, pinned-engine
leases/tampering, pre-cancellation and local Unicode source/scratch paths beyond
MAX_PATH. Signature-field and JavaScript canaries render visual copies; these are
not proof of signature validity or a complete active-content/security audit.

Evidence under `.codex-temp/pdfium-engine/cee300f69505476e87893226200b171e/`:
`renderer-build.json`, `raster-reference-af4378ca418c48a8aedde8589da1c2e1`, and
`raster-adapter-1da5cde93c7a43da9c92f27e5074225a`. Audio evidence ends in
`adapter-dd48691434034805b2a28f59b5eb0ea7`; structural PDF evidence ends in
`adapter-fb836ad39684450e859a0ebba3e4bd59` under their existing prepared engines.
Early failed raster runs are retained: explicit ICC retention was fixed; the
physical-density test now accepts the integer PNG unit conversion. Normal Release
stage `artifacts/production-staging/a2469abd2ce342c6aae6135f0983c8b7` builds without
warnings/errors and passes payload checks using `-SkipShell`. It excludes PDFium.

The subsequent checkpoints add batch budgets, page publication, direct-command
integration and combined images-to-PDF. Required Office-to-PDF remains unimplemented.
Wider fonts/ICC/CMYK/rotation/scan/form/XFA
and damaged-document fidelity, UNC paths,
dependency redistribution/runtime inventory and production engine adoption remain
open. Existing image/worker/UI suites were not rerun for this adapter-only slice;
manual, screen-reader, theme, DPI and installer acceptance are not implied.

PDF page worker and publication (2026-09-10)
------------------------------------------

Typed `pdf-raster-probe` and `pdf-render-page` commands connect the optional
renderer to the existing sequential worker. Replies contain bounded source/page
facts and output validation, never PNG byte arrays. Request validation rejects
unrelated or contradictory payloads. Page output IDs differ from document IDs;
only an existing empty single-link `.context-suite-{outputId}.tmp` reservation is
writable. The private adapter holds the original read lease through rendering and
copying, validates the PNG, and checks the reserved output digest after flushing.
The parent client gives inspection/rendering a 120-second overall deadline around
the adapter's 60-second native deadline.

The confirmed conversion plan bounds the whole selection to 4,096 pages and
512 million pixels, validates every inspected page and takes an immutable Convert
settings snapshot. It always creates copies, including with Overwrite originals
selected. One ordinary trial/paid admission covers the entire selected-file batch;
expiry does not interrupt admitted work. Paid expiry cannot start a new trial.
Execution adds a 2 GiB cumulative output limit and keeps only one page reservation
active at a time. Page names are `Document - Page 001.png`, `Page 002.png`, etc.;
existing-name collisions append `(2)`, `(3)`, and so on separately from page numbers.

Publication is atomic per page, not per document. Each page uses the existing
validated rename and recovery journal. Completed copies remain if a later page
fails or cancellation arrives. Failure stops remaining pages of that document;
later selected documents continue. Results retain document identity, page index,
individual outcome and each committed output path. No automatic rollback removes
completed copies, and the source PDF is never overwritten or recycled. The direct
checkpoint below aggregates these results and exposes partial completion.
Retained journal evidence is not automatic
recovery or visible recovery acceptance.

Regression testing exposed a scratch-cleanup race: a qpdf child could still hold
its snapshot briefly after the parent worker exited. Worker shutdown now retries
removal of that one owned directory for at most two seconds, rechecking ancestry
and refusing links on every attempt. Persistent failure retains evidence. A
controlled lock-release contract verifies that shutdown waits and completes cleanup.
The failed run is retained under qpdf evaluation `worker-452b9a19d86f4cca80b370533d5a004b`;
its original and final outputs were safe, but an owned scratch snapshot remained.

Verification passes 1,752 combined foundation/image-worker checks: 1,552 public
contracts (including page plan/protocol/naming and paid access) plus 200 real-worker
checks, including the new delayed cleanup contract and existing PNG/DDS/image
interruption coverage. The final page workflow passes 25 checks, and the PDF
regression passes 98 checks: 11 Analyze, 17 optimization/publication, 51 native
failure/recovery and 19 direct mixed PNG/FLAC/PDF Optimize. The 24 private raster
and 292 private audio checks from the preceding adapter checkpoint were not rerun;
the new worker workflow exercises the added reserved-output adapter method.

Page evidence: `.codex-temp/pdfium-engine/cee300f69505476e87893226200b171e/`
`page-worker-002af046e66b422895e6073819aff509/results/pdf-page-workflow.json`, with
separate before/after-move recovery directories. PDF regression evidence:
`.codex-temp/pdf-engine/3edb2e8361e04782a91ef8364bd3a537/`
`worker-1be40cb521e14414880d678d8bac259f`. The combined image-worker log is
`.codex-temp/pdf-page-integration.log`; final page/PDF logs are
`.codex-temp/pdf-page-workflow-final.log` and `pdf-page-regression-final.log`.
Fresh isolated Release stage `artifacts/production-staging/7c3aa9f749f040d0a82fcba1035245ec`
has zero build warnings/errors and passes normal payload checks with `-SkipShell`.
Evaluation wrappers add engines only to copies in scratch; normal packaging still
excludes them. No installation, Explorer registration, recycling or live Polar ran.

The subsequent direct checkpoint below implements per-document summaries,
partial-output reporting and retry behavior. The later
[renderer interruption matrix](pdf-page-interruption.md) covers three injected
faults, and [combined image PDF](image-pdf-candidate.md) has its own implementation.
Broader PDF fidelity, required Office conversion, production engine adoption,
visible/accessibility acceptance and independent release gates remain open.

Direct PDF-to-PNG and partial retry (2026-09-10)
----------------------------------------------

The existing Convert > PNG action now handles PDF pages alongside supported image
inputs. Content identification runs before dispatch; renamed PDFs require .pdf,
other PDF targets explain PNG, and a missing renderer declines before worker or
trial startup. Routine PDF-only conversion opens no image planner. Mixed valid
image/PDF plans use one immutable settings snapshot and one trial/paid admission;
images execute first, then PDF pages, while displayed rows retain selection order.
Necessary image/DDS prompts remain available, and cancellation before confirmation
cancels the coordinated selection.

Each PDF uses one result row showing saved/total pages, with every output and
recovery location in secondary details. Partial cancellation/failure suppresses
success sound, opens problem details and never says no files changed when pages
were saved. A retry carries forward committed page receipts, rechecks the PDF's
source hash, skips completed pages and applies current settings only to unfinished
pages. If the source changed, the app keeps earlier copies and explains that a new
Convert command is required. It blocks repeated stale-page resume. Earlier recovery
record locations survive retry in details; records are retained, not automatically
cleaned or restored. Retry state lasts only for the current results session.

The native menu keeps the PNG action identity and adds a tooltip explaining PDF
page copies. There is no new planner, menu branch, registration or installation.
The normal payload still has no PDFium renderer; the direct path is exercised only
with the verified optional engine added to isolated worker copies.

Verification passes 1,554 foundation, 25 existing page-publication and 18 new direct
PDF-page checks. Direct checks cover mixed order/admission across trial expiry,
quiet success, per-document output details, partial cancellation, retry double-click,
changed output folder, unchanged completed output hashes, source-change refusal,
activation-equivalent access recovery, before-move failure/recovery-location retry,
missing engines, other targets, renamed/protected PDFs and original preservation.
Existing 86 hidden view, 10 simulated license-harness and 13 direct image workflow
checks pass, including retained DDS decisions. Native shell contracts pass with a
separate isolated build; no package was registered. The preceding 200 image-worker,
98 PDF regression and private engine suites were not rerun for this direct-UI slice.

Final worker/direct evidence is under
`.codex-temp/pdfium-engine/cee300f69505476e87893226200b171e/`
`page-worker-883a259d8bf445009a3ed8e395330a92`. Logs are
`.codex-temp/pdf-page-direct-final.log`, `pdf-page-direct-foundation.log`,
`pdf-page-direct-views.log`, `pdf-page-image-direct.log` and `pdf-page-direct-native.log`.
The generated initial direct test run is retained; its changed-source test handler
was corrected to cancel once rather than cancelling the retry before inspection.
Full isolated Release stage
`artifacts/production-staging/a768a20778d843ae90aa8090e5c4c8e7` builds successfully
and passes curated engine, dependency/notice and file allowlist checks, including
fresh native shell/host output. Final worker/direct workflows use that same stage.
Two additional foundation contracts verify that a later uncommitted failure cannot
hide an earlier saved page and that carried receipts combine with a completed retry.
No live Polar,
reference executable, signing purchase, native recycling or installed change ran.

Actual visible layout/keyboard review of the new document results is still needed;
hidden view checks do not prove desktop or screen-reader delivery, themes or DPI.
The later [combined image-PDF checkpoint](image-pdf-candidate.md) adds its direct
command. The [initial Office engine evaluation](office-engine-evaluation.md) passes
three passive modern-format fixtures without installation; it does not enable
Office conversion or settle isolation, wider fidelity and runtime packaging.
Next implementation work includes required Word/Excel/PowerPoint-to-PDF,
wider PDF fidelity and renderer interruption coverage, production
engine adoption and the broad-file goal's remaining catalog/analysis/release gates.

The [Excel print layout evaluation](excel-print-layout-evaluation.md) adds passive
fixtures for saved print areas, manual breaks, repeated title rows, fit-to-page
and hidden cells. It separates successful export from matching page-level text
and does not establish general spreadsheet layout or enable Office conversion.

The later [Office font substitution evaluation](office-font-substitution.md)
finds silent typography changes despite matching page/text checks. Missing-font
handling must be explicit in the required Office converter; the evaluation does
not provide a production font-resolution detector or accepted fidelity tolerance.
