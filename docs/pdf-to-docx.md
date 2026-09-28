Best-effort PDF-to-Word Conversion
=================================

Status: implemented and tested in source on 2026-09-28; not installed or staged
as a new release. Existing reserved candidates are unchanged.

Convert > Word (DOCX) accepts text-based PDFs and saves a separate editable Word
copy. It uses the existing single sequential worker, paid/trial admission and
transactional, collision-safe publication. Replacement settings never authorize
replacing a PDF with DOCX. A mixed selection reports unsupported members while
continuing supported files. Source hashes are checked before and after work.

Achieved fidelity
-----------------

- Reconstruct paragraphs with font family/size, bold, italic and text color.
  Heading-size and line-gap changes separate paragraphs. Columns become a
  readable sequence of paragraphs rather than a promise of identical columns.
- Preserve embedded PNG/JPEG pictures and reconstruct ordinary ruled tables
  as editable Word cells. Page sizes, approximate margins and section breaks
  preserve the source's page organization; Word may repaginate flowing content.
- Preserve complex painted regions, masked/rotated pictures and intersecting
  labels as 150-DPI PNG figures using the pinned PDF renderer. Surrounding text
  stays editable. Completion details identify this fallback. A document that
  would contain no editable text is refused.
- Scanned or other content-bearing pages without usable text require OCR, which
  is not included. Protected, malformed, unsupported and oversized inputs fail
  without changing their originals. Unusable text mappings and unsupported text
  rendering can be refused. Pages with annotations (including interactive links
  and form widgets) are refused rather than silently losing their behavior.
  This is not an exact-layout, OCR, editable chart,
  editable formula, form, annotation, hyperlink or accessibility reconstruction
  engine. Compare important documents against the original PDF.

The format menu describes reflow and figure limitations. No routine planner,
cloud upload, account system or additional resident process is introduced.

Engine and redistribution
-------------------------

[PdfPig 0.1.16](https://www.nuget.org/packages/PdfPig/0.1.16) supplies local
extraction and layout primitives. The exact NuGet package is locked in the
private project; its net9.0 managed assemblies have no additional package
dependencies under .NET 10. The
[selection record](../tools/pdf-engine/pdf-docx-engine.json) pins the package
SHA-512, upstream revision, assembly hashes and notice hashes. Production
payload checks require these files, and the offline notice index links the
upstream Apache-2.0 license and notices. Context Suite supplies its own layout
reconstruction and OOXML writer; no reference-project files are used.

The [upstream documentation](https://github.com/UglyToad/PdfPig/blob/master/docs/index.md)
describes letters, words, images and layout analysis. This selection avoids a
Python runtime and a second native PDF extraction engine. Open XML SDK 3.5.1 is
a test-only dependency for independent DOCX schema validation; it is not shipped.

Bounds and validation
---------------------

Each PDF is limited to 64 MiB, 256 pages, two million text characters and 256
embedded images; a batch accepts at most 128 distinct paths and 2 GiB of source
data. Further per-page limits bound letters, words, paths, figure regions and
geometry. The worker has the existing process-resource boundary and a 180-second
conversion deadline. The renderer retains its own pixel/time/input limits.

DOCX output is capped at 64 MiB, embedded media at 48 MiB, and uncompressed ZIP
parts at 96 MiB. The writer reopens its package and checks document text, drawing
counts, section presence, part counts and safe paths. Text accounting includes
labels deliberately preserved inside figures, preventing unnoticed extraction
loss. The worker writes only an empty reserved output and verifies its hash;
the app owns final publication and cleanup.

Evidence
--------

- 51 private adapter checks: ordinary text, headings, columns, editable table,
  image, multiple pages, accented/Greek/Cyrillic text, artwork with labels,
  scanned and blank input. Independent Open XML schema checks cover every DOCX.
- 14 real-worker/application checks: actual direct dispatch, mixed-selection
  reporting, source/collision preservation, OCR refusal, malformed-input
  recovery, empty-reservation rules, figure fallback, cancellation, changed
  source and failures before/after publication.
- Generated DOCX files opened in the pinned LibreOffice runtime, exported to
  PDF and rendered to PNG for visual inspection. Inspection found and corrected
  heading/body merging. Final samples show separated paragraphs, intact table
  cells/images, readable multilingual text, two distinct pages and retained
  chart artwork/labels. This is bounded authored-fixture evidence, not universal
  PDF fidelity or Microsoft Word application acceptance.
- Native Explorer contracts pass all 112 filename-availability checks and
  activation batches. The existing 4,506 foundation contracts pass.

Adapter evidence: `.codex-temp/pdf-docx-tests-6249ed563d6243bc923e38d73817c62f`.
Direct/worker evidence: `.codex-temp/pdf-docx-workflow-5cb02dbf9bfe4022b97133a5f8cb1848`.
Visual evidence: `.codex-temp/pdf-docx-visual-89e4ff3f97eb412491b07d4e60b9625a`
and `.codex-temp/pdf-docx-visual-e5a8be352522443ca9e006a591e1b84f`.

Two additional real-worker checks refuse password-encrypted and owner-protected
PDFs. A scratch worker publish verifies all nine pinned PdfPig assembly/notice
files and confirms the Open XML test dependency is absent from runtime packages.
