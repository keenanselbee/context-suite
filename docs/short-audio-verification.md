Short Audio Verification
========================

Follow-up: [short MP3 padding verification](mp3-short-padding-verification.md)
corrects the five MP3 failures below. This document retains candidate 1.0.8's
original results; its failed matrix is not retroactively counted as passing.

Candidate 1.0.8 fixes FLAC conversion of recordings shorter than sixteen samples.
The 2026-09-14 matrix also exposes an unresolved MP3 padding defect. This closes
a specific FLAC failure, not the broader [audio acceptance](audio-rate-layout-verification.md).


Failure and correction
----------------------

The existing rate/layout corpus uses one-second signals. The new private
`--short-audio-matrix` mode generates stereo PCM16 WAV at 48 kHz with 1, 2, 15,
16, 46, 47, 48, 127, 1023, 1024, 1151, 1152, 2049, 4801 and 48000 frames.
Distinct moderate-amplitude channel signals include nonzero boundary samples.
Every source goes through the real file adapter to all six targets. Failures
remain in the report and make the matrix return nonzero.

The initial nine-length matrix passed 52 of 54 observations. One-sample FLAC
failed because the encoder inferred a nominal block size of one. A direct
diagnostic retained the encoder's invalid-block-size error. FLAC allows a short
final block, while its nominal minimum is sixteen samples; see
[RFC 9639 section 4.1](https://www.rfc-editor.org/rfc/rfc9639.html#section-4.1)
and the [encoder's initialization check](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/flacenc.c).

The adapter now supplies `-frame_size 16` only when the captured reference has
fewer than sixteen frames and the target is FLAC. It retains compression level 8,
the selected precision and all metadata/sample checks. This sets an encoder
block limit; it does not add samples. Inputs at or above sixteen frames retain
their previous encoding arguments. The decision uses the decoded reference at
the planned rate, rather than a filename or rounded duration.

The other initial failure was one-sample MP3. A separate diagnostic using the
same quality setting produced MP3 that both pinned decoders read as 47 frames.
The expanded matrix finds failures at 1, 2, 15, 16 and 46 frames, with 47 and
the tested longer lengths passing. The sample validator continues to reject
extra decoded data, now with an explicit length-validation error rather than
an unexpected end-of-stream exception. No padding is silently accepted or cut
away. The short-MP3 defect remains open; no minimum supported duration is adopted
from this experiment.


Actual verification
-------------------

- The final matrix retains **90 observations: 85 pass, five fail**. All fifteen
  FLAC outputs pass, and all five failures are the named MP3 cases. The suite
  intentionally returns nonzero; this is not a passing complete audio matrix.
- Successful outputs have independently decoded finite samples and the exact
  original frame count. All fifteen FLAC files additionally match every original
  PCM16 sample exactly through the separately pinned decoder. WAV no-ops retain
  identical bytes. Output hashes are reconciled against the matrix.
- Every attempted adapter operation retains source bytes/write time and removes
  its owned scratch after disposal, including the failed MP3 operations.
- **20 staged-worker direct checks** pass: six short FLAC copies with exact
  declared extent and unchanged originals, same-format no-op admission, safe
  failure without committed MP3 output, and successful later work on the same
  worker. The first direct assertion incorrectly required a null publication
  record; it now checks the actual abandoned, uncommitted reservation and absence
  of an MP3 output. That failed diagnostic is retained.
- **2,813 foundation contracts** pass, including fragmented extra-sample refusal.
  Release test-host and production builds complete with zero warnings/errors.
  The first PowerShell foundation wrapper stopped on expected listener stderr;
  the completed host run captures stderr separately and has exit code zero.

Reproduce with freshly built Release hosts and absolute paths:

```powershell
dotnet '<private contract DLL>' --short-audio-matrix '<pinned candidate bin>' '<new evidence directory>' '<pinned independent bin>'
dotnet '<public contract DLL>' --short-audio-direct '<new evidence directory>' '<staged worker>' '<matrix fixture directory>'
```

Final matrix evidence is
`.codex-temp/short-audio-final-b537185734d74f0eb91f8b73e3f288b0/matrix.json`.
The initial matrix is
`.codex-temp/short-audio-d74b3a4d5d594d6e847dd717d7f9001e/matrix.json`.
Direct encoder/decoder diagnostics and verified runtime identities are under
`.codex-temp/short-audio-diagnostic-353689d64919424489ce16782c088626`.
The final direct report is
`.codex-temp/short-audio-direct-ba3b4af7f1034009a13f5fa0b24059e0/short-audio-direct.json`.
Logs use `.codex-temp/short-audio-final`, `short-audio-direct-final` and
`short-audio-foundation-final` prefixes. Independent sample/output-hash and
payload checks are retained in `.codex-temp/short-audio-verification.json`.


Candidate and remaining work
----------------------------

Candidate **1.0.8** is staged at
`artifacts/production-staging/056d900511c1404eab414951c4d49e0f` with 116 verified
files and inventory SHA-256
`D540BB1B5E85F8B067D93E69B0DA5EF08D72E8CAE0E5914F632C301F9568023A`.
The reservation is `artifacts/production-version-receipts/1.0.8.json`.
Candidate 1.0.7's 116 files and inventory remain unchanged.

Next, investigate MP3's delay/padding handling without weakening exact duration
validation. Other input containers, short resampled signals, sample precisions,
surround layouts and short FLAC optimization are outside this new matrix.
The broader metadata, listening/player, visible UI, accessibility and integrated
release gates remain open. No installed app, Explorer registration, live license,
native recycling or publication is exercised by these isolated tests.
