FastAPI PDF to DOCX Fidelity Goal
================================

Status: complete in development on 2026-09-28, including representative-file
visual acceptance, regression checks and repository validation. The owner then
requested installation; the verified 1.2.0 candidate is now installed locally.

Objective
---------

Convert the owner's completed three-page FastAPI worksheet into a clean,
editable DOCX, using the supplied online converter's DOCX as a layout benchmark.
Improve the general converter rather than special-casing the filename or manually
rewriting the sample's answers. Keep inputs local and preserve their originals.

The benchmark DOCX is the blank worksheet. The owner's PDF includes added answer
text and seven checkmark stamps, so content must be compared against the PDF,
not replaced with the benchmark's blank fields. The benchmark establishes useful
page structure, heading hierarchy, answer rules, table layout and diagram layout.

Baseline evidence
-----------------

- Source PDF: three US Letter pages, 6,830 extracted letters, no page images,
  seven visible Stamp annotations and seven associated Popup annotations.
  The popups have no normal appearance; the stamps have appearance streams.
- The previous adapter refused the file at the blanket annotation check in
  `PdfDocxLayout.Extract`, before writing any DOCX. Removing that guard alone is
  not a fix: the seven visible checkmarks must survive.
- The source has editable answer text in its page content, mixed DejaVu and
  Minion font runs, underlined answer areas, a six-column architectural table,
  and a request-flow diagram. All three source pages were rendered and inspected.
- The reference contains editable text, semantic headings and lists, one
  seven-row by six-column table, anchored vector answer rules and a positioned
  footer. Its flow diagram is tabbed text, arrows and underlined blank slots.
  It has no embedded raster images or hyperlinks. All three rendered reference
  pages were inspected. It does not contain the completed answers or checkmarks.
- The previous writer reconstructed flowing paragraphs with a fixed after-space
  and little positional information. Table detection groups nearly equal rule
  endpoints by rounding; the source's inner and outer rules differ slightly.
  Both issues were confirmed and corrected during this goal.

Local, ignored evidence lives under `.codex-temp/fastapi-docx-goal/`: input copies,
input hashes, PDF inspection, source and reference renders, the baseline refusal,
and an isolated converter probe. Do not commit the personal sample documents,
their extracted text, or their rendered pages into either repository.

Milestones
----------

1. Completed: identify the real inputs, preserve copies and hashes, reproduce the
   current failure, inspect every page, and separate benchmark layout from source
   content.
2. Completed: preserve all seven static stamp appearances as bounded image
   overlays. Validate empty closed popup ownership; refuse nonempty content,
   actions, replies, alternate appearances and other annotation types. Stamps
   without a popup are valid. Completion details report stamp/image fallback.
3. Completed: reconstruct the seven-row, six-column table as 42 editable cells.
   Tolerant rule grouping, source column widths, multiline text and row/border
   geometry retain the answers and checkbox alignment. Stamps do not rasterize
   the table.
4. Completed: separate overlapping text/rule layers, preserve positioned lines,
   normalize font names and emphasis, and align text, images, tables and page
   sections. All three final pages have been visually inspected after rendering.
5. Completed for the representative sample: actual direct dispatch, worker and
   transactional publication produced a validated copy; the independent DOCX
   schema has zero errors. Authored regressions cover the new extraction,
   table, annotation and font paths. All 4,506 foundation contracts pass.

Acceptance
----------

- All source text, filled answers and seven visible checkmarks are retained;
  there is no duplicated, omitted, reordered or obscured content.
- Main text and the six-column table remain editable. The output is not a set
  of full-page screenshots and is not a manual recreation of this one worksheet.
- The expected three-page organization is retained without accidental extra
  pages, clipped text, overlapping answers, broken cells or missing symbols.
- Headings, spacing, answer lines, the flow diagram and footer are visually
  comparable to the source and the benchmark where their content is shared.
- A DOCX schema check, text/content accounting, original-file hashes and the
  applicable PDF-to-DOCX and worker regression checks pass. Render every page
  after the final change; XML inspection alone is insufficient.
- The original source and benchmark remain byte-identical. Figures or remaining
  fidelity limitations are reported accurately, including any annotation behavior
  that cannot remain interactive in the Word copy.

Related design: [PDF to Word](pdf-to-docx.md), [document support](document-design.md),
and the earlier [PDF improvements goal](pdf-conversion-improvements-goal.md).

Verified development result
---------------------------

The final actual-worker output is retained in ignored local evidence at
`.codex-temp/fastapi-docx-goal/worker-sample-03/Sample - Converted.docx`.
Its sibling `schema.json`, `result.json` and `render/` record the independent
schema check, actual publication result and all three rendered pages.

- Three Letter page sections and three rendered pages, with no added blank page.
- All 6,256 non-whitespace source characters remain editable; no text was
  rasterized. One native table contains 42 cells.
- Eight localized images preserve seven checkmarks and the title rule. There
  are no full-page images. Every filled answer, filename, identifier, diagram
  label, answer rule and footer was checked against the source renders.
- The source PDF and reference DOCX remain byte-identical to the saved SHA-256
  hashes. Processing stayed local; no reference-project material was used.
- 94 private adapter/writer checks pass with the pinned renderer, including
  independent Open XML schema validation. The 14 real-worker/application
  workflow checks pass. New fixtures are authored, not copied from the owner's
  documents; personal documents and renders remain ignored.
- All 4,506 foundation contracts and the repository's 218-document/public-boundary
  check pass. The Windows PowerShell wrapper treated an expected rejected-client
  stderr diagnostic as an error; the compiled foundation harness was rerun with
  separate output streams. Its evidence path uses native Windows separators,
  as required by an existing image-to-PDF destination equality assertion.

Remaining limits are explicit: text is editable in positioned lines, rather
than flowing paragraphs with semantic heading/list styles. Minion uses a close
serif substitute with slightly different glyph widths; fonts and editor layout
can affect later edits. Checkmarks are flattened appearances rather than review
annotations. The three-page visual acceptance is from the pinned LibreOffice
runtime and PDF renderer, not Microsoft Word desktop.

Owner-requested installation
----------------------------

Local version 1.2.0 was installed on 2026-09-28 from verified candidate
`artifacts/production-staging/b9a6ec17a1424e51863c671a8d38d216`. The candidate
passes all 14 worker/application checks and converts the personal sample with
identical document XML, image pixels and profiles to the accepted development
output; only PNG timestamps differ. The installed worker also converts the sample.
All three packaged Explorer COM classes activate, all installed files match the
candidate inventory, and the installation is an ordinary uncompressed copy.
License, trial and settings files remain byte-identical. The previous installation
is retained at `artifacts/local-update-backups/20260928-183608-1.1.9`.


Native rules and paragraph follow-up
-----------------------------------

On 2026-09-28 the owner approved improvements after comparing the blank
worksheet with the online conversion in Word. This follow-up is implemented in
development and was then installed locally as 1.2.2 on the owner's request.

- Simple solid horizontal strokes and long underscore blanks become native
  editable Word lines. Their coordinates, color and visible glyph/stroke
  thickness are retained, without an image's white paper backing. Explicit
  no-wrap placement prevents the lines from wrapping surrounding content.
- Compatible prose lines become one positioned paragraph with natural wrapping
  and source leading. Prompts, answer fields and ambiguous layouts stay separate.
  Recognized titles and headings receive Word styles. Repeated bottom-margin
  text becomes actual footer parts on every matching page; sections without a
  footer receive an explicit empty part to avoid inheritance.
- Source accounting includes underscores represented by line geometry. The
  filled worksheet retains 3,700 editable non-whitespace text characters plus
  2,556 underscores represented by rules, accounting for all 6,256 source
  characters. The blank worksheet retains 1,830 text characters plus the same
  2,556 rule glyphs. Each has 34 native lines, one 42-cell table, three page
  sections, three nonempty footer parts and semantic title/heading styles.
  The filled version retains seven localized checkmark images; the blank
  version needs no raster images. Neither rasterizes ordinary text.
- Both exact worker outputs open and export through local Microsoft Word 16.0
  as three pages. All six exported pages were rendered and visually inspected.
  The title rule, blank rules, prompts, answers, table, checkmarks, diagram and
  footers retain their placement. Extracted Word-export geometry puts the three
  PR-link/File label pairs within 0.33 point of their source vertical bounds.
  This verifies the light-page/print result; live dark-mode interaction is not
  claimed.
- Independent Open XML validation reports zero errors for both final outputs.
  The final focused renderer run passes 152 checks, covering native colored and
  default-black rules, glyph-derived rule thickness, no-wrap placement, dashed
  fallback, underscore identifiers, grouped prose, prompt boundaries, heading
  styles, nearby-body footer recognition and explicit empty-footer isolation.
  The no-renderer checkpoint passes 130 checks. All 4,506 foundation contracts
  pass, and the actual direct command/worker/publisher verifies each personal
  sample with unchanged originals. The 14 worker workflow checks also pass.

Final local evidence is under `.codex-temp/docx-native/blank-accepted/` and
`.codex-temp/docx-native/filled-accepted/`: `Sample - Converted.docx`, `result.json`,
`schema.json`, `word.pdf` and `word-render/page-1.png` through `page-3.png`.
Focused test evidence is
`.codex-temp/docx-native-contracts-20260928-footer-nearby/`; the isolated worker
is `.codex-temp/docx-native/worker/`. Personal inputs and generated evidence
remain ignored and local.

Remaining limits: paragraphs still occupy positioned frames, so substantial
edits can overlap later content; this is not full manuscript reflow or general
list/form reconstruction. Heading and footer recognition is conservative
inference. Font substitution and unusually complex content remain best effort.
Character accounting cannot by itself prove reading order or visual placement;
the rendered comparison is a separate acceptance check. Earlier installed
milestone evidence above remains historical.

Local 1.2.2 installation uses verified candidate
`artifacts/production-staging/69ffe2ff5a444a238c18e61fcb379d80`.
Its exact worker passes all 14 workflow checks; both worksheet outputs match the
Word-verified document parts and image content, apart from PNG timestamps.
The copied payload matches its inventory, all three Explorer identities and COM
classes pass verification, and license/trial/settings hashes remain unchanged.
Installation uses ordinary uncompressed copies. The previous 1.2.1 payload and
registration are retained at
`artifacts/local-update-backups/20260928-194002-1.2.1`.

Transparent figures and answer spacing (1.2.3)
--------------------------------------------

The next pass keeps unpainted PDF paper transparent when cropping localized
figures. It does not remove white pixels by color: actual white PDF paint stays
opaque. Authored blue-stamp fixtures cover transparent margins, opaque ink and
explicit white paint, including paired and unpaired empty popups.

Positioned Minion text uses the natural width of its Times New Roman substitute.
One measured answer line improves from 451.9 points in the previous Word export
to 466.6 points, against 468.1 points in the PDF. Table cells retain the previous
97% fit: using natural width there made one explanation wrap onto a fourth line
and moved subsequent rows. The final Word export restores three lines and
checkmark alignment. No embedded or Adobe application-private fonts are copied.

The combined pass has 164 focused adapter/schema checks, 14 actual worker
workflow checks and 4,506 foundation contracts passing. Blank and filled outputs
export through Word 16.0 as three pages with a 42-cell editable table; all six
pages were inspected. Both packages have zero independent schema errors. The
filled copy keeps seven checkmark images. Source originals remain unchanged.
Evidence is under `.codex-temp/docx-123/blank/` and
`.codex-temp/docx-123/filled-final/`; focused evidence is
`.codex-temp/docx-123-contracts-table-fit/`.

Explicit source rule colors remain unchanged. Live dark-mode line verification
was unavailable because the computer-use native pipe could not connect; no
dark-mode color override or improved dark-mode line visibility is claimed.

The exact 1.2.3 candidate is
`artifacts/production-staging/d4e4a0cd143143ef91920778be12f079`. Its worker passes
the 14 workflow checks and reproduces the Word-reviewed document parts and PNG
content. Local installation uses ordinary uncompressed copies verified against
the payload inventory; all three Explorer identities and COM classes pass.
License, trial and settings hashes remain unchanged. The installed worker also
converts the filled sample with all seven figures retaining transparent margins.
The previous 1.2.2 installation is retained at
`artifacts/local-update-backups/20260928-201906-1.2.2`.
