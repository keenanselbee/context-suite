Expanded Launch Capability Matrix
=================================

Matrix revision: **2026-09-19.4**. This update records the finite WMA Pro
surround decoder repair and isolated worker selection. Optional HEIC/AVIF scratch
packaging and reserved candidate 1.1.1 capabilities remain separate.
Catalog schema 1, revision **2026-09-16.3**, **245 records**.
Status: implemented candidates and recorded gaps; expanded launch acceptance
is incomplete. The newly selected inputs below still require full customer acceptance.

The source baseline and the last packaged candidate are different. Candidate
**1.1.0** contains images, audio and PDF engines, but no Office engine. Later
Office command and recovery work is verified in isolated scratch builds. Default
release packaging still excludes the optional audio/PDF/Office candidates. The
[explicit Office staging path](office-production-payload.md) is implemented;
all 33 scratch packaging checks pass with the current host, including copy-failure
retention and retry refusal. The fresh combined **1.1.1** candidate now includes
Office; its current evidence is recorded below and in the
[candidate record](combined-office-candidate.md). Release adoption and complete
acceptance remain open. A menu label does not establish that a particular build
can execute the operation.

Required compatibility additions selected 2026-09-16:

| Input | Required target commands | Current state |
| --- | --- | --- |
| HEIC | JPEG, PNG | Authored SDR adapter/worker, loader, resource-limit, recovery and optional combined-scratch packaging checks pass; broader fidelity, redistribution and formal release adoption pending |
| AVIF | JPEG, PNG | Authored SDR adapter/worker, loader, recovery and optional combined-scratch packaging checks pass; broader fidelity, redistribution and formal release adoption pending |
| AIFF | WAV, FLAC, MP3, M4A/AAC, Ogg Vorbis, Opus | [Isolated adapter](aiff-conversion-adapter.md) passes 366 checks/68 outputs with PCM/float, three compressed variants, tested text and artwork. The later [expanded worker](expanded-audio-worker-verification.md) exercises actual copy publication, representative tones and boundary refusals. Broader fidelity, visible/recovery acceptance and packaging remain pending |
| WMA | WAV, FLAC, MP3, M4A/AAC, Ogg Vorbis, Opus | [Expanded worker](expanded-audio-worker-verification.md) admits the repaired Standard/Pro/Lossless runtime and optional Windows Voice provider for copy publication. Independent Standard/Pro and exact Lossless/Voice comparisons retain their stated bounds; the [surround repair](wma-pro-surround-repair.md) passes 29 native cases under the existing per-channel limits, and repeats 1,008 worker checks with 796 publications. FFmpeg Voice is not used. Broader fidelity, metadata/timelines, visible/recovery acceptance and packaging remain pending |

No output or Optimize capability for these four formats is added.
The later [recovery checkpoint](expanded-audio-recovery.md) passes 265 checks
covering 30 decode/encode interruptions and retries across AIFF, Standard, Pro,
Lossless and Voice. It also repairs long Lossless raw-transport diagnostic
overflow and repeats the 1,008-check publication matrix. The later
[direct-command checkpoint](expanded-audio-direct-verification.md) passes 211
checks with seven inputs across all six targets, mixed selections, required
prompts, access changes and absent providers. Its later metadata checkpoint adds
173 checks and 42 copies, with 78 independent descriptive-value/cover-pixel
checks. Broader metadata variants, whole-app publication-crash and visible
acceptance remain separate.
The [Windows Voice candidate](wma-voice-windows-evaluation.md)
passes 36 reference-output comparisons and 12 refusal/ownership checks. Its
[optional private adapter](wma-voice-conversion-adapter.md) passes 339 checks with
222 outputs, including six-target text/artwork preservation. The provider requires
explicit composition, now verified through disposable-worker direct commands.
Visible acceptance and formal packaging remain pending.
FFmpeg's Voice failures remain open.
ALAC remains a separate evaluation. The [current goal](broad-file-support-goal.md#current-objective-and-completion-plan)
requires implementation and integrated acceptance before completion; the existing
conversion matrices below describe only implemented capabilities.

The [HEIF review bundles](heif-distribution.md) pass tampering checks and an
extracted library-only rebuild. The current decoder repeats focused bridge,
adapter, worker, loader and recovery acceptance. These are engineering results;
redistribution approval and production adoption remain pending for both formats.
The [color and alpha checkpoint](heif-color-fallback.md) resolves the earlier
missing/unspecified-color and ICC-only gaps. Its combined upstream/authored
corpus passes 659 worker/publication checks with 104 copies. Two upstream files
with inconsistent alpha transforms are explicitly unsupported; hidden HDR remains
refused. Authored sample/profile fidelity, loader and active-native recovery
checks pass for the revised decoder. Broader fidelity and visible acceptance
remain open, along with the adoption and redistribution gates above.
The [packaging checkpoint](heif-production-payload.md) adds an explicit isolated
build option and fourteen pinned decoder/notice files (5,062,827 bytes). It passes
53 packaging and five immutable-stage checks; the exact combined scratch worker
repeats all 659 corpus checks and 104 publications. This is not a replacement for
the reserved 1.1.1 stage or the required final versioned candidate.

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
| Legacy DOC/XLS/PPT | Compound directory plus agreeing supported binary headers | Bounded version, size and encryption declarations | Legacy PDF conversion is deferred by the owner for first release |
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
Alpine APK meaning. The [MIME expansion](catalog-mime-expansion.md) brings
descriptive coverage to 134 associations across 100 records, leaving 145 without
a MIME claim. Remaining alias/variant and additional MIME review is still open.
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
| XLSX > PDF | Separate PDF copy; owner-selected explicit saved-values or recalculation choice retained on retry; external data not refreshed | Known incorrect early-1900 date results blocked; renderer correction deferred, broader fidelity still requires acceptance |
| PPTX > PDF | Separate PDF copy; saved slide order, hidden slides and speaker notes excluded; positively identified zero-visible-slide decks stop before export | Unknown visibility is not treated as an empty deck; broader presentation/media/font/layout fidelity and visible acceptance open |
| Mixed images and DOCX/XLSX/PPTX > PDF | One reviewed image PDF plus one separate copy per Office document under shared admission | Office documents do not become pages of the combined image PDF |
| DOC/XLS/PPT; Office templates and macro-enabled variants | None in the current converter | Owner deferred conversion for first release on 2026-09-16; existing analysis remains |
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
is not corrected. A later six-export formula matrix confirms that modern cached
values pass preflight but recalculation can produce the same six wrong early-date
displays; 36/42 displays match overall. Formula and unsupported-inspection cases
still need a complete policy. General XLSX fidelity cannot be accepted until this
is addressed. Separate workbook-copy exports now preserve all 42 expected caches,
and the bounded reader detects the recalculated early dates in those retained
copies. A later same-document native experiment preserves the same 42 caches and
produces six identical before/after PDF pairs, with exact text, geometry and
pixels. Thirteen independent-inspector controls pass. The date errors remain;
that authored-fixture evaluation does not establish arbitrary-workbook equivalence.
The subsequent [calculated-date guard](excel-calculated-date-guard.md) adds a
hash-bound workbook copy to host completion and checks it during export admission
and independent PDF validation. Four cached/1904 cases publish; both known
recalculated-error cases are withheld with guidance and complete cleanup. This
closes the demonstrated publication gap without correcting the date renderer or
settling unknown-format interpretation and broader spreadsheet fidelity.
The later [48-sheet capacity fix](excel-calculated-date-guard.md#workbook-capacity-and-pdf-admission)
passes 43 application checks and 144 independent page comparisons, using the
unchanged 1 GiB aggregate job cap and bounded flat PDF admission. The
[failure-guidance update](office-direct-command.md#office-failure-guidance)
uses document-specific messages and passes eight actual-worker refusal checks.
Neither checkpoint resolves the outstanding date-fidelity policy.
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

The earlier retained stage is
`artifacts/production-staging/169f27e246ce43c4808afa9f99e044a3`, version **1.1.0**.
All 116 listed files and their sizes/hashes were rechecked on 2026-09-16, as were
the image/audio/PDF allowlists, dependencies and notices. Inventory SHA-256:
`160B80B502C99E225408D114450E524BE68C817A37719E3B8BD8E75737E652D2`.
No Office payload file is present. This is a fresh verification of retained bytes,
not a fresh build or rerun of all media workflows.

The fresh [combined 1.1.1 candidate](combined-office-candidate.md) is
`artifacts/production-staging/3efa4342aed9435995d8f00b52fead96`: 19,450 inventoried
files including Office, with a verified version reservation and 4,330 passing
foundation/image-worker checks. The linked record owns subsequent workflow
evidence and remaining acceptance. This is isolated candidate packaging, not
release adoption; any further changed formal payload needs **1.1.2** or the next
available version.

The [current Analyze benchmark](analyze-performance.md#maximum-selection-refresh-2026-09-16)
passes authored 256-path and maximum 4,096-path selections with per-file failures,
preserved originals and bounded inspection. It adds large-selection evidence;
visible responsiveness, optional-worker latency and true cold storage remain open.

Completion still requires:

- Implement and accept the required HEIC/AVIF to JPEG/PNG and AIFF/WMA to existing
  audio targets, including exact variant and fidelity matrices.
- Verify the owner-selected modern Office scope and known-date refusal policy;
  finish broader fonts/layout/content and network-isolation acceptance, and
  review redistribution requirements before release adoption.
- Validate the declared catalog aliases and factual coverage, and finish required
  regular-file/driver/network acceptance. Full MIME population is not a completion
  prerequisite. Preserve honest fallback when detailed inspection is unavailable.
- Close audio input/metadata and independent decoder/listening/player gaps;
  complete PDF fidelity/recovery and selected engine adoption reviews.
- Complete the remaining fidelity, metadata and independent-decoder cases against
  a selected candidate. The fresh combined stage and representative workflow
  replays do not by themselves establish every advertised boundary.
- Manually accept the expanded Analyze report and necessary audio, page-order and
  spreadsheet prompts. Record keyboard/Escape and visible status separately from
  screen-reader, other themes/DPI and installed-shell acceptance.

Signing is deferred. Native installer lifecycle, live commerce and external
release gates remain separate from finishing this expanded local implementation.
The [goal](broad-file-support-goal.md) remains active; historical passing contract
counts are not summed into a claim that this complete matrix passed together.
