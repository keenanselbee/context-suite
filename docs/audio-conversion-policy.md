Audio Conversion And FLAC Optimization Policy
============================================

Status: typed policy and private integration candidate; no customer audio
transformation command or shipping engine payload. Date: 2026-09-09.

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
paid-access operation in this component; worker integration remains pending.

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

FLAC optimization candidate
----------------------------

`OptimizeFlacAsync` uses `flac-lossless-1`, compression level 8, exact decoded
samples, known STREAMINFO agreement and the existing raw metadata reconciler.
Original non-padding descriptive blocks retain their bytes/order. Unknown
application blocks and other unsupported block types prevent
rewriting; handlers remain pending. Only a smaller reconciled result is returned;
otherwise the original byte snapshot is returned. This is still a worker
integration candidate, not a registered Optimize capability or publication receipt.

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
Before rewriting, source seek points must match a complete native frame index.
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

See [dated engine evidence](audio-engine-evaluation.md) for test counts, generated
fixtures and remaining acceptance. The full six-format matrix, broader metadata,
listening, worker/access/publication/recovery, production payload and visible UI
gates remain part of the [active goal](broad-file-support-goal.md).
