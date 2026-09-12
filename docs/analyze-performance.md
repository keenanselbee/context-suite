Analyze Performance Baseline
============================

The 2026-09-11 local benchmark measures the actual application file reader and
the actual Analyze batch coordinator, without displaying a window or starting
optional media workers. It is a baseline for regression review, not complete
launch performance acceptance.

Run `powershell -NoProfile -File tools/Benchmark-Analyze.ps1` from the repository.
The wrapper builds the Release contract host and creates new disposable fixtures
under `.codex-temp/analyze-benchmark/<guid>`. It records Windows version, CPU,
memory, runtime, host/source hashes and individual samples. No installation,
Explorer registration, license validation or customer files are involved.

Method
------

Each of ten cases starts in three fresh .NET processes. Each process measures its
first call and ten subsequent calls: three first-call observations and thirty
repeated observations per case. The reported p95 uses the nearest-rank sample.
The first-call timer includes the reader's first-use initialization but excludes
.NET process startup. Whole-process durations, including all eleven calls, are
also retained. Managed allocation deltas are process-wide and include harness
and background runtime work; they are not exact parser-only allocations.

Fixtures include an empty file, 4 KiB unknown binary, valid authored 16x16 PNG,
small JSON, 128 MiB unknown binary, approximately 8 MiB JSON, and a DOCX package
with an unrelated 64 MiB stored ZIP member. The package's document declarations
are minimal and independently authored; it is not a rendered Office fixture.
One existing file is held with exclusive sharing during the benchmark, then
released. A missing-file reader case is separate because application admission
correctly rejects missing selections before queuing them.

The mixed-selection case exercises the application coordinator with eight
existing files, including the locked file. It checks that every row completes,
the unreadable item has no analysis, readable items retain the expected identity,
and no output is published. An access implementation throws if licensing or
conversion admission is attempted. The worker path does not exist.

Every sample validates identities, file lengths and bounded reported inspection:
at most 64 KiB for header/text cases and at most 5 MiB for package analysis.
Source SHA-256 hashes, lengths and write timestamps must match after the full
run. These are application inspection counters, not kernel or device I/O traces.

Files are generated and hashed before measurement, so filesystem caches are
already populated. Nothing flushes Windows caches or changes machine settings.
Fresh-process results must not be described as disk-cache-cold performance.

Measured results
-----------------

Machine: Windows 11 Home x64, build 26200; Intel Core i9-9900K, 8 cores/16 logical
processors; approximately 32 GiB installed memory. Evidence timestamp:
2026-09-12 00:46 UTC (September 11 local time).

| Case | First-call median, ms | Repeated median, ms | Repeated p95, ms | Maximum reported inspected bytes |
| --- | ---: | ---: | ---: | ---: |
| Empty | 48.44 | 0.45 | 0.91 | 0 |
| 4 KiB binary | 48.29 | 0.55 | 2.11 | 4,096 |
| 80-byte PNG | 48.65 | 0.55 | 2.03 | 80 |
| 38-byte JSON | 53.10 | 0.56 | 2.02 | 38 |
| 128 MiB binary | 56.52 | 0.57 | 2.09 | 65,536 |
| 8 MiB JSON | 61.03 | 0.73 | 2.24 | 65,536 |
| DOCX with 64 MiB unrelated member | 84.30 | 1.27 | 8.03 | 131,093 |
| Locked-file refusal | 6.93 | 0.31 | 0.63 | No content analysis |
| Missing-file reader refusal | 8.18 | 0.27 | 0.57 | No content analysis |
| Mixed eight-file selection | 119.14 | 6.02 | 11.51 | 131,093 per file |

Repeated median managed allocation was 237,448 bytes for the 128 MiB binary,
368,432 bytes for large JSON, 214,544 bytes for the large package, and 1,018,928
bytes for the mixed selection. These observations support bounded behavior on
these inputs; they do not establish a universal memory ceiling.

Reference-machine review budgets
--------------------------------

For this exact workload on this host class, investigate a regression above
200 ms first-call median or 25 ms repeated p95 for an individual readable file;
use 500 ms and 50 ms respectively for the mixed selection. Also investigate
repeated median allocations above 1 MiB for an individual case or 4 MiB for the
mixed selection. All measured cases are within these provisional budgets.
They provide headroom over observed timing/allocation and are review triggers,
not automatic failure thresholds for arbitrary machines or a customer SLA.
The existing five-second content deadline is a separate cancellation limit.

Evidence and limitations
-------------------------

Results are under
`.codex-temp/analyze-benchmark/7eea32e291fa4b66a4dfe975d003a480`:
`machine.json`, `fixtures.json`, thirty per-process JSON files and `benchmark.json`.
The command log is `.codex-temp/analyze-benchmark-locked.log`. The Release host
builds with zero warnings/errors; original hashes/timestamps and inspection
limits pass. Earlier setup attempts failed compilation and then exposed the
incorrect missing-file batch fixture; they are not counted as passing runs.

Optional audio/PDF worker latency, image decoding, visible UI responsiveness,
Explorer activation, large selections beyond eight files, slower hardware,
remote/cloud storage and true cold storage remain unmeasured. Metadata/path
queries and initial file opening still lack a hard OS-I/O deadline. This
benchmark does not resolve that previously documented limitation or establish
screen-reader, theme/DPI or installed lifecycle acceptance.
