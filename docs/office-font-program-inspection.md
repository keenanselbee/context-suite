Office Embedded Font Program Inspection
=======================================

Reviewed 2026-09-14 using the six retained passive
[font-substitution exports](office-font-substitution.md). The inspection finds
eight embedded TrueType programs with seven distinct byte hashes. Their internal
PostScript-name declarations agree with the PDF font descriptors. This strengthens
the recorded substitution evidence without establishing authentic vendor identity,
per-character font selection or a production missing-font detector.


Observed programs
-----------------

The source documents, existing PDFs and requested-font declarations are checked
against the retained evaluation hashes. Pinned qpdf rereads each actual PDF;
the inspection does not rely only on the old font-object JSON. No Office process
or new rendering runs, and no font is installed or loaded for rendering.

| Existing export | Internal family/style declarations | Embedded program bytes |
| --- | --- | --- |
| Word control | Arial / Bold; Calibri / Regular | 27,680; 40,012 |
| Word missing-font case | Bodoni MT Black / Regular; Calibri / Regular | 14,124; 40,012 |
| Excel control | Arial / Regular | 34,060 |
| Excel missing-font case | DejaVu Sans / Book | 22,416 |
| PowerPoint control | Arial / Regular | 34,188 |
| PowerPoint missing-font case | DejaVu Sans / Book | 22,532 |

Word's Calibri program is byte-identical across the pair:
`790C06C349F8BD309BDC809EEB6FB85351FB7673CB229DB5825F16D808A4D2FC`.
This is an unchanged-program control alongside the changed title font.
The Bodoni program additionally declares typographic family `Bodoni MT` and
subfamily `Black`. Its legacy family/style pair therefore cannot be treated as
the only spelling of its family and weight. Localized name records also remain
separate rather than being flattened into one arbitrary display name.

Excel and PowerPoint's Arial programs have different complete hashes but identical
`name` tables. Their `cmap`, `glyf`, `head`, `hhea`, `hmtx`, `loca` and `maxp` table
bytes differ. For example, `glyf` occupies 13,082 versus 13,214 bytes. This proves
that a whole embedded-program hash is not a stable lookup key for the declared
font family across these documents. The differing bytes are recorded, not treated
as corruption or accepted typography differences.

These results do not identify installed source-font files or prove which program
supplied every visible character. Font-name strings remain declarations, even
inside a font program. Required Office conversion still needs renderer-environment
resolution, style/theme/alias handling, per-script glyph fallback and a compact
customer decision when fidelity cannot be maintained. The pending isolation test
and required converter are not replaced by this inspection.


Inspection and limits
---------------------

[Inspect-OfficePdfComparison.py](../tools/office-engine/Inspect-OfficePdfComparison.py)
now accepts the retained `FontSubstitution` mode as well as its existing four-PDF
`LegacyPdf` comparison. It verifies the pinned qpdf archive and all 291 unpacked
files before use, refuses changed source/PDF hashes, and records the fresh PDF
object graph, font-program bytes, table extents/hashes and selected names in a
new repository scratch directory. PDF page counts and font declarations must
match the saved export observations.

The independently authored directory/name reader follows Microsoft's
[OpenType container](https://learn.microsoft.com/en-us/typography/opentype/spec/otff)
and [naming-table specification](https://learn.microsoft.com/en-us/typography/opentype/spec/name).
It reads legacy family/style, full, PostScript and typographic family/style names
(IDs 1, 2, 4, 6, 16 and 17), retaining platform, encoding and language identifiers.
Supported Unicode records use UTF-16BE; supported Macintosh Roman records use
their declared encoding. Unknown encodings remain undecoded. Version-one language
tags remain distinct from names; user-defined version-zero platform identifiers
are not assigned a guessed locale.

Limits are 128 tables, 1,024 naming/language records, 4 KiB per decoded string and
64 KiB across decoded selected-name/language bytes. Truncated/overlapping table
extents, invalid naming offsets and malformed Unicode are refused. Absence of a
name table and an unsupported naming version are explicit. The reader does not
validate checksums, glyph instructions, layout tables, collections, CFF programs
or every font invariant. Recorded checksum values are declarations; table SHA-256
values are measured. It does not evaluate embedding or redistribution rights.


Verification and retained evidence
----------------------------------

- Ten in-memory tests cover declared encodings/languages, missing/unknown data,
  selected-name scope, malformed extents, duplicate/overlapping tables and budgets.
- All six retained PDFs are inspected successfully. An independent reconciliation
  rehashes all eight extracted programs and their tables, checks internal
  PostScript names against descriptors, and confirms original source/PDF hashes.
- Four workflow guards refuse an unrelated mode, changed Office source, changed
  PDF and an evaluation path outside the Office scratch tree before qpdf execution.
- The existing four-PDF Excel/PowerPoint modern/legacy comparison still passes.

Run the in-memory checks with
`python -B tools/office-engine/Test-OfficeFontInspection.py`. Run retained-result
inspection with `python -B tools/office-engine/Inspect-OfficePdfComparison.py '<evaluation directory>' '<prepared qpdf directory>'`.
Use only the harness's existing authored records, not customer documents.

Final font evidence is
`.codex-temp/office-pdf-inspection-21cf229a38944468946655fa27899ab2/report.json`;
the independent receipt is `.codex-temp/office-font-program-verification.json`.
Workflow guard inputs remain under
`.codex-temp/office-engine/font-program-guards-961951aedfaa428ba2bfd70624942e13`.
Logs use `.codex-temp/office-font-program-final.log` and
`.codex-temp/office-font-inspection-contracts-complete.log`.
The final legacy regression is retained at
`.codex-temp/office-pdf-inspection-041cd376848d4e7bb8e1c12d8aceecdc/report.json`,
with log `.codex-temp/office-font-program-legacy-final.log`.
An initial test fixture exceeded its own 16-bit offset representation; the corrected
aggregate-limit case uses shared string storage. That failed test run remains
recorded and is not counted as a pass.

No product code, reserved payload, installed state or private implementation
changes. No new visible, keyboard, screen-reader, theme/DPI or installed-shell
acceptance is claimed. Candidate 1.0.7 remains the current staged product.
