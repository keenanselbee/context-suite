Document Review Worksheet
=========================

Status: 58 historical authored cases are prepared for H03 human review. No human
acceptance or final-candidate replay is claimed. This pack supports the
[local acceptance contract](local-candidate-acceptance.md); it does not replace
the required real documents, remaining PDF controls or unresolved fidelity work.


Prepared material
-----------------

Open `.codex-temp/document-review/6c4037419e88459fa77187310c8cd430/review.html`.
The self-contained local pack has 144 files totaling 87,958,159 bytes, including
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
| PDF appearance | 0 | Text/vector, scanned, rotated/cropped and transparent-image review controls still need assembly |

The 58 case labels retain 46 scoped automated passes, five known failures,
five historical rendering controls and two required refusals. These are review
entries, not 58 newly executed conversions or a fresh aggregate acceptance pass.
Earlier rendering controls use the same native engine; they are not independent
Microsoft Office reference exports. The three ordinary documents use the
[expanded combined-worker evidence](office-direct-command.md#expanded-combined-worker-replay-2026-09-19).
The other cases retain their original worker/evaluation identities and reports.

The [chart failures](excel-layout-acceptance.md) remain visible even though current
preflight refuses chart-bearing XLSX. Refusal is interim protection, not D07
completion. The [PowerPoint tolerance failure](powerpoint-layout-acceptance.md)
remains failed under its existing 0.01-point limit. The two withheld date PDFs
are diagnostic artifacts with incorrect dates, never examples of acceptable
published output. Word's specific move-spacing allowance is not generalized.


Manual review
-------------

1. Open the HTML page and record the PDF viewer/version, source application if
   used, operating system, monitor and scaling.
2. Expand a case, read its checks and open its PDF in the intended viewer.
   Compare every page with the original and any labeled control. Check content,
   fonts, page order, line breaks, values, images, clipping and whitespace.
3. Leave unexamined cases as **Not reviewed**. Record observed issues or
   **No additional issue seen**; that observation never clears a recorded
   automated failure, required refusal or pending owner policy decision.
4. Choose **Download review notes** before closing. Notes include the selection
   manifest and artifact identities. Nothing uploads or saves automatically.

The worksheet cannot establish source-application fidelity without the required
reference, or replace D10-D12. Its artifacts precede the final candidate, so any
changed output requires another review. H04 workflows, installed Explorer,
screen-reader delivery, themes, DPI levels and multiple monitors remain separate.


Preparation and verification
----------------------------

```powershell
python -B tools/office-engine/Prepare-DocumentReview.py '<retained input-manifest.json>'
python -B tools/office-engine/Test-DocumentReview.py '<new review directory>'
```

The preparer accepts only hash-bound Office/PDF/JSON files under repository
scratch, refuses linked/outside paths and changed identities, requires every
acceptance group to be represented or explicitly outstanding, and refuses to
label failed inspection evidence as passed. It creates a fresh directory and
rechecks originals and copied artifacts. It does not execute Office or a worker.

Ten checks pass in
`.codex-temp/document-review-checks/e0286954f59141c6adcbc7b71dc315e2`:
all copied identities and links, complete case/status retention, unset human
observations, JavaScript syntax, and rejection of changed artifacts, outside
paths, duplicate cases, omitted groups and false pass labels. Rejected inputs
create no review pack. These are worksheet checks, not media-fidelity reruns.

Computer Use initialization succeeds, but `sky.list_apps()` returns
`Computer Use native pipe is unavailable ... The system cannot find the file
specified (os error 2)`. No helper was launched and no configuration was changed.
Visual layout, browser interaction, notes-download behavior and accessibility
remain unverified. The earlier pack `4900d9984ad44bc8a805b74c7cc24329` is retained;
the current pack shares duplicate evidence reports instead of copying each one
into every case folder. Neither pack contains a human acceptance result.
