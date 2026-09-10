Audio Conversion And FLAC Optimization Policy
============================================

Status: typed conversion candidate and direct FLAC Auto/Lossless dispatch;
audio engine shipping adoption remains pending. Updated: 2026-09-10.

The public `AudioConversionPlan` separates recognized container/codec pairs from
conversion admission. Policy `audio-fixed-1` is implemented for isolated testing;
its lossy recipes still require representative listening and compatibility review.
No arbitrary engine arguments, normalization, trimming or automatic downmixing
are exposed. Same-format Convert returns the existing bytes without re-encoding.

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
validation. It is true for admitted WAV/FLAC conversion, byte-identical same-format
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
Cross-format worker integration, broader metadata, crash/recovery coverage,
listening, production payload and visible UI acceptance remain part of the
[active goal](broad-file-support-goal.md).
