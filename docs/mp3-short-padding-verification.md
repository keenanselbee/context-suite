Short MP3 Padding Verification
==============================

The 2026-09-14 follow-up corrects the five MP3 duration failures retained in the
[short-audio checkpoint](short-audio-verification.md). The previous failed reports
remain historical evidence. Broader audio and player acceptance remain open.

The pinned muxer replaces its padding value for each packet. For recordings
shorter than 47 samples, trailing padding spans multiple packets and the final
duration declaration describes 47 samples. Both independent and candidate
decoders therefore produced extra samples. The defect is in the declaration;
the sample validator correctly refused the result.

The adapter now reconciles only a newly encoded, exclusively owned MP3 candidate
whose captured reference has 1-46 frames. It checks the complete MPEG inventory,
the pinned encoder marker, Xing fields, byte/frame counts and header checksum.
Only the reviewed 47-frame declaration with the expected encoder delay can be
corrected. It writes the delay/padding field and header checksum, leaving encoded
audio bytes intact. Existing decoded duration, sample-error, metadata and safe
publication checks still apply. Cancellation abandons the owned candidate.

The pinned muxer also calculates its checksum over a fixed 190-byte prefix,
including zero allocation padding for a shorter first frame. The reader accepts
that verified pinned layout or the standard checksum extent; rewritten headers
use the standard extent ending before the checksum. Relevant primary code is
the [FFmpeg MP3 muxer](https://github.com/FFmpeg/FFmpeg/blob/master/libavformat/mp3enc.c)
and the [LAME tag writer](https://github.com/lameproject/lame/blob/master/libmp3lame/VbrTag.c).
The inspected local FFmpeg source is pinned to revision
`9b0578816c6f94514d330d4f2ae7e44a9fb42692` by the engine source manifest.

Verification
------------

- The original 90-cell, six-target short-recording matrix now passes, including
  the five MP3 failures from candidate 1.0.8.
- A separate matrix covers nine MP3 rates (8-48 kHz), mono/stereo, and lengths
  1, 2, 15, 46 and 47 frames. All 90 outputs have the exact intended frame count
  and finite samples through the independently pinned decoder with MP3 selected.
- An uncorrected control is independently encoded for every case using the same
  source and fixed options. Byte comparison permits changes only in the three
  delay/padding bytes and two checksum bytes. All 47-frame controls are identical.
- Ten guards pass: invalid checksum, flags, encoder marker, frame count, byte
  count, truncation, invalid reference bounds, pre-cancellation and idempotence.
  Refusals leave candidate bytes unchanged. Sources retain bytes and write times;
  adapter scratch is cleaned after every attempt.

Automatic input recognition succeeds for 89 of these 90 files in the independent
runtime. The 16 kHz mono, 47-frame file fails automatic probing, including before
the correction; explicitly selecting MP3 decodes exactly 47 frames. This remains
an open tiny-file compatibility case. The matrix records automatic recognition
separately from explicit-format decoding; it does not claim all-player acceptance.

Reproduce with a Release private contract host and absolute paths:

```powershell
dotnet '<private contract DLL>' --mp3-padding '<pinned candidate bin>' '<new evidence directory>' '<pinned independent bin>'
```

Final rate/length/control evidence and candidate guards are under
`.codex-temp/mp3-padding-reviewed-f95f85e9e9a4473699d1479ee498737d`.
The original matrix follow-up is
`.codex-temp/mp3-padding-matrix-cc597365c729414187aebcee0f88d0c5/matrix.json`.
Automatic-probe diagnostics are in `.codex-temp/mp3-independent-probe.json`.
Failed intermediate checksum-layout experiments are retained separately.

Candidate 1.0.9 and publication
-----------------------------

Candidate **1.0.9** is staged at
`artifacts/production-staging/4798b0329381494f8db92a8441aa2ef0` with 116 verified
files and inventory SHA-256
`4F597A3BC94D5F46023927FED7B28723E6C6021C689DEFFBAB4BA96E09213BAF`.
Its reservation is `artifacts/production-version-receipts/1.0.9.json`.
Candidate 1.0.8's inventory and all 116 files remain unchanged.

Thirty isolated direct checks pass: six short FLAC copies with exact extents,
unchanged originals and no-op admission; six short MP3 copies on the same worker
with unchanged originals. Independently decoding all six published MP3 copies
confirms their intended 1, 2, 15, 16, 46 and 47 frame counts. The public test now
expects corrected publication instead of retaining the previous defect as an
expected failure. A refusing recycler keeps this test in copy-only scope.

All **2,813 foundation contracts** pass. Release public/private test-host and
production builds report zero warnings/errors. The direct evidence is under
`.codex-temp/mp3-padding-direct-63139637c2d44e95b6883442e0782d40`;
`.codex-temp/mp3-padding-direct-independent.json` records independent extents and
output hashes. Payload checks are in `.codex-temp/mp3-padding-payloads.json`.
Logs use the `.codex-temp/mp3-padding-direct`, `mp3-padding-foundation` and
`mp3-padding-production` prefixes. No installation, Explorer registration, live
license request, native recycling or publication was performed.

Short resampled signals, additional input containers/precisions, metadata with
tiny recordings, human listening and player compatibility remain outside this
focused matrix. Required Office conversion, visible UI and release gates remain
in [the active goal](broad-file-support-goal.md).
