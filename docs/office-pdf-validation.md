Office PDF Validation
=====================

Status: isolated application export/validation/publication transaction verified;
[customer command integration](office-direct-command.md) is implemented with
acceptance in progress. Incomplete-preparation retention remains unfinished.

An Office host completion acknowledges an export; it does not authorize final
publication. `OfficePdfWork` binds that completed candidate to its source context,
fixed export policy and existing output reservation. The private adapter issues
`OutputValidation` only after independent inspection and a checked copy into the
reservation. The application still owns the eventual output name and transaction.


Validation boundary
-------------------

The adapter holds checked read handles for the context's source snapshot and
candidate PDF. It verifies their lengths and SHA-256 values against the completed
export before inspection, rechecks them before copying, and verifies the copied
reservation's length and hash. The reservation must already exist, be empty and
single-linked, have the item's exact temporary filename, and remain outside the
Office context. No original, candidate or final output is deleted or replaced.

Pinned qpdf 12.4.1 inspects a private snapshot with recovery suppressed. Its full
JSON object inventory excludes stream payload data. The shared graph admission
checks reject ambiguous keys, unresolved references, encryption, signatures and
external streams. The Office-specific policy requires PDF 1.7 and rejects
unexpected forms, scripts, embedded files, rich media and unreviewed actions.
Ordinary opening page destinations, tagged layout attributes and manual PDF/URI
hyperlinks remain representable. No hyperlink is followed.

The adapter also requires successful qpdf structural/stream checking without
warnings, then compares qpdf's page count with the separate pinned PDFium 8044
page reader. Both readers must agree on the candidate identity. This inspection
does not render every page or establish typography, accessibility, full visual
fidelity or general PDF safety. The earlier authored-content and pixel comparisons
in [startup recovery](office-startup-recovery.md) remain separate evidence.

The fixed bounds include 128 MiB output, 4,096 pages, 32 MiB object JSON, the shared
graph depth/object budgets, the page reader's geometry/allocation limits and a
60-second cancellation deadline. The page reader currently applies its raster
geometry limits during inspection, so an unusually large printable page can be
refused even when its PDF structure is valid. The document-specific customer
message for this condition still belongs to pending application integration.

Request cancellation propagates; the component removes its owned inspection
snapshot. The caller must abandon a partial reservation after failure or
cancellation during copying. The transport must validate the host request ID
before constructing this work, and the application must retain the original-file
lease and transaction identity through final publication. Neither obligation is
replaced by this component.


Verification
------------

All 3,287 foundation contracts pass, including 49 new Office PDF policy/request
checks. Evidence is
`.codex-temp/office-preparation-foundation-1477e9a8ddfe4be48b3bdbe5e245c582`.
The tests cover ordinary destinations/links, tagged attributes, escaped keys,
unreachable forbidden objects, action chains, signatures, external streams,
page-count bounds, cancellation and mismatched reservations.

The actual reader harness passes 57 checks using retained completed exports from
the native application recovery run, plus independently authored PDFs. Word has
two pages, Excel one, and PowerPoint two. All three reservations are exact copies
of their candidates; source snapshots and candidates remain unchanged.

Fourteen failure cases cover scripts, forms, attachment declarations, signature
information, truncation, broken cross-reference data, invalid compressed streams,
an oversized page, source/candidate hash mismatches, size mismatches/bounds,
occupied reservations and pre-cancellation. Each verifies refusal, unchanged
inputs/reservation and empty inspection scratch. These cases exercise candidate
validation with fabricated completion data; they do not claim successful Office
exports of the hostile candidates.

The source/binary/fixture-bound run is
`.codex-temp/office-pdf/229300d310444b2db1d08312f982c3bd`.
Its `exit.json` records exit zero and unchanged inputs. The private harness builds
with zero warnings/errors. The first run,
`.codex-temp/office-pdf/7934d5829ace4c8bac3fb85f60753af7`, stopped at the oversized
page because its test expected a generic refusal instead of the reader's typed
resource-limit exception. The final test requires that specific exception;
production behavior was not relaxed.

Run against explicitly selected retained exports and pinned candidate readers:

```powershell
python tools/office-engine/Test-OfficePdfValidation.py `
  --pdf-engine <candidate-pdf-engine-directory> `
  --pdf-renderer <candidate-pdf-renderer-directory> `
  --exports <passed-office-export-results.json>
```

This creates repository-local evidence and generated candidates, without an
Office export, Windows profile, registration change or customer-file overwrite.
It reads the selected retained exports and verifies their receipt hashes before
building/running. Reader constructors verify their complete pinned runtime sets.


Worker dispatch and interruption
--------------------------------

`office-pdf-validate` now carries the typed request through the existing sequential
worker. The command rejects missing or mixed payloads. The client rejects
contradictory replies and validates the returned item, policy, source/output
identity, page-count bounds and engine description against the request. The
worker loads the pinned PDF readers lazily; it needs no Office runtime or profile
to validate an already completed candidate.

The client uses a 120-second outer deadline around the adapter's 60-second
validation deadline. Caller cancellation stops the worker/job and removes only
that client's checked scratch directory after process exit. Ordinary validation
refusals leave the sequential worker available for later work.

Run the same command above with `--worker` to build a matching worker into a new
repository scratch directory and exercise the actual `WorkerClient`. The runner
records sources, fixture identities and both worker/harness binaries, and checks
that they remain unchanged. This is a scratch component build, not a revised
production payload. The added strict JSON fields require matching application and
worker builds; do not mix this client with the reserved 1.1.0 worker.

All 3,310 foundation contracts pass, including 23 new typed request/result checks,
in `.codex-temp/office-preparation-foundation-51965309d7eb44d4824d467b6a5c6d62`.
The actual worker run passes 71 checks in
`.codex-temp/office-pdf/8b054e948cb2478396b05f285e015f85`.
It repeats the retained exports and fourteen refusal cases, verifies worker reuse,
then interrupts two 32 MiB authored candidates after observing the live worker's
owned inspection snapshot. One case cancels the caller; another terminates the
worker. Both verify the expected client outcome, worker exit before return,
unchanged source/candidate, empty reservation and cleared scratch. A fresh worker
successfully validates a following candidate after each interruption. These cases
observe snapshot preparation, not cancellation during the final reservation copy
or every native reader phase.

The direct adapter regression also passes all 57 checks in
`.codex-temp/office-pdf/9719312b57b74acd9a55e52d5fc39a41`.
Both runs record exit zero and unchanged inputs; worker and harness Release builds
have zero warnings/errors. No Office export or native profile is created by these
runs. Existing native Office recovery evidence remains separate.


Office batch admission
----------------------

The application now has an immutable `OfficeConversionPlan` and the matching
paid/trial admission path. It represents one PDF per selected DOCX, XLSX or PPTX,
retains selection order and the settings snapshot, and rejects duplicate IDs,
duplicate normalized paths, invalid format/policy combinations and over-budget
selections. Bounds are 4,096 files, one million total path characters and the
existing 64 MiB per-document source limit. Execution remains sequential.

Excel requires either `cached` or `recalculate`; neither is inferred by this
component. Word and PowerPoint require `none`. The owner's customer Excel default
is still pending. These are internal policy values, not new user-facing controls.
Confirmation creates no files or profile and is not a rendering admission proof:
the eventual executor must match each source's preflight identity when preparing
the isolated context. The plan always requests copies, including with the saved
overwrite preference. Office replacement is not enabled; this limitation is
recorded in the [capability table](file-type-coverage.md).

Only a valid confirmed batch reaches access admission. The selected paid state
is authoritative; expired paid access cannot start a new trial. A previously
unactivated installation uses the ordinary trial record. Once admitted, the batch
keeps that admission through later expiry. This adds no licensing transport or
customer UI and does not launch the Office engine.

All 3,338 foundation contracts pass in
`.codex-temp/office-preparation-foundation-ba91ec9d8a10422f9d1bf3284d690ea1`,
including 28 new plan/admission checks. They cover the selection and policy
bounds, cancellation before trial creation, trial expiry, paid admission without
trial creation, and paid expiry without trial fallback. Licensing uses the
existing synthetic service, without live Polar calls. The Release application
test host builds with zero warnings/errors. Earlier 71-worker/57-reader evidence
above was not rerun for this admission-only change.


Application transaction
-----------------------

`OfficeConversionExecutor` now joins the admitted batch to context preparation,
native export, independent worker validation and application-owned copy
publication. It checks the prepared original's identity against the admitted
source and the publication reservation, retains its read lease through publication,
and removes the Office profile before independent validation starts. Native profile
creation, grants and cleanup run off the UI thread. It uses the existing
sequential worker and always requests copies.

Cleanup failures retain the original owner, context leases and journal in the
executor. Subsequent Office items are declined while cleanup is pending. Disposal
retries cleanup; a still-failing owner remains available for restart recovery
after application exit. The caller must retain this executor while it owns pending
cleanup and surface its recovery records. The customer view model is not connected
yet, so these ownership/shutdown responsibilities remain an integration gate.

Completed contexts now have live-owner retirement and
[resumption after restart](office-startup-recovery.md#completed-context-retirement)
when a version-four retirement intent was recorded. Preparation failures and
interrupted exports without that intent still retain evidence. Before customer
enablement, complete their retention policy so historical records cannot exhaust
the startup scan bounds. Final reservation cleanup uses the existing publisher
and never deletes the original.

`tools/office-engine/Test-OfficeExecution.py` requires explicit
disposable-profile authorization and creates a matching repository-local worker
with the independently selected Office and PDF runtimes. It exercises real
DOCX/XLSX/PPTX exports, a collision, publication failure and cancellation before
publication. It records source/binary/fixture hashes and native profile cleanup.
The transaction passes 35 checks in
`.codex-temp/office-execution/3cb47085881548c79e110b1ac066baa0`.
The first batch publishes Word, Excel and PowerPoint copies; Word's pre-existing
output remains unchanged and the new PDF receives a collision suffix. A subsequent
publication failure leaves no PDF or reservation. Cancellation at the publisher's
validated stage cancels the current and remaining documents without publication.
All original bytes/modification times remain unchanged. All five profile folders
and Windows mappings are absent, with durable stop/cleanup journals and retained
source snapshots/candidates. No worker scratch files remain after client disposal.

The three published PDFs additionally pass independent structure, authored text,
page geometry and exact control-pixel comparisons in
`inspection-339cc37f3c154828b05bf5c3b86e247b` under that stage. Use
`Inspect-OfficeWorkerExports.ps1 -ApplicationOutputs` with the application report
and the existing independent PDF evaluation runtimes/controls for this check.
This compares the actual named outputs, not just the renderer's candidates.

The separate `--cleanup-only` run passes ten checks in
`.codex-temp/office-execution/b2f70eb4e5774beeb3d6ad43c5fdbd08`.
An exclusively held, caller-authored temp file makes the context non-fresh and
obstructs cleanup after the worker refuses export. No Office renderer starts in
this case. The executor retains its original-file lease and ownership record,
declines the remaining document, and issues no publication. After the obstruction
is released, disposal retries cleanup, removes the sixth profile/mapping and
releases the original lease. This verifies the pending-owner branch without
claiming every native cleanup failure is recoverable.

Both execution wrappers record exit zero and unchanged source, fixture and binary
hashes. The first preparation attempt,
`.codex-temp/office-execution/557890287d394531a2ad3e786e28a37b`, stopped before
profile creation because the runner initially selected `net10.0` instead of the
test host's `net10.0-windows` directory. The corrected run reused its copied worker
only after matching every top-level file to a fresh build. `--retained-worker`
now supports that explicit scratch reuse; private adapters still verify their
complete pinned runtimes. No reserved production payload was changed.

All 3,342 foundation contracts pass in
`.codex-temp/office-preparation-foundation-da4da45f8a904fdfaa442701eda64dfa`,
including four new executor refusal checks for missing engines, denied access,
foreign admission and pre-cancellation. The Release application test host builds
with zero warnings/errors. Visible/keyboard acceptance was not performed.


Live-context retirement verification
-------------------------------------

The executor now retires completed generated contexts through
[`OfficeContextPreparation.Retire`](office-context-preparation.md#completed-live-context-retirement).
Failed retirement remains in its pending cleanup list. If publication already
succeeded, the result keeps the actual PDF and reports a cleanup warning, so it
remains visible without becoming a request to publish a duplicate. Disposal
retries file cleanup independently of completed export/publication.

The updated workflow passes **43 actual Office checks** in
`.codex-temp/office-execution/5619ba678499496eb0e4e7860364775e`.
Six exports cover all three families, collision naming, publication failure,
cancellation and an exclusively held cache file introduced after publication.
All six generated contexts and ownership records are removed, along with their
Windows profiles/mappings. The post-publication warning retains exactly one PDF;
releasing the lock and disposing the executor completes retirement without
another export. Original bytes and modification times remain unchanged.
The harness copies completed journals into a separate evidence directory before
retirement; the application's context root is empty. The wrapper records exit
zero and unchanged inputs.

The separate `--cleanup-only` run passes **ten checks** in
`.codex-temp/office-execution/0656aeca57604e7791166a3ae7ddadb8`.
The obstructed native cleanup retains its owner and original lease, then completes
profile removal and context retirement after the lock is released. It exports no
PDF and starts no Office renderer. Final verification at
`.codex-temp/office-retirement-verification.json` confirms all seven profile
folders/mappings are absent, both context roots are empty, no worker/OfficeHost remains,
and the tested production sources still match. The stricter directory-substitution
foundation fixture was finalized after the six-export run; production code did
not change, and the cleanup-only run binds the final test sources.

The three normal published PDFs pass independent structure, authored text,
geometry and exact control-pixel inspection at
`inspection-f6730d517d0c48dc844c12d9442d515d` under that stage. The inspector now
accepts the additional cleanup-warning batch in application reports while still
requiring the three named outputs. Its initial attempt rejected the new report
shape before inspecting any PDF; the corrected inspection passes.

All **3,379 foundation contracts** pass in
`.codex-temp/office-preparation-foundation-975874f186df4b918f1cdb1bb3c0b395`,
including the focused retirement boundaries described in the preparation record.
The application test host builds in Release with zero warnings/errors. The
existing scratch worker no longer matched rebuilt binaries, so a fresh isolated
worker was prepared. That mismatch and a corrected C# local-name build error
both stopped earlier attempts before native profile creation. Reserved payload
1.1.0 remains unchanged. This adds no visible/keyboard or installed acceptance.


Remaining work
--------------

Connect customer command dispatch and long-lived cleanup ownership, and resolve
retention for incomplete preparation and interrupted exports without retirement
intent. Extend cancellation coverage to the reservation-copy
phase, abrupt application loss and additional resource-limit failures. Broader Office fidelity,
the owner's Excel calculation default, fresh combined packaging and actual
visible/keyboard acceptance remain open. This component changes no installed app
or reserved 1.1.0 payload and does not clear the expanded release goal.
