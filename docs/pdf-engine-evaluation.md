PDF Engine Evaluation
=====================

Date: 2026-09-09. Status: qpdf experiment and optional worker/Analyze integration;
normal packaging contains no PDF engine. No PDF transformation command is enabled.

Candidate and provenance
------------------------

The owner-selected PDF and Office actions remain in
[document design](document-design.md). This experiment covers structural PDF
inspection/recompression only. It does not replace page rendering, images-to-PDF
or Word/Excel/PowerPoint-to-PDF.

The [pinned manifest](../tools/pdf-engine/evaluation.json) selects the upstream
[qpdf 12.4.1 release](https://github.com/qpdf/qpdf/releases/tag/v12.4.1), portable
MSVC x64 ZIP, independently downloaded from the release on 2026-09-09. Archive
SHA256 is `3CD016CD433EF7232E42F4C13348A49CC14907A3C7278EF4F99120593126F7A6`,
checked against the GitHub release asset digest. The pinned source archive URL
and digest are recorded; the source archive has not yet been downloaded/reviewed.
No reference executable, installer, PATH edit or production payload is used.

The archive is 28,165,367 bytes. Its complete unpacked inventory contains 291
files totaling 87,686,030 bytes; `bin` totals 9,247,184 bytes including extra tools
and Microsoft runtime DLLs. These are measured upstream payload sizes, not a
selected shipping footprint. The executable SHA256 is
`57C003E868FB66CD343FD5AFB91BE8C2277F56434EEA8635762499731BF9F60D`;
`qpdf30.dll` is `36FE5B2023F244E5785D96B8372DBA26B75EA51B63ADF4D4F1E66AD0AA1C8A61`.
The full per-file hash inventory is retained alongside the downloaded archive.

The bundled manual identifies Apache-2.0 for qpdf. Exact linked components,
Microsoft runtime redistribution, complete third-party notices, source delivery,
curation, update policy and commercial redistribution review remain open. A
license label and subprocess boundary are not release clearance.

Method and actual evidence
--------------------------

Run the [isolated evaluation tools](../tools/pdf-engine/README.md). They create
new scratch directories, verify the archive and extracted inventory, then run a
.NET harness against independently authored PDFs. Test files include two pages
with different sizes, crop/rotation, text, vectors, a small RGB image with a
transparency mask, a link, form value and appearance, bookmark, embedded text
attachment, XMP and document-info metadata, and basic accessibility structure.
These fixtures are not certified PDF/A or accessible documents.

The fixed candidate rewrite uses `--suppress-recovery`,
`--object-streams=generate`, `--compress-streams=y`, `--decode-level=generalized`,
`--recompress-flate` and `--compression-level=9`. It does not select image
optimization, downsampling or page rasterization. This is an experiment recipe,
not an approved customer preset. See [qpdf transformation options](https://qpdf.readthedocs.io/en/stable/cli.html).

Latest evidence is under
`.codex-temp/pdf-engine/3edb2e8361e04782a91ef8364bd3a537/matrix-a1531e6aca94471cbda92047c06dcc53/`:

- **13/13 experiment checks pass**. The generated source shrinks from 27,683 to
  2,769 bytes. The original hash and write timestamp remain unchanged.
- Source, rewritten and already-compressed second-pass output have equal
  canonical root/info object graphs and decoded stream bytes under qpdf JSON
  inspection. Reference IDs are normalized and stream `/Length` is excluded;
  other dictionary entries, metadata, page content, image/mask bytes and graph
  links remain compared. Both snapshots use qpdf: this is not independent parser
  or rendered-page equality evidence.
- Page/form/attachment/bookmark facts match the authored fixture. Password-required
  input fails without a password; encryption can still be detected. An empty user
  password does not conceal owner-protected encryption in the typed facts.
- Cross-reference damage is rejected with heuristic recovery disabled. A generated
  JavaScript URL canary causes no pending loopback connection during inspection;
  this does not establish general filesystem/network sandboxing.
- On Windows build 26200, source probing took 26 ms, full JSON inspection 27 ms
  and optimization 31 ms in this run. This tiny synthetic file and warm process
  environment do not establish cold-start/large-file budgets. Peak memory was not
  measured in this experiment.

The harness has a 20-second process deadline, bounded stdout/stderr, process-tree
termination and a post-process output-file size check. It runs only its authored
small fixtures. Production memory/CPU limits, active disk-output limits,
process-start race handling and worker ownership are not implemented here.

Signature finding and implementation consequence
------------------------------------------------

The first run (`matrix-bd183dc0b0b7417ea47f545f665dd46a`) passed 11/12 checks:
qpdf's summary omitted a signature field that had no page widget. The follow-up
tests retain that case and add a widget-associated signature placeholder. The
second is reported; the first is still omitted. Both are deliberately invalid
signature placeholders, not cryptographic verification fixtures.

Therefore **zero reported signature fields never establishes that a document is
unsigned and must not authorize rewriting**. An optimizer needs a broader bounded
object-graph signature/incremental-update admission check, plus signed corpus
acceptance; unknown signature state must retain the original without rewriting.
Do not remove signature protection merely to make this candidate pass.

Public structured facts
-----------------------

`PdfProbeParser` parses a bounded qpdf JSON v2 summary into page, encryption,
reported form/signature-field, attachment and outline counts. It retains no form
values, attachment names or document text. It limits input to 1 MiB/depth 32 and
4,096 pages/fields/attachments/outlines; duplicate keys, missing sections and
contradictory declarations fail. A failed/missing probe must mean unavailable,
not a zero count. The complete foundation run passes **1,034 contracts**, including
29 PDF parser/typed-fact checks and 14 PDF reader/enrichment/transport checks.
The optional private adapter and worker integration below now enrich Analyze;
normal packaging still omits qpdf and retains the header report.

Integrated Analyze evidence
---------------------------

`proprietary/src/ContextSuite.Private/Pdf/PdfProbeAdapter.cs` verifies and holds
read leases on qpdf and its nine adjacent DLLs. It accepts only complete PDF byte
snapshots up to 16 MiB, writes a unique file in worker-owned scratch, verifies
the snapshot hash and holds a read lease during native probing. The customer path
is never sent to qpdf. The application keeps its original read lease throughout.

The subprocess starts with `@-`, waiting for command arguments on stdin. The
adapter assigns the existing kill-on-close, 1 GiB process-memory job before
sending its fixed options and snapshot path. It removes inherited `QPDF_*`
environment controls, uses a 15-second probe deadline, caps stdout at 1 MiB and
stderr at 64 KiB, and terminates/awaits the child on cancellation or failure.
Snapshots are deleted after success, failure or cancellation. Existing client
cleanup removes its owned worker directory after worker exit. Crash leftovers
when both application and worker die still require dedicated recovery acceptance;
there is no general sweep of other workers' files.

The JSON probe supplies page/encryption/form/attachment/bookmark facts. If the
file requires a password, the dedicated encryption status check supplies known
encryption with unavailable content facts. No password is supplied or bypassed.
Counts remain qualified as reported; signature summaries never authorize rewriting.
Malformed, over-budget and unavailable probes retain the useful header report.
The application skips full snapshots above 16 MiB and explains the current limit.

Current isolated evidence:

- **16 private adapter checks** pass, including Unicode scratch paths, five PDF
  variants, engine mutation exclusion, limits, failure cleanup, pre-cancellation
  and cancellation after snapshot creation starts. Evidence:
  `adapter-cea7b1d1e92f4c869f7e21d96087c2a4/probe-adapter.json` under the prepared root.
- **11 app-to-worker checks** pass: actual Analyze facts, password-required and
  owner-protected encryption, malformed-file fallback and later-file progress,
  no paid admission/publication, original hashes and scratch cleanup. Evidence:
  `worker-bb62240f91c749f0b451e2e371365d34/results/analysis-results.txt`.
- **1,034 foundation** and **76 hidden view** contracts pass. Hidden tests do not
  prove visible usability, keyboard delivery, Narrator, other themes or DPI.
- Release stage `artifacts/production-staging/b6cd87374f0946ba92c76ca7a80737b2`
  passes with zero warnings/errors and normal payload/notice/dependency checks.
  It uses `-SkipShell` and omits qpdf. The worker test copies that stage into new
  isolated scratch and adds the evaluation engine there; this augmented copy is
  not a production allowlist or commercial payload acceptance result.

The native job is a lifetime/resource control, not a filesystem/network sandbox.
DLL-loading races, actual child-process crash/timeout/worker-crash injection,
broader malicious PDFs, active-content external-access probes and large-file
performance remain open. Snapshot cancellation is not proof of cancellation
during every native parsing phase. No conversion or optimization is registered.

Independent PDFium rendering evaluation
--------------------------------------

On 2026-09-09, evaluated the independently downloaded
[pdfium-binaries chromium/8044 release](https://github.com/bblanchon/pdfium-binaries/releases/tag/chromium/8044),
using the Windows x64 non-V8 archive pinned in
`tools/pdf-engine/pdfium-evaluation.json`. The supplier is separate from the
PDFium project. The verified archive is 3,818,370 bytes; extracted payload is
8,085,886 bytes, including a 7,370,752-byte DLL. Bundled `args.gn` declares both
V8 and XFA disabled. This is evaluation-only, not a redistribution approval or
production dependency. The license and all component notices still need review.

An independently authored C++ probe builds with MSVC `/W4 /WX`, using pinned
headers and import library. Preparation records the extracted inventory; build
records source, executable and DLL hashes. The generated-fixture harness checks
those identities, limits each subprocess to 30 seconds and diagnostics to 64 KiB
per stream. The probe limits input to 16 MiB, rendering to 16 pages, 300 DPI and
16 million pixels per page. It supplies no JavaScript, navigation, file or network
callbacks and invokes no document actions. These are evaluation bounds, not a
production OS sandbox or malicious-input acceptance result.

**12/12 checks passed.** Evidence is under
`.codex-temp/pdfium-engine/cee300f69505476e87893226200b171e/matrix-c261f5159ad8439298546075329215f4/report.json`:

- Both generated source pages and their qpdf-optimized equivalents have identical
  BGRA pixels at 150 DPI, independently of qpdf's structural comparison. Cropped
  page geometry is 576 by 756 points; the rotated second page is 300 by 400 points.
- Explicit `FPDF_FFLDraw` changes pixels and includes the authored form value;
  ordinary annotation rendering alone omits it. The first rendered PNG was
  visually inspected: authored text, vector rectangle, image and form value are
  visible. This is not application UI acceptance or a broad font comparison.
- Transparent backing retains unpainted alpha. A generated 64 by 48 BGRA image
  saves to PDF and re-renders at 72 DPI with exact alpha and exact visible color
  channels; white compositing is also exact for this fixture. Fully transparent
  RGB is excluded from the visible-color assertion. ICC/HDR/EXIF are not covered.
- Password-required content fails with PDFium password error 4. Owner-protected
  content loads for inspection; this does not grant transformation permission.
- PDFium reports one signature in **both** invalid signature canaries, including
  the unattached field missed by qpdf's summary. This is promising additional
  evidence, not cryptographic validation or proof that every signature is found.
- PDFium opens the broken-xref fixture that strict qpdf inspection rejects.
  Successful rendering therefore cannot replace strict structural admission.
- All generated qpdf input hashes remain unchanged. Source/optimized two-page
  rendering took 72/37 ms in this run; these tiny warm inputs are not benchmarks.

No PDF commands are enabled by this experiment. Broader fonts, ICC, forms without
appearances, active content, valid signatures, incremental updates, malformed
streams, scanned pages, cancellation/crash controls and production publication
remain pending. The image-to-PDF round trip uses PDFium for both creation and
rendering and needs a second reader/corpus before claiming broad fidelity.

Next acceptance work
--------------------

1. Broaden private adapter failure, crash and resource-limit acceptance, then
   complete the payload/notice review before including qpdf in normal packaging.
2. Implement signature/encryption admission independent of summary omissions.
   Preserve the selected launch scope while declining unsafe individual inputs.
3. Extend the bounded PDFium evaluation above. Test fonts, ICC, larger/mixed pages, annotations, forms,
   signed/encrypted documents, scanned material and malformed compressed objects.
4. Complete publication, cancellation/failure and smaller-only checks before
   enabling PDF Optimize. Finish the remaining image/PDF and Office renderers,
   payload/notice review, integrated staging and actual UI acceptance separately.
