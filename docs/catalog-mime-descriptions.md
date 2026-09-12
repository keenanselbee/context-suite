Catalog MIME Descriptions
==========================

Revision 2026-09-11.3 adds 59 MIME identifiers across 36 of the 243 catalog
records. These describe the catalog family; they do not establish the exact
variant or media type of the inspected file. Other records have no MIME claim.

Storage and presentation
-------------------------

Catalog schema 1 gains an optional `mimeTypes` array of `{ value, source }`
records. Omitted metadata defaults to an empty array, preserving older catalog
records. Each identifier has its own HTTPS provenance. Names use lowercase,
parameter-free type/subtype syntax with 1?127 characters per component and a
maximum of 16 entries per record. Null entries, duplicate identifiers, invalid
syntax and non-HTTPS references fail validation. Syntax validation does not
itself prove IANA registration or a correct file-family association.

Analyze shows these descriptions only in technical details, explicitly labeled
as catalog MIME types with the exact variant undetermined. It looks up the final
analysis identity, so a later document/container probe cannot leave an earlier
container MIME description attached to a more specific identity. It does not
assign a generic binary MIME type to unknown or ambiguous files, change identity
confidence, infer a codec, or enable an operation.

Office records include ordinary, template and macro-enabled variants; Ogg
includes general, audio and video registrations. Neither list selects a variant.
Similarly, PNG/APNG and HEIF families retain multiple registrations. The list
is useful reference information, not an HTTP Content-Type declaration.

Provenance and reuse
---------------------

The identifiers and registration links were checked against the
[IANA media-type registry](https://www.iana.org/assignments/media-types/) on
2026-09-11. The registry reports an update date of 2026-09-03. The downloaded XML
stays in `.codex-temp/iana-media-types-20260911.xml`, SHA-256
`2af32eea294667f49c55c7121ba99c927764b5e465f4d3853d26a754ebb68490`.
Only the selected identifiers and links enter the catalog; descriptions and
format associations are independently maintained. There is no runtime registry
download or dependency on scratch/reference files.

[IANA?s protocol-registry licensing statement](https://www.iana.org/help/licensing-terms)
applies CC0 1.0 to applicable rights IANA/IETF hold in those registries. Its scope
excludes linked RFCs and other materials. No RFC prose, parser code or registry
description text is copied. Name constraints follow the registration syntax in
[RFC 6838 section 4.2](https://www.rfc-editor.org/rfc/rfc6838.html#section-4.2).

This review establishes selected registration spellings and family mappings, not
complete catalog factual review or every MIME alias in use. The historical
[source retrieval audit](catalog-source-review.json) remains unchanged.

| Catalog ID | Descriptive MIME identifiers |
| --- | --- |
| avif | `image/avif` |
| bmp | `image/bmp` |
| css | `text/css` |
| csv | `text/csv` |
| doc | `application/msword` |
| docx | `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `application/vnd.openxmlformats-officedocument.wordprocessingml.template`, `application/vnd.ms-word.document.macroenabled.12`, `application/vnd.ms-word.template.macroenabled.12` |
| epub | `application/epub+zip` |
| flac | `audio/flac` |
| gif | `image/gif` |
| gzip | `application/gzip` |
| heif | `image/heif`, `image/heif-sequence`, `image/heic`, `image/heic-sequence` |
| html | `text/html`, `application/xhtml+xml` |
| javascript | `text/javascript` |
| jpeg | `image/jpeg` |
| json | `application/json` |
| m4a | `audio/mp4` |
| markdown | `text/markdown` |
| mp3 | `audio/mpeg` |
| odp | `application/vnd.oasis.opendocument.presentation`, `application/vnd.oasis.opendocument.presentation-template` |
| ods | `application/vnd.oasis.opendocument.spreadsheet`, `application/vnd.oasis.opendocument.spreadsheet-template` |
| odt | `application/vnd.oasis.opendocument.text`, `application/vnd.oasis.opendocument.text-template` |
| ogg | `application/ogg`, `audio/ogg`, `video/ogg` |
| pdf | `application/pdf` |
| png | `image/png`, `image/apng` |
| ppt | `application/vnd.ms-powerpoint` |
| pptx | `application/vnd.openxmlformats-officedocument.presentationml.presentation`, `application/vnd.openxmlformats-officedocument.presentationml.template`, `application/vnd.openxmlformats-officedocument.presentationml.slideshow`, `application/vnd.ms-powerpoint.presentation.macroenabled.12`, `application/vnd.ms-powerpoint.template.macroenabled.12`, `application/vnd.ms-powerpoint.slideshow.macroenabled.12` |
| rtf | `application/rtf`, `text/rtf` |
| svg | `image/svg+xml` |
| text | `text/plain` |
| tiff | `image/tiff` |
| webp | `image/webp` |
| xls | `application/vnd.ms-excel` |
| xlsx | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `application/vnd.openxmlformats-officedocument.spreadsheetml.template`, `application/vnd.ms-excel.sheet.macroenabled.12`, `application/vnd.ms-excel.template.macroenabled.12` |
| xml | `application/xml`, `text/xml` |
| yaml | `application/yaml` |
| zip | `application/zip` |

Verification
------------

All 1,884 foundation contracts passed, including 22 new checks for reviewed
variants, additive JSON compatibility, identifier/provenance roundtrip, invalid
names, null/missing metadata, duplicate/over-budget arrays and qualified report
placement. Existing identity, ambiguity and operation-admission tests also pass.
The log is `.codex-temp/catalog-mime-foundation.log`.

Fresh combined Release staging is
`artifacts/production-staging/cb2dac3abece449faed09f199db6df6c`. The application
build has zero warnings/errors; image/audio/PDF candidate dependencies, notices,
payload checks and complete file inventory pass. The build used `-SkipShell`;
no native shell rebuild, installation, new real-engine workflow or visible,
screen-reader, theme/DPI acceptance is claimed. Build log:
`.codex-temp/catalog-mime-production-cb2dac3abece449faed09f199db6df6c.log`.
