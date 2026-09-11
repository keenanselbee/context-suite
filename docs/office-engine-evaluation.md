Office-to-PDF Engine Evaluation
==============================

Status: passive modern/legacy PDF exports tested through 2026-09-11; legacy Excel
and PowerPoint roundtrips have measured rendering differences. Required
Word/Excel/PowerPoint-to-PDF remains unimplemented in the customer application.
This checkpoint does not adopt or package LibreOffice. Follow the
[document design](document-design.md) and [broad-file goal](broad-file-support-goal.md).

Candidate and acquisition
-------------------------

The first standalone candidate is LibreOffice 26.2.6 Windows x64, independently
downloaded from The Document Foundation. No installed Office automation, reference
binary, installer action, Explorer registration or production payload refresh ran.
The [release announcement](https://blog.documentfoundation.org/blog/2026/09/04/libreoffice-26-2-6/)
and [official SHA256](https://download.documentfoundation.org/libreoffice/stable/26.2.6/win/x86_64/LibreOffice_26.2.6_Win_x86-64.msi.sha256)
identify the selected release; [evaluation.json](../tools/office-engine/evaluation.json)
pins the download.

- MSI size: 373,252,096 bytes.
- MSI SHA256: `F9877032FD908BEB9C0DDF06DF4AF5C2E85F419C42E14876C4CCE5AAE5FB2660`.
- Authenticode verification returned Valid, signer The Document Foundation.
- Executed version: `LibreOffice 26.2.6.3 8221e31b3ac356a1623c672912a3d2b492f7e3d1`.
- MSI database: 6,092 components, 19,496 File rows, one embedded cabinet.
- Mapped payload: 19,332 files, 1,517,294,910 bytes before any trimming.
- Excluded: 164 font/runtime files outside INSTALLLOCATION. Existing system MSVC
  runtime 14.51.36247.0 was present, so successful execution does not prove a
  self-contained distributable. No fonts were installed.

The new read-only database extractor verifies the pinned archive, extracts its
embedded stream, invokes Windows cabinet expansion, validates mapped paths and
sizes, and records per-file hashes. It never calls an installation sequence or
custom action. Microsoft documents the
[read-only database API](https://learn.microsoft.com/en-us/windows/win32/api/msiquery/nf-msiquery-msiopendatabasew)
and [stream reader](https://learn.microsoft.com/en-us/windows/win32/api/msiquery/nf-msiquery-msirecordreadstream).
The exact pinned package is trusted evaluation input; this is not a generic
untrusted-MSI service or production payload verifier.

Measured passive fixture results
--------------------------------

The fixtures are independently authored ZIP/XML, not Microsoft-produced layout
baselines. Each invocation uses a new writable profile. Measurements include
process startup and conversion on this machine, not repeated cold/warm benchmarks.

| Fixture | Independent result | Elapsed |
| --- | --- | --- |
| DOCX | Two Letter pages; explicit page break, Unicode text and two-cell table preserved | 8,528 ms |
| XLSX | One Letter page; authored print area, cached/formula result 5; hidden sheet and out-of-area marker excluded | 8,086 ms |
| PPTX | Two visible slides in authored order; hidden middle slide excluded; Unicode text and approximately 720 by 405 pt slide geometry preserved | 8,165 ms |

All three pass qpdf structural checks, PDFium parsing/rendering/text assertions,
bounded page geometry checks and unchanged-original SHA256 checks. All five
96-DPI rendered pages were visually inspected: authored text and simple geometry
were readable without observed clipping. This is a fixture render review, not
application UI acceptance or a comparison with Microsoft Office output. PowerPoint
height was 405.014 pt, within the explicit 0.1 pt tolerance.

The export filter is fixed per family. Options explicitly request lossless image
compression, no resolution reduction, tags and bookmarks, PDF 1.7, no notes/notes
pages, no hidden slides, no single-page-sheet override, no interactive form fields,
no hybrid source embedding and no encryption. These are experiment settings, not
proof of preserved tags/bookmarks/images/forms: the small fixtures do not exercise
all of them. LibreOffice documents these
[PDF export parameters](https://help.libreoffice.org/latest/en-US/text/shared/guide/pdf_params.html).
The workbook's formula cache equals its computed value, so this experiment does
not distinguish cached-value retention from recalculation.

Observed failure and execution limits
------------------------------------

With an earlier deep Unicode disposable profile, the completed PowerPoint fixture
returned exit code zero, empty stdout/stderr and no PDF. Adding a complete master,
layout and theme did not resolve that failure. A diagnostic load-to-FODP also
returned no output. The same completed fixture succeeded with a shorter ASCII
profile. Length and Unicode changed together in that initial experiment. The
controlled follow-up below now isolates a profile/temp-root length dependency.
The temporary
FODP diagnostic is removed from the committed runner; OpenDocument conversion is
not part of the selected product scope.

Actual Unicode input/output paths succeeded. LibreOffice console diagnostics
decoded the filename's accented character as U+FFFD; diagnostics must not serve as
the authoritative output identity. The runner validates the expected file itself.

The profile disables macros, active content, Python runtime and update checks
using settings present in the pinned package's registry schema. Startup uses
`soffice.com`, headless mode and a unique UserInstallation, consistent with the
[upstream startup documentation](https://help.libreoffice.org/latest/en-US/text/shared/guide/start_parameters.html).
TEMP/TMP/APPDATA/LOCALAPPDATA point into the owned profile. The wrapper has a
60-second deadline, bounded stdout/stderr and an owned process-tree kill attempt.
It does not establish filesystem/network isolation, restricted-token execution,
resource limits or proven descendant cleanup after a launcher exits. No hostile
macro, external-link, network-denial or arbitrary-customer-document test ran.

Evidence and repeatability
--------------------------

[Evaluation instructions](../tools/office-engine/README.md) describe the wrappers.
Prepared package and inventory are under
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8`.
The complete three-fixture result is
`evaluation-394c86bfa5a24a6282d727d0e519c663/office-evaluation.json` beneath it;
each result records source/PDF hashes, text, dimensions and timing. Renders, input
packages, conversion diagnostics and profile-path mappings remain beside it.
The final full-run log is `.codex-temp/office-evaluation-final.log`.

Deep-profile failure evidence includes
`evaluation-de9141f89cd744f69af0de5897d61126`; short-profile PowerPoint success is
`evaluation-3dac134323104610a92532af2c4cf2ea`. Raw cabinets, mapped files and sibling
`.codex-temp/office-profile-*` directories are intentionally retained and ignored.
The initial download/unpack used equivalent manual commands; the preparation
wrapper itself has not been run end to end. After adding an archive read lease,
the extractor's read-only inspect mode passed; the full 1.52 GB extraction was
not repeated for that change. The full test wrapper did run successfully.

After removing the temporary FODP diagnostic and checking the PDFium CMake source
identity as well, the final wrapper rerun passed all three fixtures under
`evaluation-c8ba70abe02b4e05a5c0ab6bb1a0aa74` (8,583 / 8,440 / 9,386 ms).
Its log is `.codex-temp/office-evaluation-checkpoint.log`. The earlier five-page
visual review applies to the explicitly identified render set above. Release
tool builds, PowerShell syntax checks, whitespace and the repository's public
boundary/theme/66-document link checks pass. Existing product/worker/UI suites
were not rerun for these evaluation-only tools.

Remaining implementation and acceptance
----------------------------------------

1. Establish filesystem/network denial and bounded process lifetime before testing
   hostile documents. Verify macros, DDE/OLE, external workbook/image links, Python,
   malformed inputs, passwords, timeouts, cancellation and engine crashes.
2. Add realistic modern and legacy DOC/XLS/PPT fixtures and independent reference
   PDFs. Evaluate XLSB, templates and macro-enabled variants explicitly. Test
   headers/footers, fields, tracked changes, charts, images, bidirectional text,
   font substitution/embedding and authored print areas/page breaks. Resolve
   formula recalculation, markup, notes and static animation/media policies.
3. Carry the measured short-profile policy into the eventual isolated worker,
   investigate the exact engine failure, and resolve font/runtime dependencies, payload size,
   dependency/license/source-delivery inventory and maintenance. Do not infer
   redistribution readiness or a portable runtime from this unpacked smoke test.
4. Implement the accepted family adapters, bounded worker protocol, independent
   output validation and transactional copy publication under existing admission.
   Connect direct Convert commands without restoring a general planner.
5. Verify mixed batches, retries, recovery, visible/accessibility acceptance and
   isolated packaging. Existing image/audio/PDF evidence is not new Office coverage.

Signing, native installer lifecycle, live commerce and the remaining commercial
release gates stay separate. No Office family is removed from the required launch
scope merely because the first candidate needs more work.

Controlled profile-path experiments (2026-09-10)
------------------------------------------------

The runner now has two diagnostic modes. Each reuses one generated PowerPoint
input, the same output path and the same fixed export options. Successful outputs
are independently parsed, rendered and checked for expected text/page geometry;
every attempt checks the source hash. Missing output with exit zero is recorded
as a failed conversion, not counted as a pass. Matrix completion itself is not
conversion acceptance.

`-ProfileMatrix` compares a 90-character sibling profile with a 154-character
deeper profile, each in ASCII and Unicode. Both short profiles pass (8,433 and
8,387 ms); both deeper profiles return zero with empty diagnostics and no PDF.
The result is under prepared Office evaluation
`evaluation-1ff6f94d7c064de3a96375bbd37c2205`, with log
`.codex-temp/office-profile-matrix.log`.

`-ProfileLengths` holds the parent directory, path depth and ASCII encoding fixed
while padding only the final profile directory name. This removes the depth and
Unicode differences from the first matrix:

| Profile path characters | Result | Conversion time |
| --- | --- | --- |
| 90 | Two independently validated visible slides | 9,291 ms |
| 110 | Two independently validated visible slides | 8,928 ms |
| 130 | Two independently validated visible slides | 8,629 ms |
| 150 | Exit zero, no PDF | Recorded failed conversion |
| 170 | Exit zero, no PDF | Recorded failed conversion |

Evidence: `evaluation-91e84705a5b645bf8c355031593fe555/office-evaluation.json`
beneath the pinned Office directory, and `.codex-temp/office-profile-lengths.log`.
UserInstallation and TEMP/TMP/APPDATA/LOCALAPPDATA all derive from that profile,
so this establishes a length dependency in that combined profile/temp policy;
it does not identify a failing internal filename, exact cutoff, or which variable
is responsible. It does not establish a general LibreOffice path limit. Keep
ordinary evaluation profiles short (90 characters in this repository), and
validate outputs regardless of process exit code. Other repository locations
may not fit the fixed diagnostic lengths and are explicitly refused.

The ordinary short-profile regression passes Word, Excel and PowerPoint again
(8,680 / 8,215 / 8,329 ms), with unchanged originals and the same independent
page/text/geometry checks. Evidence is under
`evaluation-ffd14d61fd9048b58946333d1443d485`, with log
`.codex-temp/office-profile-regression.log`. Native isolation, broader fidelity,
production integration and the remaining Office acceptance gates remain open.

Legacy PDF roundtrip comparison (2026-09-11)
-------------------------------------------

The `-LegacyPdf` experiment first generates DOC/XLS/PPT copies of the authored
passive fixtures with the fixed Word 97, Excel 97 and PowerPoint 97 filters.
The [bounded legacy analyzer](legacy-document-analysis.md) checks their content
identity and source/copy preservation. It then exports both modern and legacy
versions to PDF using the same options and fresh 90-character profiles. It accepts
no arbitrary document paths and does not enable a customer Office command.

All six PDF exports pass qpdf checks, PDFium parsing/rendering, expected page
counts, authored text/geometry assertions and unchanged-source hashes. Normalized
extracted text matches between each modern/legacy pair. These are narrow fixture
passes, not general conversion or fidelity acceptance:

| Family | Modern / legacy export time | Pages | Exact 96-DPI pixel comparison |
| --- | --- | --- | --- |
| Word | 8,365 / 8,542 ms | 2 / 2 | Both pages equal |
| Excel | 8,779 / 8,130 ms | 1 / 1 | 2,551 changed pixels; maximum channel difference 255 |
| PowerPoint | 8,312 / 8,394 ms | 2 / 2 | 2,728 and 2,746 changed pixels; maximum channel difference 76 |

Word/Excel page geometry stays 612 by 792 pt. PowerPoint height changes from
405.014 to 405.071 pt (width 720 pt), though both render to 960 by 541 pixels.
The authored geometry check allows 0.1 pt; this does **not** make the observed
height/pixel differences acceptable for release. The comparison records exact
BGRA differences including alpha and excluding row padding. Different pixel
dimensions would be reported as incomparable, without resampling. No visual
tolerance or image-quality score is used to hide differences.

An independent Python standard-library calculation verified all five pixel and
normalized-text comparison results and generated review PNGs from the raw BGRA
output. All five legacy pages, plus modern Excel and the first modern PowerPoint
page, were visually inspected. Text/table/slide content was readable with no
observed clipping. That visual review neither cancels the measured differences
nor proves the absence of finer layout defects. It is not application UI,
keyboard, screen-reader, theme/DPI or installed-shell acceptance.

Retained evidence is under
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-6bbf7608805549b28281f56ffa9620f7`:
`legacy-analysis.json`, `office-evaluation.json`, `legacy-pdf-comparison.json`,
per-conversion diagnostics, originals/legacy copies/PDFs, raw renders and ten
review PNGs. Logs are `.codex-temp/legacy-pdf-office.log` and
`.codex-temp/legacy-pdf-pixel-verification.log`; the independent scratch calculation
is `.codex-temp/Verify-LegacyPdfPixels.py`.

The evaluation probe builds in Release with zero warnings/errors. Edited
PowerShell syntax and repository boundary/theme/documentation/whitespace checks
pass. No production build, worker/UI regression suite, installation, registration,
native recycling or new AppContainer profile ran for this tools-only checkpoint.

The legacy files and both PDF versions are produced by the same engine. Therefore
this measures a combined modern-to-legacy-to-PDF roundtrip, not fidelity of an
independently authored Microsoft Office legacy file. The causes and acceptability
of Excel/PowerPoint differences remain unresolved; separate engine nondeterminism,
legacy storage/import/export changes and actual rendering before setting a
customer fidelity policy. Independent baseline documents, broader features,
isolation, runtime/font inventory and production integration remain required.
