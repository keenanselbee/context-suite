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


Application publication matrix
------------------------------

The opt-in `Test-OfficeExecution.py --powerpoint-slides` scenario now runs the
three ordinary slide cases through `MainViewModel`'s real Convert > PDF action,
the current worker, independent output validators and application publication.
It uses the same authored packages described above. The earlier notes-page
positive control remains evaluation evidence; production keeps notes export off.

The scenario requests Overwrite originals in its isolated settings and requires
PDF copies without recycling. It verifies selected-document order, all original
hashes/write times, absence of a spreadsheet calculation prompt, and native
profile, registry mapping, context and publication-journal cleanup. Font reports
are retained for diagnosis and the matrix fails if these authored Arial fixtures
unexpectedly require review. No shipping font-review choice is bypassed.

The independent inspection command is:

```powershell
python -B tools/office-engine/Inspect-WordFontStyles.py --powerpoint-slides `
  --execution-id '<completed execution UUID>' `
  --pdf-prepared '.codex-temp/pdf-engine/3edb2e8361e04782a91ef8364bd3a537' `
  --pdfium-prepared '.codex-temp/pdfium-engine/44820d5e04b34ceb81ff3d666fbdb622'
```

The existing pinned-inspector wrapper also supports PowerPoint, avoiding a
second copy of its engine and source verification logic. It builds the separate
evaluation host and verifies qpdf, PDFium and all retained input hashes. Inspection
recreates the authored packages and compares every part, checks publication
identity and source preservation, validates PDF structure, renders every page,
and requires exact normalized page text and 720-by-405-point dimensions. Four
negative text controls reject reordered output, exported hidden slides, note
leakage and unexpected extra text.

This establishes a bounded application workflow when its run and independent
inspection both pass. It does not establish general font/layout fidelity,
Microsoft PowerPoint pixel equivalence, hidden-information sanitization, all-hidden
decks or visible acceptance. The separate network-isolation, broader Office,
formal packaging and release gates remain open.

On 2026-09-16, execution `5c53dc392b904b3a830c064c5bbb501d` passes all eleven
application checks and publishes three copies containing seven pages in total.
All three native profiles, registry mappings and contexts are removed; originals
retain their recorded bytes and write times, and no publication journal remains.
The execution receipt reports exit code zero and unchanged captured sources,
binaries and engine inputs. Worker, contract-host and inspector Release builds
complete with zero warnings and errors.

Independent receipt `slides-inspection-receipt-94d0a884e4724414a947844723ca87d2.json`
under that execution directory passes all three PDF text/geometry comparisons
and four negative text controls. Its source and independent-validator hashes
remain unchanged. The earlier notes-page positive control was not rerun. Neither
these render files nor hidden application execution establish visual usability,
keyboard, screen-reader, other-theme/DPI or installed-shell acceptance.

Reproduce execution with authorized disposable profiles and retained pinned
engine directories, before running the independent inspection above:

```powershell
python -B tools/office-engine/Test-OfficeExecution.py --create-disposable-profiles `
  --powerpoint-slides `
  --office-engine '.codex-temp/office-execution/f8c3e213cc634163af7e2dd88484aa83/worker/office-engine' `
  --pdf-engine '.codex-temp/office-execution/f8c3e213cc634163af7e2dd88484aa83/worker/pdf-engine' `
  --pdf-renderer '.codex-temp/office-execution/f8c3e213cc634163af7e2dd88484aa83/worker/pdf-renderer'
```

The first attempt, `49d6ce0cb37b4d52bfcb09893c2d5e10`, rejected reuse of a
retained worker because its top-level files differed from the current build.
It stopped before profile creation. The successful run built a fresh scratch
worker and copied the verified engines; the reserved formal package is unchanged.
