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
