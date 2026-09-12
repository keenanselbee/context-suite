PowerPoint Slide and Notes PDF Evaluation
========================================

This experiment tests whether the pinned Office candidate follows saved slide
order, excludes hidden slides at both ends of a presentation and omits speaker
notes from ordinary slide PDFs. It supplies evidence for required PowerPoint
conversion; it does not adopt a production Office engine or complete fidelity
acceptance.


Fixtures and controls
---------------------

`PowerPointSlideFixtures` makes copies of the existing independently authored,
passive three-slide PPTX. It replaces the text with unique literal markers and
changes only ordering, visibility and note parts. The packages contain no macros,
fields, external references, media or customer content. Presentation size is
720 by 405 points; the declared notes-page size is 540 by 720 points.

| Case | Authored input | Export policy under test |
| --- | --- | --- |
| reordered | Slide list orders parts 3, 1, 2; all visible | PDF pages follow the slide list rather than part filenames |
| hidden-ends | Parts 1 and 3 hidden; part 2 visible | Only the middle slide is exported |
| notes-excluded | All three slides visible, each with a corresponding note marker | Ordinary slide pages, without note markers |
| notes-control | Byte-identical copy of notes-excluded | Test-only notes-page export establishes that the engine actually imported the notes |

The ordinary cases use the existing fixed Impress PDF options, including false
`ExportHiddenSlides`, `ExportNotes`, `ExportNotesPages` and
`ExportOnlyNotesPages`. The positive control sets the last two to true. This
control is an experiment option, not an added customer action or a selected
launch notes-export policy.

Microsoft's [notes-slide structure](https://learn.microsoft.com/en-us/office/open-xml/presentation/working-with-notes-slides)
and [slide visibility property](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.presentation.slide.show?view=openxml-3.0.1)
inform the authored parts and relationships. LibreOffice's
[PDF parameters](https://help.libreoffice.org/latest/en-US/text/shared/guide/pdf_params.html)
describe the export switches. These primary references were consulted on
2026-09-11; no reference fixture or implementation was imported.


Verification
------------

Run the existing wrapper with `-PowerPointSlides` and the independently prepared
Office, qpdf and PDFium directories. It verifies the pinned payload identities,
uses separate initialized repository-local profiles, applies the existing profile
settings and checks source hashes before and after export. qpdf validates PDF
structure; PDFium independently parses, extracts page text and renders at 96 DPI.

`Inspect-PowerPointSlides.py '<printed evaluation directory>'` separately checks
the source/PDF hashes, authored package members, slide order/visibility,
slide-to-note relationships, identical control input, retained text observations
and page geometry within 0.1 point. A completed export is not automatically a matching observation;
inspect each `SlideObservation.Matches` value.

The 2026-09-11 final run used LibreOffice 26.2.6.3 and completed all four cases:

| Case | Observed PDF page markers | Result |
| --- | --- | --- |
| reordered | Slide 3, slide 1, slide 2 | Saved ordering matched |
| hidden-ends | Slide 2 only | Both hidden slides excluded |
| notes-excluded | Slide 1, slide 2, slide 3; no note markers | Notes excluded from page text |
| notes-control | Note 1, note 2, note 3 | Positive control confirms imported note text |

All ten PDF pages passed qpdf structure checks and PDFium parsing/rendering.
Slide pages measured 720 by 405.014 points, within the existing 0.1-point tolerance;
note pages measured 540 by 720 points. All source hashes remained unchanged.
The independent inspector passed against the unmodified final report. Three
negative evidence copies were rejected for an incorrect observation, reordered
source slide declarations even with a recomputed source hash, and changed export
options. The original evidence hashes and write times remained unchanged.

Retained evidence under `.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/`:

- `evaluation-6250d8cefed041728dfece8ba0e5a290`: final fixtures, conversions,
  PDF text, renders, report/options and `independent-slide-verification.log`.
- `slide-inspector-negative-8c4cff4ad99a4f41b5f79549f8a7ddd4`: three altered
  evidence copies, rejection logs and results.

The wrapper log is `.codex-temp/powerpoint-slide-evaluation-final.log`. The
evaluation host built in Release with zero warnings/errors; its 17 existing
profile contracts passed during this run. No foundation, full worker, production
packaging or visible application acceptance run is claimed for this tool-only work.

The initial attempt stopped during fixture generation because a relationship URL
was incorrectly constructed as an XML name. The next run exported matching PDFs
but mislabeled the report mode; the independent inspector rejected it. Both
reporting/fixture errors were corrected before the final run. Earlier attempts
remain separate and are not the final acceptance evidence.


Remaining boundaries
--------------------

This tests literal text, simple shapes and specific saved slide/notes declarations.
It does not establish Microsoft PowerPoint pixel equivalence, all-hidden or empty
decks, custom slide shows, animations, transitions, embedded media, comments,
master/layout inheritance, fonts, accessibility tags or all legacy variants.
Text extraction and rendering do not establish removal of arbitrary hidden
information from all PDF objects. The positive control establishes note import
for these authored parts, not universal note-layout fidelity.

No arbitrary-document isolation is established. AppContainer testing still needs
its separate outside-repository authorization. No installation, Explorer changes,
live commerce, production Office handler or manual/accessibility acceptance is
part of this experiment.
