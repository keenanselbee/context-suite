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

The command verifies the current curated engine pin, builds the current Release
worker and contract host, and copies the engine into a fresh scratch worker.
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
