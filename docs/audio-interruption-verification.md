Audio Interruption Verification
==============================

The 2026-09-11 isolated test passes 20 new checks for cancellation, client timeout
and worker termination during actual native audio encoding. It uses the existing
packaged candidate, the real worker client and the transactional copy publisher.
No production behavior, engine pin or installed state changes were needed.


Execution and observations
--------------------------

The test authors stereo PCM24 WAVE noise at 48 kHz: a two-minute input for live
encoding and a one-second input for retries. The long input first completes a
normal WAV-to-FLAC conversion, including production exact-sample validation and
copy publication. Its committed output becomes a preservation control.

Each interrupted operation has a fresh source probe and reserved output. Before
injecting a fault, the test verifies the native executable path and worker parent,
CPU activity, growth of `encoded.candidate` over 75 ms, and the reserved-output
lock. Merely observing ffmpeg would not distinguish encoding from the earlier
reference decode. The final run observed:

| Fault | Candidate bytes before observation | Candidate bytes after observation |
| --- | --- | --- |
| User cancellation | 1,310,720 | 7,602,176 |
| Client timeout | 1,572,864 | 6,815,744 |
| Worker termination | 1,048,576 | 7,340,032 |

Cancellation uses the normal token. Timeout expires the injected client clock
after confirming the production deadline is 150 seconds; it does not wait 150
wall-clock seconds or add a shipping fault switch. Worker termination kills only
the owned worker, so its native job must terminate the encoder. The helper never
kills unrelated processes or closes applications.

All three cases produce their expected cancellation/timeout/worker-termination
classification. The worker and observed native encoder exit. Abandoning the
uncommitted reservation removes its temporary output and journal; owned worker
scratch is empty and no incomplete final file appears. Both originals retain
their hashes and write times, and all earlier committed copies retain their hashes.
A fresh worker completes a validated short-file retry after each fault.

Post-run inspection confirms all four committed output hashes, empty journals,
reservations and worker scratch, and exit of every recorded worker/encoder ID.
The publisher uses a refusing recycler. No native recycling or overwrite
acceptance is implied by this copy-only test.


Reproduction and evidence
-------------------------

Add `-IncludeInterruptions` to the existing packaged audio-worker wrapper:

```powershell
.\tools\audio-engine\Test-AudioWorker.ps1 -Packaged `
  -ProductionStage '<verified isolated production stage>' `
  -FixtureDirectory '<generated six-format audio fixture directory>' `
  -IncludeInterruptions
```

The wrapper verifies the complete stage and pinned audio inventory before
execution, runs its normal Analyze checks, then invokes `--audio-interruptions`
in the Release public contract host. It verifies the stage inventory again after
execution. The new mode authors its own noise fixtures, uses a fresh result
directory and records `audio-interruptions.json`. It can be combined with the
existing optimization/conversion switches; they are not required for this test.

The final run used unchanged stage
`artifacts/production-staging/c2a73562db084b148f407c62d74c82d4`.
Evidence is under
`.codex-temp/audio-engine/worker-176faac5934c47df9aed0dfa742625c7/interruption-results`;
the wrapper log is `.codex-temp/audio-interruptions.log`.
All **11 normal audio-worker checks** and **20 interruption checks** pass, along
with before/after payload verification. The initial new-host build caught an
incorrect confirmed-plan type name; the corrected host built and ran successfully.

This is an encoder-phase interruption matrix for WAV-to-FLAC. It does not prove
every codec, probe/reference-decode/output-validation phase, application crash,
publication boundary or native overwrite. Listening/player compatibility,
filesystem/network isolation, visible/accessibility acceptance and engine release
adoption remain separate work. The wider foundation, 298 private adapter and 116
audio workflow suites were not rerun for this test-only change. The earlier
production build remains the tested payload; no fresh production build is claimed.
