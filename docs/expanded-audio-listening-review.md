Expanded Audio Listening Review
===============================

Status: all ten AIFF/WMA comparisons are prepared; human listening and intended
player compatibility remain unverified. This supplies the expanded-input part
of H01 in the [local acceptance contract](local-candidate-acceptance.md).
The [original generated pack](audio-listening-review.md) supplies fifteen more
comparisons. Nine comparisons from three approved real recordings still need
preparation and review. No part of H01 is accepted merely by generating files.


Fixed review material
---------------------

Reuse the hash-verified generated music, transients and local synthetic speech
from a completed original pack. Prepare exactly five inputs and convert each to
WAV and Opus using the existing production worker, audio executor, local trial
and validated copy publisher. Decode each Opus copy through the same path for
comparison: ten outputs plus five decoded WAVs, fifteen publications in total.
The harness supplies each required quality/resampling/precision acknowledgement;
it does not exercise the visible consent prompt. Nothing plays automatically.

| Input | Generated content | Independent listening reference |
| --- | --- | --- |
| AIFF PCM24, 44.1 kHz stereo | 18-second melody, bass and percussion | Exact authored PCM, with only sample byte order changed in AIFF |
| WMA Standard, 48 kHz stereo | 12-second transients, high tones and stereo changes | Windows-decoded float samples, including 1,536 encoded padding frames |
| WMA Pro, 48 kHz stereo | Same transient reference | Windows-decoded float samples |
| WMA Lossless PCM24, 44.1 kHz stereo | Same 18-second music | Exact authored PCM |
| WMA Voice, 16 kHz mono | About 26.6 seconds of local synthetic speech | Windows-decoded float samples |

The WMA inputs use the existing independently authored Windows fixture writer,
adapted only in a fresh scratch source copy to read these generated samples and
permit up to thirty seconds. Its established short-corpus source is unchanged.
The installed compiler and Windows codecs author the fixtures; neither the
writer nor Microsoft codec DLLs become shipping payload files. The pinned
expanded runtime prepares bounded PCM fixtures; no reference binary is executed.

WAV checks compare every channel to the independently retained samples and exact
decoded extent. AIFF, Lossless and Voice require exact samples. Standard and Pro
retain the existing peak `1e-6` and per-channel RMS `3e-7` limits. Opus retains
the production signal-validation policy and requires the planned 48-kHz extent,
including the established ceiling rule for fractional resampling. These are
scoped automated checks; they do not establish audible quality or player support.
For lossy WMA, compare conversion to its decoded reference rather than mistaking
source encoding losses or padding for a new conversion defect.


Prepare and review
------------------

```powershell
python -B tools/audio-engine/Prepare-ExpandedAudioReview.py `
  --audio-engine '<verified expanded audio-engine directory>' `
  --generated-pack '<completed generated audio-review pack>'
```

The runner verifies the expanded engine and Windows Voice provider, binds the
input pack to its recorded media hashes, builds only isolated fixture/test/worker
outputs and records source, binary and original identities. Output goes into a
fresh `.codex-temp/expanded-audio-review/<guid>` directory. All original sources,
references and engine files must retain their hashes, and the application checks
original timestamps and worker/publication scratch cleanup. No installation,
Explorer registration, live licensing or native recycling is performed.

Open the printed `pack/review.html`. Use a comfortable unchanged volume, compare
the independent WAV reference with both results, and listen to starts, ends,
transients, pauses and stereo placement. Open the encoded WAV/Opus files in the
intended player separately. Record the player and audio device, mark any issue
with timestamps, then download review notes before closing the page. The exported
notes bind each comparison to its report, input, reference and output hashes.
They stay local; nothing is uploaded or automatically saved.

Browser layout, keyboard interaction, note-download behavior, actual listening
and intended-player playback remain unverified until exercised. Generated HTML
and JavaScript syntax checks do not establish visible or accessibility acceptance.


Recorded preparation (2026-09-19)
---------------------------------

Final pack: `.codex-temp/expanded-audio-review/653c4dfd9ff84757ba36763a464d66ae`.
All ten comparisons and fifteen validated publications pass; originals, references,
payload and source identities are unchanged. Native fixture and managed Release
builds complete without warnings/errors. AIFF/Lossless/Voice WAV references match
exactly; Standard/Pro channel errors meet their unchanged limits. Human results
remain explicitly unreviewed in the worksheet.

The earlier completed packs `473189f21f504faf9bafb2177614c8cd` and
`132ea0d08e564af49d2044abcc1bd646` pass the same output checks and are retained.
The final run includes hash-bound exported notes and additionally checks that
AIFF/Lossless fixture preparation exactly retains the original music PCM.
Initial attempt `b7f59a53b96745f2b604dff7f07929a8` fails before app conversion:
the restricted runtime intentionally has no raw signed-PCM muxer. Fixture
preparation now uses its existing WAV encoder/muxer and reads the bounded PCM;
no production dependency, quality limit or runtime capability was widened.

Twelve worksheet checks pass at
`.codex-temp/audio-review-page-guards-8ba75396a0754421a07a90c87144f396`.
Both original and expanded modes produce complete case lists and parseable
JavaScript without automatic playback/network requests. Missing/duplicate cases,
changed media hashes, outside-pack media and failed preparation are refused
without replacing the previous worksheet. These checks use fresh copied evidence;
retained packs are not rewritten.

Release foundation regression passes all 4,384 contracts at
`.codex-temp/expanded-audio-review-foundation-68cea066479744d5b1f41b607a5a632f`.
Repository validation passes the public-source boundary, system-theme policy and
all 197 documentation files. No private implementation or production behavior
changes are part of this review-pack checkpoint.

Final combined-candidate replay, all human results, the nine real-recording
comparisons and the other local/commercial release gates remain open.
