Independent WMA Codec Evaluation
================================

Status: **WMA fidelity not accepted**, 2026-09-17 local date. The Lossless fixture
writer's packet loss is corrected; 13 Lossless cases now preserve exact source
samples. Opt-in repairs now pass 20 Standard sample/extent checks, 26 Pro extent
checks and 23 Pro stereo sample checks. Voice differences remain. WMA stays outside
customer commands and the selected production payload; its required input goal
is unchanged. The original failed observations below remain part of the record.


Method and finite boundary
--------------------------

[WmaFixtureWriter.cpp](../tools/audio-engine/WmaFixtureWriter.cpp) is an
independently authored, test-only Windows Media Foundation generator/decoder.
It uses system Standard, Professional, Lossless and Voice codecs to encode our
own integer signals, retaining exact source PCM and Windows float32 decoding.
It uses fixed local filenames in a fresh repository scratch directory, rejects
existing files and bounds rate/channels/precision/duration. No music or customer
file, reference executable, installation or codec redistribution is involved.

The [runner](../tools/audio-engine/Test-WmaSystemCodecs.py) compiles with the
existing x64 compiler and SDK 10.0.26100.0, `/W4 /WX`, static CRT and DEP/ASLR.
It records source/tool hashes, OS build, loaded module paths/hashes and the exact
seven-file expanded candidate runtime. It refuses modules outside Windows and
the owned helper. Each native child has a 30-second deadline; tool compilation
has 60 seconds. Fixture PCM is at most three seconds, system decoding is limited
to 16 MiB, and candidate output is limited to the declared finite corpus.
This evaluation harness is not the production worker's isolation boundary.

The frozen matrix contains 12 profiles at three lengths: one frame, 2,049 frames
and one second, for **36 cases**:

| Family | Authored profiles |
| --- | --- |
| Standard / wmav2 | 44.1 kHz mono PCM16; 48 kHz stereo PCM16 |
| Professional | 48 kHz stereo PCM16/24; 48 kHz 5.1 PCM24 |
| Lossless | 44.1 kHz stereo PCM16; 48 kHz stereo PCM24; 96 kHz 5.1 PCM24 |
| Voice | 8, 11.025, 16 and 22.05 kHz mono PCM16 |

Signals have distinct channel tones and deterministic low-level integer detail.
Surround input uses explicit Windows 5.1 order; the decoder must retain rate and
channel count. This is a sample/extent corpus, not representative music/speech
or listening acceptance. Lossless requires exact samples and complete extent
against the authored source and independent decoder. All codecs require equal,
nonempty decoded extents between implementations for this comparison. Prefix
sample error is reported separately; it never excuses a missing tail. No
comparison aligns, pads or trims audio to pass.


Isolated Standard decoder repair
--------------------------------

The final Standard packet can leave a complete encoded frame in the decoder's
bit reservoir. The original EOF path emits existing overlap without decoding
that frame. This damages the audible tail and can omit another 512 reconstructed
samples. A retained-state diagnostic at
`.codex-temp/wma-standard-state-2cb2140a185444ac9af68070b8adeed0` consumes that
reservoir with the pinned decoder and captures its remaining overlap. All six
original Standard cases then match Windows extents; peak differences stay below
`5e-7` and RMS below `1.2e-7`. This diagnostic is separate from native repair
acceptance and is not a production decoding path.

The opt-in `--wma-standard-tail-repair` build option applies the local
[reservoir patch](../tools/audio-engine/wma-standard-reservoir-tail.patch) only
to a fresh extraction. At EOF it decodes retained bytes, rejects an exhausted
bitstream, and appends the actual overlap determined by the final block size.
The non-reservoir output path remains unchanged. It can be combined with the
Pro repair; each selected patch has its own hash, source change and version
suffix. No synthetic packet, output padding or sample alignment is used by the
patch.

Before building, the Standard acceptance limits were fixed at peak error `1e-6`
and RMS error `3e-7` against complete Windows output. Encoder padding remains
part of that comparison; no authored-duration trim is allowed. The corpus adds
14 stereo PCM16 source lengths: 511, 512, 513, 1,023, 1,024, 1,025, 2,047,
2,048, 2,050, 4,095, 4,096, 4,097, 47,999 and 48,001 frames. Together with the
six existing Standard cases and previous Pro/Lossless/Voice cases, this makes
71 observations. Broader rates, coding modes, discontinuities, malformed inputs
and listening remain separate acceptance gates.

The combined repaired build is
`.codex-temp/audio-ffmpeg-9442e1e447824b6dac315cf4128903ce`, with suffix
`contextsuite-g9b0578816c6f-wmapro-tail2-wmastandard-tail1`. Its seven runtime
files total 5,056,000 bytes, 512 bytes more than the Pro-only build. Standard
patch SHA-256 is
`356C4F674923E542C22501ADAEBDB29143E3652431AE6915D917DFD17040DAEB`;
the modified `libavcodec/wmadec.c` hash is
`F0C6D70449D578B983FA9EB6C541D230471C95DD5F31B0AC007F0C96B8E4C455`.
The original source archive/inventory and the Pro patch remain unchanged.

Fresh corpus `wma-system-matrix-4ec8e4f014164f228c1de7af3a94307e` completes
**71 observations with 62 equal nonempty extents**. All **20 Standard cases**
pass the fixed sample limits; largest peak/RMS differences are approximately
`4.77e-7`/`1.12e-7`. All **26 Pro extents**, **23 stereo Pro accuracy checks**
and **13 exact Lossless cases** still pass. Nine Voice extent failures remain,
with its earlier sample differences; the runner correctly returns exit 1 and
`accepted: false`. Equal surround Pro extents still do not certify its fidelity.

Replay `wma-standard-repair-replay-f061c3c4a5754c969c6716c278a8af82` uses the
previous Pro-repaired corpus's exact files and retained Windows output. All
**57 repair/preservation assertions** pass: six Standard cases now meet complete
Windows extent/accuracy, their already decoded prefixes remain byte-identical,
and all 51 non-Standard outputs remain byte-identical. The last group includes
known Voice failures; it is preservation evidence, not acceptance of Voice.

Additional evidence inside the combined build:

- `build-review-8094a1f9269e4b3bb2674d32a9ba5305`: both patch identities,
  eleven previously reviewed diagnostics and all seven hardened PE files.
- `expanded-inputs-3da20faf8e5c4c1a9adebd6101930ab8`: all 27 existing input
  fixtures pass, including non-reservoir WMA v1/v2 and exact AIFF PCM/float.
- `smoke-30a1826d167f460ba8fc60a776ebdb21`: all six output targets, artwork
  decoding and original preservation pass.

Twelve provenance checks pass at
`.codex-temp/wma-standard-repair-guards-d35a3f1f566d4b2d84a6bcf9896a4497`,
covering selected patches and refusal of missing/swapped/altered entries,
unselected flags and changed effective-source identity. Missing expanded-input
opt-in is also refused before dependency access. No managed production runtime
pin, worker allowlist, installed app or reserved package changed; managed suites
were not rerun for this isolated native/tooling change.


Isolated Pro decoder repair
---------------------------

The build's `--expanded-inputs --wma-pro-tail-repair` options apply the local
[terminal-overlap patch](../tools/audio-engine/wmapro-terminal-overlap.patch)
only to a fresh extraction of the pinned source archive. Original archives and
earlier candidate directories remain unchanged. The patch reserves output space
for reconstructed terminal overlap, copies the actual decoded samples before
applying end trim, and honors a one-frame stream's explicit start/end trims when
they leave nonempty audio. It does not synthesize, align or pad output samples.
XMA's existing output allocation and first-frame behavior remain unchanged.

The second evaluation build is
`.codex-temp/audio-ffmpeg-f7b5d586b8934274abfa9d281cb52761`, with runtime suffix
`contextsuite-g9b0578816c6f-wmapro-tail2`. Its seven runtime files still total
5,055,488 bytes. Patch SHA-256 is
`0DD4F873B78500E3BC6DBC068EBED3388FE61A537FE7CFA9C987F8DE90F86D4F`;
the sole changed native source, `libavcodec/wmaprodec.c`, is
`8A277B6BABC7D395F56025F1085D720947C3FE53E59FE0B026F1F8085249ED04`.
The build preserves the original 10,422-file inventory and records effective
source and patch hashes. Native review checks those identities, the original
eleven reviewed compiler diagnostics and all seven PE hardening/import results.
No new diagnostic was allowlisted. Ten final patch/provenance controls pass at
`.codex-temp/wma-pro-final-guards-e1a63d9cce26486482e743e21decd404`.

The fresh system-codec run at
`.codex-temp/wma-system-matrix-0ea7d0e97ddd4c69b0a28e65be369650` completes all
57 observations: **46 equal nonempty extents**, including **all 26 Pro cases**,
and **13 exact Lossless cases**. All **23 stereo Pro cases** meet complete-output
peak error `1e-6` and RMS error `3e-7` against Windows; observed maxima are about
`5.67e-7` and `1.55e-7`. These limits were fixed before examining the repaired
output. The three surround cases retain their earlier sample differences and
are not certified for broader fidelity or listening quality by equal lengths.
The remaining **11 Standard/Voice extent failures** keep exit 1 and
`accepted: false`; the required WMA goal remains open.

The separate replay at
`.codex-temp/wma-pro-repair-replay-344a08e2c2b948b5b68afc710b58792c` uses the
previous corpus's exact files and Windows output. All 57 repair/preservation
assertions pass: complete Pro output and recovered stereo tails meet the fixed
criteria, old Pro prefixes are byte-identical, and all 31 non-Pro outputs are
byte-identical to the previous decoder. Those 31 unchanged outputs include known
failures; this is not a claim that all WMA codecs pass.

Additional evidence inside the new build:

- `build-review-4cbb21b18a544fc2ad5a61b0db10d340`: native diagnostics, patch identity
  and seven hardened PE files.
- `expanded-inputs-7dbf14e017fe4cc9aebf09e44032c263`: 27 AIFF/WMA input fixtures,
  exact authored PCM/float comparisons and unchanged originals.
- `smoke-14d989a22ab74df0ad4218339ffa9d0d`: six output targets and PNG artwork
  decoding; the smoke version gate now checks the explicit repaired suffix.

Retained failed attempts: build `audio-ffmpeg-0ca19499e18c4cfbb7c7ba818f72fbc2`
recovered ordinary terminal overlap but still discarded the one-frame inputs;
its corrected replay `wma-pro-repair-replay-1a65b2bb7b994797ab7ad78a476fd811`
passed 54/57 repair/preservation assertions. An earlier replay stopped when its
comparison code assumed empty output had a numerical error value; empty output
now remains a recorded failure. The first recipe is retained at
`.codex-temp/wma-pro-repair-attempt1-recipe`. Smoke attempt
`smoke-044ddeacff794f619c8a82e66a48b995` rejected the new version suffix before
the exact suffix gate was updated; media assertions were not relaxed.

Standard is not merely an extra-padding discrepancy: the 44.1 kHz case differs
within authored frames 43,969-44,099, and the 48 kHz case also differs before the
authored end. Voice differences extend throughout the signal. Diagnostic
localization is retained in `.codex-temp/wma-standard-voice-divergence.json`;
its `1e-3` threshold locates differences and is not an acceptance tolerance.
These families require separate decoder evaluation/repair. Wider Pro profiles,
malformed input, metadata, actual-worker recovery, listening, source distribution
and production adoption remain open. No application payload or command changed.


Pro terminal-overlap investigation
----------------------------------

The later 57-case run at
`.codex-temp/wma-system-matrix-b4e0b36daab146de849cb4f9f92f8469` completes every
observation, with **27 equal nonempty extents** and all **13 Lossless cases**
still exact. Thirty Standard/Pro/Voice extent failures remain; exit 1 and
`accepted: false` remain the correct result. Seventeen added stereo Pro PCM16
lengths exercise 512/1,024/1,536/2,048/4,096-frame boundaries and one second.

The independently authored
[frame inspector](../tools/audio-engine/Inspect-WmaProFrames.py) reads bounded
ffprobe packet dumps for the corpus's 48 kHz, `0x00e0` Pro profile. It records
frame sizes, per-channel subframe lengths and start/end trim declarations;
it does not decode audio or modify input/output samples. The runner pins its
source hash and retains `frame-headers.json` for each Pro case.

The whole-frame count minus declared trim matches the selected decoder's output
after accounting for refusal to emit a negative-length frame. Adding the final
overlap, `(2048 - largest final channel block) / 2`, predicts the Windows and
authored extents in **all 26 Pro cases**. Stereo 2,049-frame sources lose 768
frames; one-second sources lose 512. New 511/512/513-frame sources produce no
candidate samples despite native exit 0, while Windows preserves their full
extents. Exact 2,048/4,096-frame controls and the surround cases remain useful
passing controls. The inspector's eight malformed/profile refusal checks pass.

This is evidence for missing terminal overlap, not a verified decoder repair or
a policy permitting synthesized padding. Samples must be recovered from the
decoded overlap and compared against Windows, including the full tail. No
selected FFmpeg source, binary, customer capability or production payload was
changed. The investigation used static inspection of the independently pinned
decoder's frame tiling, trim and overlap handling; its
[upstream implementation](https://www.ffmpeg.org/doxygen/trunk/wmaprodec_8c_source.html)
provides background, while the retained local source revision remains authoritative.

Earlier diagnostic evidence is retained at
`.codex-temp/wma-pro-tail-d9932da55d824dd6870c6b1fac687bd4`, including seven
original packet/header comparisons and
`boundaries-04f1b1e887f14b43a3fc5ad7388def5c` with 17 generated lengths. An initial
scratch parser omitted variable subframe tiling and produced invalid trim
interpretations; it was corrected before the versioned inspector and full run.
The old scratch `headers.json` also labels a final block length as frame bits;
use `inspected-headers.json` or the final run's `frame-headers.json` instead.


Current Lossless fixture correction
-----------------------------------

The original convenience ASF sink applied inappropriate buffering to Lossless
VBR output. The generated high-rate surround file contained codec packet-sequence
jumps, so neither decoder could recover its missing tail. Setting properties on
the encoder alone, adding an end-of-segment notification, changing input buffer
sizes/throttling and using another file-opening path did not fix the loss.

The tool now creates an explicit ASF sink/profile for Lossless, sets matching
VBR/quality settings on its stream and encoder before media-type negotiation,
and enables `MFPKEY_ASFMEDIASINK_AUTOADJUST_BITRATE`. The container can accommodate
the encoded data instead of dropping packets. Other codec fixture paths are
unchanged. This corrects fixture construction; it does not weaken any decoder
comparison or change a production encoder/decoder.

The final run at
`.codex-temp/wma-system-matrix-f48320a78b7b4df08c4b9892f23cdbe7` completes
**40 observations**, including four new Lossless 96 kHz 5.1 tail regressions at
65,535, 65,536, 65,537 and 192,000 frames. All **13 Lossless cases** match exact
authored samples and full extents in both decoders, with no packet-loss
diagnostics. There are **25 equal nonempty decoder extents overall**; the 15
Standard/Pro/Voice failures remain, so the runner still correctly returns exit 1
and `accepted: false`. No listening, worker, metadata or release gate is implied.

The corrected native tool builds with zero warnings/errors. Its executable hash
is `6B18EF9C292AE08F0936D374BEE737F515BB197095AE053070C6BDD579B2CCD2`, and its
C++ source hash is
`BE4037550098D70AB95EB569337E9D350B860B4269AC9B65C7AACDDD2D66E309`.
The earlier corrected 36-case run at
`wma-system-matrix-cc6a3a65cbde428799ddc4d7efea7894` passed all nine Lossless
cases before the four boundary regressions were added.

Diagnostic evidence is retained under `.codex-temp/`:

- `wma-drain-e28222d4dc8c4d8d9968d75f828d530b`: encoder settings/statistics and
  end-of-segment controls, still shortened.
- `wma-drain-lengths-7a1b18247f4f476bbdaa97847b27b66f`: seven duration/layout
  cases locate the tail loss; retained packet/frame logs identify sequence gaps.
- `wma-buffering-870daabfb3b044a3ab797114d922b2c1`: throttled, larger-buffer and
  single-input controls, still shortened.
- `wma-config-e7b5bdfd53c244ec8d557e5705eb25e7` and
  `wma-sink-path-8d2bc0aad9a24e18921be7c9e1df0698`: encoder-only configuration
  and file-opening controls, still shortened.
- `wma-asf-config-f0c6e3bf895f45fda9d02f1beec683eb`: explicit matching ASF/VBR
  configuration restores the formerly failing 96,000-frame system decode.

The fix follows Microsoft's
[ASF stream configuration](https://learn.microsoft.com/en-us/windows/win32/medfound/adding-stream-information-to-the-asf-file-sink),
[file-sink properties](https://learn.microsoft.com/en-us/windows/win32/medfound/setting-properties-in-the-file-sink)
and [sink-writer encoder configuration](https://learn.microsoft.com/en-us/windows/win32/medfound/mf-sink-writer-encoder-config).


Original failing results
-------------------------

Original evidence:
`.codex-temp/wma-system-matrix-0d4e45e8642f48c692044df4ef171889`.
All **36 observations completed**, but only **21 have equal nonempty decoded
extents**, and **8 of 9 Lossless cases** meet the exact authored-source check.
The runner correctly returns exit code 1 and writes `accepted: false`.
This is a completed investigation with failed codec acceptance, not a green
conversion matrix. Five generator parameter/collision guards pass; existing
PCM, WMA and Windows-decoded files retain their hashes after a repeat is refused.

| Case | Candidate / Windows / authored frames | Finding |
| --- | --- | --- |
| Standard 44.1 kHz, one second | 45,056 / 45,568 / 44,100 | Different decoded tails; both include encoding padding |
| Standard 48 kHz, one second | 49,152 / 49,664 / 48,000 | Same 512-frame decoder disagreement; padding policy remains unresolved |
| Stereo Pro PCM16/24, 2,049 source frames | 1,281 / 2,049 / 2,049 | Candidate omits 768 frames |
| Stereo Pro PCM16/24, one second | 47,488 / 48,000 / 48,000 | Candidate omits 512 frames; retained prefix agrees closely |
| Pro 5.1, three tested lengths | Equal extents | Bounded differences remain; this is not complete Pro fidelity acceptance |
| Lossless stereo, all six cases | Exact authored frames and samples | Includes PCM16/24 and one-frame signals |
| Lossless 96 kHz 5.1, one/2,049 frames | Exact authored frames and samples | Confirms tested channel order and precision |
| Lossless 96 kHz 5.1, one second | 65,536 / 65,536 / 96,000 | Both decode an exact prefix; fixture encoding/finalization loses the authored tail |
| Voice, one-frame sources at all four rates | 0 / 1 / 1 | Candidate returns no samples despite successful native exit |
| Voice 16/22.05 kHz, 2,049 frames | 1,920 / 2,049 / 2,049 | Candidate omits 129 frames |
| Voice one second at 11.025/16/22.05 kHz | 11,040/16,320/22,080 versus 11,025/16,000/22,050 | Candidate retains extra samples |

The Lossless surround encoding failure is distinct from a decoder disagreement.
Its output cannot establish preservation of the one-second authored signal even
though both decoders agree exactly. Diagnose the fixture writer/encoder drain
before using that profile as a positive lossless oracle. Do not pad its output
or treat its shortened source as an accepted substitute.

The retained build has zero warnings/errors. The helper executable SHA-256 is
`7B7529FB9C81B097A2A2E07C2D2233B181A958F3EACFD3717BFBC371553C21AA`;
its C++ source SHA-256 is
`CC65DEFDB7D89BB55316D1D457BD05BE118D8C9E47E8C1BD59E0DE722AA92780`.
The OS reports build 26200. `results.json` includes actual WMADMOE/WMADMOD and
WMSPDMOE/WMSPDMOD module hashes, complete comparisons and fixture identities.
ASF creation timestamps make newly generated container hashes run-specific;
the authored sample oracle and selected parameters remain explicit.

Earlier scratch attempts are retained.
`wma-system-628d0812dc1b44d8b3abff33038b04bb` incorrectly closed a stream already
finalized/closed by the sink; `wma-system-40a139bfaf1147318ebf6992ff0001d1` failed
compilation on signed SDK stream constants. The first full matrix
`wma-system-matrix-4f42286725f845dfbd2489e3fd016325` expected a PCM channel-mask
attribute on compressed types; explicit authored 5.1 input fixed that harness
error. Matrix `wma-system-matrix-8285cf0b157c400691dbf2d0234681ee` observed the
remaining codec problems, but classified empty Voice output as a comparison exception.
The final run records zero-length native output directly as failed extent
acceptance. None of those changes relaxed codec fidelity criteria.


Reproduction and next decisions
--------------------------------

```powershell
python -B tools/audio-engine/Test-WmaSystemCodecs.py '<expanded build workspace>'
```

The expected current result is exit 1 with a complete failed-acceptance receipt.
Inspect per-case writer/native logs and comparisons; do not report mere process
success or equal FFmpeg builds as fidelity evidence. No app build is needed.

Next extend Standard/Pro fidelity and hostile-input evidence beyond the repaired
corpus. Investigate Voice separately or evaluate a bounded Windows decoding path
against the same authored samples and independent evidence. Resolve broader
Standard timeline behavior and retain the corrected Lossless fixture regression.
Malformed/discontinuous timeline tests remain a separate gate.
Then finish ASF metadata compatibility, actual-worker publication/recovery,
resource budgets and human listening/usability before enabling WMA commands.
No required codec family is silently removed by these failed observations.

Primary design sources are Microsoft's
[Windows Media Audio encoder](https://learn.microsoft.com/en-us/windows/win32/medfound/windowsmediaaudioencoder),
[audio encoding configuration](https://learn.microsoft.com/en-us/windows/win32/medfound/configuringaudioencoding),
[Lossless configuration](https://learn.microsoft.com/en-us/windows/win32/medfound/configuring-standard--professional--or-lossless-audio-encoding)
and [sink-writer input configuration](https://learn.microsoft.com/en-us/windows/win32/api/mfreadwrite/nf-mfreadwrite-imfsinkwriter-setinputmediatype).
The helper uses those documented APIs; it does not copy Microsoft example
implementations into the product or introduce a WMA output command.
