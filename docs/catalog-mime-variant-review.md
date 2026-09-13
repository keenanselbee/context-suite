Catalog MIME Variant Review
===========================

Reviewed 2026-09-13. All **59 current MIME claims across 36 records** match the
IANA registry's spelling and registration link, with case-insensitive identifier
comparison. All 59 individual registrations were retrieved. Their family and
variant associations are reviewed below. The remaining 207 records make no MIME
claim; additional meaningful coverage and the wider alias review remain open.

Catalog revision **2026-09-13.1** adds seven alias associations to four existing
records. It preserves all 243 IDs, names, descriptions, MIME fields, confidence
rules, detectors and executable capabilities. The [previous MIME record](catalog-mime-descriptions.md)
lists every identifier; each retains its individual registration source in the
[catalog](../src/ContextSuite.Core/Analysis/file-types.json).


Registration and variant findings
----------------------------------

This table partitions the 36 MIME-bearing records. Counts refer to their current
identifiers, not aliases or independently detected formats. Registrations do not
prove a selected file's conformance, encoding parameters or application behavior.

| Catalog IDs | MIME claims | Reviewed association and qualifications |
| --- | --- | --- |
| `avif`, `heif` | 5 | AVIF is the AV1 specialization; HEIF/HEIC and sequence types have distinct codec/brand constraints and shared suffixes. No brand inspection or sequence validation is added. |
| `bmp`, `gif`, `jpeg`, `tiff`, `webp` | 5 | Registered image families agree. This does not establish every grouped suffix, compressed BMP variant, JPEG application segment or TIFF profile. |
| `png` | 2 | PNG and APNG share a signature. Listing both does not establish animation. |
| `svg` | 1 | XML-based image content; the registration also describes gzip aliases. Generic XML parsing does not establish SVG semantics. |
| `css`, `csv`, `json`, `markdown`, `yaml` | 5 | The named text/data families agree. Dialects, charset/variant parameters and application semantics remain undetermined. JSON permits more roots than the local object/array detector. |
| `html` | 2 | HTML and XML serialization use different registrations. XHTML explicitly includes both xhtml and xht; a generic XML parse does not validate XHTML. |
| `javascript` | 1 | text/javascript names js and mjs. It does not establish JSX or CommonJS semantics for the grouped jsx/cjs aliases, which need separate alias evidence. Deprecated MIME spellings are not added. |
| `text` | 1 | Plain-text family only; a log suffix or readable sample does not establish a complete encoding or transport declaration. |
| `xml` | 2 | application/xml and text/xml do not select a vocabulary/schema. External-entity execution is never permitted by a MIME claim. |
| `epub` | 1 | EPUB's OCF ZIP container, not any ZIP archive or XML document. |
| `pdf` | 1 | application/pdf includes multiple versions and subset standards. It does not establish PDF/A, PDF/X, accessibility, encryption or signature validity. |
| `gzip`, `zip` | 2 | Registered compressed-stream/archive families agree. The old ZIP registration does not establish later ZIPX methods or extraction compatibility. |
| `flac` | 1 | Native FLAC; Ogg-contained FLAC belongs to Ogg container handling. No decoded-audio guarantee follows. |
| `m4a` | 1 | audio/mp4 describes MPEG-4 audio. The registration names mp4/mpg4, not every m4a/m4b convention. It does not choose AAC/ALAC or justify an audio-only identity from a generic MP4 probe. |
| `mp3` | 1 | audio/mpeg covers more than Layer III and lists mp1/mp2/mp3. That is not authority to alias every MPEG audio layer to this MP3 record. |
| `ogg` | 3 | General, audio and video registrations are distinct. Audio includes spx. A container marker does not identify its codec or satisfy the registrations' stream constraints. |
| `doc`, `xls`, `ppt` | 3 | The legacy registrations name Word, Excel and PowerPoint; they are not exact specifications for every grouped document/template/slideshow version. |
| `docx`, `xlsx`, `pptx` | 14 | Ordinary, template, macro-enabled and PowerPoint slideshow registrations agree with the grouped suffixes. Lowercasing macroEnabled in identifiers does not change registration URL case or prove actual macro presence/absence. |
| `odt`, `ods`, `odp` | 6 | Each ODF family and template counterpart agrees with its paired suffixes. Package validity, encryption and rendered layout remain separate. |
| `rtf` | 2 | Both application/rtf and text/rtf describe RTF. Neither selects a version or establishes embedded-content safety. |


Alias corrections
-----------------

The [AVIF registration](https://www.iana.org/assignments/media-types/image/avif)
lists avif, heif, heifs and hif. AVIF previously listed only avif; it now includes
the three shared names. The
[HEIF sequence registration](https://www.iana.org/assignments/media-types/image/heif-sequence)
and [HEIC sequence registration](https://www.iana.org/assignments/media-types/image/heic-sequence)
name heifs and heics, now included in the grouped HEIF record. Both may use hif.
Undecoded heif/heifs/hif inputs retain AVIF and HEIF candidates rather than
selecting a codec or exact variant.

The [XHTML registration](https://www.iana.org/assignments/media-types/application/xhtml+xml)
supports adding xht to HTML. The
[audio/ogg registration](https://www.iana.org/assignments/media-types/audio/ogg)
supports adding spx to Ogg. It remains a family hint; no Speex codec detection or
conversion is added. Other registration/catalog suffix differences are recorded
above rather than mechanically importing every name. Registrations can describe
a wider family; established aliases may have evidence in other primary sources.


Evidence and verification
--------------------------

The registry reports update date 2026-09-03. Its downloaded XML SHA-256 is
`2af32eea294667f49c55c7121ba99c927764b5e465f4d3853d26a754ebb68490`, matching the
earlier snapshot. Individual registration responses, hashes and extension-review
excerpts remain in
`.codex-temp/catalog-mime-review-100bf65e08c444a8bf788d748d0d3e9b/review.json`.
That report establishes retrieval and identifier/link membership; the table
supplies the separate semantic review. Neither substitutes for file tests.
No registration prose or database is embedded in the product. The existing
[registry reuse record](catalog-mime-descriptions.md#provenance-and-reuse) applies.

All **2,403 foundation contracts pass**, including 12 added checks. Shared
HEIF/AVIF suffixes exercise uppercase lookup, undecoded ambiguity and contradictory
PDF content. Further cases retain a HEIC-sequence filename hint, identify only
Ogg for an SPX marker, and keep well-formed XHT content at generic XML identity.
Log: `.codex-temp/catalog-mime-alias-foundation.log`; the matching exit file records
code 0. This is current-worktree evidence, including separate trial-policy edits.
It is not full image/audio decoding or visible acceptance.

The independent delta/reconciliation report is
`.codex-temp/catalog-mime-alias-delta.json`. It permits only the revision and seven
appended aliases, verifies the inventory's changed suffix lists, and accounts for
every current MIME-bearing ID and all 59 claims without duplicate counting.
The first table reconciliation found an omitted PDF row; adding that row completes
the review partition without changing code. All 108 documentation files,
source-boundary/theme policy and both repositories' whitespace checks pass.

The existing staged payload predates these aliases. This checkpoint does not
replace its receipt or claim fresh production/installed acceptance; the next
integrated package must include the new catalog revision.
