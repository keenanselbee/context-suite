Office Export Interruption Evaluation
====================================

Status: generated Word, Excel and PowerPoint control exports, cancellation,
owner-crash and same-profile recovery passed, 2026-09-14. Filesystem/network
isolation and the required customer Office converter remain incomplete.


Test boundary
-------------

The existing [Office lifetime tests](office-process-lifetime.md) interrupt engine
startup without an open document. The new `-DuringExport` mode of
[Test-OfficeEngineLifetime.ps1](../tools/office-engine/Test-OfficeEngineLifetime.ps1)
instead authors passive 96-page Office files with distinct 384-square noise BMPs.
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
and export the existing small passive fixture from the same document family using
the same disposable profile (one Excel page, two Word/PowerPoint pages).
qpdf must accept that PDF and its page count. Original bytes and
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


Excel and PowerPoint extension (2026-09-14)
-----------------------------------------

The harness now supports `-ExportFamily Word,Excel,PowerPoint`, retaining Word
as the default. Each family has separate evidence and profiles after one complete
payload verification. Excel has 96 visible sheets, each with an explicit print
area and a distinct 384-square BMP; PowerPoint has 96 explicitly ordered visible
slides and the same distinct bitmap set. Both are authored from the existing
passive templates. The heavy Excel fixture contains no formulas. The complete
control must produce 96 pages and more than 32 MiB through the family's fixed
PDF filter before interruption tests begin.

The first attempt, `lifetime-f2cd5ad803dc4789b3c9e24f63a12bf3-excel`, failed
during fixture generation, before launching Office. The XML relationship builder
incorrectly combined an `XNamespace` with a relationship type string. The corrected
generator passes independent package inspection for both families: 96 distinct
bitmaps, visible pages, ordered headings, complete internal relationships and no
external relationship declarations. Fixture-only runs remain at
`.codex-temp/office-export-fixtures-excel` and
`.codex-temp/office-export-fixtures-powerpoint`; the inspection helper is
`.codex-temp/Inspect-OfficeVisualExport.py`.

The second attempt, `lifetime-0733451e903c4230afcd1dc9b4cbf89d-*`, passes all
Word cases and the Excel control and cancellation/recovery cases. Excel owner
failure observes a growing incomplete PDF and both tracked handles signal in
5 ms, but immediate exclusive source opening fails with a sharing violation.
A subsequent read-only inspection opens that source exclusively and finds no
remaining Office/evaluation process. This identifies an overly strict immediate
file-release assumption; it does not establish which component retained the
file. PowerPoint was not reached. Log: `.codex-temp/office-visual-export-fixed.log`.

Recovery now retries only sharing/lock violations within one five-second budget
for source and profile files. Other errors fail directly. Each successful release
records elapsed time and sharing-violation count in `file-release.json`; an
interrupted case must also lack a final `Export interruption.pdf`. Three focused
contracts pass for delayed release, a lock held beyond the deadline and a missing
file, with evidence at `.codex-temp/office-export-file-release-contracts`.

The third attempt, `lifetime-e34e4bcd286b41c09acbda15aaa3ec9b-word`, passes
the Word control, then fails while finding a replacement engine after a recorded
exit code 81. The Windows .NET exception record identifies a null executable
module in `Observe`; the retained record is
`.codex-temp/office-visual-export-null-module.txt`. The observer now treats an
unavailable module as an unverified candidate and continues its existing bounded
search. Launcher/retained identities still fail when unavailable; no identity or
job-membership requirement is removed. The wrapper also preserves complete
native diagnostics before checking the exit code.

The final run passes all nine control/interruption cases at
`lifetime-8e4d4891f7854e90bfa33ad9a6f2bf3a-{word,excel,powerpoint}` under the
pinned Office workspace. Log: `.codex-temp/office-visual-export-observer.log`, exit
zero. Each complete control passes qpdf structure and 96-page checks. Each of
the six interrupted cases passes same-profile recovery and original byte/time
preservation, and lacks a final PDF in its interrupted output folder.

| Family | Complete PDF bytes / export ms | Cancel: growing PDF bytes / cleanup ms | Owner crash: growing PDF bytes / cleanup ms | Recovery pages per interruption |
| --- | --- | --- | --- | --- |
| Word | 42,566,344 / 9,720 | 885,789 to 2,657,437 / 21 | 1,328,701 to 3,100,672 / 23 | 2 |
| Excel | 42,611,469 / 10,260 | 1,772,842 to 2,659,281 / 20 | 1,329,623 to 2,659,281 / 21 | 1 |
| PowerPoint | 42,590,176 / 9,723 | 1,328,880 to 2,658,304 / 20 | 885,908 to 2,658,304 / 19 | 2 |

PowerPoint owner-crash recovery encounters one sharing violation and obtains
exclusive source/profile access after 23 ms. The other five cases obtain access
without retries. This exercises the release wait with the actual engine as well
as the focused contracts (86 ms delayed release, 5,002 ms persistent-lock failure,
immediate missing-file failure). These are observed timings, not performance
guarantees. The control and recovery jobs all report zero active members after
cleanup; both tracked launcher/engine handles signal for every interruption.

Independent package/output inspections pass for Excel and PowerPoint and retain
`fixture-and-output-review.json` in each evidence directory. They verify distinct
bitmaps, ordered visible pages and complete internal relationships, then recheck
source hashes, incomplete temporary PDF data and absence of a completed PDF in
the interrupted folder. Excel's source SHA-256 is
`4523B996BF72E2900567760752F694D5EB43F9C5597781345B1BE98673161A59`;
PowerPoint's is `B08545900470B0905B208F67CC69D5464E37ECF956ECC54A82C844BD011D5A65`.
ZIP timestamps are not normalized; independently regenerated packages need not
share these hashes.

The two real startup cancellation/owner-crash checks also pass again with the
updated observer, using the just-verified payload. Evidence:
`lifetime-visual-startup-regression`, log
`.codex-temp/office-visual-startup-regression.log`. The new observer preserves
the distinction between startup and active-export evidence. No production
payload changes or new foundation/media-suite results are claimed.

Final Release build: zero warnings/errors, log
`.codex-temp/office-visual-complete-build.log`. Public source boundaries,
system-theme policy and 118 documentation files pass, log
`.codex-temp/office-visual-repository-final.log`. The first documentation check
stopped on Git's line-ending warning for an unrelated edit; the successful run
suppresses that warning with a process-local `core.safecrlf=false` setting while
retaining whitespace validation. No Git configuration or unrelated file changes
were needed. The final process inspection finds no Office/evaluation process.


Reproduction and remaining scope
--------------------------------

```powershell
.\tools\office-engine\Test-OfficeEngineLifetime.ps1 -PreparedDirectory '<pinned repository-local Office workspace>' -DuringExport -PdfPreparedDirectory '<pinned repository-local qpdf workspace>'
# Add -ExportFamily Word,Excel,PowerPoint to run all three families sequentially.
```

The wrapper verifies complete Office and qpdf inventories before execution.
Generated documents, outputs, diagnostics and short disposable Office profiles
stay under repository `.codex-temp`. No AppContainer profile, installation,
Explorer registration or customer document is involved.

Remaining requirements include full descendant inventory after owner death,
filesystem/network isolation, rendering policies and the required
customer converter. The [AppContainer authorization](office-isolation-evaluation.md)
remains pending. This experiment does not satisfy those requirements or mark
the [broad support goal](broad-file-support-goal.md) complete.
