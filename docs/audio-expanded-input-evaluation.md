AIFF and WMA Input Evaluation
============================

Status: isolated native evaluation, 2026-09-16. The later
[AIFF adapter checkpoint](aiff-conversion-adapter.md) adds managed conversion
evidence; AIFF/WMA commands are not yet enabled in Context Suite.
The selected production audio payload, installed app
and reserved combined 1.1.1 stage are unchanged.

The [required compatibility goal](broad-file-support-goal.md) adds inputs for the
six existing targets. This checkpoint establishes a restricted decoder candidate
and reproducible sample observations; it does not establish application support,
metadata preservation, listening acceptance or production adoption.


Restricted candidate
--------------------

The [source build](audio-ffmpeg-build.md) now accepts `--expanded-inputs`.
Without that flag its component selection remains unchanged. The opted-in build
adds the AIFF and ASF demuxers and these fourteen decoders:

```text
pcm_s8 pcm_s16be pcm_s24be pcm_s32be pcm_f32be pcm_f64be
pcm_alaw pcm_mulaw adpcm_ima_qt wmav1 wmav2 wmapro wmalossless wmavoice
```

It adds no encoder, output container, protocol or device. The existing exact
configuration gate still requires network/GPL/nonfree options disabled. The
unchanged pinned FFmpeg source and verified dependencies build successfully at
`.codex-temp/audio-ffmpeg-ef27d9f1282b4326836fb1835c9ca901`. All source/dependency
inventories and executable version checks pass. Its seven runtime files total
5,055,488 bytes, 210,944 bytes above the selected earlier restricted build.
This is uncompressed engine size, not an installer or performance budget result.

The native review passes with the same eleven previously reviewed diagnostics
and all seven emitted x64 PE hardening/import checks. Evidence is
`build-review-97bd45be8d4a4cf69c72c1d3f391cb21` inside that workspace.
No additional diagnostic was allowlisted. The source/build license review and
distribution bundle must be refreshed before this candidate is adopted.


Finite decoding observations
----------------------------

[Test-AudioExpandedInputs.py](../tools/audio-engine/Test-AudioExpandedInputs.py)
verifies candidate runtime identities and the retained, independently selected
supplier archive before executing either engine. It writes disposable fixtures
inside a fresh evidence directory and uses fixed local arguments with deadlines.
It accepts no customer inputs. The supplier comparison is a separate build of
the same FFmpeg revision, not an independent codec implementation.

The PCM containers are independently authored from Apple's original
[AIFF 1.3](https://www.mmsp.ece.mcgill.ca/Documents/AudioFormats/AIFF/Docs/AIFF-1.3.pdf)
and [AIFF-C specification](https://www.mmsp.ece.mcgill.ca/Documents/AudioFormats/AIFF/Docs/AIFF-C.9.26.91.pdf).
Their explicit sample oracle covers integer extrema, zero, non-round values,
different stereo channels, odd mono chunk padding and exact fractional floats.
The pinned supplier encodes the five compressed fixtures from an authored tone.

| Input variant | Observed decoding | Still required |
| --- | --- | --- |
| AIFF signed PCM 8/16/24/32 | Mono/stereo at 48 kHz; exact authored samples | Rates/layouts, metadata and target matrix |
| AIFF-C NONE signed PCM 8/16/24/32 | Mono/stereo at 48 kHz; exact authored samples | Same boundaries as AIFF PCM |
| AIFF-C sowt PCM16 | Mono/stereo; exact little-endian sample interpretation | Other declared widths and variants |
| AIFF-C fl32/fl64 | Mono/stereo; exact finite float samples | Precision prompts, extremes and nonfinite refusal |
| AIFF-C A-law, mu-law, IMA4 | Stereo 48 kHz tone; 48,000 decoded frames; candidate/supplier agree exactly | Independent codec/sample-quality evidence and compressed-source consent |
| WMA standard v1/v2 | Stereo 48 kHz tone; candidate/supplier agree exactly | Duration/delay policy, metadata, quality and target matrix |
| WMA Pro/Lossless/Voice | Compiled decoders only | Representative fixtures and all fidelity evidence |
| Protected ASF, other codecs and unusual AIFF-C | No acceptance claim | Explicit early refusal and malformed/hostile corpus |

All 27 fixtures pass at
`expanded-inputs-02bc716634f2415bad77f4f71078b4cd` inside the candidate workspace.
Twenty-two PCM/float cases match their exact authored oracle. All 27 candidate
and supplier decodes match each other exactly, with original hashes unchanged.
The WMA encodes made from 48,000 frames decode to 47,104 frames in both builds.
That agreement does **not** establish preservation of the original tone's
duration: investigate encoder delay/framing and a representative-file oracle
before defining conversion acceptance. Do not hide the difference with padding
or trimming, or claim these are gapless WMA fixtures.

The existing six-output smoke also passes at
`smoke-164124dcfa9e4a4880cba427ba30495c`, including supplier decoding, exact
WAV/FLAC samples, bounded lossy-tone error and embedded PNG decoding. Those source
inputs are the existing WAV fixture; this does not test each new input against
all six targets.

[Test-AudioConfiguration.py](../tools/audio-engine/Test-AudioConfiguration.py)
passes fifteen gate checks at
`.codex-temp/audio-config-guards-eff34fef64404dfdb5bb93dc95134b0e`.
It copies real generated baseline/expanded headers, checks explicit opt-in in
both directions and rejects missing input decoders/demuxers, an added WMA output,
HTTP, H.264, network, GPL/nonfree and wrong architecture. Original headers remain
unchanged. These mutation checks supplement the real generated-header check.


Reproduction and next integration boundary
------------------------------------------

Build with the existing [pinned dependency command](audio-ffmpeg-build.md), adding
`--expanded-inputs`, then run:

```powershell
python -B tools/audio-engine/Check-AudioBuild.py '<expanded workspace>'
python -B tools/audio-engine/Test-AudioCandidate.py '<expanded workspace>' '<pinned evaluation bin>'
python -B tools/audio-engine/Test-AudioExpandedInputs.py '<expanded workspace>' '<pinned evaluation bin>'
python -B tools/audio-engine/Test-AudioConfiguration.py '<baseline workspace>/build' '<expanded workspace>/build'
```

The later AIFF adapter implements bounded AIFF inventory and typed planning.
Next complete ASF metadata inventory and WMA input planning,
preserving tags/artwork and refusing significant unsupported information.
Classify compression by actual codec, map big-endian floats correctly to WAV,
retain all precision/resampling/lossy-generation prompts, and keep input-only
formats out of output choices. Protected content must be refused. Then test
actual adapter/worker publication, cancellation/retry and source preservation
before changing runtime pins, menu capabilities or production packaging.
ALAC remains a separate evaluation; no new Optimize preset is implied.
