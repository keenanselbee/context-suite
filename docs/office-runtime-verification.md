Office Runtime Verification
===========================

Status: candidate payload leases verified; worker routing and customer Office
conversion remain incomplete

The private runtime verifier pins the evaluated native host and the complete
LibreOffice 26.2.6.3 file inventory before returning a lease. It reads and hashes
all 19,332 runtime files (1,517,294,910 bytes), checks exact membership including
the three expected empty package-cache directories, and rejects linked entries.
The source is the independently prepared candidate, never `reference/`.

The host is 103,936 bytes with SHA-256
`8DFF459F2F10F82B5DD82EA887FC4FD07F15DDAB45372C38202B6E4A4508E060`.
The canonical UTF-8 inventory is 2,276,994 bytes with SHA-256
`70DAF53038F8B4E8B5FDF74B6877393616094762C0E0BDC5245D657C16DBCD2D`.
Each line is relative path, byte count and uppercase SHA-256, separated by tabs;
paths are sorted ordinally and lines end with LF. The inventory writer accepts
only the retained candidate's exact inventory and upstream archive identity.
This is not a facility for selecting arbitrary engine versions or executables.


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
