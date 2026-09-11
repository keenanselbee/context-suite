Document Support Design
=======================

Status: bounded package analysis and a structural PDF optimization candidate implemented; transformation worker/publication, renderers and launch acceptance pending

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
  Office family. Legacy Office analysis remains pending. Optional PDF probing
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

- Images to PDF with an explicit page/order/output policy.
- PDF pages to images with fixed reviewed resolution and predictable naming.
- PDF optimization with declared preservation of text/vector/document features.
- Word, Excel and PowerPoint to PDF with evaluated font/layout fidelity and dependencies.

OpenDocument analysis remains required; OpenDocument-to-PDF has not been selected.
Exact legacy/modern input variants, presets and engine choices remain to be
evaluated. No rendering engine is adopted or installed by this scope decision.

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
Images-to-PDF, PDF pages-to-images and Word/Excel/PowerPoint-to-PDF remain required
and unimplemented.

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
