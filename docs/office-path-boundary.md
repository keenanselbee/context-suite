Office Host Directory Boundary
==============================

Status: native boundary reproduced and host manifest corrected; real worker
interruption replay exposed a separate cleanup failure

The [startup investigation](office-worker-interruption.md) was caused by a
specific directory-length boundary, not the authored document size. Every
retained failure lacked an extension registry backend directory whose absolute
Windows path was exactly 248 characters. The successful ordinary contexts did
not place any of those backend directories at that length.

A scratch-only debugger observed newly launched owned diagnostic processes,
forwarded exceptions to their handlers and changed no memory or registers.
Distinct exception recording identified a wrapped folder-creation failure in
the bundled or temporary extension cache. Bounded decoding of the generated UNO
exception fields then identified the owned registry URI and `NOT_EXISTING_PATH`.
The reported parent existed; the missing backend beneath it was 248 characters.
The initial decoder omitted derived-class padding and returned no URI. Its
corrected layout accounts for the pinned x64 generated exception types.

The engine's [Windows URL conversion](https://raw.githubusercontent.com/LibreOffice/core/libreoffice-26.2.6.3/sal/osl/w32/file_url.cxx)
leaves paths through 248 characters unprefixed and adds an extended prefix above
that boundary. Its [directory creation](https://raw.githubusercontent.com/LibreOffice/core/libreoffice-26.2.6.3/sal/osl/w32/file_dirvol.cxx)
then calls `CreateDirectoryW`. The host previously lacked a long-path-aware
manifest. This explains why shortening a context did not reliably fix startup:
a different fixed backend name could land on the same boundary.


Change and native evidence
--------------------------

The native host now embeds `longPathAware=true`; the builder snapshots that
manifest alongside its source and linker configuration. The new host is 104,960
bytes with SHA-256
`6C2300992201F054E28E7B72715304CA63E0AABEFA909CF7D356D3F275D77677`, built at
`.codex-temp/office-host/5d1d22b353764d3c8f4ab244ad5fdc08`. Extraction of its actual
embedded resource confirms the setting. The private runtime verifier pins this
new identity; the engine payload and runtime inventory are unchanged.

`python tools/office-engine/Test-OfficePathBoundary.py` builds two independently
authored probes from the same source, one with the host manifest and one without.
At `.codex-temp/office-path/429a89ea4ee84192b5768e1ce3c382de`, **24 assertions** pass:

- The legacy probe creates a 247-character directory but fails unprefixed paths
  of 248, 249, 260, 261 and 320 characters.
- The manifested probe creates every tested unprefixed directory.
- Explicit extended paths succeed in both probes; created directories are removed.

Both probe builds and the host build pass with zero warnings/errors. No Windows
profile, installer, Explorer registration or system setting is changed by this
boundary test. Each component stays below the filesystem's component-name limit.

The debugger evidence roots under `.codex-temp/office-worker` are
`0067ca5ef0434390a55571ed02d9a2a5`, `f09a6944d1704767ac690cee04491984`,
`83ccda26fdac423c87fc7f4c2ffc016d` and `878b13734e444adcafe71bf271d08aab`.
The first three contain ordinary/interruption pairs; the last captures the exact
URI for the ordinary case. All seven diagnostic profile receipts report removal.
Debugger timing is not startup-performance evidence.

The first manifest-enabled replay at `862f6b2846834c1084fb2f5f4dd43d6f` reached
growing output from the original 96-page Word fixture and canceled execution.
Its harness then incorrectly requested exclusive source access before disposing
the application's intentional safety lease. The corrected assertion runs after
process termination and profile-owner disposal. This failed test remains retained;
it is not a passed interruption matrix.

The subsequent replay at `bae70af149c640678bfda6a338182da6` completes Word and
Excel cancellation and following exports using the original 96-page fixtures.
Both retained native handles were signaled when the client returned; recorded
stop durations were about 304 and 331 milliseconds. These are individual
observations, not performance guarantees or independent PDF acceptance.
PowerPoint then fails while checking profile children during grant revocation,
with Windows sharing error 32. Four profile receipts report removal; the fifth
records retained ownership. The run is failed, not a completed matrix.

On resumption no test processes remained. All 331 entries beneath that engine
profile could be opened with the same access/share flags. The transient lock's
owner and exact entry were not captured. The recorded profile name, SID and
Windows mapping agreed; only the profile-directory grant remained. A scoped
scratch recovery reused the owner's checked path/child/ACL cleanup, removed only
that recorded grant and Windows profile, and verified all 332 ACLs against their
unrelated entries and all 116 file hashes. The original failed receipt remains;
`824b4709e4a9430ebab1df50f1429ddc-recovery.json` records the later removal.
This manual recovery does not implement production crash recovery.

Grant-entry failures now retain the exact path and native error in diagnostics.
The interruption harness writes stop observations before profile disposal so
cleanup failure cannot erase the current stop's evidence. No retry, relaxed
sharing mode or resource-limit change is introduced by these diagnostics.

The focused ownership suite now passes **53 checks** at
`.codex-temp/office-owner/a6a9dd532bd442d9818fc45742e89abc`, including an
intentionally locked new cache file. Cleanup reports that exact entry and error
32, keeps the Windows profile and unresolved grant, and succeeds through the same
owner after the lock is released. The test verifies preserved bytes and removed
access. This demonstrates recoverable ownership; it does not identify the earlier
PowerPoint lock holder or add automatic retry to the application.

The new host pin separately passes **30 runtime contracts** at
`.codex-temp/office-runtime/4274f9799486426ea2c2f52d6ca185f8/contracts`.

The diagnostic replay at `76e29f79508642e589dfff14645ce425` then completes Word
cancellation/recovery and records Excel cancellation before another sharing-error
cleanup failure. Its retained observation confirms the native handle was signaled
at client return. The failing path is a generated Office temporary file beneath
the isolated profile's redirected `Packages/.../AC/Temp` directory. A later
extended-path Windows attribute query reports file-not-found while its parent
still exists; the lock holder remains unknown. Scoped recovery removes the exact
recorded Excel profile and verifies 332 ACLs and 116 file hashes, retaining the
failed receipt beside `3bd9e463c0d84a97a61d31cd924c9010-recovery.json`.

The owner now supports [bounded asynchronous sharing-conflict retries](office-profile-ownership.md#asynchronous-sharing-conflict-recovery).
Its focused 56-check suite covers persistent-lock exhaustion, retained ownership
and successful cleanup after lock release. The real-worker harness awaits that
cleanup. Replay `556f332dfd514885bd3e00575a710fa4` passes all three cancellation
and following-export cells. Independent inspection at
`contracts/inspection-f07ed84c8bea43eaa13afdc534f49bba` passes qpdf structure,
authored text, geometry and exact PDFium control pixels for the three recovery
PDFs. The run then fails before its first worker-loss stop because the test
observer dereferenced unavailable process main-module information. All seven
profiles are verified absent; the failed overall run is preserved.

The observer now waits for that information without accepting a process whose
executable identity is unknown. The runner also accepts `--mode cancel`,
`--mode worker-loss` or `--mode deadline`, each requiring all three families and
30 checks. Its default remains the whole nine-cell, 90-check matrix. Selecting
remaining modes does not promote a partial run to a full pass. Worker-loss
replay `ccfb22fe4f7047cd86f8fdfeaec8b3c2` and the subsequent deadline replay are
reconciled in the follow-up below.

The later [worker-lifetime reconciliation](office-worker-lifetime.md) completes
the mode replays and PDF inspection, but rejects all three worker-crash return
boundaries: the client returned before the host exited. Application-owned job
tracking now addresses that separate lifecycle gap. The follow-up records all
nine corrected return-boundary cases, independent recovery-PDF inspection and
broader image/audio/PDF regressions. Earlier per-mode passes must not be read as
full acceptance; use the corrected source-bound evidence in that follow-up.


Remaining platform and workflow scope
-------------------------------------

This machine already has `LongPathsEnabled=1`. Microsoft documents that both the
[system policy and application manifest](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation)
are needed for the unprefixed long-path behavior. The test reads that value and
refuses the comparison when it is disabled; it never writes the registry.
Windows with that policy disabled still needs explicit production-context and
engine-path acceptance. Do not silently change a customer's system policy or
claim that this manifest proves universal long-path support.

Complete the original nine active interruption/recovery cells and independent
inspection of their recovery PDFs. Production context journals, app-loss recovery,
validation/publication, broader document admission and customer Office commands
remain separate work. Reserved production candidate 1.1.0 is unchanged.
