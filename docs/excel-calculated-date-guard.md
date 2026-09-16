Calculated Excel Date Guard
==========================

Status: implemented in the isolated native host, worker and application. Known
recalculated early-date errors are withheld from publication. Correct rendering
for those dates, broader formatting fidelity and launch acceptance remain open.

The [same-document experiment](excel-date-system-evaluation.md#native-same-document-copy-experiment-2026-09-16)
established that the authored workbook copies preserve calculated values without
changing their corresponding PDFs. The production path now uses that evidence
shape to close the demonstrated source-preflight gap: a modern saved value can
recalculate to an early date that the renderer displays incorrectly.


Export and validation
---------------------

The fixed native host loads the document once, exports its PDF, then saves
`calculated.xlsx` from the same document without taking ownership or rewriting
the source. The copy stays inside the already granted output directory. There
are no additional filesystem or network grants. Word and PowerPoint exports do
not create a workbook copy.

Host protocol version 3 binds the workbook's byte count and SHA-256 to the same
terminal reply as the request, calculation choice, source and PDF identities.
Excel requires a nonempty identity; the other families require zero bytes and a
null hash. Old or incomplete replies are refused. The PDF recipe and ownership
journal policy identifiers remain unchanged. Existing staged hosts must be
rebuilt with their matching reader; historical version-2 replies are not new
version-3 completion evidence.

The copy must be a regular, single-link ZIP package of at most 64 MiB. This is
an acceptance bound after writing, not a live disk quota. Existing job memory,
process lifetime, cancellation and context ownership rules still apply. A failed
or interrupted export cannot produce the required terminal completion.

The worker verifies the copy's actual bytes and recognized XLSX identity before
returning an export candidate. Independent PDF validation repeats that check
before filling the output reservation. Both paths use a read lease, bounded
package/date parsing and a five-second inspection deadline. A positively
identified early-1900 date produces a stable failure category across IPC. The
application explains the date limitation, suggests exporting from Excel and
keeps Analyze available. Native diagnostic text never becomes the UI message.

Unknown or unsupported date-format inspection remains unknown. This positive-risk
guard is not complete validation of every workbook, a correction to the renderer,
or permission to remove required Excel conversion from launch scope. The original
source guard remains in place. Both paths preserve originals and the existing
mandatory-copy policy for Office PDFs.


Verification
------------

All **4,110 foundation contracts pass**, including the versioned completion,
required/forbidden workbook identities, size/hash bounds, missing fields and
worker serialization checks. The source-bound log uses
`.codex-temp/office-calculated-foundation-final`. Its only later C# change is the
application test's journal-capture correction described below; production source
is unchanged from that run.

The private guard passes **63 focused checks** using the retained six native
copies: expected date refusal, saved/1904 controls, missing/changed/malformed
copies, matching hashes for malformed data, cancellation and released read leases.
The source-bound log and receipt use `.codex-temp/office-calculated-guard`.

The actual application passes **32 checks** across six isolated exports. Four
saved-value/1904 PDFs publish as validated copies. Both recalculated 1900 variants
fail with the date explanation and publish no PDF. All original bytes/timestamps
remain unchanged; all six profiles, mappings, contexts, output reservations and
publication journals are removed. Evidence is
`.codex-temp/office-execution/da972a0d20b24b21a7082ca82c006dca`.

The [independent inspector](../tools/office-engine/Inspect-CalculatedWorkbookPublications.py)
checks all 42 numeric caches and six rendered candidates against the ordinary
native controls, including their known wrong dates. Page geometry and raw pixels
match exactly; the four published copies match their candidate hashes. Its
receipt is `calculated-inspection-b3d59099f32644f78e16889ec1228f7e/results.json`
under that application evidence. Nine rejection controls cover changed sources,
copies, PDFs, cached values, pixels, cleanup and publication outcomes; see
`.codex-temp/office-calculated-inspector-controls.json`.

The first application attempt at
`.codex-temp/office-execution/50d23da34b5144b180a1724b809035d8`
published the first cached-value PDF but failed its evidence-capture assertion.
Its callback tried to reopen the live ownership journal. The corrected harness
reads the retained copy instead. The failed attempt and its output remain;
its single native profile/mapping and context were verified absent before retry.
It is not counted as a passed matrix. The final run reuses that stage's worker
only after comparing its top-level files to the current build and verifying
unchanged runtime inputs.

The new source-bound native build is
`.codex-temp/office-host/adf5d9ba85264d1d9da425cd36d8ed75`.
Its 112,640-byte host has SHA-256
`C01466D70277C56D8AD43E3C9DCAFE2676E3644B3473197C1EAFAFA14F757765`.
The private runtime lease and public candidate pins agree with that executable
and its sources. The 19,332-file Office runtime is unchanged.

The ordinary Word/Excel/PowerPoint application regression passes **43 checks**
in `.codex-temp/office-execution/bdc152709b9243e9988f7a65f5078687`. It covers normal
copy publication, a collision, failed publication, cancellation before publication,
obstructed temporary cleanup and retry, unchanged originals and all six removed
native contexts. It does not inject interruption during the new workbook save.
The outer log is `.codex-temp/office-calculated-regression.log`.
Independent qpdf/PDFium inspection preserves exact prior text, geometry and
pixels across the three ordinary PDFs and their five pages. Its receipt is
`inspection-b8080ceaca2849f280d2af245ce932d9/results.json` under that run.

All **33 packaging checks pass** with the new host at
`.codex-temp/office-payload-tests-dfb43ac88ccb4b33a4665fb4be4b749d`, including partial
copy retention, refusal to overwrite a failed stage, pinned source/runtime
identities and the combined allowlist. The retained 1.1.0 base is unchanged;
this scratch packaging test does not reserve a new version or establish a
current combined production release.


Remaining work
--------------

Exercise interruption before workbook-copy writing finishes and during the
following validation, including application-owner recovery. Resolve the underlying early-date
fidelity and unsupported formatting policy. Preserve useful failure behavior
while completing that required conversion scope. Native network-denial evidence,
fresh combined production packaging, redistribution/adoption and manual
accessibility/theme/DPI acceptance remain separate. No installation, Explorer
registration, live Polar request or formal release is part of this checkpoint.


Stops after observed workbook growth
------------------------------------

The worker interruption harness now accepts `--workbook-copy` for cancellation,
worker termination and application-deadline expiry. It requires a live native
host with the owned AppContainer identity, a ZIP header and two increasing
workbook sizes while the export remains incomplete. A missed observation fails
explicitly. The existing PDF-growth modes keep their three-family scope.

All **36 checks pass** in
`.codex-temp/office-worker/6abf0f32b81142449e402ea9c303aaa1`. The generated workbook
is 5,332,659 bytes. Observed size pairs are 2,097,152 to 5,344,869 bytes for
cancellation, 5,111,808 to 5,344,869 for worker termination, and 4,194,304 to
5,344,869 for deadline expiry. Each request rejects the unfinished export, waits
for the worker and host to exit, releases input/PDF/workbook handles and permits
a fresh ordinary XLSX export through the same client. Stops return in 66-82 ms
on this machine. Deadline expiry is injected into the existing clock; this does
not measure a real 180-second elapsed timeout.

All six disposable profiles and registry mappings are absent after cleanup.
Original hashes/timestamps remain unchanged. Worker, harness, fixture and source
identities match the recorded inputs. Context files are deliberately retained as
evidence; profile removal does not imply deletion of those repository folders.

The three following PDFs pass independent qpdf/PDFium checks for authored text,
page geometry and exact retained control pixels. The inspection command adds
`-WorkbookCopy` for this explicit one-XLSX scope and verifies the companion
workbook hash and length. Two negative controls reject a missing scope flag and
an altered workbook hash. The run's `verification.json` identifies all three
inspection receipts and the source identities used.

**Timing limit:** all three stopped workbook files are complete readable ZIP
archives. These results prove cleanup and retry after observed workbook growth,
before an export result is accepted. They do not establish that termination
landed before the ZIP writer finished, or during the later managed date
inspection. Those narrower interruption points remain open. No production
implementation, payload pin or release version changes in this checkpoint.


Unfinished-write experiment
---------------------------

A subsequent experiment required the stopped workbook to lack its ZIP end
record. **That interruption matrix did not pass.** Increasing the sheet count,
sampling briefly without a timer delay, triggering on the first incomplete ZIP,
and using larger embedded images did not establish the required stop timing.
Completed writes are not counted as partial-write coverage.

The retained attempts under `.codex-temp/office-worker` are:

| Run | Actual result |
| --- | --- |
| `b7a9acb01d684214ae1895bffdeb82d8` | 48 sheets: native export fails after producing a PDF; no workbook copy appears |
| `c2acd30da0294498a4b7c8b4f3e06dc1` | 24 sheets: export completes before a second partial size is observed |
| `99cb9976acfa4965a950d1f7971068a9` | 24 sheets: faster sampling still misses the second partial size |
| `82c667cad3e44e09acd5f71817cd6214` | 24 sheets: export completes before any incomplete ZIP is observed |
| `229774b70f1648868b0d3a4477f8f474` | Eight larger images: cancellation starts after partial bytes, but the stopped ZIP is complete |
| `36a829ff92654dbfbce6bfc847f6ce52` | The same fixture with earlier lifetime-job termination still leaves a complete ZIP |

The speculative application shutdown change and stricter stop-harness changes
were removed. Their patches remain in `.codex-temp/unfinished-workbook-{public,private}.patch`.
The existing 36-check test retains its documented scope. The two cancelled
eight-sheet attempts returned no accepted result and stopped their exact host
before returning; neither proves interruption of an unfinished write.

All six disposable profiles and registry mappings are absent. Original fixture
hashes and retained worker files are unchanged. Independent ZIP inspection
confirms the five produced workbook copies are complete. The 48-sheet PDF passes
qpdf structure checks and contains 48 pages; this is not text/layout acceptance.
At that checkpoint its native failure remained unexplained. The investigation
below diagnoses the capacity failure. The subsequent
[Office failure guidance](office-direct-command.md#office-failure-guidance)
corrects the worker exception's generic damaged-image wording; the result window
already used a generic Office explanation and now has more specific guidance.

The authored fixture generator now supports `--workbook-stop-fixtures <fresh
isolation-directory> <sheets> [bitmap-size]`: 8, 12, 24 or 48 sheets, 384- or
1024-pixel bitmaps, and at most 60 MiB of generated bitmap data. Word/PowerPoint
stay ordinary controls in this mode. The existing three-family twelve-page
mode retains identical package-part contents. Fresh reproduction of the 48-,
24- and eight-sheet fixtures also matches all prior package-part contents.
Evidence is `.codex-temp/workbook-write-verification-fd6eac32df7747fd82eff2df26341269/results.json`;
its `InterruptionMatrixPassed` is explicitly false. Production sources, native
limits, engine pins and release versions remain unchanged.


Workbook capacity and PDF admission
-----------------------------------

The authored 48-sheet XLSX exposed two independent production limits. The native
engine wrote a valid PDF but failed to save its calculated workbook near the
512 MiB per-process allowance. Raising that allowance to 1 GiB completed the
same exports at 571,764,736 bytes (about 545 MiB) peak process commitment. The
aggregate job limit remains 1 GiB, shared by the host and its descendants. The
eight-process limit, timeout, profile/grant boundaries and native host pin are
unchanged.

Evaluation-only diagnostics now retain the library's bounded save error and
Windows job peak memory. The old native host's `Windows error 5` suffix was not
proof of a filesystem permission failure: the library save does not establish
that `GetLastError` describes its failure. Ordinary-process comparisons under
the same job limits reproduce the failed save. Saving the workbook before its
PDF still fails near 512 MiB, so changing export order is not a fix.

The comparison evidence is under
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8`:

| Run | Result |
| --- | --- |
| `workbook-save-2b98a7680f554782b0fee3d2651ed2e8` | Original order, 512 MiB: save fails at 536,784,896 peak process bytes |
| `workbook-save-777bb8eb83d1459eb7199b671b851b72` | Workbook first, 512 MiB: save fails at 536,469,504 peak process bytes |
| `workbook-save-410170ff6fe8484889bc0d37f2481922` | Original order, 1 GiB: both PDFs and workbook complete at 571,764,736 peak process bytes |

These are passive authored-fixture comparisons, not AppContainer isolation
acceptance. Original hashes/timestamps and profile calculation settings remain
unchanged. The source-bound evaluation helper is
`.codex-temp/office-date-native/d707c0c43b744c4cb3b6cb25b0901dac`;
the shipping native host was not rebuilt or repinned. All sixteen evaluation
process-lifetime checks pass in
`.codex-temp/office-workbook-process-contracts-024ea996664a44e8a82814eb1c15397b`.

The first application run with the new memory allowance completed the workbook
save but failed independent PDF admission. Its 48-page PDF's ordinary reference
chains exceeded the rewrite oracle's recursive hashing depth. Admission now
validates every object and reference through a bounded flat scan. Forbidden
content, schema, JSON nesting, byte, node and object checks remain; full rewrite
hashing keeps its original depth and canonical-byte limits. All **4,120 foundation
contracts pass**, including ten new long-outline/reference regression checks.
The source-bound log and receipt use `.codex-temp/office-capacity-foundation`.

The failed application run is
`.codex-temp/office-execution/f5109065be3c41c3b7a72275ab1fbbf4`;
the confirming direct validation diagnostic is
`.codex-temp/office-worker/bd11a06d73c5435493ef6bb596e7da29`.
All five profiles and registry mappings from these attempts are absent; the
application contexts are retired. The read-only reconciliation is retained in
`.codex-temp/office-capacity-verification.json`. Failed attempts remain evidence,
not passed matrices.

With both fixes, the fresh actual application run passes **43 checks** in
`.codex-temp/office-execution/a05e947d5d7c4ae0ae95aaa26ba3fb79`. It publishes the
48-sheet Excel PDF and ordinary Word/PowerPoint copies, exercises collision,
publication failure, cancellation and obstructed-cleanup retry, preserves all
original bytes/timestamps and removes all six owned profiles, mappings, contexts
and journals. `exit.json` records exit zero and unchanged source/fixture/binary
inputs. Both Release builds have zero warnings/errors.

The [capacity inspector](../tools/office-engine/Probe/ExcelWorkbookCapacityInspection.cs)
independently checks four PDFs: the prior 512 MiB attempt's completed PDF, the
successful capacity comparison's before/after PDFs, and the application-published
copy. qpdf splits each into 48 pages; PDFium inspects and renders each page at
96 dpi. All authored sheet labels and their order, 612-by-792-point page geometry
and exact control pixels match across **144 page comparisons**. The receipt is
`contracts/capacity-inspection-932efc4ac0e74bab89ba9175e34c864b/results.json`
under the fresh application run. Inspection source/input identities and exit zero
are recorded in `.codex-temp/office-capacity-verification.json`. This fixture has
exact equality; it does not broaden the owner's separate spacing tolerance.

These fixes do not establish cancellation before a workbook ZIP finishes, fix
early-1900 date rendering, complete broader Office fidelity, or provide fresh
combined release packaging. The resource allowance is a bounded capacity fix,
not a claim that every admitted 64 MiB workbook can be rendered within it.
