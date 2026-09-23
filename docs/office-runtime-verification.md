Office Runtime Verification
===========================

Status: candidate payload leases verified; subsequent worker, command and
[optional staging](office-production-payload.md) integration are recorded
separately. Expanded release acceptance remains incomplete.

The private runtime verifier now pins the source-built integration candidate
described below before returning a lease. The historical MSI-based selection
hashed all 19,332 runtime files (1,517,294,910 bytes), checking membership including
the three expected empty package-cache directories, and rejects linked entries.
The source is the independently prepared candidate, never `reference/`.

The preceding MSI-based host was 112,640 bytes with SHA-256
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
That historical canonical UTF-8 inventory is 2,276,994 bytes with SHA-256
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
unexpected sibling files, directories and links are
refused. A colocated runtime directory is allowed and retains the complete
runtime verification above. Separate runtime storage remains supported for the
isolated component harness.

The source-built selection adds exactly three app-local startup dependencies,
with pinned sizes/hashes and read leases acquired before host membership checks.
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


Source-built application selection, 2026-09-23
----------------------------------------------

The paired selection now uses the reviewed source-built renderer and a newly
built 112,640-byte application host with SHA-256
`3F5254F6D6188F5B3637C13BFF29520750073D1F88EA3687630EB4DB5E870059`.
The host loads the renderer entry point from `sofficeapp.dll` and Windows loop
exports from `vcllo.dll`, using the existing restricted loader flags. The host
build is `.codex-temp/office-host/5193108da1ce449c964cb429295accfa`; its receipt
binds all four native source/build inputs. The historical host is not relabeled
as a build of the changed source.

The 486,131-byte inventory has SHA-256
`C0FC7304D818A1A3E6E1F87C72556C2079F5DCD01BA32CC25ED4E4C66DB6CDD4`
and pins 3,828 runtime files totaling 545,259,841 bytes. The three package-cache
directories remain required. `msvcp140.dll`, `vcruntime140.dll` and
`vcruntime140_1.dll` are pinned beside the host as well as in the runtime.
The [packaging selection](../tools/office-engine/payload-candidate.json) records
their exact sizes and hashes. No system runtime installation is performed.

The final focused replay at
`.codex-temp/office-runtime/12df574f3445457288a5dec7880825d2` passes **69 checks**.
It covers missing/corrupt startup dependencies, retained read sharing, released
handles after failure/success, unexpected host entries and actual colocated
package membership. Source, managed and inventory identities remain unchanged.
The preceding 69-check run `5ed1d6762b8c491c96f32aa7bc956f26` predates recording
both selection JSON identities in the public runner.

The [Word layout replay](word-layout-acceptance.md#source-built-application-replay-2026-09-23)
also passes actual application publication and independent PDF inspection.
This is isolated application integration, not completed engine adoption:
the native rebuild, recovery, affirmative network enforcement, redistribution
and final-candidate replay remain open. The subsequent
[Excel application replay](excel-layout-acceptance.md#paired-application-acceptance-2026-09-23)
passes all four authored chart cases and narrows refusal to newer/external charts
and incomplete inspection. Reserved 1.1.0/1.1.1 payloads are untouched.


Reconstructed selection with shape resources, 2026-09-23
--------------------------------------------------------

The current isolated application selection uses reconstructed runtime04 and
source kit04 from the [source build](office-source-build.md#ooxml-shape-data-packaging-repair).
The 486,450-byte inventory has SHA-256
`738D1AD2B320A3E1FF6D6E3173CD6B25D5E70C8B707E3B1EBD79220FD0BD738A`
and binds 3,831 runtime files totaling 547,045,673 bytes. The application host,
three adjacent CRT dependencies and required empty directories are unchanged.
Both public packaging records and the private runtime lease select this identity.
Its fresh repository copy is
`.codex-temp/office-repaired-application/8825206a1d11484ca725f28992a67c91/application-engine`.

Runtime verification at `.codex-temp/office-runtime/a91448f9729d4d1fa62a6fdacf4c60f0`
passes **69 checks** with unchanged inputs. The actual application replay at
`.codex-temp/office-execution/45172c40ea494afabd97ab556b49910e` passes **43 checks**:
ordinary Word/Excel/PowerPoint copy publication, collision handling, publication
failure, pre-publication cancellation and post-publication cleanup retry. All six
profiles and contexts are retired; original bytes/times and captured inputs are
unchanged. Release worker and contract builds have zero warnings/errors.

The independent inspection `inspection-521af1fce8b94d4da1c2f52b8f24622e` in that
run passes structure, authored text, geometry and exact historical control pixels
for all three PDFs. This closes the ordinary application PowerPoint fill defect
without relaxing the oracle. The [four-case Excel chart replay](excel-layout-acceptance.md#reconstructed-application-replay-2026-09-23)
also passes through the current worker and unchanged independent oracle.
The [PowerPoint layout replay](powerpoint-layout-acceptance.md#reconstructed-application-replay-2026-09-23)
retains the historical font-size tolerance failure with identical output rasters
and glyph observations. Earlier failed outputs remain retained. The subsequent
[native interruption](office-worker-lifetime.md#reconstructed-runtime-interruption-replay-2026-09-23)
and [application startup recovery](office-startup-recovery.md#reconstructed-runtime-application-recovery-2026-09-23)
replays pass on this identity, including exact following-output comparisons.
The [six-case calculated-date replay](excel-calculated-date-guard.md#reconstructed-calculated-date-replay-2026-09-23)
also preserves the expected copies and protective refusals. Broader layout,
remaining safety requirements and the final combined candidate still require
their checks. No product
version, installed application or reserved payload changed. The later
[packaged source record](office-production-payload.md#packaged-source-identity-2026-09-23)
provides durable provenance beside the unchanged engine; redistribution and
adoption remain open.


Repeatable verification
-----------------------

From the public repository, with the private checkout and restored dependencies:

```powershell
python .\tools\office-engine\Test-OfficeRuntime.py `
  --package '<retained repaired-application or execution directory>/office-engine'
```

This creates a new `.codex-temp/office-runtime/<guid>` evidence directory, reads
the pinned inventory, builds the private contract harness and runs it. Historical
MSI copy/build receipts cannot select the new runtime. Only
authored scratch fixtures and host/inventory copies are changed. The retained
runtime is read, hashed and opened for sharing assertions without writing bytes.
No Office process, Windows profile, installation or Explorer change is involved.

The historical run at `337d125099524baea180562d7a69bae3` passes **30 checks** with a
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
