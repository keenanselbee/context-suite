Audio Conversion And FLAC Optimization Policy
============================================

Status: typed audio conversion, worker/publication integration, direct Convert
actions with a compact quality prompt, and direct FLAC Auto/Lossless dispatch.
Visible acceptance and engine shipping adoption remain pending. Updated: 2026-09-12.

The public `AudioConversionPlan` separates recognized container/codec pairs from
conversion admission. Policy `audio-fixed-1` is implemented for isolated testing;
its lossy recipes still require representative listening and compatibility review.
No arbitrary engine arguments, normalization, trimming or automatic downmixing
are exposed. The encoding API retains same-format bytes; the application batch
skips same-format files without publication or trial admission.

Fixed target policies
---------------------

| Target | Encoding candidate | Precision and rate policy |
| --- | --- | --- |
| WAV | Native unsigned PCM8, signed PCM16/24/32 or float32/64 | Preserve integer precision; preserve floating precision; lossy sources use decoded float32 |
| FLAC | Compression level 8 | Preserve PCM16/24/32; PCM8 uses sample-exact PCM16 storage; floating/lossy decoded samples use PCM24 with explicit precision acknowledgement |
| MP3 | LAME VBR quality 2 | Mono/stereo only; retain supported source rate; decline unsupported rates rather than resample silently |
| M4A/AAC | AAC-LC 192 kb/s, fast-start M4A | Retain supported source rate and channels |
| Ogg Vorbis | libvorbis quality 5 | Retain source rate and channels |
| Opus | libopus audio application, 160 kb/s VBR | Output decoded rate is 48 kHz; any other source rate requires resampling acknowledgement |

The policy accepts one audio stream in WAVE PCM/float, native FLAC, MP3,
M4A/AAC or Ogg Vorbis/Opus. ALAC and other codecs are not admitted by recognizing
their container. Additional streams/artwork and unknown multichannel layouts
require preservation work before conversion. Native FLAC optimization now has a
separate raw-preservation path for embedded artwork, described below.
Rates are bounded to 8–192 kHz and
channels to 1–8, with target-specific restrictions. This policy range is not a
claim that every rate/layout pair has passed engine tests.

The [rate/layout matrix](audio-rate-layout-verification.md) tests fourteen rates
for mono/stereo and nine surround layouts at 48 kHz. The pinned Opus encoder now
uses explicit mapping family 1 for surround, correcting 5.0/6.1 channel order.
AAC currently admits 2.1, quad, 4.0, 5.0, 5.1 and 7.1 layouts; Vorbis/Opus admit
quad, 5.0, 5.1, 6.1 and 7.1. These target restrictions apply to conversion,
retaining the existing same-format no-op behavior. Unsupported speaker layouts
receive early WAV/FLAC guidance, without automatic downmixing or relabeling.

Lossy-to-lossy conversion, resampling and floating-to-integer precision reduction
have independent typed acknowledgement flags. Accepting one does not authorize
another. A lossless target explicitly makes no claim to restore lost quality.
These decisions must be connected to the existing focused prompt before shipping;
the public policy alone does not implement that customer flow.

The pinned FFmpeg encoder initially returned PCM24 despite a PCM32 FLAC plan.
Only that plan now sets its explicit experimental 32-bit encoder option, and
checks both reported precision and exact decoded samples. This behavior follows
the [encoder's documented implementation](https://www.ffmpeg.org/doxygen/trunk/libavcodec_2flacenc_8c_source.html).
Independent FLAC-decoder/player compatibility and extreme-value coverage remain
release gates; experimental mode is not enabled for other codecs.
The later [Xiph decoder matrix](audio-independent-flac.md) verifies exact PCM32
extrema and partial final frames for mono/stereo/six-channel conversion and
optimization. It advances independent decoder evidence without establishing
older-player/device compatibility or listening acceptance.

Vorbis uses `-page_duration 1` to flush each encoded packet to an Ogg page.
The pinned default packing produced incorrect decoded lengths for generated
short clips whose audio shared one final page. Separate packet pages retained
the intended count; no samples are padded, truncated or ignored by validation.
The additional page overhead is accepted for correct framing. Extreme signals
can still fail the unchanged signal-error bound, including the generated
full-scale one-sample impulse and a 535 Hz LFE signal. This is not a listening
quality certification or permission to omit low-frequency-channel information.

Private candidate and process ownership
--------------------------------------

`AudioEncodingAdapter` verifies and holds read leases on the pinned encoder,
probe and DLLs. Each native encode/decode uses a bounded owned snapshot. Only its
read-only file handle and stdout/stderr handles are inherited; the input has no
customer path. `SeekableAudioProcess` starts suspended, assigns the existing
kill-on-close/1 GiB Windows job, then resumes parsing. Its explicit handle list
follows [Windows process-creation guidance](https://learn.microsoft.com/en-us/windows/win32/procthread/creating-processes).
Input permits only `fd` protocol and the selected demuxers. `FFREPORT` is removed.

The first pipe-based MP3 validation produced the wrong decoded sample count.
Seekable `fd` input fixes this generated case and permits M4A seeking without
passing source paths. The [FFmpeg protocol documentation](https://ffmpeg.org/ffmpeg-protocols.html#fd)
distinguishes seekable file descriptors from pipes. This is not a general OS
network/filesystem sandbox; supported-container external-resource and DLL-loading
acceptance remain required. The separate quick Analyze probe still reads a pipe
prefix and retains its existing duration/coverage limitations.

The file API accepts at most 512 MiB input and requires output below 512 MiB.
Decoded float64 data is bounded to 1 GiB per side; fixed 64 KiB buffers capture an
owned reference file and compare decoded output incrementally. The small-fixture
byte-array API retains a 64 MiB input/output limit. Diagnostics are bounded to
64 KiB and work to 120 seconds (individual probes retain 15-second limits).
These are current resource bounds, not a promise that every file within them
will finish before the deadline. Scratch capacity must accommodate the encoded
snapshots/candidates and decoded reference.

`AudioEncodingArtifact` retains one read-only candidate lease and records source
and output SHA-256 digests. Disposal removes that owned candidate; failed work
removes its known scratch files. The caller's source position is restored.
Native input uses a synchronous owned snapshot even when the caller uses an
asynchronous file handle. There is no application publication, recycling or
paid-access operation in this component; the FLAC worker/application integration
below owns those boundaries.

Validation checks container/codec, sample rate, channel count/layout, FLAC
precision, decoded frame count and finite samples. Lossless paths require exact
float64 decoded values, retaining the full signed PCM32 range. The lossy RMSE
bound detects gross errors; it is not a listening-quality guarantee. Resampled
output is now compared with a reference decoded at the planned output rate.
This gives time-aligned sample-error measurements, but both sides use the same
pinned engine. Independent resampler fidelity and listening acceptance remain
required; resampling is never reported as exact preservation of source samples.

Descriptive tags are merged only when global/stream values do not conflict,
mapped explicitly, then compared with actual output. The candidate reports missing
tags; it must not be treated as a publishable validation receipt. Full container
metadata, duplicate values, chapters, loops, artwork and supported omission
decisions remain open. Technical encoder/container labels are not descriptive-tag
preservation claims.

WAV conversion metadata admission
---------------------------------

`WaveMetadata` now walks the complete RIFF chunk chain before a WAV conversion
can encode. It follows the [RIFF chunk and word-alignment rules](https://learn.microsoft.com/en-us/windows/win32/xaudio2/resource-interchange-file-format--riff-),
checking the declared file extent, chunk extents, one format before one sample
chunk, frame alignment and any fact sample count. It seeks past sample data and
preserves the caller's stream position. Limits are 512 MiB input, 4,096 total
chunks (including INFO fields) and 256 KiB of INFO bytes, under the existing
operation deadline. RF64, RIFX and other RIFF variants are not admitted by this
inventory.

The current handler accepts PCM8/16/24/32 and IEEE float32/64, including recognized
extensible subtypes. Extensible speaker masks must agree with channel count;
nonstandard mono/stereo speaker positions and differing valid/storage precision
require further policy work. Native codec/rate/channel facts must agree with this
inventory, and same-rate decoded frame count must agree with the sample extent.

Mapped INFO fields are title, artist, album, comment, date, genre, track, copyright
and encoder software. Descriptive values must survive the source probe and actual
encoded output. The encoder label is technical provenance and may change on
re-encoding. Text currently requires ASCII with proper NUL termination; carriage
return, newline and tab are allowed. Non-ASCII/code-page interpretation, other control characters, duplicate values and
unmapped fields need a preservation handler. Do not infer permission to drop them.

Cue points, sampler loops, broadcast metadata, iXML, associated labels, embedded
ID3 and unknown chunks stop conversion before encoding. Declared JUNK/PAD chunks
are padding. Inventory covers metadata after the sample chunk too; appended data
outside the RIFF extent is rejected. Same-format byte retention remains a no-op.

Private candidate results now distinguish `SourceMetadataVerified` from sample
validation. It is true for admitted WAV/FLAC/Vorbis/Opus/MP3/M4A conversion, byte-identical same-format
retention and the separately validated FLAC optimization path. Other cross-format
source containers remain unverified candidates even when flattened probe tags
match. Their metadata handlers, artwork transport, application conversion admission
and menu integration remain required; WAV coverage does not fulfill the six-format
launch matrix by itself.

FLAC conversion metadata admission
----------------------------------

Cross-format FLAC conversion now inventories the original blocks and ordered
UTF-8 comments before encoding. STREAMINFO, padding, seek tables and comments
are admitted; application data, cue sheets, artwork and unknown blocks need
preservation handlers. The complete source frame index and CRCs are validated,
and any seek table must match those frames. Same-rate decoded length must match
the declared sample count when present.

Comment names use an explicit alias map for album artist, track/disc number,
disc subtitle and description, consistent with the native
[Vorbis-comment mappings](https://www.ffmpeg.org/doxygen/trunk/vorbiscomment_8c.html).
The pinned native fixture verifies these aliases. Every descriptive value must
survive both source probing and output probing exactly. Duplicate or colliding
aliases stop conversion; technical encoder/vendor provenance may change.
Chapter, loop, artwork and playback-gain comments require a separate policy.
Limits are 128 fields, 128 characters per mapped name and 4,096 per value;
NUL is refused. These limits supplement the bounded FLAC header parser.

Unicode, multiline values, quotes and backslashes pass as literal quoted native
arguments with no shell. NUL and command lines longer than 32,766 UTF-16 code
units are rejected before process creation. FLAC-to-WAV currently requires ASCII
values (including tab/newline/carriage return); Unicode WAV encoding remains
undecided. Unsupported output tags fail validation instead of silently disappearing.
Same-format retention and raw-preserving FLAC optimization keep their separate
policies; these conversion restrictions do not remove their richer metadata support.

Ogg Vorbis and Opus conversion metadata admission
-------------------------------------------------

`OggMetadata` scans the complete input with one reusable page buffer and bounded
packet storage. It checks page CRCs, serial/sequence continuity, beginning/end and
continuation flags, packet completion and monotonic granules. Input remains under
512 MiB; limits are 131,072 pages and 1 MiB per packet. Encoded Vorbis/Opus
candidates must also pass this inventory and agree with the planned audio
properties and probed comments. It restores the caller's
stream position on success, failure and cancellation. This follows the
[Ogg framing specification](https://xiph.org/ogg/doc/framing.html); it is not a
managed audio decoder or full codec conformance validator.

One logical Vorbis or Opus stream is admitted. Chained/multiplexed streams and
trailing data stop conversion. Vorbis identification/comment/setup headers must
be ordered; native decoding validates the setup and audio. Opus version 1,
mapping families 0/1 and one to eight channels are handled, with family 0 limited
to mono/stereo. Rate and channel facts must agree with the native probe. Nonzero
Opus header gain requires further policy because it changes playback amplitude.
Pre-skip remains a decoder responsibility; the final granule is recorded as a
container extent, not an independently proven decoded sample count. See
[Opus encapsulation](https://www.rfc-editor.org/rfc/rfc7845.html).

FLAC, Vorbis and Opus now share the bounded
[UTF-8 comment-list structure](https://xiph.org/vorbis/doc/v-comment.html) and
conversion alias/value rules. Vorbis requires its comment framing byte; Opus
trailing data is discardable only when its first low bit permits this. Preserved
binary extensions, duplicate comments, artwork, chapter/loop/gain semantics and
unmapped output values prevent conversion. All admitted descriptive values must
match source and output probes. Unicode-to-WAV still requires an explicit text
encoding policy. MP3 and M4A have the narrower inventories described below;
their remaining metadata variants still need handlers.
Raw comment admission and native probe filtering share the same technical-tag
exclusions: encoder, major_brand, minor_version, compatible_brands, handler_name
and vendor_id. This avoids treating copied M4A container labels as missing
descriptive comments when validating an Ogg output.

MP3 conversion metadata admission
----------------------------------

`Mp3Metadata` reads an initial ID3v2.3.0 or ID3v2.4.0 tag, then walks every MPEG
Layer III frame boundary while seeking past compressed samples. MPEG 1, 2 and
2.5 rate/bitrate forms are supported; rate and channel count must remain stable
and agree with the native probe. The frame count includes encoder-information
frames and is not a decoded duration. Native decoding owns bit-reservoir/audio
validity and gapless trim; this inventory does not validate MPEG audio CRCs or
arbitrary ancillary payloads. Free-format audio, emphasis/copyright flags,
trailing tags other than a validated ID3v1 trailer, undeclared data and truncated
frames require further handling or are refused.

Limits are 2 MiB of ID3 data, 4,096 tag frames, the shared 256 KiB text budget,
one million MPEG frames and the existing 512 MiB file/deadline bounds. The caller's
position is restored on all outcomes. Version-specific sizes, padding, v2.4
footer and data-length indicator are checked. Unsynchronisation is reversed
before v2.3 frame traversal or for each affected v2.4 frame. The implementation
follows the [ID3v2.3 standard copy](https://id3lib.sourceforge.net/id3/id3v2.3.0.html)
and [ID3v2.4 structure](https://github.com/id3/ID3v2.4/blob/master/id3v2.40-structure.txt).

Admitted text frames cover title, artist/album artist, album, track/disc, composer,
copyright, encoded-by, publisher, language, performer, date, disc subtitle,
grouping and textual genre; encoder identity is technical provenance. Custom
text fields use the shared alias/semantic rules. Latin-1 and BOM-qualified UTF-16
are accepted in both versions; v2.4 also supports UTF-16BE and UTF-8. Single
undefined-language comments with an empty description are supported. Duplicate
or multiple values, composite/refined genres, named/language-specific comments,
artwork, lyrics, chapters, ratings, objects/private frames, extended headers,
compression/encryption/status flags, APE and older ID3v2 versions need handlers.
Refusal retains originals; these gaps remain part of completing common MP3 support.

A final 128-byte ID3v1.0/1.1 trailer is now inventoried separately from MPEG
frames. Latin-1 title, artist, album, year and comment fields, the v1.1 track byte
and defined genre are preserved. NUL/space padding is removed; nonpadding data
after a terminator and ambiguous control/code-page bytes are refused. Only one
trailer is admitted and its boundary cannot conceal a truncated audio frame.
See the [ID3v1 layout and encoding](https://id3.org/id3v2-00) in Appendix A.

When both tag versions occur, missing fields are added and agreeing fields are
combined. A fully occupied legacy field may be the fixed-width prefix of a richer
v2 value; a shorter padded mismatch is a conflict. An agreeing year may retain
the v2 date, and an agreeing track retains leading zeros and an optional total.
Contradictory title/artist/album/comment/year/track/genre values block conversion;
native probe precedence cannot silently discard them. The merged result still
obeys the shared field/value limits.

Genre codes 0-147 map to their defined names, including established Winamp
extensions; an ID3v1 genre byte of 255 is unclassified. Numeric v2 text and legacy
parenthesized references are recognized, as are Remix/Cover and redundant matching
refinements. Version-three escaped parentheses and version-four literal text are
kept distinct. Multiple references, different refinements and unreviewed numeric
codes remain unsupported. The mappings follow
[ID3v2.3 genre semantics and Appendix A](https://id3lib.sourceforge.net/id3/id3v2.3.0.html)
and the [established extension assignments](https://ffmpeg.org/doxygen/trunk/id3v1_8c_source.html).
These are file-declared classifications, not inferred musical qualities.

The pinned native probe truncates an authored v2.3 unsynchronised title from
`AÿàB` to `Aÿà`. MP3 conversion therefore uses the complete managed inventory as
the descriptive source of truth, disables inherited global/stream metadata,
and writes each admitted value explicitly. Ogg targets also receive explicit
stream tags. Actual output tags and decoded audio must still validate. This
does not correct the separate read-only native Analyze probe's tag reporting;
shared analysis integration remains open. Unicode-to-WAV stays unadmitted until
its text-encoding policy is settled.

M4A conversion metadata admission
---------------------------------

`M4aMetadata` inventories one self-contained AAC-LC track before native probing
when a cross-format input starts with `ftyp`. Other MOV-family layouts cannot
become verified conversion candidates. The parser follows the container's
[data references](https://developer.apple.com/documentation/quicktime-file-format/media_data_reference_atom)
and [sample-to-chunk tables](https://developer.apple.com/documentation/quicktime-file-format/sample-to-chunk_atom/sample-to-chunk_table)
without resolving a resource or reading compressed samples. It checks atom
extents, sample descriptions, MPEG-4 descriptors, sample sizes/timing, 32/64-bit
chunk offsets and complete nonoverlapping local `mdat` coverage. Native codec,
rate and channels must agree with the AAC configuration; decoding and existing
sample comparisons still own actual audio validity.

The initial profile permits reviewed M4A/ISO brands, version-zero movie/track/media
headers, normal playback rate/volume/balance, identity matrices, no edit or one
normal-rate trim, and paired roll-recovery sample groups. A sole track's alternate
group number has no selection effect. AAC priming and trimming remain decoder
responsibilities; the inventory's packet count is not a decoded sample count.
Container sample-entry rate/channel hints are advisory; the AAC configuration
and native interpretation must agree. The caller's stream position is restored
on success, refusal and cancellation.

Bounds are 512 MiB per file, 16 MiB for the movie buffer, 16 media-data regions,
16,384 root atoms and 16,384 movie/child atoms, one million samples/table entries,
256 KiB of tag text, 128 fields and 4,096 characters per value. Child atoms retain
slices of the movie buffer. The operation's existing deadline applies.

Admitted iTunes-style tags include title, artist/album artist, album, comment,
date, textual genre, copyright, composer, grouping, description and track/disc
numbers with optional totals. UTF-8 and UTF-16BE text are supported; the media's
packed language is retained. Comment and description remain distinct instead
of applying Vorbis aliases to M4A. Encoder identity is technical provenance.
Source and actual output probes must retain every admitted descriptive value.
Unsupported destination tags fail validation; the original remains intact.

Artwork, freeform metadata (including iTunes gapless/gain fields), numeric genre
codes, localized or repeated values, creation/modification timestamps, version-one
headers, ALAC/HE-AAC, external references, extra tracks, complex edits, fragmented
files and unknown atoms still need handlers. These are explicit coverage gaps,
not permission to omit information. The tests cover generated AAC files only;
this is not full M4A conformance or independent decoder/listening acceptance.
Same-format byte retention keeps its existing separate policy.

FLAC optimization candidate
----------------------------

`OptimizeFlacAsync` uses `flac-lossless-1`, compression level 8, exact decoded
samples, known STREAMINFO agreement and the existing raw metadata reconciler.
Original non-padding descriptive blocks retain their bytes/order. Unknown
application blocks and other unsupported block types prevent
rewriting; handlers remain pending. Only a smaller reconciled result is returned;
otherwise the original byte snapshot is returned. This encoding artifact is not
itself a registered Optimize capability or publication receipt.

`CreateFlacOptimization` admits one FLAC audio stream plus explicitly reported
attached pictures. Ordinary video and cross-format artwork conversion remain
outside that path. The original picture/comment blocks are restored before
output probing and exact audio validation. Artwork stream tags do not override
album/audio tags, and original duplicate and multiline comments are not rebuilt
from the probe's flattened dictionary or passed back as command-line arguments.

`FlacDescriptiveMetadata` inventories comments and pictures according to
[RFC 9639 sections 8.6 and 8.8](https://www.rfc-editor.org/rfc/rfc9639.html#section-8.6).
It keeps ordered duplicate fields, checks UTF-8 and field framing, and records
picture declarations plus payload digests. Bounds are 4,096 comments, 256 KiB of
aggregate descriptive text and 31 pictures inside the existing 32 MiB header
budget. Image dimensions are declarations, not trusted decoding instructions.
Linked artwork is recognized without resolving it and prevents optimization
until a location-preservation policy exists. Cuesheet semantics,
other metadata handlers and hostile embedded-image acceptance remain
open; this inventory is not a complete FLAC conformance validator.

Seek tables now have a dedicated rebuild path following
[RFC 9639 section 8.5](https://www.rfc-editor.org/rfc/rfc9639.html#section-8.5).
Every FLAC optimization validates complete source and output native frame indexes,
including files without seek tables. Existing source seek points must match the
source index before rewriting.
The index must cover contiguous samples and all audio bytes, use the declared
sample clock and agree with known STREAMINFO sample counts. A managed scan checks
each indexed frame's sync bytes and full-frame CRC16. This uses the pinned
engine's packet interpretation, not an independent FLAC decoder.

After encoding, each original target maps to the preceding output frame.
Offsets remain relative to the first audio frame, so moving metadata does not
invalidate them. Points mapping to one frame coalesce; unused and existing
placeholder slots stay reserved. Descriptive blocks retain their original bytes.
The legacy metadata-only API still refuses seek-table rewriting without an index.
Bounds are 65,536 seek slots, 131,072 indexed frames and 8 MiB packet JSON; the
indexing/CRC passes share the operation's 120-second deadline. They are separate
from the quick probe's 15-second limit. Native variable-boundary/long-seek corpus
and malformed-frame coverage remain acceptance work.

FLAC worker and application publication
--------------------------------------

`flac-probe` and `flac-optimize` carry bounded file references and typed facts,
not complete recordings. Inspection uses a validated synchronous read-only source
handle, held against writes/deletion and inherited without its customer path.
Encoding and decoding continue to use owned snapshots. Inspection hashes the
whole source but does not promise full frame validation; that occurs during work.
Source facts and the digest are checked again before encoding.

`AudioFileAdapter` writes only an existing empty, single-link application
reservation with the expected item filename. Shared `ReservedMediaOutput` checks
the opened handle and ordinary path, rejecting redirects, unavailable files and
hard links. Image output retains its 128 MiB cap; audio explicitly selects 512 MiB.
The FLAC client deadline is 150 seconds for inspection plus encoding/validation;
the encoder retains its separate 120-second bound.

`FlacOptimizationPlan` snapshots settings and eligibility. Confirmed batches use
the existing paid/trial admission once; expiry blocks new work without revoking
admitted work. `FlacOptimizationExecutor` processes items sequentially through the
existing transactional publisher, including collision-safe naming, source-change
checks, smaller-result admission, cancellation and reservation cleanup. Copies
remain default; overwrite requires existing explicit settings and platform gates.
The isolated workflow verifies copies only, with native recycling forbidden.

The application now routes existing Optimize Auto/Lossless commands to FLAC when
the optional verified encoder is present. Balanced and Smallest remain PNG-only
and explain that restriction. Bounded header inspection selects the family before
native probing, without starting the trial or expanding document packages. A
filename/content mismatch gives rename guidance before admission: the publisher's
same-extension Optimize guard is retained. Renaming is a manual correction for an
already identified format, never a substitute for conversion.

Mixed PNG/FLAC selections share one settings snapshot, one paid/trial admission,
one sequential worker and one quick-workflow completion. PNG work runs first,
followed by FLAC, while result rows retain selection order. Each executor checks
the admitted request ID before writing. A family cannot trigger a second admission
after expiry. Existing Try again behavior keeps each failed action and file,
captures current saved settings and suppresses duplicate retries. No planner is
opened for optimization. Missing audio binaries give an unavailable-build result
without starting a worker or trial.

See [dated engine evidence](audio-engine-evaluation.md) for generated fixtures and
test counts. Normal packaging still excludes the evaluation audio engine.
Broader metadata, crash/recovery coverage,
listening, production payload and visible UI acceptance remain part of the
[active goal](broad-file-support-goal.md).

Audio conversion worker and publication
----------------------------------------

`audio-file-probe` and `audio-convert` use bounded file references, target enums,
typed source facts and explicit consent flags. No complete recordings or arbitrary
engine arguments cross IPC. Probing holds a validated read-only source handle,
hashes the source and plans the target without starting access. Standard M4A
cross-format probes inventory local structure before native parsing. A successful
probe is not complete metadata admission: encoding must pass the container's full
preservation checks and actual output validation before publication.

`AudioConversionBatch` freezes target, files and captured Convert settings. It
combines required lossy-transcoding, resampling and precision decisions for the
executable items. Confirmation reconstructs the expected plan and rejects changed
eligibility, rates or consent requirements. Unsupported and already-target items
remain identifiable; an all-unchanged selection cannot start an operation.
Copies remain default. An alternate output folder always keeps originals;
replacement still requires saved consent and the existing verified-platform gate.

`AudioConversionExecutor` admits one confirmed batch through existing paid/trial
access, then uses one sequential worker and the shared transactional publisher.
Already-target items receive unchanged results without an output. The publisher's
reserved source digest/length must match the plan. The file adapter rechecks
source facts/digest, fixed encoding policy, consent, metadata verification, decoded
length and required exact-sample validation. Only then does it fill the existing
empty, single-link reservation. The shared copy helper verifies the reserved
output digest and length again; final naming/publication belongs to the app.

Conversion uses the existing 512 MiB encoded and 1 GiB decoded limits, a 30-second
file-probe bound, 120-second encoding bound and 150-second worker-client bound for
inspection plus work. Unsupported features return a stable per-file failure
category; they do not terminate the worker or prevent a later file. Cancellation
and failures abandon uncommitted reservations and preserve committed outputs.

The isolated workflow verifies all 30 cross-format pairs, six no-op cases,
collisions, access expiry, source changes, unsupported metadata/references,
cancellation, alternate folders and unsafe reservations. Tests inject a refusing
recycler; no native overwrite/recycling acceptance is implied. Paid-access
contracts also verify no trial fallback after paid expiry. Listening, broader
metadata, crash recovery and shipping-engine acceptance remain open.

The later [encoder interruption matrix](audio-interruption-verification.md)
passes real cancellation, client timeout and worker termination during a growing
WAV-to-FLAC encode. It verifies native exit, cleanup, preserved originals and
committed copies, and successful fresh-worker retries. Other phases, codecs and
application/publication crash boundaries retain their separate acceptance scope.

The [application publication crash matrix](audio-publication-crash-verification.md)
adds five copy-publication checkpoints for WAV-to-FLAC conversion and FLAC
optimization. Restart discovery preserves journals and surviving files, and fresh
operations complete without overwriting that evidence. This does not implement
automatic recovery or establish native overwrite and visible recovery acceptance.

Direct Convert and required quality decisions
---------------------------------------------

The Convert submenu adds WAV, FLAC, MP3, M4A (AAC), Ogg Vorbis and Opus after the
existing image targets, followed by the shared separator and Settings command.
Stable actions are `wav`, `flac`, `mp3`, `m4a`, `vorbis` and `opus`. There is only
one submenu level. Native enumeration neither probes files nor checks licensing;
the application checks the actual inputs and optional engine availability.

`MainViewModel` probes each input for the fixed target and freezes the existing
Convert settings. Already-target files are unchanged without admission or output.
Unsupported files retain per-file results while eligible files continue. A
missing engine returns an unavailable-build result without starting the trial or
worker. Routine conversions automatically confirm their complete safe plan and
use the quiet workflow; they never open the image planner.

When required quality consent is nonzero, `AudioConversionWindow` explains only
the applicable lossy-transcoding, 48 kHz resampling and precision consequences.
One Convert action explicitly accepts them for the executable files. Enter is
not a default confirmation; Escape cancels. Files start collapsed. Disclosure
changes refit the window height; at the minimum height, details scroll while
status, License, Convert and Cancel remain reachable. This uses the existing
system theme, not a new workspace or preset editor.

The prompt reads access without admitting work. Closing License refreshes that
same plan; expiry disables Convert, activation enables it and deactivation
disables it again. Closing the prompt cancels an outstanding access read.
Final paid/trial admission rechecks access after confirmation. Cancelling or
declining the prompt starts no admission or publication. Try again preserves the
failed action/files, captures current saved settings and suppresses duplicate
clicks, as in existing quick actions.

Twenty isolated direct-conversion checks exercise the real application queue,
worker and publisher with simulated access and disposable copies. Foundation
contracts cover the fixed decision state; hidden view contracts cover bindings,
disclosure sizing, minimum layout and automation peer text. They do not prove
visible usability, actual keyboard focus, screen-reader delivery or other
themes/DPI. Normal packaging still excludes the evaluation audio engine.
