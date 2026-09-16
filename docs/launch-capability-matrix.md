Expanded Launch Capability Matrix
=================================

Matrix revision: **2026-09-16.7**. This checkpoint refreshes Office staging checks
after public baseline `c0c8ac3` and private baseline `6b8d84a`.
Catalog schema 1, revision **2026-09-16.2**, **245 records**.
Status: implemented candidates and recorded gaps; expanded launch acceptance
is incomplete. This matrix does not change the selected launch scope.

The source baseline and the last packaged candidate are different. Candidate
**1.1.0** contains images, audio and PDF engines, but no Office engine. Later
Office command and recovery work is verified in isolated scratch builds. Default
release packaging still excludes the optional audio/PDF/Office candidates. The
[explicit Office staging path](office-production-payload.md) is implemented;
all 33 scratch packaging checks pass with the current host, including copy-failure
retention and retry refusal. Fresh formal combined packaging remains
open. A menu label does not establish that a particular build can execute the
operation.

Read `Implemented` below as an application/worker path with bounded admission and
recorded automated evidence, subject to every stated condition. It does not mean
all variants, visual acceptance, redistribution or release adoption have passed.
`None` means no current transformation; `Open` means unresolved required scope
or acceptance, not permission to silently omit it from launch.


Analyze: recognition and factual inspection
-------------------------------------------

All readable regular files receive basic facts and either a qualified identity
or an unknown result. Analyze is local, read-only and independent of licensing.
It rejects devices, linked paths and offline/recall-marked items; remote-driver
and real cloud-provider behavior still need acceptance. Five seconds is a work
budget with cancellation, not a guaranteed return time from every Windows driver.

| Input family | Recognition | Detailed analysis currently implemented | Transformation relationship |
| --- | --- | --- | --- |
| Any regular file, including empty, extensionless and unknown | Filename hints, bounded content evidence or unknown | File size, inspected extent, evidence/confidence and available facts; unknown is a useful result | No conversion inferred |
| Catalog entries | 245 descriptions; 34 have content-identification routes and 211 are filename hints only | Plain-language typical uses, qualified evidence, optional descriptive MIME identifiers | Catalog has no executable permissions |
| DDS | Supported header structure | Texture dimensions, representation, mip/array/depth and payload declarations | Separate bounded texture conversion below |
| PNG, JPEG, GIF, BMP, WebP | Signatures and bounded headers | Available declared dimensions and format-specific header facts | Analyze does not decode pixels or certify transparency, animation or conversion eligibility |
| WAVE, FLAC, AIFF/AIFF-C, AU | Container/header evidence | Supported rate/channel/precision/extent declarations; derived timing where justified | AIFF/AU recognition does not add conversion |
| Eligible audio with optional probe | Container/codec evidence from bounded snapshot | Available stream, rate, channel, duration, bitrate and tag facts with limits | Execution separately inventories the complete admitted source |
| PDF | Initial signature; optional complete bounded probe | Header version; optional pages, encryption and document inventories | Zero signature-field count does not certify unsigned content |
| OOXML Word/Excel/PowerPoint | Agreeing package relationship, content type and XML family | Scoped package, sheet/slide, visibility, font-reference, workbook-setting, revision and relationship declarations | Grouped family identity does not admit templates or macro-enabled variants for conversion |
| ODT/ODS/ODP | Agreeing MIME, manifest and content family | Available declarations, sheet/slide elements and declared encryption | Analysis required; OpenDocument conversion has not been selected |
| Legacy DOC/XLS/PPT | Compound directory plus agreeing supported binary headers | Bounded version, size and encryption declarations | Legacy PDF conversion remains unimplemented and unresolved |
| JSON/XML/text | Bounded structure or strict encoding sample | Root/count facts for complete small JSON/XML; possible text encoding | No application semantics, DTD resolution or code execution |
| ZIP/compound files | Container structures | Bounded directory/allocation declarations; selected package facts | No archive extraction |
| Windows executables/fonts | Supported signatures and bounded headers | PE machine/section declarations; font flavor/table/count declarations | No execution, font installation or rendering |
| Other catalog families | Qualified hints unless a listed content route applies | Typical uses and generic facts | No video conversion, database execution, 3D processing or game-asset extraction |

The [coverage record](file-type-coverage.md) owns exact facts, confidence semantics
and resource budgets; the [inventory](file-type-inventory.md) owns the per-record
list. Basic reads use a 64 KiB prefix. Small complete JSON/XML is limited to
64 KiB and depth 32; optional audio uses at most a 1 MiB snapshot, PDF probing
16 MiB, and legacy compound inspection 2 MiB of managed reads. Package reads
have separate [ZIP/XML limits](document-design.md). These are different paths,
not one universal full-file read allowance.

All current typical-use descriptions have a review record, including the added
Alpine APK meaning. Remaining alias/variant and MIME review is still open.
No external format database or source prose has been imported. Detailed
provenance and detector coverage remain separate from descriptive coverage.


Image conversions
-----------------

`C` is an implemented conditional conversion; `T` additionally requires the
focused DDS policy; `-` is not a Convert operation. Rows are input, columns output.

| Input | PNG | JPEG | WebP | BMP | TGA | DDS |
| --- | --- | --- | --- | --- | --- | --- |
| PNG | - | C | C | C | C | T |
| JPEG | C | - | C | C | C | T |
| WebP | C | C | - | C | C | T |
| BMP | C | C | C | - | C | T |
| TGA | C | C | C | C | - | T |
| Bounded DDS | T | - | - | - | - | T |

These are the existing 27 conditional pairs, including DDS representation
changes. Same-format ordinary raster conversion belongs to Optimize. DDS-to-PNG
exports an explicitly selected color mip; it is not general numeric texture
export. Array/cube/volume, unsupported representations and broader DDS variants
retain the [DDS boundary](dds-conversion-goal.md).

Raster admission applies orientation/color handling and actual format limits.
Direct JPEG uses quality 90 and direct WebP uses lossless encoding. The other
common raster targets retain their fixed format settings.
Transparent-to-opaque output requires an explicit background. Automatic metadata
handling can quietly omit unsupported extras on a copy; it is not a privacy
scrub. Animation and meaningful unsupported alpha/HDR structures are not silently
flattened. Fixed common-format defaults and DDS choices remain as specified in
the [converter design](converter-design.md) and [image policy](../src/ContextSuite.Core/Images/ImageConversion.cs).


Audio input and output matrix
-----------------------------

`C` means implemented with isolated cross-format worker/publication evidence for
the admitted container/codec and metadata subset. `Unchanged` creates no output
and does not admit a new paid batch. This is 30 cross-format pairs and six no-ops,
not a claim that every rate, channel layout and metadata combination works.

| Input container/codec | WAV | FLAC | MP3 | M4A AAC-LC | Ogg Vorbis | Ogg Opus |
| --- | --- | --- | --- | --- | --- | --- |
| RIFF WAVE PCM/float | Unchanged | C | C | C | C | C |
| Native FLAC | C | Unchanged | C | C | C | C |
| MP3 | C | C | Unchanged | C | C | C |
| M4A/AAC-LC | C | C | C | Unchanged | C | C |
| Ogg Vorbis | C | C | C | C | Unchanged | C |
| Ogg Opus | C | C | C | C | C | Unchanged |

The [typed plan](../src/ContextSuite.Core/Audio/AudioConversionPlan.cs) checks
content, not the filename alone. Raw ADTS `.aac`, ALAC, WMA, AIFF, AU, WavPack,
MIDI, playlists and audio projects have no admitted conversion pair. A shared
MP4/Ogg container signature does not admit arbitrary codecs or video streams.
M4A input specifically requires the reviewed AAC-LC configuration; HE-AAC and
other AAC profiles are not admitted merely because a probe calls them AAC.

| Target | Fixed recipe | Necessary conditions |
| --- | --- | --- |
| WAV | PCM8/16/24/32 or float32/64 | Preserve supported source precision; decoded lossy input uses float32 |
| FLAC | Level 8 | Exact integer samples; PCM8 stored exactly in PCM16; float/lossy input uses PCM24 with precision acknowledgement |
| MP3 | LAME VBR quality 2 | Mono/stereo; one of the nine admitted rates from 8 to 48 kHz |
| M4A/AAC | AAC-LC 192 kb/s | Admitted rates from 8 to 96 kHz and supported speaker layout |
| Ogg Vorbis | Quality 5 | Supported rate/layout; finite-output and error checks still apply |
| Opus | Audio, 160 kb/s VBR | 48 kHz decoded output; acknowledgement for rate changes and supported surround mapping |

Input rates are bounded to 8-192 kHz and one audio stream with 1-8 known channels.
MP3 never automatically downmixes. AAC and Ogg targets have distinct surround
allowlists; the [222-case rate/layout matrix](audio-rate-layout-verification.md)
records actual tested cells and refusals. The [finite-input repair](finite-audio-resampling.md)
and independent decoding evidence cover specified short resampling cases, not
every clip or device. Listening and player compatibility remain open.

Lossy-to-lossy conversion, resampling and precision reduction require applicable
explicit consent in one compact prompt. Complete metadata inventories must admit
the source; tags and supported PNG/JPEG cover bytes must survive. Unrepresentable
picture roles/descriptions, unknown chunks, loops, cue sheets, chapters, conflicting
or unsupported tags and additional streams can block conversion. No blanket tag
or artwork guarantee follows from a `C` cell. The [audio policy](audio-conversion-policy.md)
owns the detailed source/target preservation rules and evidence.


Document transformations
------------------------

| Input and command | Implemented output and fixed policy | Current limits and remaining acceptance |
| --- | --- | --- |
| Supported PNG/JPEG/WebP/BMP/TGA > PDF | One raster-only PDF 1.7; several images require explicit page-order review; orientation, supported samples/color/alpha and physical dimensions retained | No DDS, OCR or tagged/accessibility PDF; all selected images must qualify; visible order review open |
| PDF > PNG | One numbered PNG per page, 150 DPI, supported crop/rotation/UserUnit applied | Copies only; retains stored visible appearances, not editable forms, attachments or verifiable signatures; protected/unsupported/oversized inputs can be refused |
| DOCX > PDF | Separate PDF 1.7 copy per document; final text, tracked markup hidden; source revisions retained | Exact ordinary document content type and matching suffix; broader fields, comments, protection, fonts and pagination open |
| XLSX > PDF | Separate PDF copy; explicit saved-values or recalculation choice retained on retry; external data not refreshed | No implicit calculation default; known early-1900 date display errors remain unresolved |
| PPTX > PDF | Separate PDF copy; saved slide order, hidden slides and speaker notes excluded; positively identified zero-visible-slide decks stop before export | Unknown visibility is not treated as an empty deck; broader presentation/media/font/layout fidelity and visible acceptance open |
| Mixed images and DOCX/XLSX/PPTX > PDF | One reviewed image PDF plus one separate copy per Office document under shared admission | Office documents do not become pages of the combined image PDF |
| DOC/XLS/PPT; Office templates and macro-enabled variants | None in the current converter | Recognition/evaluation is not implementation; launch variant decision or implementation and fidelity evidence still required |
| ODT/ODS/ODP | No transformation selected | Keep required bounded analysis; adding PDF conversion needs a scope decision |

Images-to-PDF permits at most 128 MiB/16 million pixels per image and
512 MiB/128 million pixels/4,096 pages per batch, with a 128 MiB output bound.
PDF-to-PNG permits 128 MiB source, 16 million pixels per page and 4,096 pages/
512 million pixels per batch. Office permits 64 MiB source, 128 MiB PDF and
4,096 pages per document. Bounds do not guarantee completion within the deadline.
See the [image PDF plan](../src/ContextSuite.Core/Pdf/ImagePdfPlan.cs),
[PDF page protocol](../src/ContextSuite.Core/Pdf/PdfRasterProtocol.cs) and
[Office protocol](../src/ContextSuite.Core/Office/OfficeHostProtocol.cs).

The [Office command](office-direct-command.md) and
[actual-app recovery](office-app-preparation.md) are implemented and tested;
they are not still an unimplemented planner. The [Word revision matrix](word-revision-application.md)
records thirteen native publications plus six paragraph controls. Owner-approved
small spacing differences preserve content, formatting and layout; the measured
move fixture has its own 0.071-point horizontal bound. That allowance does not
accept changed dates, missing content, new page breaks or substituted fonts.

The [early-1900 Excel experiment](excel-date-system-evaluation.md) still has six
incorrect displayed dates despite successful export. A bounded stored-numeric-date
inspection now returns separate preflight evidence and blocks positively
identified risks before application preparation or private export. The renderer
is not corrected, and formula/unsupported-inspection cases still need a complete
policy. General XLSX fidelity cannot be accepted until this is addressed.
Native file/profile ownership and interruption evidence
also do not by themselves close the separate
[Office network-isolation evidence](office-isolation-evaluation.md).


Optimize and original-file policy
---------------------------------

| Input | Available presets | Publication rule and boundary |
| --- | --- | --- |
| PNG | Auto, Lossless, Balanced, Smallest | Validated smaller result only; lossy presets have fixed quality/alpha limits; the approved fdEC omission exception is retained |
| Native FLAC | Auto, Lossless | Same lossless recompression policy; exact decoded samples and admitted raw metadata; smaller result only |
| PDF | Auto, Lossless | Structural optimization, no image downsampling; 16 MiB source bound; encrypted/signature-bearing or unsupported objects refused; smaller PDF copy only |
| Other formats | None | A Convert target or catalog description does not add Optimize |

All work defaults to copies. Saved per-tool **Overwrite originals** applies only
where existing validated publication and platform gates permit it; unsupported
metadata/format cases still keep originals. Every PDF/document transformation
above always creates copies. Recycling happens only after validated publication,
with no permanent-delete fallback. A no-smaller result is a valid unchanged
outcome. The [quick start](customer-quick-start.md) owns customer-facing guidance.


Packaging, evidence and work to finish
--------------------------------------

The retained stage is
`artifacts/production-staging/169f27e246ce43c4808afa9f99e044a3`, version **1.1.0**.
All 116 listed files and their sizes/hashes were rechecked on 2026-09-16, as were
the image/audio/PDF allowlists, dependencies and notices. Inventory SHA-256:
`160B80B502C99E225408D114450E524BE68C817A37719E3B8BD8E75737E652D2`.
No Office payload file is present. This is a fresh verification of retained bytes,
not a fresh build or rerun of all media workflows. A changed final package needs
the next reserved version, **1.1.1**, and complete relevant integrated checks.

Completion still requires:

- Resolve Office date fidelity and exact legacy/template/macro variant scope;
  finish broader fonts/layout/content and network-isolation acceptance, then
  complete formal Office staging and review its redistribution requirements.
- Finish catalog alias/MIME coverage and remaining regular-file/driver/network
  acceptance. Preserve honest fallback when detailed inspection is unavailable.
- Close audio input/metadata and independent decoder/listening/player gaps;
  complete PDF fidelity/recovery and selected engine adoption reviews.
- Assemble one fresh selected payload, verify its source/runtime identities and
  run the relevant image/audio/PDF/Office and recovery checks against that build.
- Manually accept the expanded Analyze report and necessary audio, page-order and
  spreadsheet prompts. Record keyboard/Escape and visible status separately from
  screen-reader, other themes/DPI and installed-shell acceptance.

Signing is deferred. Native installer lifecycle, live commerce and external
release gates remain separate from finishing this expanded local implementation.
The [goal](broad-file-support-goal.md) remains active; historical passing contract
counts are not summed into a claim that this complete matrix passed together.
