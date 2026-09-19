Audio Listening Review
======================

Status: automated pack preparation passed; human listening, player compatibility
and visual worksheet acceptance remain open.

This review supports the fixed recipes in [audio conversion policy](audio-conversion-policy.md)
and the [broad file support goal](broad-file-support-goal.md). Generated clips make
the first comparison repeatable. They do not replace representative real music,
recorded voices, intended players/devices or the application's visible acceptance.


Prepare the pack
----------------

From the repository root, with the existing .NET build prerequisites and a
retained, pinned audio-engine candidate:

```powershell
python -B tools/audio-engine/Prepare-AudioReview.py --audio-engine 'artifacts/production-staging/169f27e246ce43c4808afa9f99e044a3/audio-engine'
```

The command verifies the complete curated or expanded audio payload, including
its optional Voice provider, builds the current Release worker and contract host,
and copies the verified engine into a fresh scratch worker. Use an `audio-engine`
directory under isolated production staging or repository scratch; arbitrary
native directories and partial binary-only copies are not admitted.
It generates music and transient WAVs plus local Microsoft David or Zira desktop
speech. An already installed voice is required; it downloads nothing and plays
nothing through the speakers. Every generated file and test profile stays under
the repository's `.codex-temp/audio-review/<run-id>` directory.

The real worker and application executor publish FLAC, MP3, M4A/AAC-LC, Vorbis and
Opus copies for each clip, then decode every copy to WAV through the same flow.
The test host explicitly confirms the plan's required precision/resampling
consent; this is not a test of the customer's consent UI. A disposable local
trial admits the test. No customer key, live licensing request, installed-app
change or Explorer registration is involved.

Preparation checks all thirty validated copy publications, rates, channels,
frame counts, exact FLAC PCM round-trips, unchanged originals and encoded inputs,
and removal of worker contexts and pending publication records. Logs, source and
binary hashes, results and an exit receipt remain beside the pack. The local HTML
writer verifies every media hash before creating the worksheet.


Manual review
-------------

1. Open the printed `pack/review.html` in a browser. Use a comfortable, unchanged
   volume and record the player and headphones or speakers used.
2. For each clip, listen to the original WAV, then each decoded WAV. Compare the
   start, middle and end; note clicks, missing samples, smeared attacks, noisy
   tails, consonants and stereo placement. Deliberate noises and high tones are
   described beside the transient reference.
3. Choose a listening result for each comparison. For a difference, record the
   codec, timestamp and what changes relative to the reference.
4. Open each encoded file in the intended player separately and record whether
   it opens and plays. A decoded WAV playing successfully does not establish
   that the player supports its encoded source. Browser links may play or
   download a file; use the pack's encoded file with the intended player.
5. Select **Download review notes** before closing the page. Notes are not
   automatically persisted or uploaded. Unreviewed entries remain explicitly
   unreviewed in the exported JSON.

Complete this generated set before adding a small owner-selected collection of
real recordings. Keep private recordings local and identify the intended
players/devices in their review evidence. A pass here does not close the release
goal or any unrelated Office, installer, accessibility or commerce gate.


Recorded preparation
--------------------

Run `48d5bbe352ca4e4596a7f0ebd8bb981b` passed on 2026-09-15 local time. Its folder
is `.codex-temp/audio-review/48d5bbe352ca4e4596a7f0ebd8bb981b`.

- Both Release builds completed with zero warnings and errors.
- Three references produced fifteen encoded clips and fifteen decoded WAVs
  through thirty successful application publications.
- The music reference is 18 seconds, 44.1 kHz, stereo, 24-bit PCM. The transient
  reference is 12 seconds, 48 kHz, stereo, 24-bit PCM. Speech uses the installed
  Microsoft David Desktop voice at 48 kHz, mono, 16-bit PCM.
- FLAC retains exact PCM samples for all three references. Every comparison
  retains its planned rate, source channels and expected sample count.
- The execution receipt reports exit code zero and unchanged captured sources,
  binaries, engine and speech inputs; source hashes and write times are preserved.
- The worksheet was subsequently adjusted to open encoded links separately and
  allow narrow cards. Its refreshed static verification is recorded in
  `worksheet-verification.json`; the conversion evidence remains bound to its
  original input receipt, with the page writer identified as the sole later
  source change.

On 2026-09-16, browser inspection could not run: the in-app browser was
unavailable and the browser inventory was empty. No visual, keyboard,
screen-reader, theme, DPI, listening or encoded-player result is claimed.
The exported notes start unreviewed. No shipping source or formal package changed.


Expanded-runtime refresh (2026-09-19)
-----------------------------------

Run `5d364e6972cf44098f8d58d25db063b1` prepares the same finite fifteen comparisons
using the current repaired expanded runtime and separately pinned Voice provider.
Its inputs record the expanded profile and all twenty deployed audio files.
The source runtime is the worker from the
[Voice distribution checkpoint](windows-voice-distribution.md), not reserved 1.1.1.
Both managed builds have zero warnings/errors. All thirty application-owned
copy publications and existing sample/extent/preservation checks pass; the exit
receipt confirms unchanged captured inputs. The page writer validates every
media hash. No playback, visual review or encoded-player acceptance occurred.

Open `.codex-temp/audio-review/5d364e6972cf44098f8d58d25db063b1/pack/review.html`
for this current generated-reference review. Human/player entries remain
unreviewed. The pack's input fixtures are WAVE references; the presence of the
Voice provider does not turn this into AIFF/WMA listening evidence. The separate
ten expanded-input comparisons and nine real-recording comparisons in
[H01 of the acceptance contract](local-candidate-acceptance.md#human-media-and-workflows)
remain to be prepared and reviewed. Final combined-candidate replay remains open.
