Office PDF Validation
=====================

Status: independent validation and worker dispatch verified; customer Office execution and
publication integration remain unfinished.

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


Remaining work
--------------

Connect worker validation to application Office admission, execution, safe copy
publication, cancellation and recovery. Exercise cancellation during copying,
caller reservation cleanup, publication failure and resource limits through that
integrated path. Broader Office fidelity,
the owner's Excel calculation default, fresh combined packaging and actual
visible/keyboard acceptance remain open. This component changes no installed app
or reserved 1.1.0 payload and does not clear the expanded release goal.
