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
require preservation work before conversion. Rates are bounded to 8–192 kHz and
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

Current bounds are 64 MiB encoded input/output, 128 MiB decoded float64 bytes per
comparison side, 64 KiB diagnostics, and 120 seconds for the operation (individual
probes retain 15-second limits). Large/long media require a streaming comparison
before broad release; these limits are not the final whole-song capacity promise.
Candidate output and snapshots are cleaned after work. There is no application
publication, recycling or paid-access operation in this component.

Validation checks container/codec, sample rate, channel count/layout, FLAC
precision, decoded frame count and finite samples. Lossless paths require exact
float64 decoded values, retaining the full signed PCM32 range. The lossy RMSE
bound detects gross errors; it is not a listening-quality guarantee. Resampled
output has no reported time-aligned error metric yet and cannot pass final
fidelity acceptance on these facts alone.

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
application blocks, seek tables and other unsupported block types prevent
rewriting; handlers remain pending. Only a smaller reconciled result is returned;
otherwise the original byte snapshot is returned. This is still a worker
integration candidate, not a registered Optimize capability or publication receipt.

See [dated engine evidence](audio-engine-evaluation.md) for test counts, generated
fixtures and remaining acceptance. The full six-format matrix, broader metadata,
listening, worker/access/publication/recovery, production payload and visible UI
gates remain part of the [active goal](broad-file-support-goal.md).
