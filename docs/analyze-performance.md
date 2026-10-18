Analyze Performance Baseline
============================

For the current text-counting behavior, see the
[streaming follow-up](#streaming-text-counts-2026-10-16). Earlier measurements
below describe the former prefix-only behavior.

The 2026-09-11 local benchmark measures the actual application file reader and
the actual Analyze batch coordinator, without displaying a window or starting
optional media workers. It is a baseline for regression review, not complete
launch performance acceptance.

The later [optional audio/PDF baseline](optional-analyze-performance.md) passes
seven cases in 35 fresh application/worker sessions and 210 commands against
preserved candidate 1.1.1. It measures the optional-worker latency omitted from
the historical runs below; final expanded-candidate and visible acceptance remain.

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
released. The missing-file reader case remains a separate measurement. At the
initial baseline, application admission rejected missing selections; the later
[admission fix](analyze-io-cancellation.md#admission-follow-up-2026-09-11) handles
them per row. The recorded benchmark selection itself has not changed.

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
The five-second read deadline is a separate cancellation limit.

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
queries and initial file opening now have
[synchronous cancellation requests](analyze-io-cancellation.md), with driver-dependent
completion rather than a hard OS-I/O return deadline. This benchmark does not establish
screen-reader, theme/DPI or installed lifecycle acceptance.

Reader cancellation refresh (2026-09-11)
---------------------------------------

After adding the synchronous I/O scope, the same benchmark ran again under
`.codex-temp/analyze-benchmark/6db84f4ede3a42e1b804ca27ca1896fc`, with command log
`.codex-temp/analysis-io-benchmark.log`. The Release host builds with zero warnings
and errors; all source hash/time and reported inspection checks pass.

The eight-file mixed selection measures 136.33 ms first-call median and 6.98 ms
repeated median / 9.93 ms p95. Individual cases have at most 99.21 ms first-call
median and 3.17 ms repeated p95. Every case remains within the timing/allocation
budgets above. This refresh includes the current readers but retains the same
small synthetic workload; it does not extend the environment or worker coverage.


Maximum-selection refresh (2026-09-16)
-------------------------------------

The benchmark now includes 256-file and 4,096-file selections through the actual
application coordinator. The latter reaches the request's current path limit.
Each selection has distinct Unicode filenames containing independently authored
small PNG, JSON, DOCX, unknown binary and empty-file contents. A locked file and
a missing path precede two readable trailing files. All rows retain their input
order; both failures remain per-file and later analysis completes. No output,
license admission or optional worker is allowed.

The original ten cases also run unchanged, including large-file bounded reads.
Each of twelve cases has three fresh processes with eleven samples per process:
three first calls and thirty repeated calls. Fixture generation and hashing warm
the file cache; these are not cold-storage measurements. The larger cases allow
180 seconds per child, compared with 30 seconds for the small cases. That harness
bound does not change the application's per-file read deadline.

The same Windows 11 build 26200 / i9-9900K / 32 GiB machine, using .NET 10.0.11,
records:

| Selection | First-call median, ms | Repeated median, ms | Repeated p95, ms | Repeated median allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Original eight-file case | 135.78 | 7.34 | 9.96 | 1,076,244 |
| 256 distinct paths | 351.97 | 222.56 | 242.02 | 16,218,568 |
| 4,096 distinct paths | 3,512.79 | 2,757.81 | 2,858.42 | 260,368,048 |

Allocation is cumulative process-wide managed allocation per batch, including
harness observations. It is not retained result memory, peak working set or a
memory ceiling. Across these two large selections, allocation per selected path
is similar; that observation does not establish all-format scaling. Original
individual cases remain within their existing timing and allocation budgets,
with at most 97.40 ms first-call median and 3.03 ms repeated p95.

For this authored workload and reference host, investigate 256-file regressions
above 1 second first-call median, 500 ms repeated p95 or 32 MiB repeated median
allocation. For the maximum selection, use 8 seconds, 5 seconds and 384 MiB
respectively. These provisional review triggers allow headroom over the measured
baseline; they are not customer promises or thresholds for unrelated hardware
and document complexity.

Evidence is `.codex-temp/analyze-benchmark/70552a2bf20c4da1972ef5ce51709e51`:
`machine.json`, `fixtures.json`, `inputs.json`, 36 process receipts and
`benchmark.json`. All 4,103 existing fixtures retain their bytes, sizes and
write timestamps. All identities, input order and reported inspection limits
pass. Source and host identities remain unchanged during execution; the Release
build has zero warnings/errors. A later comment-only correction to the wrapper
is recorded separately with identical Python syntax trees and the exact tested
source retained as `Benchmark-Analyze.tested.py`.

No production behavior changes. The test host now accepts the application's
actual maximum selection instead of an artificial 32-path benchmark limit.
Visible rendering and responsiveness, retained/peak process memory, optional
audio/PDF workers, true cold storage, other machines and remote/cloud paths
remain unmeasured. This closes the former small-selection-only evidence gap,
not the complete performance or expanded release gate.
Streaming text counts, 2026-10-16
---------------------------------

Version 1.4.1 counts recognized text beyond the 64 KiB identification prefix,
up to 256 MiB within the existing five-second read budget. Fixed byte/character
buffers and an incremental decoder avoid retaining the file or allocating lines.
Header-only callers retain the smaller budget. Inspection receipts now distinguish
header/structure bytes from `TextBytes`, and verify completed text scans reach EOF.

The same 12-case benchmark (three fresh processes and 30 repeated samples per
case) passes with unchanged source fixtures, identities and input ordering:

| Case | First-call median, ms | Repeated median, ms | Repeated p95, ms | Repeated allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| 8 MiB JSON, all 8,388,622 bytes counted | 99.10 | 27.88 | 30.88 | 569,064 |
| 128 MiB binary, still header-only | 66.97 | 0.66 | 2.35 | 239,384 |
| Mixed eight-file selection | 192.94 | 35.46 | 42.16 | 1,328,176 |
| Mixed 256-file selection | 408.93 | 250.33 | 302.04 | 18,224,872 |
| Mixed 4,096-file selection | 4,246.54 | 3,242.83 | 3,620.86 | 289,907,336 |

The large-text p95 exceeds the former 25 ms review trigger because it now scans
8 MiB rather than 64 KiB. This reviewed cost is acceptable for the owner's
requested deeper count and tolerance of a few seconds. Allocations remain below
the existing 1 MiB single-file review trigger. Other cases remain within their
existing timing/allocation review budgets. These are local observations, not SLAs.

A separate first-call check of the reported 89,758-byte HTML counted 220 lines
in 100 ms; an independent newline count agrees. An authored 32 MiB text file
counted 262,144 lines in 197 ms. Process-wide first-call allocation was about
1.37 MiB in both checks, including initialization; this is not peak memory.

Evidence: `.codex-temp/analyze-benchmark/d4185895c0d0439a85e591a4ac4fc1a4`
contains the 36 process receipts and source/host hashes. The subsequent source
change only clarified the identification evidence wording; counting logic is
unchanged. The 55 focused text checks cover Unicode/BOMs, fragmented reads,
CR/LF/CRLF boundaries, byte limits, timeout fallback, cancellation, invalid tails,
unexpected EOF, presentation, header-only routing and source preservation.
The full-budget cap test uses a synthetic stream rather than a large fixture.
The existing driver-dependent I/O cancellation limitations still apply.
