PDF Batch Performance
=====================

Status: both workloads pass against preserved candidate 1.1.1; final expanded
candidate replay remains pending. This implements the PDF timing gate in the
[local acceptance contract](local-candidate-acceptance.md).
It does not replace PDF fidelity, human review or final combined-candidate replay.


Declared workload and limits
----------------------------

Before execution, both workloads retain the existing limits: five runs each,
median at most 20 seconds and maximum at most 40 seconds per complete batch.

- Export one authored ten-page Letter PDF to ten PNG copies at the fixed 150 DPI.
  Each page contains a heading, thirty text lines, a vector rule and a distinct
  colored rectangle. Expected outputs are 1275 by 1650 pixels in source order.
- Combine ten distinct 1600-by-1200 RGB24 BMP images into one ten-page PDF. Each
  image has horizontal red and vertical green gradients plus a distinct blue
  value. No stored resolution means the existing 96-DPI policy produces pages
  measuring 1200 by 900 points. Confirm the initial selected page order once.

The fixture generator is independently authored in `PdfPerformanceContracts`.
These ordinary generated workloads do not establish worst-case, scanned-document,
high-resolution-photo, hostile-input or maximum-capacity latency.

Interleave the two workloads across five iterations. Each uses a fresh
application model and managed worker, without changing machine caches or settings.
The timer starts before model/worker construction and stops after the admitted
command becomes idle. It includes inspection, local trial access, programmatic
page-order confirmation, rendering/encoding, output validation and publication.
It excludes source generation, audit hashes, post-timing page checks, disposal
and human decision time. No dialog is displayed and no audio is played.

After timing, check every PNG's dimensions, page-specific marker and visible
text/vector ink. Check combined PDF page count, physical size and the retained
reviewed order. The production validators remain enabled inside the timed path.
Hash all outputs, preserve every source's bytes/timestamp and verify worker,
reservation and temporary-file cleanup after disposal. These checks corroborate
complete work; they are not a new comprehensive PDF appearance oracle.

Record individual timings, hardware, source/host/candidate identities and managed
worker peak working set. Native descendant/job peak and cold-storage behavior
remain unmeasured. A budget failure stays failed under the declared limits.


Execution
---------

```powershell
python -B tools/pdf-engine/Test-PdfPerformance.py --production-stage '<versioned candidate>'
```

The runner validates the candidate and inventory before and after execution. It
builds the test host and uses the exact candidate worker; it neither rebuilds nor
copies over the candidate. Evidence goes under a fresh `.codex-temp/pdf-performance`
directory. Candidate 1.1.1 may establish the existing PDF baseline but cannot
establish acceptance of the still-unbuilt candidate with all expanded inputs.
Installation, Explorer registration, live licensing and recycling are not involved.

Candidates containing HEIC/AVIF support now receive the pinned HEIF verification
as part of the full-package check. Original candidates without HEIF retain their
existing checks. The HEIF verifier and deployment manifest are included in source
identities. This fixes benchmark admission for the expanded candidate without
changing timing limits, worker selection or the file allowlist.
The [shared admission regression](combined-expanded-inputs.md#benchmark-package-admission)
records the original/expanded package checks and tamper refusals.


Candidate 1.1.1 measurement (2026-09-19)
---------------------------------------

Evidence: `.codex-temp/pdf-performance/5f761f0faaa74db3a74fd09c6fbaf862`.
The exact worker comes from `artifacts/production-staging/3efa4342aed9435995d8f00b52fead96`.
All ten operations pass with fifty PNG and five combined-PDF copies. Candidate
inventory, executable/source inputs and all original bytes/timestamps remain
unchanged. Every fresh worker exits and publication scratch is cleaned.

| Operation | Five measured runs (seconds) | Median | Maximum | Result |
| --- | --- | --- | --- | --- |
| Ten PDF pages to PNG | 4.049, 3.482, 3.646, 3.685, 3.527 | 3.646 | 4.049 | Pass |
| Ten images to one PDF | 2.495, 2.457, 2.490, 2.420, 2.398 | 2.457 | 2.495 | Pass |

The reference machine is the Intel Core i9-9900K with sixteen logical processors
and 34,270,330,880 bytes of reported physical memory (approximately 32 GiB).
Managed-worker peak working set ranges from 108.0 to 381.9 MiB, measured before
the post-timing page probe. It is not a native-descendant or combined-job peak.
The test-host Release build has zero warnings/errors. All ten runs are retained;
no warm-up sample is discarded. These results are generated-workload measurements
on this machine, not customer latency promises or a human review result.

The Release foundation replay passes 4,384 contracts in
`pdf-performance-foundation-c35758ba9a4e4dbea9751e547e86f721`. Two runner guards
reject missing/out-of-staging candidates before creating fixtures or workers;
their receipt is `pdf-performance-guards-66438fbd42534d69808b0fbb37d0acc4`.
Repository boundary/link checks pass 193 documentation files. No shipping
implementation or private source changes are part of this benchmark checkpoint.
