Isolated PDF Engine Evaluation
=============================

These tools evaluate an independently sourced pinned qpdf build using generated
disposable files. They do not install anything or change production staging,
Explorer registrations, PATH, licensing or customer files. The tools are not a
second application edition or an approved production adapter.

From the repository root:

```powershell
.\tools\pdf-engine\Prepare-PdfEvaluation.ps1
.\tools\pdf-engine\Test-PdfEvaluation.ps1 -PreparedDirectory '<printed repository-local scratch directory>'
.\tools\pdf-engine\Test-PdfAdapter.ps1 -PreparedDirectory '<prepared directory>' -FixtureDirectory '<generated matrix directory>'
.\tools\pdf-engine\Test-PdfWorker.ps1 -ProductionStage '<fresh isolated production stage>' -PreparedDirectory '<prepared directory>' -FixtureDirectory '<generated matrix directory>'
```

Preparation downloads and verifies the exact archive in `evaluation.json`, then
records its extracted inventory. Testing rechecks that inventory, authors files
in a new `matrix-*` directory, runs bounded diagnostics and writes `report.json`
plus generated PDFs and inspection JSON. Retain failed runs as evidence; never
silently select a newer upstream build. Do not pass customer files to this harness.

Read [the evidence and limits](../../docs/pdf-engine-evaluation.md), especially the
signature-summary omission and the limited generated-page rendering evidence.
The private adapter and worker are now connected for isolated tests. The package
remains evaluation-only; shipping it requires broader process/resource acceptance
and completed redistribution review. The worker test creates an augmented copy of
the supplied isolated stage; it never modifies that stage or claims its augmented
file list is production-approved.

The separate PDFium evaluation uses a pinned non-V8 Windows build:

```powershell
.\tools\pdf-engine\Prepare-PdfiumEvaluation.ps1
.\tools\pdf-engine\Build-PdfiumEvaluation.ps1 -PreparedDirectory '<printed PDFium directory>'
.\tools\pdf-engine\Test-PdfiumEvaluation.ps1 -PreparedDirectory '<PDFium directory>' -FixtureDirectory '<generated qpdf matrix directory>'
```

The native build uses the installed Visual Studio x64 C++ tools and Windows SDK,
with no installation or production-stage changes. Testing compares rendered
pixels, checks form drawing, records signature/encryption observations and
round-trips an authored BGRA image. JSON and PNG evidence stays in a new scratch
directory. This is not a general PDF converter or a production sandbox.

The private adapter tests also exercise a structural optimization candidate:
fixed lossless stream recompression, complete object/stream preservation checks,
signature/encryption/revision refusal, smaller-only results and owned snapshot
cleanup. `probe-adapter.json` now records both probe and optimization checks, and
`optimized-candidate.pdf` retains the generated accepted candidate. `Test-PdfWorker.ps1`
also runs the file-based optimization worker through real trial admission and
application-owned copy publication. Its separate `optimization-results` directory
records collisions, source changes, protection refusal, cancellation and no-change
outcomes. Add both `-AudioPreparedDirectory '<prepared audio directory>'` and
`-AudioFixtureDirectory '<generated audio matrix>'` to also test direct Auto/
Lossless on PDF-only and mixed PNG/FLAC/PDF batches. Those extra tests create an
isolated combined engine payload and record `direct-results/pdf-direct.json`.
They do not add engines to normal staging or change Explorer registration.

The worker script also runs `--pdf-failures`: a generated large PDF must first
optimize successfully, then the harness observes its owned native child before
cancellation, controlled deadline expiry and worker-only termination. It tests
publication move failures and abrupt test-application exits at five publication
checkpoints. Only owned test processes are terminated; no native recycling runs.
Long local source/snapshot/output paths are included. `failure-results/pdf-failures.json`
records the interruption/move results; `failure-results/app-crashes` deliberately
retains journals, originals and candidate evidence for restart-discovery checks.
This verifies retained evidence, not automatic restoration or visible recovery UX.

To compare that candidate independently, add
`-OptimizedCandidate '<generated adapter directory>\optimized-candidate.pdf'`
to `Test-PdfiumEvaluation.ps1`, using the same source fixture directory. It must be
inside repository PDF scratch. Three additional checks compare source/candidate
page geometry and both authored pages' rendered pixels. This does not establish
broad document fidelity or native failure/sandbox acceptance.

For the optional PDF-to-PNG adapter candidate, build the private native host with
`Build-PdfiumEvaluation.ps1 -PreparedDirectory '<prepared PDFium directory>' -Renderer`,
then run `Test-PdfRasterAdapter.ps1 -PreparedDirectory '<prepared PDFium directory>'
-FixtureDirectory '<generated qpdf matrix>'`. The existing evaluation probe must
also be built. The wrapper verifies host/source/runtime identities, generates
fresh comparison pixels through the separate evaluation bridge and runs the
private adapter contracts. It records separate `raster-reference-*` and
`raster-adapter-*` directories. No renderer is added to normal production staging.
The adapter also fills checked page reservations through an optional worker
command. `Test-PdfPageWorker.ps1 -ProductionStage '<isolated production stage>'
-PreparedDirectory '<prepared PDFium directory>' -FixtureDirectory '<generated
qpdf matrix>'` copies the stage into new repository scratch and adds the verified
renderer there. It exercises batch access, numbered PNG copies, collisions,
partial cancellation/failure and retained recovery evidence. No installed payload
is changed. The direct PDF-to-PNG command remains pending; see
[the document design](../../docs/document-design.md).
