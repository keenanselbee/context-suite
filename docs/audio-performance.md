Audio Conversion Performance
============================

Status: all twelve rate/target groups pass the declared isolated timing budgets.
This implements the audio workload in the
[local candidate acceptance contract](local-candidate-acceptance.md#performance-and-package-review-limits).
It is separate from listening acceptance and must be repeated against the final
combined candidate's audio payload.


Workload and measurement
------------------------

The frozen workload is sixty seconds of generated stereo music at both 44.1 and
48 kHz, with 24-bit integer samples. Each rate has five operations for each of
the six existing targets: WAV, FLAC, MP3, M4A/AAC, Vorbis and Opus. This produces
sixty measured publications across twelve rate/target groups. Each group's
median must be at most 15 seconds and its maximum at most 30 seconds, as declared
before measurement. A missed limit is recorded as failure, not adjusted afterward.

WAV output uses a FLAC reference prepared through the application before timing.
Other targets use the generated WAV. This avoids timing a same-format no-op.
Every measured operation starts a fresh worker and times source inspection,
planning, local access admission, encoding, validation and copy publication.
The harness explicitly accepts the plan's required precision/resampling choices;
human response time and visible dialogs are outside this measurement.

Fixture creation, comparison hashes, benchmark bookkeeping and worker disposal
are outside the timed interval. Source inspection and the application's own
source/output validation remain inside it. Sources are read before each sample
to bind their identities; these are fresh-process observations, not cold-storage
measurements. Targets are interleaved across five iterations at each rate.
No caches, Windows settings or engine quality limits are changed.

Every operation requires a validated copy, a recorded engine identity, preserved
source bytes/write time, worker exit and no retained publication/worker contexts.
Each completed sample is written immediately, retaining partial evidence if a
later operation fails. The results include source/output hashes, encoding plans,
output sizes, worker PIDs and managed-worker peak working sets. Native-descendant
or whole-job peak memory is not measured; worker memory must not be described as
the complete conversion footprint.


Run the benchmark
------------------

```powershell
python -B tools/audio-engine/Prepare-AudioReview.py --performance `
  --audio-engine '<verified isolated worker>/audio-engine'
```

The existing preparation tool verifies the complete curated/expanded payload and
optional Voice provider, builds a fresh Release worker/contract host, then copies
the selected audio payload into a fresh repository-owned scratch worker.
Performance mode generates no speech, creates no listening worksheet and plays
nothing. Ordinary listening-pack behavior remains the default.

The evidence folder is `.codex-temp/audio-review/<run-id>`. It retains source and
binary identities, build logs, read-only CPU/memory observations, individual
samples, `pack/performance-results.json` and the process exit/unchanged-input
receipt. The tested worker is that exact scratch composition, not the installed
app or the reserved 1.1.1 package. Source and binary changes during the run fail
the wrapper's identity check.

This procedure needs no installation, Explorer registration, real license key,
network licensing, audio playback or native recycling. Generated originals and
all publications remain in the evidence folder.


Recorded measurement (2026-09-19)
---------------------------------

Run `4ae232c5a98141bba739660ff7330767` uses the repaired expanded audio profile
with all twenty pinned runtime/notice files, including the separate Voice
provider. The source payload is the retained worker from the
[Voice distribution checkpoint](windows-voice-distribution.md). Presence of that
provider does not make this a WMA/Voice performance test; the measured inputs are
the declared ordinary WAV/FLAC music references.

Both Release builds pass with zero warnings/errors. The wrapper exits zero with
all captured sources, binaries and engine inputs unchanged. All sixty measured
operations publish validated copies and pass original/cleanup checks; two setup
FLAC publications occur outside the measurements. The host is the recorded
Intel Core i9-9900K, 16 logical processors and 34,270,330,880 bytes of physical
memory (approximately 32 GiB).

| Target | 44.1 kHz median / maximum | 48 kHz median / maximum |
| --- | --- | --- |
| WAV | 1.531 / 1.653 s | 1.615 / 1.634 s |
| FLAC | 1.721 / 1.740 s | 1.795 / 1.838 s |
| MP3 | 1.851 / 1.881 s | 1.942 / 1.958 s |
| M4A/AAC | 4.761 / 4.870 s | 4.781 / 4.846 s |
| Vorbis | 2.145 / 2.200 s | 2.250 / 2.369 s |
| Opus | 2.326 / 2.395 s | 2.148 / 2.159 s |

Each cell summarizes five fresh-worker operations. The largest median is 4.781
seconds against the 15-second limit; the largest individual time is 4.870 seconds
against 30 seconds. Managed-worker peak working sets range from 51.8 to 54.8 MiB;
this excludes native-descendant memory. No cold-storage, whole-job memory,
listening, visible workflow or final combined-candidate result is claimed.

The shared-harness regression passes 4,384 foundation contracts with exit zero
in `.codex-temp/audio-performance-foundation-e7892fbf6942463b9ac3c9d88d98464f`.
The expected rejected-client diagnostic remains in its separate stderr log.
Default listening mode also repeats all thirty validated publications in
`audio-review/09de6f5e147a4bb5a0a50a649a91a03d`, with unchanged captured inputs.
Its music/transient references are byte-identical to the preceding review pack;
`default-fixtures-regression.json` records that comparison. Both builds again
have zero warnings/errors. No listening or browser review occurred.
