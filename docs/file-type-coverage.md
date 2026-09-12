File Type Coverage
==================

Revision: 2026-09-11.10; expanded catalog and bounded structure analysis, not release acceptance

Recognition and capabilities
----------------------------

The embedded [catalog](../src/ContextSuite.Core/Analysis/file-types.json) currently
contains 243 independently authored descriptions; aliases do not inflate this count.
The [inventory](file-type-inventory.md) lists each entry and its actual detector status.
Catalog schema 1 has stable IDs, extension aliases, exact filenames,
families, typical uses, optional MIME identifiers and source references. It contains no commands or
conversion permissions. Validate its schema, IDs, aliases and sources with the
foundation contracts. Preserve IDs across revisions; incompatible schemas require
an explicit reader update and migration decision.

The historical [source retrieval audit](catalog-source-review.json) covers 223 references:
186 retrieved, 7 search-indexed and 30 unavailable to the research tool. This
is a reachability/title review, not completed factual, variant or reuse-rights
acceptance. Unavailable retrieval does not establish a broken URL. MIME coverage
beyond the first 36 records and complete description/detector provenance remain pending. The later
[common image review](image-header-analysis.md) verifies four descriptions and
their newly implemented bounded detectors against primary specifications.
The [alias review](catalog-alias-review.md) adds six missing alternative meanings
and corrects the static/import-library description; none adds a content detector.

The [MIME review](catalog-mime-descriptions.md) supplies 59 descriptive identifiers
for 36 records, with per-identifier provenance and qualified technical details.
The [font header review](font-header-analysis.md) verifies five font descriptions
and adds bounded declarations without installation, rendering or decompression.
The [audio purpose review](catalog-audio-review.md) checks all eighteen Audio
descriptions and updates fourteen records' wording/references. It separates
playlists, projects, musical instructions and audio containers; full alias and
variant provenance remains a separate review scope.
The [document purpose review](catalog-document-review.md) checks 31 document,
spreadsheet and presentation records and clarifies fourteen descriptions/references.
It preserves recognition fields and does not imply Office conversion is available.
The [archive/package purpose review](catalog-archive-review.md) checks 29 archive,
compressed-stream, software-package and disk-image descriptions, updating sixteen
records. Variant/alias and executable-capability acceptance remain separate.
The [source/configuration purpose review](catalog-source-code-review.md) checks
49 records and improves twenty descriptions/references, distinguishing source,
automation, declarations and settings. It adds no execution or syntax validation.
The [data purpose review](catalog-data-review.md) checks all 37 Data records and
updates fifteen descriptions/references, including Feather versions and pickle
loading behavior. Database, scientific and application semantics remain separate.
The [video purpose review](catalog-video-review.md) checks ten records and improves
seven descriptions/references. Containers remain distinct from codecs, and no
video conversion or playback capability is added.
The [document font reference scan](document-font-references.md) adds optional
literal names and unresolved theme references from selected OOXML XML parts,
with declared scope and unavailable results on budget/format failure.
The [workbook settings scan](workbook-settings-analysis.md) reports saved date
and calculation flags from the main spreadsheet XML without extra package reads.
Missing flags are not assigned defaults; effective rendering behavior is not inferred.
The [Word revision scan](word-revision-analysis.md) counts four marker kinds in
the main XML, with explicit exclusions and unavailable counts when compatibility
processing is required. It does not decide what a PDF export should display.
The [PowerPoint visibility scan](powerpoint-slide-analysis.md) distinguishes
visible, hidden and default-setting slides from a complete bounded reference scan.
Invalid or over-budget slide details retain identity and declared totals.

Current implementation and remaining work are separate:

| Type | Identification evidence | Detailed facts now | Convert / Optimize |
| --- | --- | --- | --- |
| DDS | Signature and existing DDS header parser | Existing texture structure, raw identifiers, dimensions, mip/array/depth, alpha and color interpretation, payload accounting and warnings | Existing bounded DDS conversion; no new operation |
| PNG | Signature, optional first IHDR | Declared dimensions, sample depth and raw color type; transparency/animation explicitly unavailable | Existing bounded image conversion and PNG presets |
| JPEG / GIF / BMP / WebP | Signatures and bounded supported headers | Declared dimensions and format-specific header facts; actual transparency/animation/orientation/color remain unavailable | Recognition adds no transformations; existing image capability rules remain separate |
| PDF | Initial PDF signature; optional bounded complete-snapshot worker probe | Header version; with the candidate engine, reported pages/encryption/forms/attachments/bookmarks; locked content remains unavailable | Optional PDF-to-PNG and structural optimization implemented; default release adoption pending |
| ZIP | Initial record signature plus bounded ZIP32 directory when available | Directory count; selected document declarations only; no extraction | None |
| Compound file | Signature and bounded CFB directory/allocation inspection | Container version, sector size and reachable/root stream counts; container alone does not identify an Office family | None |
| DOC / XLS / PPT | Root-level stream names agreeing with supported binary headers | Bounded legacy version/size/encryption declarations; rendered pages and active content unavailable; identity likely | PDF conversion selected; implementation pending |
| DOS/Windows executables | MZ; PE signature/COFF header at a bounded declared offset | Raw PE machine and section count where present; no execution | None |
| Text | Strict UTF-8/UTF-16/UTF-32 sampling with recognized BOMs where present | Possible encoding, explicitly derived; no application-purpose inference | None |
| JSON | Whole-file object/array parsing within 64 KiB and depth 32 | Root kind and top-level count; no application semantics | None |
| XML | Whole-file parsing within 64 KiB and depth 32; DTDs prohibited and resolver disabled | Root name, namespace and element count; no schema validation | None |
| WAVE | RIFF/WAVE and bounded chunk/header declarations | PCM/float versus uninterpreted codec, channels/rate, sample container/valid bits, raw speaker mask, first data chunk size and derived PCM timing | Fixed audio conversion implemented with the optional reviewed engine |
| AIFF / AIFF-C | FORM tag and bounded Common Chunk | Declared channels/frames/original bits, raw compression code and 80-bit rate; approximate rate/timing | None added |
| AU | Signature and fixed header | Declared encoding/channels/rate/extent; interpreted PCM precision and aligned derived timing | None added |
| FLAC | Marker and first STREAMINFO declaration | Channels/rate/precision/sample count and derived duration, declared checksum presence, observed comment/picture block counts and metadata-list completeness; no frame or metadata-content validation | Fixed audio conversion and lossless recompression implemented with the optional reviewed engine |
| Ogg | Ogg page marker | Container only; does not imply Vorbis or Opus | Optional audio conversion requires separately probed supported codec; recognition alone is insufficient |
| DOCX / XLSX / PPTX | Agreeing package relationship, main content type and main XML root within fixed limits | Declared sheet/slide counts, bounded slide visibility and macro-enabled type; scoped Word revision markers; rendered pages unavailable; identity likely | PDF conversion selected; implementation pending |
| ODT / ODS / ODP | Agreeing MIME, manifest and supported unencrypted content family | Sheet/slide elements, declared content encryption; encrypted content remains unavailable; identity likely | Analysis only selected |
| Other readable regular files | Generic fallback regardless of extension | Size, inspected-byte count, unknown identity or qualified filename hint | No new operation |
| TrueType / OpenType / font collections / WOFF / WOFF2 | Font tags and available fixed headers; additional plausibility checks for numeric TrueType tag | Declared flavor, table/font counts and packaged-size references; table contents, glyphs, names and rights unavailable | None |

Other existing image conversions retain their earlier matrix; absence from this
initial Analyze catalog does not remove them. Likewise, a catalog entry does not
add a transformation. See the [converter design](converter-design.md) and
[expanded goal](broad-file-support-goal.md) for their respective scopes.

Confidence and limits
---------------------

Confirmed means the DDS header or bounded JSON/XML structure was parsed, not that
all format/application semantics are valid. Other content matches are qualified as
likely. A filename hint explicitly says its specific format was not validated.
Exact filenames take precedence over suffixes; the longest suffix wins, with
shared aliases retaining multiple candidates. Text compatibility can narrow hints
without validating a programming language or application type. Contradictory filenames and
unconfirmed ZIP/Office combinations carry an explanation. Ambiguous and unknown
states remain part of the result model; no confidence percentages are invented.

The basic application reader inspects at most 65,536 bytes, with no decompression,
recursion, full-file hash, worker requirement or paid admission. ZIP inputs can
add bounded directory/document-part reads and decompression under the same lease;
see [document budgets and evidence](document-design.md). Compound inputs can add
at most 2 MiB of managed reads under that lease for the
[legacy Office reader](legacy-document-analysis.md). When the optional
audio payload is present, eligible media can additionally supply at most 1 MiB
to its worker probe under the same read lease. Optional PDF probing uses a complete
snapshot up to 16 MiB because its engine needs seekable input; larger files retain
the header report. Explicit [combined production staging](pdf-production-payload.md)
includes the reviewed audio/PDF candidates; default release packaging omits them.
It rejects linked paths,
devices and offline/recall-marked items before reading content. An opened disk-file
handle is checked again; write/delete sharing is denied, and length/write-time
changes invalidate the result. Hard links do not require the publication path's
one-link restriction because Analyze is read-only.

The asynchronous content read has a five-second cancellation deadline. Filesystem
metadata queries and handle opening occur off the UI thread but do not yet have a
hard wall-clock timeout. Network-filesystem stalls, real cloud-provider hydration
behavior, mapped concurrent writers and reparse-point races need dedicated
acceptance before making stronger guarantees. These are not covered merely by
testing an offline attribute on a generated local file.

The WAVE/FLAC header reader uses at most 256 chunk/block
headers inside the same 64 KiB prefix. Length arithmetic never allocates from file
declarations. WAVE timing covers only its first data chunk and only consistent
PCM/float declarations; compressed streams need a later probe. FLAC zero total
samples means unknown, while zero sample rate has no inferred playback duration.
Metadata block observations do not validate tags/artwork contents or prove their
absence when inspection is incomplete. Audio identity stays likely; samples are
not decoded. Independently implemented from the
[FLAC specification](https://www.rfc-editor.org/rfc/rfc9639.html#section-8.2),
[WAVE fields](https://learn.microsoft.com/en-us/windows/win32/api/mmreg/ns-mmreg-waveformatex)
and [extensible WAVE fields](https://learn.microsoft.com/en-us/windows/win32/api/mmreg/ns-mmreg-waveformatextensible).

The first report shows type, qualified confidence, typical uses and size. Evidence
and typed technical facts are in a collapsed expander. No selected file content is
displayed or logged by the text detector. Missing detailed properties are reported
as unavailable, not absent. The existing shell manifest already applies Analyze
to `*`; its state check does no content parsing. No registrations were changed.

Verification
------------

- `tools/Test-Foundation.ps1`: 1,034 contracts pass, including 412 Analyze/catalog/audio
  checks. Fixtures cover every initial signature, each truncated prefix, altered
  signatures, misleading names, UTF encodings, hostile PE offsets, PNG declared
  sizes, unknown/empty files, cancellation and bounded real-file reads. Additional
  checks cover catalog record reachability, compound suffixes, ambiguous names,
  qualified language hints, JSON/XML facts, malformed/deep structure, DTD rejection
  and valid prefixes that must not imply whole-file structural confirmation.
  Audio checks include PCM/float/extensible WAVE, contradictory rates/lengths,
  unknown subformats, FLAC zero/maximum counts, invalid STREAMINFO, incomplete
  metadata, record limits, truncated prefixes and 1,000 deterministic mutations.
  The total also includes 25 separate FLAC recompression-metadata contracts and
  21 typed audio-probe parser/protocol contracts. Optional audio probing is connected
  through the worker and Analyze and has 11 isolated workflow checks; normal
  packaging still omits the evaluation audio payload.
  See [their independent scope](audio-engine-evaluation.md).
  A later artwork slice adds explicit attached-picture facts with an
  `embedded artwork` label and omits irrelevant audio-only fields for those
  streams. Its isolated worker/Analyze run has 14 checks; fresh overall foundation
  totals and staging are tracked in the [active goal](broad-file-support-goal.md).
  The new full-header FLAC descriptive inventory is used by private optimization,
  not by the bounded header-only Analyze reader. It does not change the header
  coverage claims above.
  Another 47 document contracts cover bounded ZIP32/OOXML/OpenDocument inspection,
  unsupported/malformed fallback and source preservation; see [document evidence](document-design.md).
  Another 29 [PDF parser/typed-fact checks](pdf-engine-evaluation.md) and 14 PDF
  reader/enrichment/transport contracts pass, plus 16 private adapter and 11
  isolated app-to-worker checks. Optional PDF Analyze integration now supplies
  detailed properties when its evaluation engine is present; ordinary staging
  still omits that engine.
- A real application view-model batch handles a locked file followed by unknown
  binary, PDF and text results. A nonexistent worker and throwing access service
  prove this path needs neither worker startup nor licensing. Source bytes and
  write timestamps remain unchanged; no output publication is created.
- `tools/Test-ViewContracts.ps1`: 76 hidden contracts pass, including the initially
  selected summary, filename-hint labeling and collapsed bound evidence. These do not prove visible layout,
  focus, screen-reader delivery, other themes or DPI levels.

Tests author disposable fixtures under `.codex-temp/foundation-tests/analysis-*`
and retain them for inspection. Signature fixtures intentionally do not claim to
be complete valid documents/media. No third-party fixture assets, reference code
or registry database have been copied. Catalog descriptions are original short
explanations; linked specifications supply identification facts, not copied prose.

Next: finish catalog source/description review, extend container and
family analysis with explicit budgets, complete real activation/mixed-file
acceptance and the manual report review, then deliver audio and selected document
actions. The isolated Release stage
`artifacts/production-staging/b6cd87374f0946ba92c76ca7a80737b2` includes optional PDF
worker integration and passes managed build/payload checks with `-SkipShell`.
It excludes evaluation audio/PDF engines and has no new visual/installed acceptance.
