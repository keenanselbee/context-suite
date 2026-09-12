Audio Publication Crash Verification
===================================

The 2026-09-11 matrix passes **74 checks across ten abrupt process terminations**:
WAV-to-FLAC conversion and FLAC optimization, each at five publication boundaries.
The killed test process hosts the real application executor, access store,
publisher and worker client. This exercises application-layer crash recovery
evidence without opening a window or changing the installed application.


Boundaries and preserved evidence
--------------------------------

Each case starts with a separate generated original and fresh local trial. The
optimization fixture adds a valid 256 KiB padding block to the existing authored
FLAC, making a smaller result available without changing audio or descriptive
metadata. A refusing recycler keeps all operations copy-only.

| Termination point | Journal state at termination | Expected surviving candidate |
| --- | --- | --- |
| Prepared | Prepared | Empty reserved temporary file; no validation fingerprint |
| Validated | Validated | Validated temporary file matching its recorded fingerprint |
| Publishing | Publishing | Validated temporary file before the final move |
| PublishedBeforeRecord | Publishing | Final output matching the recorded candidate; temporary path gone |
| Committed | Committed | Final output matching the recorded candidate; temporary path gone |

The existing test-only `PublicationIo` boundary triggers termination. For the
move-before-record case, it first performs the real non-overwriting filesystem
move and then kills its own process. Other cases kill the process at the named
checkpoint. There is no shipping fault flag or fake media encoder. Cases after
Prepared have passed real worker semantic validation before publication.

All originals retain their hashes, file fingerprints and write timestamps.
No backup path is created. All four post-validation optimization cases retain
their descriptive FLAC metadata and have smaller candidates. Each owned worker
exits after its parent; PID plus creation time distinguishes it from PID reuse.

Reconstructing `OutputPublisher` and `MainViewModel` exposes the retained recovery
notice without starting a worker. Discovery leaves the journal bytes and files
unchanged. It does not automatically replay a move, remove leftovers or declare
an old operation recovered. The journal state alone is insufficient at the
move-before-record boundary; the actual file and fingerprint are also evidence.

Each case then runs a fresh operation through the real worker and publisher.
It completes a validated copy, chooses another name when a committed copy already
exists, and preserves the old journal, original and prior temporary/final file.
For an uncommitted old operation, its previously unused final name can be used by
the fresh operation. Thus the old output path's later existence alone cannot
prove that the old operation committed. No automatic cleanup is inferred from it.


Reproduction and results
------------------------

```powershell
.\tools\audio-engine\Test-AudioWorker.ps1 -Packaged `
  -ProductionStage '<verified isolated stage>' `
  -FixtureDirectory '<generated six-format audio fixtures>' `
  -IncludePublicationCrashes
```

The wrapper verifies the payload before and after execution and runs its normal
audio Analyze checks. The public Release host provides
`--audio-publication-crashes <new-evidence> <worker> <fixtures>` and an internal
child mode. Results include each terminated process's checkpoint, worker identity,
journal, candidate hash and successful fresh-operation result. Recovery records
and surviving candidates are intentionally retained for inspection.

The final run passes 74 crash checks and 11 normal audio-worker checks on unchanged
stage `artifacts/production-staging/c2a73562db084b148f407c62d74c82d4`.
Before/after candidate inventory checks pass. The Release test host builds with
zero warnings/errors. An initial test compilation failed on an await/span
comparison; reading the comparison bytes before calling SequenceEqual fixes it.
No production behavior needed changing for the completed matrix.

Evidence:
`.codex-temp/audio-engine/worker-8d6f2fa7f344440e991e233daaa99673/publication-crash-results`.
The log is `.codex-temp/audio-publication-crashes.log`. Independent post-run
inspection verifies all ten source hashes, prior candidate/copy hashes, retained
journals and fresh-output lengths against the recorded evidence.

The [live encoder interruption test](audio-interruption-verification.md) covers
different failure points. Neither matrix proves all codecs, power loss, native
overwrite/recycling, manual recovery usability, visible startup delivery,
screen-reader behavior or installer lifecycle. The general foundation, private
adapter and full audio workflow suites were not rerun for this test-only change.
No new production build, engine adoption or release clearance is claimed.
