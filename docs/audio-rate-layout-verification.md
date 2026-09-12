Audio Rate and Speaker Layout Verification
==========================================

The 2026-09-12 matrix exercises the real private adapter against the pinned curated
audio runtime. It exposes a channel-order defect in the default Opus encoder path
and layouts that the selected presets cannot preserve. The fix explicitly selects
Opus mapping family 1 for supported surround audio. Bitrate, sample-error limits,
resampling consent and lossless preservation requirements remain unchanged.


Authored corpus and exact coverage
----------------------------------

Every source is an independently generated, one-second PCM16 WAV with distinct
channel tones at moderate amplitude. LFE content uses 60 Hz. Multichannel files
declare explicit WAVE extensible speaker masks. Source hashes and write times must
remain unchanged, and adapter scratch must be empty after every operation.
There is no music, speech, customer recording or third-party audio in the corpus.

Mono and stereo are tested at 8,000, 11,025, 12,000, 16,000, 22,050, 24,000,
32,000, 44,100, 48,000, 64,000, 88,200, 96,000, 176,400 and 192,000 Hz. Nine
multichannel layouts are tested separately at 48,000 Hz. Each of the 37 sources
is submitted to all six targets: **222 observations**. This is not a Cartesian
test of every surround layout at every rate or every supported source container.
The earlier six-by-six container matrix remains separate evidence.

| Input layout at 48 kHz | WAV | FLAC | MP3 | M4A/AAC | Ogg Vorbis | Opus |
| --- | --- | --- | --- | --- | --- | --- |
| Mono / stereo | Unchanged | Exact | Pass | Pass | Pass | Pass |
| 2.1 | Unchanged | Exact | Refused | Pass | Refused | Refused |
| Quad | Unchanged | Exact | Refused | Pass | Pass | Pass |
| 4.0 | Unchanged | Exact | Refused | Pass | Refused | Refused |
| 5.0 | Unchanged | Exact | Refused | Pass | Pass | Pass after fix |
| 5.0(side) | Unchanged | Exact | Refused | Refused | Refused | Refused |
| 5.1 | Unchanged | Exact | Refused | Pass | Pass | Pass |
| 5.1(side) | Unchanged | Exact | Refused | Refused | Refused | Refused |
| 6.1 | Unchanged | Exact | Refused | Refused | Pass | Pass after fix |
| 7.1 | Unchanged | Exact | Refused | Pass | Pass | Pass |

At the tested mono/stereo rates, MP3 refuses rates above 48 kHz and AAC refuses
176.4/192 kHz, matching their existing fixed-rate rules. Other tested cells pass.
Opus explicitly resamples non-48 kHz input after the required acknowledgement.
An accepted conversion retains the expected decoded frame count, channel count,
speaker layout, admitted metadata and finite sample comparisons. The generated
signals additionally require RMSE below 0.1; lossless FLAC requires exact samples.
This fixture bound does not replace production validation or listening review.


Channel-order discovery and correction
--------------------------------------

The initial matrix recorded 186 passing observations, 25 refusals and 11 validation
failures. No failed adapter result was admitted for publication. Two Opus failures
were channel permutations, not insufficient bitrate: with the pinned default
mapping, distinct 5.0 output channels matched source indices 0,1,3,4,2; 6.1 matched
0,1,5,3,2,4,6. Explicit family 1 restored the intended order in both cases.

The diagnostic runs compare every decoded channel against every authored source
channel. With family 1, same-channel RMSE is below 0.004 for 5.0 and below 0.025
for 6.1, with each channel closest to its own source. The test-only direct engine
runs retain unadmitted artifacts for inspection. The final acceptance matrix uses
the production adapter, with all ordinary validation still enabled.

The pinned FFmpeg source at revision
`9b0578816c6f94514d330d4f2ae7e44a9fb42692`, `libavcodec/libopusenc.c`, distinguishes
its default legacy multistream path from explicit family selection. The
[FFmpeg implementation documentation](https://ffmpeg.org/doxygen/trunk/libopusenc_8c_source.html)
explains that distinction; [RFC 7845](https://www.rfc-editor.org/rfc/rfc7845.html),
section 5.1.1, defines mapping families and channel meaning. This checkpoint
changes the application's fixed arguments, not the upstream source or runtime.
The complete adapter suite now includes 5.0 and 6.1 regression cases.

Other failed cells changed speaker positions or could not encode the given layout.
The pinned AAC implementation's PCE configuration also maps side-layout inputs
to back-channel declarations. The public plan now refuses those target/layout
pairs before encoding and explains that WAV or FLAC can preserve them. It does
not downmix, rename speaker positions or accept an output with a different layout.
These are limits of the selected presets/runtime, not universal claims about all
possible AAC, Vorbis or Opus implementations. Richer layout support remains a
future implementation/compatibility task rather than permission to drop channels.


Reproduction and retained evidence
----------------------------------

First verify the staged audio identity, then build and run the focused private
contract host with a new repository-local evidence directory:

```powershell
python -B tools/audio-engine/Stage-AudioPayload.py --payload '<isolated production stage>' --inventory
dotnet build proprietary/tests/ContextSuite.Audio.ContractTests -c Release --nologo
dotnet artifacts/managed/bin/ContextSuite.Audio.ContractTests/Release/net10.0/ContextSuite.Audio.ContractTests.dll --rate-layout-matrix '<stage>/audio-engine' '<new .codex-temp evidence directory>'
```

The focused host records each cell and rejects unexpected successes, refusals or
validation failures. A successful run contains **188 passing observations**
(151 conversions and 37 unchanged WAV cases) and **34 expected refusals**.
Refusals are not counted as successful conversions.

The final matrix is
`.codex-temp/audio-rate-layout-108c05febe9b4f238230d0864d4628cb/matrix.json`, with
log `.codex-temp/audio-rate-layout-final.log` and recorded exit code 0. It uses
the native runtime from stage `f3c475e2de144a9789ebd7df46353f4e` and the newly built
adapter, not that stage's older managed application. The initial matrix location
is retained in `.codex-temp/audio-rate-layout-current.txt`. Channel diagnostics are
in `.codex-temp/opus-layout-diagnostic-161809eacd584651bf31d9580662fa6e/diagnostic.json`.

The full curated adapter suite passes **300 checks**, including both new channel
regressions, at
`.codex-temp/audio-ffmpeg-8982dc9e3a0646f0bc201d7c61784128/adapter-68e2eee078674fbfb3bc735100d0dde0`.
The Release foundation passes **2,386 contracts**, including 19 new admission and
preserved-alternative checks. Logs are `.codex-temp/audio-surround-adapter.log`
and `.codex-temp/audio-surround-foundation.log`. The focused matrix predates a
display-name-only change from M4a/Vorbis to M4A (AAC)/Ogg Vorbis in refusal text;
the full suites include that change. No matrix outcome depends on that wording.

Fresh combined stage
`artifacts/production-staging/4921457d351a4daf85270a7f6672b542` builds the updated
managed application/worker and native shell, with zero reported build warnings
or errors and passed engine, notice, dependency and file-inventory checks. The
packaged worker passes **11 audio checks, 52 conversion/publication checks and
20 direct-conversion checks** against the generated six-format corpus. Evidence:
`.codex-temp/audio-engine/worker-7af3047fcd2c4cf3a7113351b638182d` and
`.codex-temp/audio-surround-worker.log`. The runtime inventory remains unchanged
after the run. This stage also includes catalog revision 2026-09-12.1 and the
optional PDF candidates; their full engine suites were not rerun here.

This is generated-signal and adapter evidence. It does not certify listening,
speaker playback, every source format/rate/layout
combination, screen-reader delivery, visible UI or installed behavior. Copies
and source preservation are retained; no native recycling was tested.


Independent Opus decoder investigation
--------------------------------------

The subsequent [decoder probe](../tools/audio-engine/Test-OpusDecoder.py) compares
the actual 33 accepted Opus outputs with explicit FFmpeg native `opus` and Xiph
`libopus` decoders. The pinned supplier runtime contains both implementations;
its archive and executable/DLL hashes are verified before and after decoding.
Production uses FFmpeg's native decoder. These are separate audio decoders with
shared FFmpeg container handling, not an independent end-to-end playback stack.
The [FFmpeg codec documentation](https://ffmpeg.org/ffmpeg-codecs.html#libopus)
describes the two implementations.

All 33 outputs decode to exactly 48,000 finite frames with both decoders, without
trimming, padding or an output resampling request. At 48 kHz, all seven layouts
also preserve the distinct source-channel signals: each decoded channel is closest
to its own source channel, with same-channel RMSE below the existing generated
fixture bound of 0.1. Both implementations are checked against the source.
A swapped-channel reference is detected, as is a truncated encoded file. Source
and encoded-output hashes and write times remain unchanged.

The strict cross-decoder sample comparison still exits 1; the later spectral
comparison below provides additional evidence without changing that threshold.
Its initial diagnostic maximum-difference threshold of 0.0001 is not an Opus
conformance or listening standard. Explicit floating-point decode requests do not
remove the differences:

| Layout | Maximum absolute decoder difference |
| --- | --- |
| 5.0 | 0.0339211 |
| 5.1 | 0.0339525 |
| 6.1 | 0.000124209 |
| 7.1 | 0.0366483 |

The larger differences concentrate in the center channel of 5.0, 5.1 and 7.1.
A diagnostic shift comparison does not explain them as a simple constant timing
offset. The native implementation has a SILK resampling path, but inspecting that
path does not establish the cause. No production preset or validation tolerance
was changed to obtain a pass. The mode investigation below narrows the difference;
perceptual review remains required before closing independent Opus fidelity acceptance.

Reproduce using the retained accepted adapter matrix and verified supplier bin:

```powershell
python -B tools/audio-engine/Test-OpusDecoder.py --matrix '<rate-layout matrix directory>' --decoder '<verified evaluation bin>'
```

The complete float-request run is retained at
`.codex-temp/opus-decoder-c4f40c7f41d1448cb4ca578c6cb171a3/opus-decoder.json`,
with `.codex-temp/opus-decoder-float.log` and recorded exit code 1. The report
explicitly marks `review-required` and retains differences, channel comparisons,
negative controls and decoder identities. Earlier default-sample-format
observations remain at
`.codex-temp/opus-decoder-519c42a86592471898b9148bcda5c3bf/opus-decoder.json`;
the diagnostic shift results are `.codex-temp/opus-center-lags.json`.
This probe is generated-tone evidence, not human listening, other-player
acceptance or independent verification of the input resampler.


Codec-mode and spectral comparison follow-up
--------------------------------------------

Eight disposable encodes compare the normal `audio` application mode with
`lowdelay` for 5.0, 5.1, 6.1 and 7.1. Both keep the same 160 kb/s VBR setting and
explicit mapping family 1. With `audio`, the larger differences recur. With
`lowdelay`, every channel's maximum decoder difference is below 0.00000015.
The [FFmpeg options documentation](https://ffmpeg.org/ffmpeg-codecs.html) explains
that low-delay mode disables voice-optimized modes. This narrows the observed
variation to the voice-capable path; it does not prove a specific filter defect.
The production preset remains `audio`.

[RFC 6716 section 4.2.9](https://www.rfc-editor.org/rfc/rfc6716.html#section-4.2.9)
permits different SILK resampling methods and explains why phase differences can
defeat simple sample alignment. Its
[testing guidance](https://www.rfc-editor.org/rfc/rfc6716.html#section-6.1) uses
`opus_compare` for a spectral metric. A score of zero or higher passes that tool, but the
RFC recommends a score above 90 for 48 kHz decoding unless listening establishes
acceptable quality. Running this metric on authored tones does not perform the
RFC's full decoder conformance test, including official vectors and range states.

The new [comparison tool](../tools/audio-engine/Test-OpusComparison.py) verifies
the already-pinned Opus source archive, builds its unmodified `src/opus_compare.c`
in fresh repository scratch, and compares the actual 33 adapter outputs. Each
speaker is extracted from explicit floating-point native/Xiph decodes, quantized
to PCM16 identically and duplicated into stereo for the tool's input convention.
No frames are shifted, dropped or padded. Xiph is the reference. Output/runtime
identities are checked again afterwards; originals are not inputs to this decoder
comparison and are not modified.

All **72 speaker comparisons** pass the upstream metric. Silence and a different
speaker tone both fail as expected. Scores range from **48.8 to 100**. Four
comparisons remain below the 48 kHz listening-review recommendation:

| Source layout | Speaker index (zero based, WAVE order) | Score |
| --- | --- | --- |
| 5.0 | 2, center | 62.0 |
| 5.1 | 2, center | 61.3 |
| 7.1 | 2, center | 48.8 |
| 7.1 | 7, side right | 77.7 |

The report separates `metricStatus: passed` from `fidelityAcceptance: incomplete`
and lists these four results in `listeningReview`. This is additional independent
decoder evidence, not permission to close listening, player or overall fidelity
acceptance. The earlier strict numeric probe and its failed result are retained.
Neither production encoding nor production validation changed.

```powershell
python -B tools/audio-engine/Test-OpusComparison.py --matrix '<rate-layout matrix directory>' --decoder '<verified evaluation bin>' --opus-source-archive '<retained pinned opus.zip>'
```

Final evidence is
`.codex-temp/opus-comparison-b2c0ffd513a5456fbec7db86027a16c8/comparison.json`,
with `.codex-temp/opus-comparison-final.log` and recorded exit code 0. Build output,
source/license, comparator hash and each compared PCM pair are retained beside
the report. Mode evidence is
`.codex-temp/opus-modes-4f392141016045f88538840fcd3c223d/mode-comparison.json`,
produced by the retained `.codex-temp/InspectOpusModes.py` diagnostic. Those
eight direct encodes are diagnostic artifacts, not adapter-admitted outputs.
