Office Production Candidate Payload
===================================

Status: the source-built application integration selection passes 48 scratch
packaging checks. [Combined candidate 1.1.1](combined-office-candidate.md) retains
its historical MSI-based engine and is unchanged. A fresh combined formal
candidate, integrated acceptance, redistribution review and adoption remain open.

The build can now include the independently prepared Office candidate alongside
the image/audio/PDF engines. This is an optional packaging path for the existing
[Office command](office-direct-command.md), not a new engine selection or a
change to the retained 1.1.0 package. Office fidelity and isolation acceptance
remain separate requirements under the [launch matrix](launch-capability-matrix.md).


Pinned input and layout
----------------------

[The selection record](../tools/office-engine/payload-candidate.json) now pins the
rebuilt 112,640-byte native host, three app-local startup DLLs and the canonical
486,450-byte runtime inventory enforced by the
[private runtime lease](office-runtime-verification.md).
The inventory identifies all 3,831 runtime files and their sizes/hashes, totaling
547,045,673 bytes. Staging checks the archive and inventory pins against the
[source-runtime record](../tools/office-engine/source-runtime-candidate.json),
and the host against four current private source/build-input hashes. It does not
copy private source or local build receipts. The selected runtime04 archive
contains this host and the reviewed renderer. Historical selections below used
an earlier archive with a separately rebuilt host.

Staging also copies the exact public source-runtime record to
`ContextSuite.OfficeSource.json` beside `office-engine/`. This records the source,
patches, source kit, configuration, selected toolchain and build receipts without
embedding machine paths or private source. The 6,274-byte record is required by
Office-enabled production verification; missing or changed bytes are refused.
Its LF checkout rule preserves this identity across Git checkouts. It is outside
the engine inventory, so engine bytes and the private runtime lease are unchanged.

The historical [calculated-date guard](excel-calculated-date-guard.md) advanced the host to
completion protocol version 3 and repeats all 33 packaging checks with that pin.
Evidence is `.codex-temp/office-payload-tests-dfb43ac88ccb4b33a4665fb4be4b749d`.
The combined scratch check still uses the retained 1.1.0 base managed payload;
it is not a fresh formal package of the current application and worker.

The input is a complete `office-engine` directory retained from an isolated
worker evaluation, containing the host, three startup DLLs, `runtime-files.txt`
and `runtime/`.
An editable copy receipt alone cannot authorize different files: every inventory
byte and every runtime file must match the committed pins. The full runtime
retains the supplied `LICENSE.html`, `NOTICE`, `CREDITS.fodt` and other inventoried
vendor material. Presence of this material is not a completed redistribution,
source-availability or component-license review.

The verifier rejects missing or additional files and directories, including
unlisted empty directories and linked entries. It requires the three empty
package-cache directories already checked by the worker. It checks complete
source membership and hashes before writing, then verifies copied files and the
whole resulting tree. Existing destinations, overlapping source/destination
trees and paths outside repository scratch or isolated production staging are
refused. Input engines must be in scratch. A partial failed stage is retained for
inspection and cannot be silently overwritten.

This is static build-time verification. It neither executes LibreOffice nor
creates a Windows profile, and does not replace the worker's live file and
directory leases or the per-document isolation and publication checks.


Build and verify
----------------

Add `-OfficeEngineDirectory '<verified scratch worker>/office-engine'` to the
[combined PDF production build](pdf-production-payload.md#build-and-verify).
Office requires a fresh `StagingId` and all three PDF input arguments: the qpdf
and PDFium prepared directories and pinned qpdf source archive. PDF engines
provide independent output validation; the Office flag cannot bypass that
dependency. The audio input option remains independent.

The normal build still omits Office. The production verifier requires both
`-AllowOfficeCandidate` and `-AllowPdfCandidate`; its default allowlist rejects
Office files. Membership uses a case-insensitive hash set to avoid quadratic
lookup work for the expanded payload.

Before a formal combined stage, finalize the version under the existing reserved
payload policy. Version **1.1.0** remains tied to its original image/audio/PDF
bytes; **1.1.1** is also reserved. A changed formal payload needs the next
available version.
The scratch test below exercises the packaging path without reserving a product
version or modifying an installed or retained stage:

```powershell
python -B tools/office-engine/Test-OfficePayload.py `
  --engine-directory '<verified scratch worker>/office-engine' `
  --base-payload '<retained image/audio/PDF production stage>'
```

It copies the base to a fresh scratch directory, removes only that copy's stale
inventory receipt, stages Office, and checks the combined production allowlist.
It exercises corruption, missing files/notices, empty-directory membership,
linked paths, host-source mismatch and build/verifier opt-in guards. It also
injects an interrupted copy and a corrupted copy into separate scratch stages.
Both must stop at the damaged host, retain that partial file for inspection,
fail payload verification and refuse to overwrite the same destination on retry.
This is deterministic copy-fault coverage, not a power-loss or disk-failure test.

The retained results identify the source and base paths, full Office selection,
test/build/verifier inputs, native host source pins, runtime lease, inventory
writer and all base-file hashes. Neither the original base nor source engine is
modified. These receipts identify the inspected inputs; they do not reproduce
the native host build or prove renderer execution from the combined scratch copy.


Recorded verification
---------------------

The run at
`.codex-temp/office-payload-tests-4f85fd196c254266978b985713c42a56`
passes all **27 checks**, including complete copied membership, byte identities,
the native junction cases and the combined image/audio/PDF/Office allowlist.
The source engine was the retained worker at
`.codex-temp/office-execution/f19507c697f44c64b149e1b2e2192292/worker/office-engine`.
The base was the reserved 1.1.0 stage
`artifacts/production-staging/169f27e246ce43c4808afa9f99e044a3`.
Final verification confirms unchanged source-runtime identities, base-file hashes
and test-source hashes. `results.json` records the exact checks and scratch
combined payload. No Office process or Windows profile was created.

The later [font-review checkpoint](office-font-review.md) updates the native
host and its source pins. The current full Office payload verifies at
`.codex-temp/office-execution/cd4db5e58f5f414599199aa6b5602677/worker`.
The private repository fixes LF endings for the pinned native source files so
Git checkout preserves their byte identities. The complete payload and thirteen
native exports have separate verification evidence in the font-review record.

The 2026-09-16 run at
`.codex-temp/office-payload-tests-035aa07baebd4d5d83574371c93b024c`
passes all **33 checks** with the current pinned host, including six new
interrupted/corrupted-copy checks. Its Office source is
`.codex-temp/office-execution/d99391e39ffb46649b8f7cf05653408d/worker/office-engine`;
its base is the same reserved 1.1.0 image/audio/PDF payload above. All 19,332
runtime files, host and inventory pass complete copied verification and the
combined production allowlist. Original runtime identities, twelve captured
source inputs and all 117 base files remain unchanged. `results.json` records
the exact selection and paths; each production-entry-point check has its own log.

Both injected copy faults retain the damaged host, stop before copying later
files, fail verification and refuse retry into the existing directory. The
disposable partial stages remain available for inspection. The complete combined
scratch copy intentionally has no inherited formal inventory receipt. No product
version was reserved, production build performed, Office engine executed or
Windows profile created. The retained base contains older managed application
assemblies; this test does not establish that the current application and all
engines have been packaged or executed together. Repository boundary, theme and
165-document checks pass; managed/media execution was not rerun for this test-only
change.


Source-built selection verification, 2026-09-23
------------------------------------------------

The run `.codex-temp/office-payload-tests-ce77a211249c4021a8ff4d8f3785fb92`
passes **39 checks**, including missing and same-length corruption of each of
the three host-adjacent DLLs, exact membership, partial-copy retention, junction
refusal, opt-in guards and the combined production allowlist. The source is
`.codex-temp/office-repaired-application/a38f188ec7ed45c192e64535b64d63e2/application-engine`;
the base remains the read-only reserved 1.1.0 stage. All source/base identities
are unchanged. The combined scratch copy contains historical managed binaries
and is not an executable acceptance result for the current paired selection.

The previous run `office-payload-tests-35afd5114ea445b08d193947f26b8566`
reached the final combined allowlist and failed on `office-engine/msvcp140.dll`.
The Office verifier already pinned and checked all three startup dependencies;
the outer production allowlist now includes only those selected names after
successful Office verification. Extra files remain refused. The failed copy and
log are retained. Historical MSI isolation-copy verification uses a separate
[evaluation record](../tools/office-engine/msi-runtime-evaluation.json) rather
than silently applying the new runtime inventory to old evidence.

The [runtime lease checks](office-runtime-verification.md) and
[actual Word publication/inspection](word-layout-acceptance.md#source-built-application-replay-2026-09-23)
provide separate scoped evidence. The subsequent
[Excel application replay](excel-layout-acceptance.md#paired-application-acceptance-2026-09-23)
passes the four authored chart cases and retains the narrower unsupported-chart
and incomplete-inspection guards.


Reconstructed shape-resource selection, 2026-09-23
-------------------------------------------------

The selected runtime04 ZIP is 223,036,511 bytes with SHA-256
`469821C5CAADA154DBC1E7C7FE277C6C66AE0B004E5E66F0D655BAC9BEDDB8FA`.
It contains 3,836 files totaling 548,517,003 bytes, including the host, inventory
and adjacent CRT files. The paired source-kit04 identities are recorded in
[source-runtime-candidate.json](../tools/office-engine/source-runtime-candidate.json).
The [runtime verification](office-runtime-verification.md#reconstructed-selection-with-shape-resources-2026-09-23)
records the application selection and exact ordinary-document pixel comparison.

`.codex-temp/office-payload-tests-7d733a6fab224eb1990bb949e0878c33` passes
**42 checks**. Three new cases individually remove preset shape geometry,
adjustment names and VML shape types; each incomplete runtime is refused. Existing
corruption, membership, partial-copy, path and production-allowlist checks pass.
The original runtime, reserved 1.1.0 base and captured source inputs are unchanged.
This packaging-only scratch composition retains historical base assemblies; it
is not a new formal candidate or evidence for final combined application execution.


Packaged source identity, 2026-09-23
----------------------------------

`.codex-temp/office-payload-tests-ae684b8c57c74d7eb1f1d36fd94aae01` passes
**48 checks**. Six additions verify the exact packaged source record, reject
missing/corrupt/changed-policy records, preserve a pre-existing record without
starting an engine copy, and require the record through production verification.
Source/base bytes and captured inputs remain unchanged. No engine was executed,
Windows profile created, native build started or reserved package changed.

`python -B tools/office-engine/Test-OfficeSourceRecord.py` adds a fast check without
copying an engine. Its nine checks pass at
`.codex-temp/office-source-record/7539d0da80e84cbba119dd84f35ef69d`: stable checkout
bytes, matching selection, and refusals for a different record version, archive,
inventory, host hash/size or runtime count/size. The packaged record SHA-256 is
`030E76B2CB780240C17C6161EC52FF63FFA062ADBC0E41FB7E1D0847C0254001`.

This supplies durable build provenance for future candidate packages, not a
complete hermetic toolchain inventory, bit-for-bit rebuild claim or distribution
approval. The [source-build record](office-source-build.md#remaining-build-and-adoption-work)
keeps final source delivery, notices, tool/runtime eligibility and adoption open.


Remaining acceptance
--------------------

The later [Python notice checkpoint](office-python-notices.md) requires a pinned
supplemental notice outside the engine and passes 55 payload checks. The runtime
inventory and native binaries are unchanged; historical stages remain untouched.

The historical [combined candidate](combined-office-candidate.md) closed its
earlier formal-build gap; it does not contain this reconstructed engine or record.
A fresh versioned candidate and exact combined execution remain required.
This checkpoint does not resolve the known early-1900 Excel date errors, broaden
supported Office variants, prove font/layout fidelity, complete network-isolation
evidence or approve engine redistribution. Representative image/audio/PDF/Office
execution and recovery must be verified against the actual staged worker, without
treating a successful build as proof of complete acceptance. No visible UI, assistive technology,
theme/DPI, installer lifecycle, live commerce or signing acceptance is added.
