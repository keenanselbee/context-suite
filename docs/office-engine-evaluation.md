Office-to-PDF Engine Evaluation
==============================

Status: initial passive modern-format experiment passed, 2026-09-10. Required
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
profile. Length and Unicode changed together, so neither is established as the
cause. Keep that failure open for a controlled profile-path matrix. The temporary
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
3. Resolve profile-path behavior, font/runtime dependencies, payload size,
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
