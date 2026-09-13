Catalog Detector Provenance
===========================

Reviewed 2026-09-13 against public implementation `53a6acb` and catalog
`2026-09-12.1`. This reconciles the current content-identification routes with
their format evidence. It does not add formats, alter confidence or enable an
operation. Exact filename aliases and MIME associations remain separate reviews.

There are **34 catalog IDs reachable through content identification**: 24 from
the prefix reader, nine from bounded document inspection, and MP3 from the
optional audio probe. The other **209 records** supply filename explanations;
their text-compatible flags can narrow hints but are not language validators.
These counts include generic containers and text. They are not counts of complete
decoders, conversion targets, or guaranteed identification of every valid variant.


Identification routes and evidence
----------------------------------

The prefix route is [HeaderAnalyzer](../src/ContextSuite.Core/Analysis/HeaderAnalyzer.cs).
It sees at most 64 KiB and uses the known file size for extent checks. A signature
can retain a likely family when later header details are malformed or unavailable.
Only the existing DDS parser and whole-file JSON/XML structure paths produce
confirmed identification; even those do not certify application semantics.

| Catalog IDs | Implemented evidence and limits | Provenance |
| --- | --- | --- |
| `dds` | Initial `DDS `; supported legacy/DX10 header parsing can promote likely to confirmed. No texture decoding. | [Microsoft DDS layout and validation](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-pguide), implemented by [DdsParser](../src/ContextSuite.Core/Dds/DdsParser.cs) |
| `png` | Eight-byte signature; an available 13-byte first IHDR supplies dimensions, raw depth and color type. No CRC, later-chunk or pixel validation; no APNG-versus-static inference. | [W3C PNG signature and IHDR](https://www.w3.org/TR/png-3/) |
| `jpeg`, `gif`, `bmp`, `webp` | JPEG SOI/marker prefix, GIF87a/89a, BMP `BM`, or RIFF/WEBP. Supported first headers add declarations; raw headerless DIB is not identified by this route. | [Image-header review](image-header-analysis.md), including ITU JPEG, W3C GIF, Microsoft BMP and Google's WebP specifications and exact supported variants |
| `pdf` | Initial `%PDF-`; an available digit-dot-digit supplies the header version. This does not identify PDF/A, PDF/X, accessibility or actual document compatibility. | [RFC 8118 section 8](https://www.rfc-editor.org/rfc/rfc8118.html#section-8); optional complete-snapshot facts remain under [PDF integration](pdf-production-payload.md) |
| `zip` | Initial local-header, empty-directory or spanning/data-descriptor signature (`PK` followed by 03/04, 05/06 or 07/08). No archive-family inference from the marker. The later document reader has stricter ZIP32 limits. | [PKWARE APPNOTE sections 4.3 and 8.5](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT); a spanning marker match does not imply split-archive parsing support |
| `ole` | Initial CFB eight-byte signature. Optional directory/allocation inspection retains a generic container unless root streams and binary declarations agree on a supported Office family. | [Microsoft CFB header](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-cfb/05060311-bfce-4b12-874d-71fd4ce63aea), [bounded compound inspection](legacy-document-analysis.md) |
| `mz`, `pe` | Initial `MZ`; PE additionally needs a bounded offset at 0x3C, `PE` and two null bytes, and an available COFF header. No section, executable safety or loadability validation. MZ alone does not establish Windows PE. | [Microsoft PE layout](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format), DOS stub, signature and COFF sections |
| `wave` | RIFF plus WAVE form type. At most 256 chunk headers; supported format declarations and first data chunk only. RF64, RIFX and Wave64 are outside this prefix detector. | [Microsoft RIFF/WAVE structure](https://learn.microsoft.com/en-us/windows/win32/xaudio2/resource-interchange-file-format--riff-), [WAVEFORMATEX](https://learn.microsoft.com/en-us/windows/win32/api/mmreg/ns-mmreg-waveformatex), [WAVEFORMATEXTENSIBLE](https://learn.microsoft.com/en-us/windows/win32/api/mmreg/ns-mmreg-waveformatextensible), [current field limits](file-type-coverage.md) |
| `aiff`, `au` | FORM/AIFF or AIFC, or `.snd`; bounded Common Chunk/fixed-header facts, with uninterpreted compression codes retained. | [AIFF/AU review](aiff-au-header-analysis.md), linking Apple's specifications and Sun/Oracle's AU definitions |
| `flac` | Initial `fLaC`; available first STREAMINFO and bounded metadata-block declarations. No compressed-frame, checksum-content or metadata-content validation. | [RFC 9639 stream and metadata structure](https://www.rfc-editor.org/rfc/rfc9639.html), [field scope](file-type-coverage.md) |
| `ogg` | Initial `OggS` capture pattern. No page checksum or codec identification from the prefix. An `.opus` name does not establish an Opus stream. | [RFC 3533 section 6](https://www.rfc-editor.org/rfc/rfc3533.html#section-6) |
| `truetype`, `opentype`, `font-collection`, `woff`, `woff2` | Numeric sfnt tag with directory plausibility, or OTTO/ttcf/wOFF/wOF2 tags. Font contents and decompression are outside this route. | [Font-header review](font-header-analysis.md), linking Microsoft OpenType and W3C WOFF/WOFF2 specifications |
| `text` | Strict UTF-8, or BOM-selected UTF-16/32; selected control characters exclude a text guess. A partial trailing character is allowed at the sample boundary. This is an encoding heuristic, not a text-format specification. | [Unicode encoding/BOM definitions](https://www.unicode.org/faq/utf_bom.html); the heuristic and control filter are Context Suite policy |
| `json` | A complete file within the prefix budget, beginning with an object or array, parses at depth at most 32. Scalar JSON remains text or a filename hint. Duplicate object names are counted, not rejected. | [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259.html); its grammar also permits scalar roots, so the current detector is explicitly a subset |
| `xml` | A complete file within the prefix budget parses through XmlReader, with depth limit, no DTD and no resolver. Root/namespace/counts do not establish SVG, HTML, an Office document or schema validity. | [W3C XML 1.0](https://www.w3.org/TR/xml/); no-DTD/depth constraints are local analysis limits, not general XML validity rules |
| `docx`, `xlsx`, `pptx` | A bounded ZIP32 package has one internal main relationship, a supported agreeing content type and matching Strict/Transitional main XML family. Ordinary/template/macro-enabled types share family IDs. No XLSB or rendering inference. | [Document package design and primary sources](document-design.md), [DocumentAnalysis](../src/ContextSuite.Core/Analysis/DocumentAnalysis.cs) |
| `odt`, `ods`, `odp` | Agreeing MIME and manifest plus supported unencrypted content family; encrypted main content uses agreeing package declarations and leaves content facts unavailable. No flat-XML or arbitrary ZIP inference. | [ODF package specification](https://docs.oasis-open.org/office/OpenDocument/v1.3/OpenDocument-v1.3-part2-packages.html), [document reader scope](document-design.md) |
| `doc`, `xls`, `ppt` | Root CFB streams agree with supported Word FibBase, BIFF8 workbook BOF or PowerPoint current-user/document headers. Earlier/other binary variants retain container facts. | [Legacy document review](legacy-document-analysis.md), including exact Microsoft field sources, version constraints and authored hostile fixtures |
| `mp3` | Only the optional bounded audio probe can promote this family to content identity. ID3, frame-sync-like bytes and filenames are probe-admission hints, not standalone MP3 detectors. | [AudioAnalysis mapping](../src/ContextSuite.Core/Analysis/AudioAnalysis.cs), [ffprobe format/stream reporting](https://ffmpeg.org/ffprobe.html), [pinned audio evaluation](audio-engine-evaluation.md) |


Optional probes and filename precedence
---------------------------------------

The [audio enrichment](../src/ContextSuite.Core/Analysis/AudioAnalysis.cs) maps
reported `wav`, `flac`, `mp3` and `ogg` containers to likely content identities.
The first, second and fourth already have prefix routes. All other container
strings retain the preceding identity while adding explicitly reported facts.
In particular, an MP4 probe does not promote the catalog's audio-only `m4a`
description: an ISO media container can hold other stream kinds. Probe failure
retains the basic result, and no worker is required for the generic reader.

The [PDF probe](../src/ContextSuite.Core/Analysis/PdfAnalysis.cs) requires an
already content-identified PDF and a complete bounded snapshot. It enriches that
identity rather than adding a detector for another catalog ID. Engine-reported
facts are not equivalent to decoded or rendered fidelity.

Exact filenames precede suffixes, and the longest suffix takes precedence over
shorter suffixes. Shared aliases retain candidate lists. Text compatibility can
select a filename-based identity while keeping the specific language unvalidated.
Content-identified JSON/XML remains its generic parsed family even when the
filename suggests a text-based application format. Empty files keep empty-file
identity. Recognition never confers a transformation capability.


Reconciliation and remaining work
---------------------------------

Review found and corrected one stale inventory row: MP3 was labeled filename-only
without mentioning its optional content probe. Generic detector labels now state
the actual bounded evidence. The 243 catalog records, aliases, descriptions and
runtime implementations are unchanged.

The reconciliation checks unique catalog/inventory IDs, all 34 content IDs against
the table above, and all 209 remaining rows as filename hints. It also binds the
review to hashes of the inspected identification sources. This is a static scope
and provenance review, not a new runtime or full-format conformance test.
The retained report is `.codex-temp/catalog-detector-reconciliation.json`.
All 107 documentation files, source-boundary/theme policy and both repositories'
whitespace checks pass. No product build is needed for this documentation-only change.
Existing foundation evidence is the 2,391-contract run recorded in the
[goal](broad-file-support-goal.md); it was not repeated for documentation changes.

Earlier image, font, audio and document reviews supply their scoped primary-source
and fixture evidence. This pass additionally retrieved the DDS, PNG, PE, CFB, Ogg,
FLAC, PDF registration, JSON, XML, Unicode and ffprobe references on 2026-09-13.
The older Adobe PDF download could not be retrieved; RFC 8118 directly supplies
the limited header evidence needed here. No specification text, external database
or implementation was imported.

This closes the current detector-route provenance inventory. It does not close
every alias/MIME association, untested format variants, hostile-input/resource
acceptance, or visible report acceptance. Future detector changes must update
this table and the inventory, with appropriate independent fixtures.
