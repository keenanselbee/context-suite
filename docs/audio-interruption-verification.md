Audio Interruption Verification
==============================

The 2026-09-11 isolated test passes 20 new checks for cancellation, client timeout
and worker termination during actual native audio encoding. It uses the existing
packaged candidate, the real worker client and the transactional copy publisher.
No production behavior, engine pin or installed state changes were needed.

The later five-target matrix below extends this to **96 checks** across FLAC,
MP3, M4A/AAC, Ogg Vorbis and Opus. The original run remains historical evidence;
the current harness uses a tone fixture for its same-target retries.


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


Five-target encoder interruption matrix (2026-09-11)
--------------------------------------------------

The harness now repeats cancellation, client-deadline expiry and worker-only
termination for all five compressed targets. Each case derives its reservation
extension and consent from the existing fixed target plan. It verifies the same
native parent/path, CPU activity, candidate growth and output lock before
injecting the fault. No synthetic pass substitutes for observing a live encoder.

The two-minute PCM24 noise input remains the interrupted source. The one-second
retry now contains separately authored 440 Hz and 660 Hz stereo tones at 40%
amplitude, using the same 48 kHz PCM24 layout. It exercises successful lossy
publication without claiming that the interrupted noise input meets every codec's
signal-error bound. The initial full FLAC conversion remains a preservation
control; every subsequent successful retry also becomes a hash control.

| Target | Cancellation | Client timeout | Worker termination | Same-target validated retries |
| --- | --- | --- | --- | --- |
| FLAC | Pass | Pass | Pass | 3 |
| MP3 | Pass | Pass | Pass | 3 |
| M4A/AAC | Pass | Pass | Pass | 3 |
| Ogg Vorbis | Pass | Pass | Pass | 3 |
| Opus | Pass | Pass | Pass | 3 |

All 15 cases retained the correct failure classification, stopped the owned
worker/encoder, removed unfinished reservations/journals/scratch, preserved both
original hashes/write times and earlier committed copies, and completed a
same-target retry using a fresh worker. The timeout still uses the injected
clock after checking the 150-second production deadline; this is not a timed
150-second endurance test. A refusing recycler prevents native recycling.

All **96 interruption checks** and **11 normal audio-worker checks** pass with
the unchanged staged worker:
`artifacts/production-staging/c2a73562db084b148f407c62d74c82d4`.
The wrapper's complete pre-run payload check and before/after pinned audio
inventory checks pass. The Release public test host compiles and runs successfully.
No production code or engine payload changed, so no new production build is claimed.

Evidence:
`.codex-temp/audio-engine/worker-177f760ce67c4606a0f0415aebd42a50/interruption-results/audio-interruptions.json`.
The log is `.codex-temp/audio-five-target-interruptions.log`. Evidence includes
target/fault pairs, observed worker/native process IDs and candidate byte growth,
abandoned publication outcomes, retry outcomes and committed-output hashes.

Separate post-run inspection confirms all 15 distinct target/fault pairs, both
original hashes, all 16 committed-output hashes, each retry's expected extension,
container header and recorded length, empty journals/worker scratch and absence
of unfinished temporary files. All recorded process IDs are absent. Container
headers are an additional inspection, not independent decoding or a substitute
for the production semantic/sample validation used before publication.

This covers one lossless source layout and five compressed targets during
encoding. WAVE output, other input codecs/rates/layouts, reference decoding,
output validation, optimization and publication interruption phases need their
own evidence. The short tone is not representative listening or player/device
acceptance. Native overwrite, visible/accessibility acceptance and engine release
adoption remain separate gates. The foundation, private adapter and full audio
workflow matrices were not rerun for this test-only expansion. Public-source
boundary, system-theme policy, 95 documentation files and both repositories'
whitespace checks pass.
