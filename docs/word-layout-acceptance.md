Word Multilingual And Anchored-Image Acceptance
==============================================

Status: authored D04 application export and independent inspection pass; human
and approved real-document acceptance remain pending.
This is the combining-accent, right-to-left/CJK and anchored-image group in the
[finite acceptance contract](local-candidate-acceptance.md). It does not replace
the existing Word styles/revisions corpus, approved real documents or human review.


Declared fixture and oracle
----------------------------

The passive, independently authored DOCX contains one Letter page, one-inch
margins, four 18-point text paragraphs and one page-anchored bitmap. Explicit
24-point line spacing plus 12-point paragraph spacing fixes four 36-point text
bands starting 72, 108, 144 and 180 points below the page top.

- The Latin line contains `COMBINING cafe` followed by a combining acute accent,
  then `A` followed by a combining ring. These are decomposed source characters.
- Four Chinese characters use explicitly selected Microsoft YaHei and left-to-right
  placement. Their source code points are U+4E2D, U+6587, U+6D4B and U+8BD5.
- The Hebrew word has source code points U+05E9, U+05DC, U+05D5 and U+05DD. It uses
  Arial with explicit paragraph/run right-to-left properties and logical start
  alignment, placing its right edge at the right margin. All four paragraphs use
  logical start alignment; the other three have left-to-right direction.
- An Arial `ANCHOR_MARKER` line provides a final text position. The first paragraph
  owns a floating bitmap whose page-relative position is (360,240), sized 144 by
  96 points. Its left half is red and right half blue. It must not reflow the text.

The fixture uses the already installed Arial and Microsoft YaHei fonts; the
execution receipt binds the two source font-file hashes before/after the run.
It copies no Windows font into a distributable artifact. The package contains
no macros, external relationships, fields, tracked changes or embedded objects.
Its direction and anchor declarations follow Microsoft's
[run direction](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.righttolefttext),
[paragraph direction](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.bidi)
and [floating anchor](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.drawing.wordprocessing.anchor)
models.

Before export, the oracle requires the complete character inventory and Latin,
CJK and marker sequences. It compares canonical Unicode decompositions, retaining
every accent; NFC/NFD-equivalent representation is allowed, compatibility folding
is not. This follows [Unicode canonical equivalence](https://www.unicode.org/faq/normalization.html).
Whitespace and PDF direction-format marks are excluded from the character
inventory; the actual glyph positions separately establish reading direction.
Each CJK/Hebrew source character occurs exactly once; CJK positions ascend and
Hebrew positions descend in source-character order.

Every glyph must fit its declared text band and page margins within one point.
Left-aligned lines start within four points of the left margin; Hebrew ends
within four points of the right margin. Fonts must be embedded, use the expected
regular family and remain within 0.01 point of the authored 18-point size.
PDFium records each glyph's bounds, size, font name/flags and embedded-program
hash. This corroborates font selection; program hashes alone do not prove every
glyph outline matches an external reference document.

The 96-DPI image check requires each colored half at its authored page position,
within two raster pixels at the edges, at least 97% filled area and eight code
values of channel variation. Page geometry retains the existing 0.1-point limit;
raster dimensions follow the probe's ceiling rule. Negative controls remove an
accent or glyph, add text, reverse Hebrew placement, substitute a CJK font, move
text, remove embedding and remove the image. A failed baseline cannot pass controls.


Execution
---------

```powershell
python -B tools/office-engine/Test-OfficeExecution.py --create-disposable-profiles `
  --word-layout --office-engine '<pinned Office engine>' `
  --pdf-engine '<pinned qpdf engine>' --pdf-renderer '<pinned PDF renderer>'
python -B tools/office-engine/Inspect-WordFontStyles.py --word-layout `
  --execution-id '<printed execution ID>' --pdf-prepared '<qpdf preparation>' `
  --pdfium-prepared '<PDFium preparation>'
```

The existing explicit disposable-profile authorization remains required. The
actual application command must publish a validated copy, preserve original
bytes/times and remove its worker, profile mapping, context and publication
records. A reported missing font keeps this fixture unaccepted even if the test
retains a PDF for diagnosis. Independent inspection verifies regenerated package
parts, qpdf structure, PDFium pixels/text/font observations and unchanged sources.
No installation, registration, live licensing or native recycling is involved.


Fixture correction and retained first observation
------------------------------------------------

Execution `29d943fd64034b0cb232bef95c12511c` passed all seven application checks
with unchanged inputs. Independent receipt `161821f662c149d4ba125db8add57595`
failed placement: the Hebrew ink ended at 107.068 points rather than near the
540-point right margin. Exact characters, direction, embedded fonts and anchored
image checks passed. The assistant inspected the rendered page and confirmed
the left-positioned Hebrew line; this is not owner acceptance.

The original fixture incorrectly assumed legacy `jc=right` means the physical
right edge in a bidirectional paragraph. Microsoft's
[Word compatibility note](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oe376/26ecf09a-0f0b-4574-9907-ebd1ddf3015f)
documents reversed left/right semantics for those paragraphs in Word 2007.
The pinned renderer's
[paragraph importer](https://github.com/LibreOffice/core/blob/8221e31b3ac356a1623c672912a3d2b492f7e3d1/sw/source/writerfilter/dmapper/DomainMapper.cxx)
also explicitly exchanges those values. This observation does not establish a
product alignment defect against an independently rendered Word reference.

The corrected fixture uses explicit `jc=start`, supported by
[Office 2010 and later](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.justificationvalues),
and places `bidi` before `spacing` according to the
[Open XML SDK paragraph schema](https://github.com/dotnet/Open-XML-SDK/blob/v3.3.0/data/schemas/schemas_openxmlformats_org_wordprocessingml_2006_main.json).
It retains the intended right-margin placement, all text/image geometry and
every original oracle tolerance. The first source/PDF/inspection remains intact;
the corrected fixture requires a fresh export, not rescoring changed evidence.


Corrected application and PDF evidence (2026-09-19)
-------------------------------------------------

Fresh execution `bc059d6942004100a60aafb021a33630` passes all seven application
checks. The retained worker from the first execution matched the freshly built
worker's complete top-level file set before reuse; native engine identities
remain pinned. The source document, engine inputs, managed sources/binaries and
the two installed font files remain unchanged. The disposable profile directory,
registry mapping, worker, context and publication journal are removed.

Independent receipt `7fd4afad1a324cf9a613151ed4a5ff91` passes with unchanged inputs.
Its results are in `word-layout-inspection-291b018ad63746cca4442aad235d8df3` beneath
that execution directory. qpdf accepts the structure. PDFium reports one exact
612-by-792-point page with an 816-by-1056-pixel opaque raster. Complete character
inventory, combining accents, CJK/Hebrew direction, all four text bands and the
two colored image halves pass. Hebrew ink ends at 539.118 points, within the
unchanged four-point right-edge limit. Both expected font families are embedded;
every measured glyph remains exactly 18 points.

All eight negative controls reject their altered observations. A separate
schema-order receipt checks each of the four paragraph-property and four
run-property child sequences against the pinned SDK schema; this is not a full
package-schema validation. The assistant inspected a lossless PNG of the actual
PDFium raster: accents, Chinese and Hebrew text are visible, the Hebrew line is
right aligned and the image retains its authored position. This does not prove
owner acceptance, screen-reader delivery or comparison with a real Word report.

The shared bounded PDFium glyph reader also repeats the prior PowerPoint
observations exactly in receipt `36d7025feded4374b734c773d10a9073`. That comparison
retains the known D09 font-size failure; it does not change the pending tolerance
decision. The Word result closes the authored automated D04 case only. Excel
chart preservation, Office network enforcement, D10-D12 real documents, human
review and final combined-candidate replay remain required.

The Release foundation run `word-layout-foundation-fee4db61524c4773bcd127f0d0dfc725`
passes 4,384 contracts. Six parser/authorization/scenario guards pass in
`word-layout-guards-7ab17ea3f07a4f78a92303afc97d034c`. Release builds have zero
warnings/errors; repository validation passes 192 documentation files. No
production behavior, installed app or reserved candidate payload is changed.


Source-built application replay, 2026-09-23
------------------------------------------

The [paired runtime selection](office-runtime-verification.md#source-built-application-selection-2026-09-23)
repeats D04 through a freshly built application worker at
`.codex-temp/office-execution/dec1c8897aeb4588b5361965a93f5a51`.
The application publishes a validated 31,691-byte PDF copy, preserves the source
and retires its operation context and disposable Windows profile. The normal
font decision path needs no substitution prompt for this fixture. The receipt
binds the actual worker and native runtime; this is not a formal packaged-worker
or final-candidate result.

Independent qpdf/PDFium inspection passes in
`word-layout-inspection-receipt-02b96bdd2eca472fa0046e48e00a5c0d.json` within that
execution directory. The unchanged character, accent, CJK/RTL direction,
geometry, embedded-font, anchored-image and eight negative-control assertions
pass. Both execution and inspection retain unchanged recorded inputs; Release
builds report zero warnings/errors. This replay adds automated evidence only:
human review, real documents and final-candidate acceptance remain open.
