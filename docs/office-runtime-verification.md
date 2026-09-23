Office Runtime Verification
===========================

Status: candidate payload leases verified; subsequent worker, command and
[optional staging](office-production-payload.md) integration are recorded
separately. Expanded release acceptance remains incomplete.

The private runtime verifier pins the evaluated native host and the complete
LibreOffice 26.2.6.3 file inventory before returning a lease. It reads and hashes
all 19,332 runtime files (1,517,294,910 bytes), checks exact membership including
the three expected empty package-cache directories, and rejects linked entries.
The source is the independently prepared candidate, never `reference/`.

The currently pinned host is 112,640 bytes with SHA-256
`C01466D70277C56D8AD43E3C9DCAFE2676E3644B3473197C1EAFAFA14F757765`.
The earlier font-reporting host is 110,592 bytes with SHA-256
`D8FE02337D606C3ECD17F3919F7D6F1208BB3085A142339A7ECFAC69100F5662`;
see [Office font review](office-font-review.md). The preceding long-path-aware
host was 104,960 bytes with SHA-256
`6C2300992201F054E28E7B72715304CA63E0AABEFA909CF7D356D3F275D77677`.
The original verifier evidence below used the preceding 103,936-byte host,
`8DFF459F2F10F82B5DD82EA887FC4FD07F15DDAB45372C38202B6E4A4508E060`.
See the [interruption investigation](office-worker-interruption.md) for the
intermediate diagnostic host and the [directory-boundary correction](office-path-boundary.md)
for that manifest, evidence and remaining platform limitation.
The canonical UTF-8 inventory is 2,276,994 bytes with SHA-256
`70DAF53038F8B4E8B5FDF74B6877393616094762C0E0BDC5245D657C16DBCD2D`.
Each line is relative path, byte count and uppercase SHA-256, separated by tabs;
paths are sorted ordinally and lines end with LF. The inventory writer accepts
only the retained candidate's exact inventory and upstream archive identity.
This is not a facility for selecting arbitrary engine versions or executables.

The subsequent [runtime performance work](office-runtime-performance.md) reuses
verified directory handles within acquisition while preserving every file hash
and final file-handle check. It also adds redirected-cached-directory tests and
the retained scratch-package verification command.


Lease boundary
--------------

Keep the verifier alive through execution. It holds read-only file handles with
read sharing, plus ancestor and runtime-directory handles with directory-list
access and no delete sharing. Ordinary file writes, replacement and directory
renaming are blocked while those handles are held. Verification failure disposes
all acquired handles; successful callers must dispose their lease as well.

Directory handles do not prevent creation of new child entries. The caller must
recheck exact membership after execution before accepting completion. The lease
does not claim protection against hostile full-trust processes or previously
created writable memory mappings. It neither grants AppContainer access nor
creates a profile, admits a document, launches Office or validates/publishes PDF.
Combine it with the [application profile owner](office-profile-ownership.md) and
[managed process boundary](office-managed-launcher.md) during worker integration.

The host directory is also checked before runtime acquisition and during every
membership recheck. The pinned host and inventory must share a directory;
unexpected sibling files (including startup DLLs), directories and links are
refused. A colocated runtime directory is allowed and retains the complete
runtime verification above. Separate runtime storage remains supported for the
isolated component harness. The existing host/runtime pins are unchanged.

This establishes a closed host-file list before adding the repaired renderer's
three app-local startup dependencies. Those DLL identities and read leases must
be added together with the paired runtime selection; this checkpoint does not
adopt them or claim that the repaired renderer is ready for application use.
As with runtime directories, post-acquisition additions can be detected at the
recheck; directory handles do not prevent creation of a new child.


Host directory verification, 2026-09-23
--------------------------------------

The focused replay at
`.codex-temp/office-runtime/4585b9f9876c40c2b4906e897f305ef2` passes **54 checks**
with zero Release build warnings/errors and unchanged recorded inputs. It
verifies the full pinned runtime both with an isolated host copy and in the
actual colocated scratch package. New cases refuse an unlisted startup DLL,
refuse a valid inventory stored outside the host directory, verify released
handles after refusal, and detect a DLL or directory inserted during a lease.
Restoring the exact host directory restores successful membership verification.
The harness never executes these authored files or the Office host.

The public runner now records the shared `RuntimeDirectoryLeases.cs` identity
alongside its Office wrapper and requires the actual colocated-package assertion
when invoked with `--package`. Its managed build uses one MSBuild worker.
The first baseline replay at `cafd8794082e47d89164a2748c821893` failed on a stale
test expectation for the shared path validator's diagnostic. The expectation
was corrected to its current exact message; path admission was not relaxed.
The intermediate 53-check run at `f3614f8972fb46a399cf9a573ea85b16` passed before
the final actual-package assertion was added. All receipts remain retained.

This is runtime-integrity evidence, not rendering or performance acceptance.
The native source build was active during these checks. Repaired-engine
selection, startup dependency pins/leases and full application replay remain
required; the existing chart refusal is unchanged.


Repeatable verification
-----------------------

From the public repository, with the private checkout and restored dependencies:

```powershell
python .\tools\office-engine\Test-OfficeRuntime.py `
  --copy-receipt <retained-office-copy.json> `
  --host-receipt <retained-office-host-build.json>
```

This creates a new `.codex-temp/office-runtime/<guid>` evidence directory, writes
the pinned inventory, builds the private contract harness and runs it. Only
authored scratch fixtures and host/inventory copies are changed. The retained
runtime is read, hashed and opened for sharing assertions without writing bytes.
No Office process, Windows profile, installation or Explorer change is involved.

The final run at `337d125099524baea180562d7a69bae3` passes **30 checks** with a
Release build reporting zero warnings and errors. Checks include same-length
host/inventory corruption, missing files, unexpected files and empty directories,
missing directories, cancellation before acquisition, exact runtime membership,
blocked file writes/directory renaming, and resource release after failure and
success. `build.json` records source and managed-payload hashes;
`contracts/results.json` records the actual assertions and runtime identity.
The earlier `cd8e36b1d1654da19542e4e1fcac53f7` run failed because its test did not
catch the expected `InvalidDataException`; it is retained as a failure. A prior
24-check run predates the final authored membership cases and host-size bound.

This checkpoint does not rerun native Office exports or expand their existing
acceptance claims. Production context construction, typed worker requests,
independent PDF validation, transactional copy publication, application-crash
recovery, calculation/font decisions, content/network enforcement and runtime
adoption remain open. Reserved candidate 1.1.0 is unchanged. No visible UI,
installed lifecycle, screen-reader, theme/DPI or commerce acceptance is claimed.

The subsequent [typed worker integration](office-worker-export.md) consumes this
lease during actual exports and shares its directory-handle implementation with
operation-path identity checks. Its context/publication and acceptance limits are
recorded separately from this original verifier checkpoint.
