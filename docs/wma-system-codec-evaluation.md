Independent WMA Codec Evaluation
================================

Status: **WMA fidelity not accepted**, 2026-09-16 local date. The Lossless fixture
writer's packet loss is corrected; 13 Lossless cases now preserve exact source
samples. Standard/Pro/Voice sample-extent differences remain. WMA stays outside
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

Next identify and repair the selected decoder's Pro/Voice tail handling or
evaluate a separately bounded Windows decoding path against this same corpus.
Resolve Standard padding/timeline behavior; retain the corrected Lossless
fixture regression. Malformed/discontinuous timeline tests remain a separate gate.
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
