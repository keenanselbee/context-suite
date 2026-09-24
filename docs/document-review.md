Document Review Worksheet
=========================

Status: 58 Office cases and four PDF appearance controls from candidate 1.1.2
are prepared for H03 review. Human acceptance is unperformed; D09 remains failed.
The older historical pack is retained separately. These materials support the
[local acceptance contract](local-candidate-acceptance.md); it does not replace
the required real documents or unresolved fidelity work.


Current candidate collection, 2026-09-24
---------------------------------------

Open
`.codex-temp/candidate-112-owner-review-05a7185bd7be4aaab425ef39fdd92cc1/review.html`.
This entry point collects 25 audio comparisons, sixteen HEIC/AVIF outputs and
62 document/PDF cases. Its unchanged artifact copies total 362,905,660 bytes.
The document worksheet is `packs/documents/review.html` beneath that directory.
The page does not launch an application, play media or upload anything.

All current outputs come from the preserved 1.1.2 release worker. The 62 document
entries retain **59 scoped automated passes, one PowerPoint typography failure
and two required early-date refusals**. Four repaired chart cases and five print
cases now use passing candidate application/independent inspection. Historical
rendering controls remain explicitly labeled references, not Microsoft Office
exports. The D09 limit remains 0.01 points; no owner approval is inferred.

`document-inputs.json` binds the selected artifacts, current independent reports,
candidate hashes and original selection. `manifest.json` identifies every copied
worksheet file; `verified.json` confirms all 400 local worksheet links, media
copies against generation receipts, retained case statuses and unset human
observations. The existing document worksheet verifier also passes its ten
identity, markup, download-script syntax and refusal checks at
`.codex-temp/document-review-checks/63133e4f390d43bfaeec65c4f1132569`.

The retained preparation wrapper reuses the document preparer's artifact and
worksheet handling, then changes the new page's historical-worker notice to
identify 1.1.2. It retains overall acceptance as false and all case failures.
No prior pack or evidence report is changed. Browser appearance, interaction,
notes download and accessibility remain unverified. Three real recordings and
D10-D12 documents remain outstanding. The separate workflow supplement now
provides [current H04 launch instructions](manual-workflow-review.md), after
all eight headless selections pass and every released application file matches
the inventoried stage. Its new overview and workflow pages leave the original
media index, manifest and verification receipts unchanged. Visible acceptance
remains unperformed.


Historical prepared material
----------------------------

Open `.codex-temp/document-review/3673ab009b4e4258a543ed7e7555533a/review.html`.
The self-contained local pack has 157 files totaling 88,557,075 bytes, including
the 48-page capacity PDFs and recorded inspection reports. Sources are copied
byte-for-byte after their recorded hashes are checked; retained evidence remains
unchanged. The selection manifest, copied-file hashes and unset human status are
retained in `input-manifest.json` and `prepared.json`.

| Group | Prepared cases | Review material |
| --- | --- | --- |
| D01 | 1 | Two-page ordinary Word and earlier same-engine control |
| D02 | 15 | Font/style controls, including explicit missing-font decisions |
| D03 | 19 | Thirteen revision and six paragraph cases, including intentionally different controls |
| D04 | 1 | Combining accents, CJK/RTL text and anchored image |
| D05 | 6 | Ordinary workbook and five historical print-layout controls |
| D06 | 6 | Four published date PDFs and two explicitly withheld early-date results |
| D07 | 5 | Four known chart failures and the separate 48-sheet capacity comparisons |
| D08 | 4 | Ordinary deck and three visible-slide/order controls |
| D09 | 1 | Three-slide layout deck with unresolved font-size failure |
| D10-D12 | 0 | Approved real documents and independent references still required |
| PDF appearance | 4 | Text/vector, synthetic scanned page, rotated/cropped page and transparent image |

The 62 case labels retain 50 scoped automated passes, five known failures,
five historical rendering controls and two required refusals. These are review
entries, not 62 newly executed conversions or a fresh aggregate acceptance pass.
Earlier rendering controls use the same native engine; they are not independent
Microsoft Office reference exports. The three ordinary documents use the
[expanded combined-worker evidence](office-direct-command.md#expanded-combined-worker-replay-2026-09-19).
The other Office cases retain their original worker/evaluation identities and
reports. The four new PDF controls use the same expanded worker, as detailed below.

The [chart failures](excel-layout-acceptance.md) remain visible in this historical
pack. At that checkpoint preflight refused chart-bearing XLSX; the current
candidate collection above includes the repaired chart outputs. The
[PowerPoint tolerance failure](powerpoint-layout-acceptance.md)
remains failed under its existing 0.01-point limit. The two withheld date PDFs
are diagnostic artifacts with incorrect dates, never examples of acceptable
published output. Word's specific move-spacing allowance is not generalized.


Manual review
-------------

1. Open the HTML page and record the PDF viewer/version, source application if
   used, operating system, monitor and scaling.
2. Expand a case, read its checks and open its PDF/PNG in the intended viewer.
   Compare every page with the original and any labeled control. Check content,
   fonts, page order, line breaks, values, images, clipping and whitespace.
   PNG previews use a checkerboard. PDF-to-PNG preserves transparent page
   backgrounds; black text needs a white or checkerboard viewer background.
3. Leave unexamined cases as **Not reviewed**. Record observed issues or
   **No additional issue seen**; that observation never clears a recorded
   automated failure, required refusal or pending owner policy decision.
4. Choose **Download review notes** before closing. Notes include the selection
   manifest and artifact identities. Nothing uploads or saves automatically.

The worksheet cannot establish source-application fidelity without the required
reference, or replace D10-D12. Use the current candidate collection above;
any later changed output requires another review. H04 workflows, installed Explorer,
screen-reader delivery, themes, DPI levels and multiple monitors remain separate.


Preparation and verification
----------------------------

```powershell
python -B tools/office-engine/Prepare-DocumentReview.py '<retained input-manifest.json>'
python -B tools/office-engine/Test-DocumentReview.py '<new review directory>'
```

The preparer accepts only hash-bound Office/PDF/PNG/JSON files under repository
scratch, refuses linked/outside paths and changed identities, requires every
acceptance group to be represented or explicitly outstanding, and refuses to
label failed inspection evidence as passed. It creates a fresh directory and
rechecks originals and copied artifacts. It does not execute Office or a worker.

Ten checks pass in
`.codex-temp/document-review-checks/d91bca4e8966457585a6b89460a8c09e`:
all copied identities and links, complete case/status retention, unset human
observations, JavaScript syntax, and rejection of changed artifacts, outside
paths, duplicate cases, omitted groups and false pass labels. Rejected inputs
create no review pack. These are worksheet checks, not media-fidelity reruns.

Computer Use initialization succeeds, but `sky.list_apps()` returns
`Computer Use native pipe is unavailable ... The system cannot find the file
specified (os error 2)`. No helper was launched and no configuration was changed.
Visual layout, browser interaction, notes-download behavior and accessibility
remain unverified. Earlier packs `4900d9984ad44bc8a805b74c7cc24329` and
`6c4037419e88459fa77187310c8cd430` are retained. Current packs share duplicate
evidence reports instead of copying each one into every case folder. None
contains a human acceptance result.


PDF controls on the expanded worker
----------------------------------

`tests/ContextSuite.Core.ContractTests/PdfReviewContracts.cs` independently
authors four one-page PDFs and three expected PNG bitmaps. The synthetic scan
has only a bitmap: block-letter SCAN, ruled lines, a gray marker and patterned
paper. It has no text layer or OCR. The rotated/cropped page has independently
specified quadrant positions. The embedded-image control has alpha 255, 128,
64 and zero. The text/vector PDF contains three text labels, red/blue rectangles
and a rule; its automated oracle checks geometry, marker colors and visible
ink, not exact glyph fidelity.

Execution `.codex-temp/pdf-review/e9383510f4584c9698e1de5fc9d707be` passes
**21 command, pixel and cleanup checks**, publishing four named PNG copies at
150 DPI through one direct application batch. The scan, crop/rotation and alpha
outputs match every expected pixel. All four PDFs and three reference PNGs
retain their bytes and modification times; the worker exits and no publication
journal, worker scratch or temporary output remains. The test host builds in
Release with zero warnings/errors. No Office profile is created.

The unchanged worker is the one in
`.codex-temp/combined-inputs/e549e5e3509942b4b8ec8e0d6545f93a/combined`.
Full package and before/after inventory checks pass; source, host and candidate
identities remain unchanged. The completion receipt binds the exact worker and
result report to inventory SHA-256
`ABA4BCE94D364BC6327BB8545D884D636D0B2A31E031DEBF8186E414D10AE5A6`.
This does not rerun PDF optimization, image-to-PDF order, hostile/recovery suites
or the full final candidate. Synthetic controls do not establish real-scan or
general document fidelity. Assistant inspection of the PNG artifacts is limited;
it does not count as owner review or desktop/UI acceptance.

```powershell
python -B tools/pdf-engine/Prepare-PdfReview.py --payload '<inventoried combined payload>'
python -B tools/pdf-engine/Write-PdfReviewManifest.py `
  --document-manifest '<Office selection without PDF cases>' --pdf-review '<completed PDF evidence>'
python -B tools/office-engine/Prepare-DocumentReview.py '<new combined selection>'
```

The PDF runner builds only the test host and uses the exact selected worker.
The manifest writer checks the completed receipt, result and media hashes,
requires all four scenes and refuses to append duplicate PDF cases. The current
selection is `.codex-temp/pdf-review-manifests/57d33f36080340eb8aa829476a747967.json`.
Five admission checks pass under
`.codex-temp/pdf-review-guards/99c727d75b40471aa81a7e7574ce0dad`, refusing outside or
incomplete payloads, failed review evidence, duplicate PDF cases and changed
result reports before creating review outputs. The worksheet's ten checks also
pass with all 62 cases, including the new PNG preview links. Human acceptance
and D10-D12 remain open.
