Office Export Interruption Evaluation
====================================

Status: generated Word control export, cancellation, owner-crash and same-profile
recovery passed, 2026-09-14. Filesystem/network isolation and the required
customer Office converter remain incomplete.


Test boundary
-------------

The existing [Office lifetime tests](office-process-lifetime.md) interrupt engine
startup without an open document. The new `-DuringExport` mode of
[Test-OfficeEngineLifetime.ps1](../tools/office-engine/Test-OfficeEngineLifetime.ps1)
instead authors a passive 96-page Word file with distinct 384-square noise BMPs.
The complete control must export 96 PDF pages and more than 32 MiB of data;
the independently pinned qpdf checks structure and page count. This tests
completion, not broad font/layout fidelity.

For each interruption the observer must retain the actual launcher and engine
handles, verify their executable identities and owned-job membership, and observe
a PDF with a valid header, increasing length and no end marker. Startup alone or
an output filename alone cannot satisfy the trigger. A bounded replacement-engine
search handles an engine restart under the same live launcher and job, recording
the earlier PID and exit code. No unrelated process is stopped.

Cancellation uses the existing owned-job cancellation path. The owner-crash case
kills only the disposable evaluation owner, so job-handle closure must terminate
its children. Both engine handles must signal within five seconds. The crash
observer independently reopens and checks PIDs, creation times and executable paths
and confirms that the PDF is still incomplete immediately before interruption.

Recovery must release source/profile handles, retain the interrupted evidence,
and export the existing two-page passive Word fixture using the same disposable
profile. qpdf must accept that PDF and its page count. Original bytes and
modification time and the required profile declarations are checked. This is
evaluation-only; no application publication or customer recovery claim follows.


Retained observations
---------------------

The first attempt under the pinned Office prepared directory at
`lifetime-9a0662f7d9cb441fa6c31b7b29e29944` passes the control: 96 pages,
42,566,344 bytes, 11,885 ms and an empty job after cleanup. Cancellation then
fails because the observed engine exits before a growing PDF is seen.
Log: `.codex-temp/office-export-lifetime.log`.

The second attempt, `lifetime-35f37712893544488998c7b99eb7a2b8`, also passes the
control and fails its interruption trigger. Expanded observation across the
owned profile/input/output directories records startup/profile activity without
a readable growing PDF. The earlier observer treated any engine exit as terminal;
the next experiment therefore follows replacement engines under the live launcher.
Log: `.codex-temp/office-export-lifetime-observation.log`; the case's
`observed-files.json` records actual file observations. Failed evidence remains.

The final run passes all three cases at
`lifetime-659e352a383849959c4bfd26f899a848`, with log
`.codex-temp/office-export-lifetime-restarts.log` and exit zero:

| Case | Trigger or completed control | Result |
| --- | --- | --- |
| Complete export | 96 independently checked pages, 42,566,344 bytes | 10,550 ms; zero active job members after cleanup |
| Cancel | Temporary PDF grew from 885,789 to 2,657,437 bytes without an end marker | Retained launcher/engine handles signaled in 8 ms; same-profile two-page export passed |
| Owner crash | Temporary PDF grew from 885,789 to 2,657,437 bytes without an end marker | Retained launcher/engine handles signaled in 19 ms after owner termination; same-profile two-page export passed |

Both interrupted cases recorded one initial engine exit with code 81 followed
by a live replacement under the same owned launcher/job. This establishes the
restart behavior in this run; it does not infer that every exit code 81 or every
missing PDF has this cause. Actual PDF data was observed below each short
profile's `TMP` directory. The observer does not assume a `.pdf` extension.

After recovery, the interrupted temporary files still existed at 3,100,672 and
5,757,830 bytes respectively, and neither interruption folder contained a final
`Export interruption.pdf`. Those post-stop sizes can exceed the trigger size
because the engine continues until termination. The source hash remained
`6942E28B5E27EABED35C34FB20B3BA152E74E1F5AF460FD8729961D803A7FB14`;
source modification time and profile declarations also passed the harness.
The recovery jobs both reported zero active members. No Office/evaluation process
remained in the final process inspection. This is not a complete descendant-handle
inventory for the owner-crash case.

The evidence contains `export-lifetime.json`, per-case `profile.json`,
`observed-files.json`, `observation-processes.json`, `startup-exits.json`,
`stopped.json`, and control/recovery `pdf-check.json`. The Release evaluation host
build passes with zero warnings/errors. No production code or payload changed;
foundation/media suites and production staging were not rerun for this tool-only
checkpoint.


Reproduction and remaining scope
--------------------------------

```powershell
.\tools\office-engine\Test-OfficeEngineLifetime.ps1 -PreparedDirectory '<pinned repository-local Office workspace>' -DuringExport -PdfPreparedDirectory '<pinned repository-local qpdf workspace>'
```

The wrapper verifies complete Office and qpdf inventories before execution.
Generated documents, outputs, diagnostics and short disposable Office profiles
stay under repository `.codex-temp`. No AppContainer profile, installation,
Explorer registration or customer document is involved.

Remaining requirements include Excel/PowerPoint rendering interruption, full descendant inventory after owner
death, filesystem/network isolation, rendering policies and the required
customer converter. The [AppContainer authorization](office-isolation-evaluation.md)
remains pending. This experiment does not satisfy those requirements or mark
the [broad support goal](broad-file-support-goal.md) complete.
