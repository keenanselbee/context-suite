Optional Audio And PDF Analyze Performance
=========================================

Status: all seven cases pass against candidate 1.1.1; final expanded-candidate
replay remains pending. This covers the optional
worker timing gate in the [local acceptance contract](local-candidate-acceptance.md),
separately from the [generic Analyze benchmark](analyze-performance.md).


Declared method
---------------

Use seven retained, independently generated fixtures: the 18-second stereo music
WAV and its FLAC, MP3, AAC-LC M4A, Ogg Vorbis and Ogg Opus listening-pack outputs,
plus the ten-page text/vector PDF from the PDF performance corpus. Audio is
44.1 kHz except the 48 kHz Opus recipe. Verify receipt hashes before copying the
fixtures into fresh benchmark scratch. No customer files or playback are involved.

Each fixture runs in five fresh test-host/application/worker sessions. Each
session makes one first call and five subsequent calls through the actual Analyze
command. The fixed limits are two seconds for the median of five first calls and
one second for nearest-rank p95 of twenty-five repeated calls, per fixture.
Do not discard slow first calls or widen these limits after a failure.

The timer covers command admission through the application becoming idle,
including file reads, optional worker startup and native probing. It excludes
test-host process startup, model construction, fixture preparation, audit hashing
and final disposal; whole-process time is separately retained. The same worker
must serve all six commands in each session. No licensing method may be called,
no output may be published, and source bytes/timestamps must remain unchanged.

Require actual codec, sample-rate and channel facts for audio; page count and
encryption facts for PDF. Header-only fallback cannot pass. Preserve the existing
1-MiB audio-probe cap, so WAV/FLAC inspection is a bounded prefix, not full-file
validation. The small PDF is inspected in full. Record facts and reported byte
counts for every sample, along with process-wide managed allocations and managed
worker peak working set. Native descendant/job memory is not measured.

M4A retains the existing filename-qualified identity alongside the content probe's
shared `mov,mp4,m4a,3gp,3g2,mj2` container and AAC stream facts. Require that
qualification explicitly; do not promote the broad container report into proof
of an M4A-specific identity. The other six fixtures retain content-based identity.

Generated inputs and candidate hashes populate filesystem caches before timing.
Fresh-session timing is not cold-storage timing. This workload does not establish
maximum-size, malformed, remote-storage, visible UI or screen-reader performance.


Execution
---------

```powershell
python -B tools/Benchmark-OptionalAnalyze.py --production-stage '<versioned candidate>' `
  --audio-review-id '<completed generated listening pack>' `
  --pdf-performance-id '<completed PDF performance evidence>'
```

The runner verifies the exact candidate and its inventory before/after measurement,
builds only the test host and retains all receipts under a new
`.codex-temp/optional-analyze-performance/<guid>` directory. Preserved candidate
1.1.1 supplies a baseline; the final expanded candidate still needs its replay.
Installation, Explorer registration, live licensing and native recycling are not
part of this test.


Retained harness attempt
------------------------

The first attempt `f107a630e0184ed696a2b13c40a1eafb` stopped after its first WAVE
session at a cleanup assertion: Analyze had never created a publication directory,
and the harness incorrectly enumerated that absent directory. The corrected
check accepts absence or an empty directory and still rejects any retained
publication/worker entry. No production code or timing limit changed. This first
attempt is retained and contributes no passing timing samples.

Attempt `423a0b9659d240989efb548843b89851` then exposed serialization of a default
optional array in the full analysis model. The benchmark now records explicit
identity, byte-count, fact and warning fields instead. The diagnostic session
`7cdd236ee09f4a1d85c71144fecc6d60` exposed the harness's incorrect assumption that
M4A already had content-based identification. Its actual AAC/container facts and
filename-qualified identity match the existing policy above. These harness
corrections change neither production behavior nor performance thresholds.
One-session-per-fixture smoke `3588898eeb08400db92ef05ab9097632` passes all seven
cases with six observations each; it is excluded from the declared five-session
measurement. Earlier failures remain intact.


Measured baseline (2026-09-19)
------------------------------

Evidence `16c881d78f9146898c27fa85c30d6c42` completes all 35 sessions and 210 Analyze
commands with unchanged original, candidate and harness identities. It uses the
exact worker in `artifacts/production-staging/3efa4342aed9435995d8f00b52fead96`.
The candidate inventory passes before and after execution. Each session retains
its optional facts and its worker exits without publication or temporary files.

| Case | First-call median, seconds | Repeated p95, seconds | Result |
| --- | --- | --- | --- |
| WAV PCM24 | 0.575 | 0.030 | Pass |
| FLAC | 0.575 | 0.032 | Pass |
| MP3 | 0.601 | 0.035 | Pass |
| AAC-LC M4A | 0.615 | 0.051 | Pass |
| Ogg Vorbis | 0.577 | 0.040 | Pass |
| Ogg Opus | 0.568 | 0.039 | Pass |
| Ten-page PDF | 0.584 | 0.043 | Pass |

The host is the Intel Core i9-9900K with sixteen logical processors and
34,270,330,880 bytes of physical memory, approximately 32 GiB. Managed-worker peak
working set ranges from 43.2 to 65.7 MiB across recorded samples. This is not
native-job memory or a universal latency bound. The Release host builds with
zero warnings/errors. No shipping code or installed state changed.

Foundation replay `optional-analyze-foundation-0aa5684bc085454d95ee68abfc326467`
passes 4,384 contracts. Three argument/path guards pass in
`optional-analyze-guards-3119c772e5d44cca8dac4ab88bc78c41`; repository boundary and
link verification passes 194 documentation files. This completes this baseline
measurement only; human, environment and final expanded-candidate gates remain.
