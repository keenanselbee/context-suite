PDF Production Candidate Payload
================================

Status: explicit isolated production packaging implemented; default release
adoption, broader fidelity, native hardening and redistribution review pending.
Updated: 2026-09-11.

The existing PDF Analyze/Optimize, PDF-to-PNG and combined image-to-PDF workflows
can now share a fresh production stage with the image and audio engines. This
does not implement the separately required Word/Excel/PowerPoint-to-PDF actions.


Selected inputs and layout
-------------------------

[payload-candidate.json](../tools/pdf-engine/payload-candidate.json) pins 45 files
in three directories: `pdf-engine` for qpdf, `pdf-renderer` for the authored
PDFium host and `pdf-validator` for the authored combined-image validator.
Every runtime and notice has an exact byte length and SHA-256 identity.

The original qpdf Windows ZIP and non-V8/non-XFA PDFium archive remain the
[previously evaluated versions](pdf-engine-evaluation.md). Staging reads selected
members directly from their verified archives rather than trusting an expanded
directory or its editable inventory. The two private hosts retain the exact
binary hashes enforced by their adapters; current host/CMake source hashes must
also match. Private sources and local build receipts are not copied into the app.

The pinned qpdf 12.4.1 source archive is now retained at
`.codex-temp/pdf-source-dd5161627622489f882c1eabf523c4e9/qpdf-source.tar.gz`:
19,713,921 bytes, SHA-256
`F045AA277BE2356FF53A89A8622945958291177D2483AFC20EDE7C8A8CD3873C`.
It was downloaded from the existing `sourceUrl` in
[evaluation.json](../tools/pdf-engine/evaluation.json), without changing that pin.
Its full original `LICENSE.txt` and `NOTICE.md` accompany both qpdf copies.
The Windows ZIP's short manual license summary is insufficient by itself.

PDFium's original root license and all 14 component-notice files accompany its
DLL. This preserves the supplied notice material; it does not establish that the
supplier's complete build/source provenance or linked-component review is done.
The authored candidate notice records these limits and the separate Microsoft
runtime redistribution review.

The renderer imports `MSVCP140.dll`, `VCRUNTIME140.dll` and `VCRUNTIME140_1.dll`.
Its earlier evaluation payload omitted these adjacent files. The new candidate
includes exact copies from the pinned qpdf Windows archive. The qpdf and validator
directories retain their eight pinned Microsoft runtime DLLs. Windows/UCRT imports
remain operating-system dependencies; no runtime is installed or placed on PATH.


Build and verify
----------------

From the repository root, use previously prepared qpdf/PDFium directories, their
completed pinned native hosts and the pinned qpdf source archive:

```powershell
.\tools\Build-Production.ps1 -Configuration Release -StagingId ([guid]::NewGuid()) `
  -AudioDistributionDirectory '<verified audio review bundle>' `
  -QpdfPreparedDirectory '<prepared qpdf directory>' `
  -PdfiumPreparedDirectory '<prepared PDFium directory>' `
  -QpdfSourceArchive '<verified qpdf source archive>'
.\tools\pdf-engine\Test-PdfProduction.ps1 -ProductionStage '<printed stage>' `
  -PdfFixtureDirectory '<generated qpdf matrix>' `
  -ImagePdfFixtureDirectory '<generated image-PDF matrix>' `
  -AudioFixtureDirectory '<generated audio matrix>'
python -B tools/pdf-engine/Test-PdfPayload.py '<same combined stage>'
```

PDF staging requires all three PDF input arguments and a fresh staging ID.
The audio option is independent for builds; the combined acceptance harness
requires it to exercise mixed image/audio/PDF optimization. The harness invokes
the staged worker directly and checks the full recursive inventory before and
after execution. It does not copy or inject optional engines. All inputs and
output/recovery evidence are generated disposable files in repository scratch.
Simulated access and refusing recyclers prevent live licensing and native recycling.

The build's payload verifier requires explicit `-AllowPdfCandidate` for these
files. Default release packaging still rejects them. Engine adoption, source and
component review, Microsoft eligibility, host hardening, wider documents/readers,
visible/assistive acceptance and installer lifecycle remain distinct gates.


Evidence
--------

Full managed/native stage
`artifacts/production-staging/16af9543593945e5a9c9a53c915c7ec7` passed with zero
managed warnings/errors and complete image/audio/PDF identity, notice, allowlist
and inventory checks. The combined inventory contains 108 files totaling
45,502,350 bytes, excluding the inventory itself; PDF files account for
26,670,026 bytes. Log:
`.codex-temp/pdf-production-build-16af9543593945e5a9c9a53c915c7ec7.log`.

Nineteen payload checks passed at
`.codex-temp/pdf-payload-tests-942e01cf74e84285985a8ad56cd7ce18`. They reject changed
engines/hosts, missing renderer runtime, missing/changed licenses and notices,
extra DLLs, existing/development stages, incomplete build inputs, wrong archive
sizes/hashes before any target writes and default release-allowlist use.
`native-imports.json` records the complete native import
closure for 25 native files with each non-Windows dependency adjacent to its
importing binary.
This is static import evidence, not a clean-machine installation test.

The actual staged worker passed **217 PDF workflow checks**: 11 analysis,
17 optimization/publication, 51 interruption/recovery, 19 direct mixed
optimization, 25 page publication, 18 direct PDF-to-PNG, 56 combined-image
publication/recovery and 20 direct combined-PDF checks. Results:
`.codex-temp/pdf-engine/production-8290847375ac484e81ab04a4099b353e`; log:
`.codex-temp/pdf-production-worker-16af9543.log`. The complete stage inventory
remained unchanged after execution. Hidden execution does not establish visual,
keyboard, screen-reader, theme/DPI or installed Explorer acceptance.

The same combined stage passed the **116 audio workflow checks**, including
artwork, FLAC optimization, all conversion pairs and direct access/quality
decisions, with its complete inventory unchanged afterward. Results:
`.codex-temp/audio-engine/worker-fc7318a8567e440bbbc7ab252d92e956`; log:
`.codex-temp/pdf-production-audio-16af9543.log`. The full image engine and private
adapter suites were not rerun for these packaging changes.
