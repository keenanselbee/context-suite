Office-to-PDF Engine Evaluation
==============================

Status: passive modern/legacy PDF exports tested, with owned-job modern exports
verified on 2026-09-13; legacy Excel
and PowerPoint roundtrips have measured rendering differences. Required
Word/Excel/PowerPoint-to-PDF remains unimplemented in the customer application.
This checkpoint does not adopt or package LibreOffice. Follow the
[document design](document-design.md) and [broad-file goal](broad-file-support-goal.md).

The later [embedded-image experiment](office-image-evaluation.md) passes six
modern exports: the retained cases keep 1,024-square PNG resolution, exact
visible RGB samples and alpha; explicit 150-DPI controls reduce to 300-square.
Independent PDFium rendered patches also pass. This closes that specific
embedded PNG evidence gap without establishing broader image/document fidelity.

The [Word export-interruption experiment](office-export-interruption.md) now
passes cancellation and owner-crash checks triggered by a growing temporary PDF,
plus same-profile recovery after each. It retains the larger complete control
and independent qpdf checks. This does not establish the pending isolation
boundary or implement the customer converter.

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
TEMP/TMP/APPDATA/LOCALAPPDATA point into the owned profile. The later
[owned-job launcher](office-process-lifetime.md) replaces the original process-tree
kill attempt with creation-time job assignment, bounded diagnostics/deadline and
verified descendant cleanup. Three passive modern exports pass that launcher.
It does not establish filesystem/network isolation, restricted-token execution
or general Office resource acceptance. No hostile macro, external-link,
network-denial or arbitrary-customer-document test ran.

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

Retained modern-run control (2026-09-11)
--------------------------------------

The earlier `evaluation-ffd14d61fd9048b58946333d1443d485` and the comparison run
`evaluation-6bbf7608805549b28281f56ffa9620f7` contain byte-identical uncompressed
fixture package parts. Their modern exports have identical extracted text, page
geometry and all five complete BGRA render buffers. ZIP container hashes can
differ because fixture entry timestamps differ. The read-only comparison records
per-page hashes in `.codex-temp/legacy-pdf-modern-control.json`; it executed no
engine or customer document and changed no fixture.

This control finds no modern-render variability between those two retained runs.
It points to the combined legacy roundtrip as the source of the earlier observed
differences for these fixtures. It does not prove universal determinism or locate
the change within legacy export, storage precision, import or PDF rendering.
The later expanded Word fixture below is intentionally different and is not part
of this identical-input control.

Expanded Word header/footer fixture
----------------------------------

The current authored Word fixture adds separate first-page/default headers and a
shared first/default footer with local PAGE and NUMPAGES fields. Both field caches
contain `99`, so the PDF assertions require actual pagination results `Page 1 of 2`
and `Page 2 of 2`, as well as the correct header on each page and absence of stale
`99`. Existing body, Unicode text, table, page-break, geometry and source-hash
assertions remain. No external fields, macro code or embedded objects are added.

Fixture structure follows the documented
[header relationships](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-replace-the-header-in-a-word-processing-document),
[footer references](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.footerreference?view=openxml-3.0.1)
and [simple fields](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.simplefield?view=openxml-3.0.1).
This is independently authored package XML, not copied sample source or an Office
automation dependency. Broader fields, external updates and tracked changes still
need explicit policy and isolated testing.

The expanded fixture passes both DOCX-to-PDF (10,051 ms) and generated DOC-to-PDF
(10,711 ms), including header selection, rendered page-number fields, two Letter
pages, original preservation and exact equality of both 96-DPI page renders.
Both legacy page PNGs were visually inspected: header/body/footer placement was
readable with no observed overlap or clipping. The full six-export run also
repeats the earlier Excel and PowerPoint pixel-difference counts exactly.

However, Word's normalized extracted text now differs: PDFium returns header,
footer, then body for DOCX; header, body, then footer for DOC. Keep this difference
in `legacy-pdf-comparison.json`; do not sort text or discard it to obtain a pass.
Read-only qpdf inspection finds `/Marked: true` and identical 11 raw
structure-element/root dictionaries plus their parent tree. The decoded page
streams place header/footer content inside `/Artifact` blocks; the legacy form
also supplies pagination subtypes. These observations do not prove semantic
tagging, reading order in a particular viewer, or screen-reader delivery. Visual
identity and generic text-extraction order are distinct acceptance questions.

Evidence is retained in
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-5b5f47a7412e4dd692ad4dfd8b4a6d95`,
including original/legacy documents, six PDFs, reports, raw renders and ten PNGs.
The Word folders also retain `structure-review.json` and decoded page streams;
`word-structure-control.json` records the narrow dictionary comparison. Logs:
`.codex-temp/office-word-layout.log` and `.codex-temp/office-word-layout-pixels.log`.
The independent scratch metric/PNG helper is `.codex-temp/Verify-WordLayoutPixels.py`.
The earlier modern-render control refers to the original fixture, not this
expanded header/footer version.

The current evaluation tool builds in Release with zero warnings/errors;
repository source-boundary/theme/documentation/whitespace checks pass. No customer
code, production staging, installed state, native AppContainer profile or
worker/UI acceptance changed for this checkpoint. Required Office conversion,
isolation, broader fidelity and external release gates remain open.


Separate profile and environment paths (2026-09-11)
--------------------------------------------------

`Test-OfficeEvaluation.ps1 -EnvironmentPaths` now separates UserInstallation from
TEMP, TMP, APPDATA and LOCALAPPDATA. It keeps the same generated passive PowerPoint
input, output directory and PDF options throughout. Each process gets new owned
profile/data directories. The control root is 90 characters; the long root is
170. Environment paths append their variable name, so their actual lengths are
94-103 or 174-183 characters. Exact paths are retained in each `conversion.json`.

| Long path selection | Result | Export time |
| --- | --- | ---: |
| None (short control) | Valid two-page PDF | 9,009 ms |
| Profile and all four environment paths | Exit zero, no PDF | 4,388 ms |
| Profile only | Exit zero, no PDF | 4,272 ms |
| TEMP only | Valid two-page PDF | 8,353 ms |
| TMP only | Valid two-page PDF | 8,203 ms |
| APPDATA only | Valid two-page PDF | 8,224 ms |
| LOCALAPPDATA only | Valid two-page PDF | 8,319 ms |

All five completed PDFs pass qpdf/PDFium parsing, authored page/text/hidden-slide
checks and original preservation. A separate Python read-only comparison verifies
the recorded path lengths, unchanged original hashes, exact extracted text and
identical complete page BGRA buffers against the control for all successful
variants. It does not rescale or apply a visual tolerance. The two missing PDFs
remain recorded failures, not successful conversions because the matrix finished.

For this fixture and engine, making UserInstallation long is sufficient to
reproduce the failure with short environment paths. Each individually lengthened
environment variable works with a short profile. This narrows the earlier combined
profile/temp observation to the profile path; it does not locate the internal
failing filename or establish a universal cutoff. It also does not prove every
combination of long environment paths or every document type works.

Keep the per-job Office profile independently short in the eventual worker and
validate actual outputs. Do not infer a limit on user source/output filenames
from this profile experiment or silently relocate customer files. The normal
evaluation profile already uses the measured short sibling location; this
checkpoint does not introduce a customer converter or a production workaround.

Evidence is under
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-d39ba676f72048b286c3153fadc83f82`:
`office-evaluation.json`, per-case diagnostics and exact environment paths,
five PDFs, source fixture, text/renders and `environment-path-control.json`.
The command log is `.codex-temp/office-environment-paths.log`; the independent
comparison is `.codex-temp/Verify-OfficeEnvironmentPaths.py`. The wrapper verifies
the retained Office/qpdf inventories and PDFium bridge/library before launch.

The probe builds in Release with zero warnings/errors. PowerShell syntax,
whitespace and repository source-boundary/theme/73-document checks pass. No
customer implementation, production staging, native AppContainer profile,
installed state, visible UI or assistive-technology acceptance changed. Required
Office isolation, broader fidelity and integration remain open.


Excel and PowerPoint geometry/structure inspection (2026-09-11)
--------------------------------------------------------------

[Inspect-OfficePdfComparison.py](../tools/office-engine/Inspect-OfficePdfComparison.py)
now provides a repeatable read-only inspection of the four retained authored
Excel/PowerPoint PDFs from a completed `-LegacyPdf` run. It verifies the pinned
qpdf archive, complete 291-file unpacked membership and contents, and the exact
PDF hashes from the evaluation record before inspection. It retains full qpdf
JSON, decoded page streams/diffs, page boxes, font dictionaries/program hashes and
structure-role counts. No Office process or new rendering runs.

The final inspection uses the earlier expanded-Word fixture run
`evaluation-5b5f47a7412e4dd692ad4dfd8b4a6d95`; those Excel/PowerPoint fixtures are
unchanged. Evidence is `.codex-temp/office-pdf-inspection-b690e91d90c14586b97145a389b2ef78`.
The earlier independent scratch inspection is
`.codex-temp/office-geometry-2471f82bed0841f1b8f30f68b1704251`.

For Excel, the modern and legacy PDFs keep a 612 by 792 pt page and an identical
34,060-byte embedded TrueType program (SHA-256
`9537DC5F1CB5335512B3BEDF0D59E30664B9613F7EF305A1473DFE99F25C9B81`).
Decoded page operators show concrete placement changes: the first text origin
moves from `(36.992, 746.306)` to `(37.984, 747.298)` pt; the last right-aligned
value moves from x `392.202` to `391.096` pt. Grid positions and the outer rectangle
width differ slightly. Both PDFs contain the same counts of Workbook, Worksheet,
Table, TR, TD and P structure roles. Equal role counts are not proof of equivalent
structure trees or reading order.

For PowerPoint, the embedded 34,188-byte TrueType program is also identical within
the pair (SHA-256
`8261B31846B92CA39984DA974875415DA8A595D5BA0C5FDF0C00255FD28E8F5E`).
Page height changes from `405.014173228346` to `405.070866141732` pt. The first text
origin changes from `(43.058, 337.493)` to `(43.087, 337.52)` pt, and the shape and
background path operators change. Both pages exhibit the same pattern. Structure
also changes: modern output has two Div and two P elements; legacy output has two
Div, two L and two LI elements. The page marked-content operators agree with that
role change. Do not discard these differences because the extracted visible text
matches or because the coordinate differences are small.

These observations rule out differing embedded TrueType program bytes as the
explanation for these paired results. They locate differences in generated PDF
geometry and structure, but do not isolate the underlying legacy export/import
or layout step. The same-engine legacy roundtrip still is not an independent
Microsoft Office baseline. Broader layout, print settings, semantic tagging and
actual screen-reader acceptance remain required; no new fidelity tolerance is
accepted here.

The inspection command passes on the retained inputs without changing PDF bytes.
Four negative cases refuse outside-scratch input, the wrong evaluation mode,
swapped modern/legacy labels and a changed PDF. Their evidence is
`.codex-temp/office-engine/inspection-guards-69d48e1fd498490fbb437ff6ea85a9de`.
Python syntax and repository boundary/theme/73-document/whitespace checks pass.
No production, media-worker, application UI, AppContainer or installed acceptance
was rerun. Output limits in this diagnostic are checked after subprocess capture;
it is not a production parser or a hostile-document execution boundary.
