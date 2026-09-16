Office Production Candidate Payload
===================================

Status: explicit isolated staging and verification implemented; 27 scratch
packaging checks pass. Formal combined staging, redistribution review and release
adoption remain open.

The build can now include the independently prepared Office candidate alongside
the image/audio/PDF engines. This is an optional packaging path for the existing
[Office command](office-direct-command.md), not a new engine selection or a
change to the retained 1.1.0 package. Office fidelity and isolation acceptance
remain separate requirements under the [launch matrix](launch-capability-matrix.md).


Pinned input and layout
----------------------

[The selection record](../tools/office-engine/payload-candidate.json) pins the
110,592-byte long-path-aware native host and the canonical 2,276,994-byte runtime
inventory already enforced by the [private runtime lease](office-runtime-verification.md).
The inventory's fixed SHA-256 identifies all 19,332 vendor files and their sizes
and hashes, totaling 1,517,294,910 bytes. Staging checks the upstream archive pin
against the independent evaluation record and the host against four current
private source/build-input hashes. It does not copy private source or build receipts.

The input is a complete `office-engine` directory retained from an isolated
worker evaluation, containing the host, `runtime-files.txt` and `runtime/`.
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
bytes; a changed formal payload needs **1.1.1** or the next available version.
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
linked paths, host-source mismatch and build/verifier opt-in guards. Its retained
results identify test-source and base-payload hashes. Neither the original base
nor source engine is modified.


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
Git checkout preserves their byte identities. The older 27-check matrix above
was not rerun; the current complete payload and thirteen native exports have
separate verification evidence in the font-review record.


Remaining acceptance
--------------------

This checkpoint does not resolve the known early-1900 Excel date errors, broaden
supported Office variants, prove font/layout fidelity, complete network-isolation
evidence or approve engine redistribution. A fresh formal combined payload and
representative image/audio/PDF/Office execution and recovery checks against its
actual staged worker remain required. No visible UI, assistive technology,
theme/DPI, installer lifecycle, live commerce or signing acceptance is added.
