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

Structural optimization adapter candidate (2026-09-10)
------------------------------------------------------

Implemented `pdf-structural-1` in the private adapter and the public
`PdfRewriteInventory` preservation oracle. qpdf's complete JSON v2 contains the
object inventory and optional inline stream bytes; its summaries are separate.
See [qpdf JSON semantics](https://qpdf.readthedocs.io/en/latest/json.html) and
[strict inspection/encryption exit statuses](https://qpdf.readthedocs.io/en/12.0/cli.html).
The adapter checks encryption first, inventories dictionaries without stream
payload extraction, then rejects signatures/protection markers, external streams
and trailer revision links before strict checking and full decoded inventory.
Warnings and heuristic recovery are not accepted.

The recipe preserves existing object-stream use, unreferenced objects/resources
and PDF version; it compresses/recompresses supported streams at level 9 with no
image optimization. Output travels through bounded stdout, never a customer path.
The output must pass strict checking and complete graph comparison. Rooted
structure, decoded/retained stream data, ordinary unreferenced components and
original document identity are compared across renumbering. Reviewed xref/object
stream and linearization storage may differ; unknown extra metadata remains part
of comparison. A non-smaller candidate returns identical source bytes.

Bounds: 16 MiB source/output, 32 MiB JSON per inventory, 32,768 objects, one million
inventory/traversal nodes, graph depth 64 and 64 MiB total canonical data. One
60-second deadline covers the operation. Each native child joins the existing
kill-on-close, 1 GiB memory-limited job before receiving arguments; stdout and
stderr readers terminate it on failure. Owned source/output snapshots are hashed,
held read-only and removed after use. These controls are not an OS filesystem or
network sandbox. Native-phase cancellation/crash injection and residual recovery
remain acceptance work.

**1,488 foundation contracts** pass, including **32 new structural PDF checks**.
They cover renumbering/cycles, content and identity changes, missing streams,
invalid encodings, escaped/duplicate names, unreachable signature dictionaries,
protection markers, external references, revision links, object/depth limits and
ordinary unreferenced information. The initial native invocation accidentally
used the archived failed `matrix-bd183...` fixture set; it failed the existing
signature-summary expectation. No guard was weakened. Fresh fixtures passed all
**13 qpdf evaluation checks** at:

```text
.codex-temp/pdf-engine/3edb2e8361e04782a91ef8364bd3a537/
  matrix-038ef085a612442ba0d28ad20f4f8d47/report.json
  adapter-ffdda32352954e0aaff4a0b065ab90a1/probe-adapter.json
  adapter-ffdda32352954e0aaff4a0b065ab90a1/optimized-candidate.pdf
  adapter-ffdda32352954e0aaff4a0b065ab90a1/incremental.pdf
  worker-81952d7efd1044df80ad0cfe1e530eb0/results/analysis-results.txt
```

**27 private checks** pass (the preceding 16 probe checks plus 11 optimization
checks). The generated two-page PDF shrank from **27,683 to 3,942 bytes**. Both
attached and unattached signature canaries are refused, as are password-required,
owner-protected and damaged-xref inputs. A generated incremental revision remains
readable for Analyze and is explicitly refused for optimization. Repeated work,
pre-cancellation and snapshot cleanup pass.

**15 independent PDFium checks** pass at
`.codex-temp/pdfium-engine/cee300f69505476e87893226200b171e/matrix-8eced96ca69f4ca78738da0e1ebe9403/report.json`.
The added three checks compare the candidate's page geometry and both rendered
pages at 150 DPI with widgets: pixels are identical. That candidate came from
`adapter-f75dbaa408704899a9317cb10311f843`, before adding the incremental fixture
test and escaped-revision guard; the encoding recipe was unchanged. This is two authored pages, not broad
font/color/forms/accessibility or signed-document fidelity acceptance.

The existing **11 PDF Analyze worker checks** pass against isolated Release stage
`artifacts/production-staging/9d52ae4f454141e3b5482fc00beb0814`. Compilation has zero
warnings/errors; normal curated identity, payload, dependency and notice checks
pass. The stage uses `-SkipShell` and still excludes evaluation PDF/audio engines.
Transformation worker dispatch, access/publication, crash recovery, broader PDF
features, larger inputs, source/payload review and the other required document
actions remain open. No menu capability, installation, native recycling, visible
UI, signing or live commerce is enabled or accepted by this slice. Audio/image
engine and hidden-window suites were not rerun because those implementations did
not change. The broad-file goal remains active.

PDF optimization worker/publication (2026-09-10)
-----------------------------------------------

The candidate now has typed file-probe/work/result messages, an immutable PDF
optimization plan, normal trial/paid admission and a sequential application
executor. The worker holds a checked source read handle, verifies its planned
SHA-256/length and writes only an existing empty single-link reservation. The
engine still receives owned snapshots, never the user source or final output
path. The app verifies the reservation digest and publishes a collision-safe
copy. Saved overwrite settings never authorize PDF replacement in this slice.
No-smaller results discard the reservation and leave no duplicate output.

**1,507 foundation contracts** pass, including 17 new PDF plan/IPC/trial/executor
boundary checks and two paid admission/expiry checks. **11 PDF Analyze checks**
and **17 new real-worker optimization checks** pass against isolated Release
stage `artifacts/production-staging/5af83dd3c17a41a9b5d613a59cb95a58`:

```text
.codex-temp/pdf-engine/3edb2e8361e04782a91ef8364bd3a537/
  worker-55ac8502da7d4deea3d6180925704661/results/analysis-results.txt
  worker-55ac8502da7d4deea3d6180925704661/optimization-results/pdf-optimization-workflow.json
```

The generated workflow verifies one admission across trial expiry, denial of a
new expired batch, mandatory copies with overwrite selected, collision naming,
original hashes, unchanged results, post-plan source mutation, complete-object
signature refusal, known encrypted-file exclusion, later-file continuation,
cancellation between files and immediately after reservation, nonempty and
hard-linked reservation refusal, independent worker source-hash validation and
owned temporary/record cleanup. No actual recycling runs. Native-phase
cancellation, killed-worker recovery and injected publication failures are not
covered by these cancellation checks.

**15 PDFium checks** pass against the actual first published copy, including
matching page geometry and identical rendered pixels on both generated pages:
`.codex-temp/pdfium-engine/cee300f69505476e87893226200b171e/matrix-7e3a93e52d2e46bfac9a3e3e907d235e/report.json`.
The source/copy lengths remain 27,683/3,942 bytes. This is narrow generated
coverage, not broad PDF appearance or accessibility acceptance.

Release compilation reports zero warnings/errors, with the existing curated
identity, payload allowlist, dependency and notice checks passing. `-SkipShell`
performs no installation/registration. Evaluation engines remain outside normal
staging and are copied only into the isolated test payload. No direct PDF menu
dispatch, visible UI, screen-reader/theme/DPI, installer, signing or live commerce
acceptance is claimed. Previous private PDF/audio and image/native/hidden suites
were not rerun; their dated evidence remains separate. Direct mixed-family
dispatch, broader document fidelity, native failure/recovery and engine adoption
remain open, as do images/PDF and Office-to-PDF conversion.

Direct PDF optimization (2026-09-10)
------------------------------------

Auto and Lossless now select structural PDF optimization directly in the existing
Optimize dispatcher. Content/extension mismatches, missing engines, known
encryption and inappropriate Balanced/Smallest requests receive per-file results
before paid admission. PDF copies remain mandatory regardless of saved overwrite
preferences. All executable family plans share one request ID/settings snapshot
and one admission; the existing sequential worker processes PNG, then FLAC, then
PDF while result rows retain selection order. Cancellation preserves committed
outputs and marks remaining work cancelled. A mixed invocation has one aggregate
completion and no routine planner.

**19 direct PDF checks**, **17 optimization workflow checks** and **11 PDF Analyze
checks** pass against fresh isolated Release stage
`artifacts/production-staging/028f1b935325415caa7d5343b4d101f2`. The added direct
suite uses generated PNG/FLAC/PDF inputs and optional pinned audio/PDF engines:

```text
.codex-temp/pdf-engine/3edb2e8361e04782a91ef8364bd3a537/
  worker-6b3cead527234e5092acb6f1857c9898/direct-results/pdf-direct.json
  worker-6b3cead527234e5092acb6f1857c9898/optimization-results/pdf-optimization-workflow.json
```

It verifies all three families completing after trial expiry under one admission,
one quiet completion, format/order retention, PDF-only expired denial, simulated
activation/deactivation retries, captured Settings, duplicate-retry suppression,
mandatory source-folder copies, no-smaller success, mismatched extension guidance,
PNG-only preset guidance, known encryption without admission, signature refusal
with later valid files, cancellation between families, absent engines without
worker launch, original hashes and publication cleanup. The initial test build
had an array/immutable-array type mismatch; correcting the authored test resolved
it without changing production behavior.

**1,507 foundation**, **86 hidden view**, **13 existing image direct-command** and
native shell contracts pass. The native build uses isolated scratch and verifies
all menu commands activating complete selections without installation. Normal
payload identity/allowlist/dependency/notice checks pass and still exclude qpdf
and FFmpeg. This is automated dispatch/binding evidence, not visible usability,
screen-reader/theme/DPI delivery or installed Explorer acceptance. The previous
PDFium rendering and private PDF/audio/image engine results remain separately
dated; they were not rerun for this dispatcher change. Native-phase termination/
recovery, broader document coverage, production engine adoption and remaining
required document conversions remain open.

Final isolated staging is
`artifacts/production-staging/ecdfc84ac0a04b03a9ee2df7b3b7a639`, rebuilt after retaining
Auto's existing PNG quality wording in the extended PDF tooltip. Its native shell
contracts and normal payload checks pass. The managed implementation is unchanged
from the tested `028f1b...` stage; UI and media suites were not repeated for that
tooltip-only adjustment.
