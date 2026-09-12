Word Tracked-Revision PDF Evaluation
===================================

This test evaluates whether the pinned Office candidate carries tracked insertion
and deletion text into PDFs under different saved revision-display settings.
It is a prerequisite for choosing the required Word-to-PDF export policy, not
an implementation of that converter or a decision to accept/reject revisions.


Authored fixtures and evidence boundary
--------------------------------------

`WordRevisionFixtures` creates four small DOCX files containing literal control,
inserted and deleted markers. Each package has five XML/relationship parts,
Letter page geometry and no fields, macros, external references, attachments or
customer content. The clean control contains ordinary accepted text. The other
three retain identical tracked insertion/deletion elements and differ only in
revision display declarations: explicitly shown, explicitly hidden or omitted.

The independently authored structure follows Microsoft's descriptions of
[inserted runs](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.insertedrun?view=openxml-3.0.1),
[deleted runs](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.deletedrun?view=openxml-3.0.1)
and [revision display settings](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.revisionview?view=openxml-3.0.1),
consulted on 2026-09-11. The display setting controls annotation visibility;
it does not remove stored revisions. Those semantics do not by themselves
establish another application's PDF-export behavior.

The evaluation uses the existing pinned, uninstalled LibreOffice candidate and
the unchanged Writer PDF options. Each export receives a separately initialized
repository-local profile with the existing post-initialization profile settings.
The wrapper verifies the Office MSI/payload, qpdf inventory and PDFium probe/DLL
identities before running. The default test does not create an AppContainer
profile and establishes no arbitrary-document isolation.

qpdf checks PDF structure. PDFium independently reads text, reports geometry and
renders opaque 96-DPI pages. Results must contain both control markers, one page
and the expected geometry. Inserted/deleted marker presence is recorded as an
observation; successful export is not silently counted as revision fidelity.
Each source hash is checked before and after export. The output hashes, extracted
text and raw render results are retained with the report.


Reproduction
------------

From the Context Suite repository:

```powershell
.\tools\office-engine\Test-OfficeEvaluation.ps1 `
  -PreparedDirectory '<verified Office evaluation directory>' `
  -PdfPreparedDirectory '<verified qpdf evaluation directory>' `
  -PdfiumPreparedDirectory '<verified PDFium evaluation directory>' `
  -WordRevisions
python -B .\tools\office-engine\Inspect-WordRevisions.py '<printed evaluation directory>'
```

The separate inspector verifies the exact four-case set, source/PDF hashes,
source revision elements and display flags, control text, declared geometry,
retained page text and the reported marker-presence booleans. It is a consistency
check of retained evidence, not a second Word renderer or a new independent text
extraction pass. The PDFium extraction remains the renderer-independent oracle.


Results and remaining work
--------------------------

All four cases exported one structurally checked, independently parsed/rendered
Letter page. The pinned candidate was LibreOffice 26.2.6.3. The observed PDF text
was:

| Source case | Inserted marker present | Deleted marker present |
| --- | --- | --- |
| Clean accepted-text control | Yes | No |
| Revisions explicitly shown | Yes | Yes |
| Revisions explicitly hidden | Yes | No |
| Display setting omitted | Yes | Yes |

This demonstrates that the tested exporter can include deleted draft text when
the source displays revisions or omits the display setting. It also distinguishes
hidden revisions from removing them: the hidden source still contains its tracked
deletion, while that deletion's marker is absent from the extracted PDF text.
The converter must not promise a clean final document based merely on successful
export, nor silently accept revisions as a workaround. The setting-sensitive
behavior is an observation from these fixtures, not a universal interoperability
guarantee or an approved customer default.

Evidence is under
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-a38705672ae340219531fb35fde37a7c`;
the log is `.codex-temp/word-revision-evaluation.log`. All four source/PDF hashes,
revision declarations and retained text observations pass the separate inspector.
Two altered-evidence controls are rejected: flipping the reported deleted-marker
boolean, and changing the source display declaration while updating its recorded
hash. They remain under the same prepared directory at
`revision-inspector-negative-e44b29189d634eefa6cc35f78a80b058`.

All **17 existing profile declaration contracts** pass. The Release evaluation
host builds with zero warnings/errors. Public-source boundary, system-theme
policy, 96 documentation files and both repositories' whitespace checks pass.
No production code, engine pin, installed app or Explorer registration changed.
The wider foundation, audio, image, PDF and Office matrices were not rerun;
there is no fresh production build or commercial release clearance claim.

Only simple inline insertions/deletions are covered. Formatting revisions,
paragraph/table changes, moves, comments, mixed authors, legacy DOC, document
protection and revisions interacting with fields or page layout need separate
evidence. No Word-produced PDF baseline, strikeout/color/baloon visual acceptance
or general typography fidelity is established by extracted marker text.

The final converter still needs explicit revision handling, isolation, engine
packaging, font/layout acceptance and ordinary trial/paid publication integration.
These observations cannot authorize silently accepting or rejecting revisions,
modifying originals or claiming that all draft material is excluded from a PDF.
