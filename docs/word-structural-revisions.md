Word Structural Revision Controls
=================================

The 2026-09-14 follow-up tests the owner's final-text Word export policy on
formatting changes, table-row changes and moved text. All twelve exports pass
their expected text and source-preservation checks. Formatting and table final
pages exactly match clean controls. The move comparison differs by 151 pixels,
so the combined exact-pixel matrix **fails**. The later
[owner-approved spacing check](word-revision-application.md#owner-approved-spacing-acceptance)
accepts this measured case separately while retaining that diagnostic failure.


Authored scope
--------------

[WordRevisionStructureFixtures](../tools/office-engine/Probe/WordRevisionStructureFixtures.cs)
creates nine passive DOCX files: clean final, tracked, and before-change variants
for each of three cases. All request Arial at 12 points on one Letter page and
contain fixed control markers. There are no fields, scripts, external targets,
embedded fonts or customer content. Source and PDF hashes are recorded, and
source write times are checked before/after export and during inspection.

| Case | Authored change | Final expectation | Controls |
| --- | --- | --- | --- |
| Formatting | Italic run becomes bold, retaining former properties in a revision | Current bold appearance and unchanged text | Clean bold and former italic documents |
| Table rows | Delete an old row and insert a new row | Keep the unchanged row and new row; omit the old row | Clean two-row final and former two-row table |
| Move | Move one literal span across a gap marker | One copy of the span at its destination | Clean destination and former source-position documents |

Microsoft's [run-property revision description](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.runpropertieschange?view=openxml-3.0.1)
distinguishes current properties from stored former properties. Its
[deleted-row description](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.deleted?view=openxml-3.0.1)
also distinguishes row revisions from cell-content revisions. This fixture marks
both independently. The [move-range description](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.movefromrangestart?view=openxml-3.0.1)
links source and destination ranges by name; the authored pair retains identical
text and separately matched start/end IDs. These are independently authored
controls, not copied reference documents or complete OOXML schema validation.

Each clean/before document uses `ExportTrackedChanges=false`. Each tracked
document is exported with false and true, yielding twelve exports. True is an
experimental positive control, not another customer preset. The existing pinned,
uninstalled LibreOffice 26.2.6.3 candidate and initialized disposable profiles
remain in use. No AppContainer profile is created; arbitrary-document isolation
is still unproved.


Actual results
--------------

qpdf validates PDF structure. PDFium independently extracts text and renders
96-DPI opaque BGRA pages. Every export retains one Letter page and its exact
expected normalized text, including marker count/order. Every source retains
bytes and write time. Recorded jobs have zero active members after cleanup.

| Case | Tracked final versus clean final | Before-change pixels differ | Show-changes pixels differ |
| --- | --- | --- | --- |
| Formatting | Exact match | Yes | Yes |
| Table rows | Exact match | Yes | Yes |
| Move | 151 changed pixels | Yes | Yes |

The move PDFs embed the same 28,344-byte Arial font program, SHA-256
`03EBA8F91E4BFFDE37418343A4DC6B8E9F31AE94EC28293D574AC66F6346FF7A`.
Decoded content streams have different text-run boundaries and position commands.
A fresh bounded read through PDFium's
[character-origin API](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/public/fpdf_text.h)
measures horizontal differences up to 0.07000732421875 PDF points for characters
in the moved span; their vertical origins agree. Thus matching extracted text
and embedded fonts do not establish identical positions. The 96-DPI crop was
inspected as a diagnostic, not a customer UI or Microsoft Word baseline review.

The historical exact comparison is unchanged: no image rescaling, source rewrite
or revision removal hides this difference. The owner subsequently accepted small
spacing differences with content, formatting and layout preserved; the linked
application inspection applies a bounded check to this case. It does not establish
support for arbitrary tracked moves. Additional paragraph-mark interactions,
more complex tables/moves, comments, protection, mixed authors, fields, pagination
and legacy DOC also retain their separate verification gaps.


Verification and reproduction
-----------------------------

Run `Test-OfficeEvaluation.ps1 -WordRevisionStructures` with the same three prepared
directory parameters described in the [original revision evaluation](word-revision-evaluation.md).
Then run `python -B tools/office-engine/Inspect-WordRevisionStructures.py '<printed evaluation directory>'`.
The export harness retains all text observations and fails after the matrix if
any text expectation differs. The separate inspector verifies the exact twelve
source/profile pairs, package declarations, hashes, write times, export flags,
job cleanup, text and render extents before comparing complete pixels. It returns
one for the currently observed move difference.

Six altered-evidence guards pass for mode, duplicate pair, export flag, source
hash, incomplete job cleanup and truncated pixel-buffer refusal. An in-memory
matching-buffer control verifies the inspector's success branch; it changes no
real render or evidence and is not counted as a real fidelity pass. A final read
of unmodified evidence still returns one with the 151-pixel difference. The
original eight-export final-text inspection remains passing. The Release harness
build has zero warnings/errors, and all 17 profile declaration contracts pass.

Evidence:

- `.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-d6a2210601d34f9c9b09ea9b18a52fb3`
- `.codex-temp/word-structures-evaluation.log` and `.codex-temp/word-structures-inspection.json`
- `.codex-temp/word-structures-guards.json` and `.codex-temp/word-structures-final-text-regression.log`
- `.codex-temp/word-move-fonts-2d5dcb98bd844aad99c38141a7930552` and `.codex-temp/word-move-character-origins.json`
- `.codex-temp/word-structures-move-comparison.png` (clean, final-text, then show-changes crops)

The first crop utility attempted an unavailable Pillow import and produced no
image. The retained diagnostic was subsequently generated with Python's standard
library; no dependency was installed.

No shipping implementation, engine pin or reserved product payload changes.
Customer Office conversion and required isolation remain unfinished. No installed
state, Explorer registration, live commerce, native recycling or new UI/keyboard,
screen-reader or theme/DPI acceptance is involved.

The [application-command follow-up](word-revision-application.md) subsequently
exports these nine documents and the four inline controls through native
isolation, independent validation and final copy publication. All 30 application
checks and thirteen expected-text/page checks pass. Formatting and table finals
match clean controls; moved text reproduces the same 151-pixel discrepancy.
All thirteen full pixel buffers match their corresponding earlier command-line
evaluations. The later bounded spacing acceptance settles this measured case;
broader moved-text fidelity remains open. The renderer has not been repaired or
changed by the acceptance policy.
