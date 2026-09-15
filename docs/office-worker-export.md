Office Worker Export
====================

Status: typed worker export verified for authored DOCX/XLSX/PPTX; production
context construction, validation/publication and customer command remain open

The application worker client now sends `office-export` with an operation ID,
owned context path/profile name, exact copied-source identity, modern Office
variant and explicit calculation policy. Excel must specify cached results or
recalculation; there is no implicit product default. The other variants require
`none`. All use the fixed final-text PDF policy. Unrelated command payloads,
invalid context identities and mismatched completion replies are rejected.

The worker derives the host, runtime and inventory paths from its own optional
`office-engine` directory. The request cannot select an executable, engine
arguments, environment variables or arbitrary output names. The private adapter
checks the fresh four-directory context, requires a read-only source snapshot,
holds its read lease, verifies content-based modern Office identity, acquires the
[complete pinned runtime lease](office-runtime-verification.md), and constructs
the bounded explicit environment and native handle identities. The existing
capability-free launcher owns execution, its limits and process cleanup.

The adapter matches the host completion to the source and candidate bytes,
rechecks source identity and runtime membership, and returns an
`OfficeExportCandidate`. This type intentionally contains no `OutputValidation`.
The worker never publishes it. Production document admission, independent PDF
validation and application-owned transactional copy publication remain separate.
The client allows 180 seconds for the request, including runtime verification;
the native process retains its separate two-minute deadline.
The later [interruption investigation](office-worker-interruption.md) replays
ordinary exports successfully but retains startup failures before its active
stop points. Cancellation/loss acceptance is still incomplete.


Actual isolated evidence
------------------------

The private harness links the real application `WorkerClient` and
`OfficeSandboxOwner`, creates fresh authorized test profiles, grants only copied
inputs/owned writable directories and the pinned runtime, and communicates with
the actual worker. No application, installer or Explorer registration is changed.

The completed run is under
`.codex-temp/office-worker/ce6f4c03432d41cb836192e492ac22d2/contracts-3`:

- **39 worker checks** pass across DOCX, XLSX and PPTX, including mutable/wrong-hash
  source refusal, successful following export, completion/candidate hashes,
  context-reuse refusal, unchanged source/original files, profile-folder/mapping
  removal and exclusive file opens after all using processes exit.
- The three candidates are 44,319, 26,915 and 26,626 bytes respectively. Excel uses
  the explicitly selected cached test mode; this does not resolve its product
  default or verify recalculation through this worker path.
- Independent inspection at `inspection-abff754b07e647ae86de566a85b80f39` passes
  qpdf structure checks, authored text expectations, page geometry and exact
  PDFium control-pixel comparisons for all three PDFs. It re-inspects retained
  `cs41` controls; it does not claim new full-trust Office exports.
- **3,023 foundation contracts** pass, including 37 new Office request/candidate
  checks. Worker, private harness, inspection and application test-host builds
  have zero warnings/errors. The shared directory helper also passes the **30**
  runtime-lease regression checks at `c1e91678ad9440428e5246d42a9c4638`.

The first attempt lacked the copied source's read-only attribute. A read-only
diagnostic verified its content identity and full runtime; a separately owned
adapter diagnostic identified the native source-attribute refusal. Both test
profiles were removed. The adapter now rejects that condition before launching.

The next attempt exported Word successfully but exposed cleanup of Office cache
paths longer than 260 characters. The application owner now uses extended Win32
paths for its already-validated roots and children. A precisely scoped recovery
of that owned test profile verified its removal and SID absence from all 21,296
runtime/context entries. The test remains recorded as a failed run with recovered
cleanup. The recovery-only compiled helper and its binaries remain in ignored
scratch; no recovery hook is included in product or maintained test code.

The expanded ownership test passes **50 checks**, including long-path grant,
revocation and original-byte preservation, at
`.codex-temp/office-owner/6903af9141f94a4e806a4500b4a662de`. The successful worker run
then verifies cleanup of actual Office-generated long cache paths. Failed worker
builds/receipts were retained before refreshing the scratch worker; the complete
runtime copy was reused unchanged. The successful build/source receipt is
`retry-1bddc022cd5c48f6b7a45d2c2ddfb1b6/build.json` within the worker evidence root.
`.codex-temp/office-worker-final-verification.json` reconciles these source
receipts, candidate/source hashes and absence of all seven involved test profiles.


Repeatable commands and remaining work
-------------------------------------

From the public repository with the private checkout and restored dependencies:

```powershell
python .\tools\office-engine\Test-OfficeWorker.py --create-disposable-profile `
  --copy-receipt <retained-office-copy.json> --host-receipt <host-build.json> `
  --fixtures <authored-office-fixtures-directory>

.\tools\office-engine\Inspect-OfficeWorkerExports.ps1 `
  -WorkerResults <contracts-results.json> -ControlDirectory <retained-cs41> `
  -PdfPreparedDirectory <prepared-qpdf> -PdfiumPreparedDirectory <prepared-pdfium>
```

The first command requires the explicit profile opt-in before preparing files or
launching anything. It creates a fresh scratch worker/runtime and test contexts,
records source/build identities and keeps failure evidence. The second verifies
the independent PDF tooling and writes a new inspection directory after worker
cleanup has passed. Neither command installs, registers or publishes anything.

Next, construct and journal production operation contexts, integrate independent
PDF validation and copy publication, and exercise cancellation, worker loss and
application loss through those actual boundaries. Prior lower-level crash and
cancellation checks remain scoped evidence. Resolve broader source admission,
active content/network enforcement, fonts, calculation, legacy variants and
runtime adoption before enabling customer Office conversion. Startup performance
is not yet accepted. Reserved candidate 1.1.0 is unchanged; this checkpoint is not
visible UI, installed lifecycle, theme/DPI, screen-reader, commerce or release
acceptance.
