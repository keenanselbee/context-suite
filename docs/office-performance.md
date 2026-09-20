Office To PDF Conversion Performance
===================================

Status: all three document types pass the isolated timing and cleanup checks;
final expanded-candidate replay remains pending. This measures the
ordinary Office workload in the [local acceptance contract](local-candidate-acceptance.md).
Passing timing does not resolve the separate [Excel chart-loss](excel-layout-acceptance.md),
[PowerPoint typography](powerpoint-layout-acceptance.md) or
[network-enforcement evidence](office-isolation-evaluation.md) gates.


Declared method
---------------

Reuse the independently authored ordinary Office fixtures: a two-page Word
document with headers, a table and page-number fields; a one-page Excel print
area with a cached sum and an excluded hidden sheet; and a two-slide PowerPoint
presentation. The fixture builder removes the original hidden middle slide from
the newly generated presentation before recording its identity, so it contains
exactly two slides. Their visible labels remain slide 1 and slide 3. Retained
fixtures from earlier tests are untouched.

Run each document five times, interleaved, with a fresh application model,
worker and disposable native profile for every command. Keep the fixed
45-second median and 75-second maximum per document type. Retain every sample;
never drop a slow first call or relax a limit after a failure.

Measure model/worker construction through command completion, including recovery
scan, document preflight, trial admission, runtime identity checks, native profile
creation/removal, export, independent PDF validation and named-copy publication.
The existing Excel calculation prompt receives an automated saved-values answer;
the callback is included, but human decision time is not. Missing-font review
is unexpected for these ordinary fixtures and fails this benchmark. Keep all
production security policies intact.

Copy the completed native ownership journal immediately before context retirement
so profile/SID cleanup can be checked afterward. This small evidence capture is
included in timing. Exclude fixture generation, audit hashing, post-export page
and text inspection, and application/worker disposal. Candidate verification
warms filesystem caches; fresh worker processes do not imply cold-storage timing.
Record managed worker peak working set before post-export probing. Native Office
job memory, recalculation timing and other environments are not measured here.

Every result must be a validated copy with the expected page count, geometry and
authored text. Check Word header/footer values and page order, Excel's printed
sum and omitted hidden/outside-print-area text, and both PowerPoint slide labels.
The independent PDFium text reader inspects published PDFs after timing. These
checks are bounded completeness evidence, not complete document fidelity review.
Original bytes/timestamps must remain unchanged; each owned native profile,
registry mapping, context, publication journal and worker must be removed.


Execution
---------

```powershell
python -B tools/office-engine/Test-OfficePerformance.py `
  --production-stage '<versioned combined candidate>' --create-disposable-profiles
```

The explicit profile flag is required. Use it only under authorization for the
disposable Office profile workflow. The runner verifies the complete candidate
and inventory, builds only the test host and invokes the candidate's unchanged
worker. Source/binary identities, individual outcomes, extracted text, ownership
journals and timing samples stay under a fresh
`.codex-temp/office-performance/<guid>` directory. Full identity and inventory
checks run again afterward. Installation, Explorer registration and live licensing
are outside this benchmark. Final expanded-candidate replay remains required.

When a candidate contains HEIC/AVIF support, the runner includes the pinned HEIF
payload checks in its full-package verification and records the HEIF verifier
and deployment manifest among its source identities. Candidates without HEIF
retain their original verification. Neither path accepts arbitrary extra files
or changes the timing limits, exact-worker selection or profile authorization.
The [shared admission regression](combined-expanded-inputs.md#benchmark-package-admission)
records valid-package and tamper-refusal evidence separately from timing results.


Retained harness attempt
------------------------

Attempt `e772ccd94c634e98b65795aef15fc4f8` publishes its first Word PDF and passes
the page/text checks, then stops while reading the retained cleanup journal.
The harness renamed its copy `retained.ownership`, violating the reader's
existing item-ID filename binding. The corrected harness retains the original
filename in a separate evidence directory. Product ownership validation and
timing thresholds are unchanged; this failed attempt supplies no passing timing
sample. Future attempts also write each completed command's timing before
post-export inspection so any subsequent failure retains its observed duration.

Independent read-only cleanup evidence at
`.codex-temp/office-performance-cleanup-f0d6e11bc8dd48448695d57bf3ccad28`
verifies the retained journal's complete hash chain, terminal profile-deleted
entry, absent native profile/registry mapping and empty context/publication
directories. The candidate and source identities were unchanged and the final
inventory verification passed in the failed attempt.


Measured baseline (2026-09-19)
------------------------------

Evidence `2852e65e2234461290bd9c56d9cf31d3` completes all fifteen commands against
the unchanged worker in candidate
`artifacts/production-staging/3efa4342aed9435995d8f00b52fead96` (version 1.1.1).
The host exits zero and the source/binary identity audit reports no changes.
After the wrapper's observation handle was lost, the final read-only inventory
verification was resumed separately and passed; no conversions were repeated.
Its `inventory-after.command.json` records `ResumedFinalVerification: true`.

| Document | Median seconds | Maximum seconds | Result |
| --- | --- | --- | --- |
| Two-page DOCX | 25.618 | 36.625 | Pass |
| One-page XLSX, saved values | 30.995 | 50.031 | Pass |
| Two-slide PPTX | 23.475 | 41.810 | Pass |

All fifteen PDFs pass the scoped page geometry and authored-text checks. Every
original retains its bytes/timestamp, all fifteen native profiles and registry
mappings are absent, and application disposal leaves no worker, context or
publication scratch. Managed worker peak working set ranges from 46.0 to
52.9 MiB; this excludes native Office job memory. The Release host builds with
zero warnings/errors. Three boundary checks pass at
`.codex-temp/office-performance-guards-2116bd81eb3a49f9aef482cf26948331`.

The owner paused work after this run and subsequently authorized resumption.
The retained source identities still match the worktree on resumption. Release
foundation regression passes all 4,384 contracts at
`.codex-temp/office-performance-foundation-f75d381f2d8c451785f90fd775d7030d`.
Repository checks pass the public-source boundary, system-theme policy and all
196 documentation files. This result does not close chart fidelity, typography
policy, network enforcement, human review or final expanded-candidate acceptance.
