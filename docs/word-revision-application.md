Word Revision Application Acceptance
===================================

Status: all 30 application-command checks and all thirteen PDF text/geometry
checks pass. The exact-pixel matrix remains failed: moved text reproduces the
151-pixel difference from its clean final control. The other final comparisons
match exactly. No tolerance or source rewrite has been introduced.


Scope
--------

The owner selected final text with tracked-change markup hidden for Word PDF
conversion. The [inline evaluation](word-revision-evaluation.md) verifies the
explicit export option, and the [structural evaluation](word-structural-revisions.md)
adds formatting, row changes and moves. Those earlier evaluations used the
candidate's command-line export path. This matrix sends the same authored
documents through `MainViewModel`'s Convert > PDF command, application source
admission, the sequential worker, native isolation, independent validation and
transactional copy publication.

The contract host links the existing fixture generators; it creates no new
customer documents or alternate renderer policy. Thirteen passive DOCX files
cover four inline visibility variants and three structural cases with clean,
tracked and before-change documents. One command uses an isolated local trial
and a selected output folder. Overwrite preferences are deliberately enabled;
Office originals must still remain copies. No spreadsheet-choice prompt is
allowed. Each result must identify independent validation and a committed copy.

Original hashes and modification times, retained native ownership journals,
removed profile folders/mappings, empty context storage and worker shutdown are
checked after the command. The wrapper records source and complete binary hashes,
checks the selected worker against the current build, and verifies unchanged
inputs after execution. Failed outcomes remain in `outcomes.json` even when the
execution acceptance report cannot be completed.


Independent inspection
----------------------

The separate inspector reads all thirteen actual published PDFs with pinned
qpdf and PDFium. It verifies source/publication identities and unchanged files,
and compares every authored ZIP part with a fresh fixture declaration. It checks
one Letter page, exact normalized expected text, and a complete 96-DPI opaque
pixel buffer. The source's stored revisions are preserved.

Inline shown/hidden/omitted variants must match the clean final document. Each
structural tracked final must match its clean final; before-change controls must
differ. These exact comparisons retain changed-pixel counts. The inspector
records text and pixel outcomes separately and returns a failing exit if either
matrix fails, after retaining all comparisons. A successful export is not
silently promoted to a fidelity pass. Show-changes positive controls remain the
earlier evaluation's evidence; this application run exercises only the selected
fixed production policy.


Reproduction
------------

With other Context Suite processes closed, use a freshly staged isolated worker:

```powershell
python tools/office-engine/Test-OfficeWordRevisions.py `
  --create-disposable-profiles --worker '<scratch worker executable>'
tools/office-engine/Inspect-OfficeWorkerExports.ps1 `
  -WordRevisions -WorkerResults '<printed run>/contracts/results.json' `
  -PdfPreparedDirectory '<verified qpdf evaluation directory>' `
  -PdfiumPreparedDirectory '<verified PDFium evaluation directory>'
```

Native execution requires the existing explicit disposable-profile authorization.
The inspector creates no native Office profile and does not invoke Office again.
Both commands retain their evidence under repository scratch. They make no
installation, Explorer-registration, live commerce or native recycling changes.

This verifies application orchestration, not a visible dispatcher/router test.
It establishes no keyboard, screen-reader, theme/DPI or installed-shell result.
Paragraph-mark revisions, comments, protection, fields, more complex moves and
tables, and broader font/pagination fidelity remain separate requirements. No
Microsoft Word baseline or commercial release clearance is claimed.


Observed results
----------------

Native execution at
`.codex-temp/office-execution/9ce831c6bd2747eca0ea2ff0e3a3ea9a` exits zero with
unchanged source/binary inputs and all **30 application checks** passing. All
thirteen originals retain their bytes and modification times; all thirteen
profile folders, mappings and generated contexts are removed. The final Windows
inventory also reports no Context Suite test application, worker or Office host.
The worker is retained at
`.codex-temp/office-execution/f548e4039fb14259bfd923c4a44b7e4b/worker`.

The run's `revision-inspection-5fa1e76b644f4ff88197c34daf24ed7d` independently
checks all thirteen PDFs and retains every text and pixel observation. The
inspector deliberately exits one because the combined exact matrix is not met:

| Comparison | Expected text | Changed pixels from clean final | Outcome |
| --- | --- | --- | --- |
| Inline, revisions shown in source | Pass | 0 | Pass |
| Inline, revisions hidden in source | Pass | 0 | Pass |
| Inline, display setting omitted | Pass | 0 | Pass |
| Formatting, tracked final | Pass | 0 | Pass |
| Table rows, tracked final | Pass | 0 | Pass |
| Moved text, tracked final | Pass | 151 | Exact-pixel failure |
| Former italic formatting control | Pass | 1,381 | Required difference observed |
| Former table-row control | Pass | 374 | Required difference observed |
| Former move-position control | Pass | 1,947 | Required difference observed |

The four clean controls also pass their own expected text and page checks. A
separate read of the retained evidence verifies that all thirteen authored XML
packages and all thirteen complete pixel buffers match the corresponding earlier
command-line final-text evaluations exactly. This is recorded in the run's
`evaluation-comparison.json`. The native application path therefore reproduces
the earlier results, including the move discrepancy; it does not fix that
spacing behavior or independently establish a fidelity tolerance.

Worker, contract-host and independent-inspector Release builds complete with
zero warnings/errors. The first contract build found a missing namespace import
and array conversion in the new test harness; both were corrected before native
execution. The inspector's existing three-family mode is also rerun successfully
against retained Word/Excel/PowerPoint publications, in
`.codex-temp/office-execution/cb8320577db3481aabe99f9fb1fcb3c0/inspection-9950f829fe8f4acc91f41bec142b08d6`.
No wider foundation, image or audio suite is claimed as a new run here. No
shipping implementation, runtime pin or formal production package changed.
