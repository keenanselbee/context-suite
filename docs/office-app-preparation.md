Office Application Preparation Recovery
=======================================

Status: all 55 actual-app preparation recovery checks pass in the complete
default layout after correcting the native profile environment. The following
Word export succeeds at the original failing path length. Broader fidelity,
visible acceptance and expanded packaging remain separate gates.

The [application test host](../tests/ContextSuite.Application.TestHost/OfficeAppLifecycleContracts.Preparation.cs)
kills an actual preparation owner before copying, after a partial write, or after
blocked cleanup has recorded retirement intent. It then starts the actual App
dispatcher/router with isolated settings and trial storage. The locked case
holds the partial snapshot against deletion while startup recovery runs.

Following Analyze and Convert requests go through separate application processes
and the real per-user router. Successful recovery must show retry guidance,
remove the recorded context, preserve originals and permit subsequent work.
The locked case must keep Analyze usable while recovery is pending, block Office
conversion with review guidance and retain the partial snapshot. Closing the app
releases the test obstruction; the parent verifies subsequent cleanup. No command
is resumed automatically, and no Explorer registration is changed.

The partial-copy case additionally exports Word to a PDF through application
admission, native isolation, worker execution, independent PDF validation and
copy publication. A valid trailing XML comment in the existing document part
makes the disposable source exceed one copy buffer without changing its visible
content or adding undeclared package parts.


Running the focused checks
--------------------------

Run [Test-OfficeAppPreparation.py](../tools/office-engine/Test-OfficeAppPreparation.py)
with `--create-disposable-profiles`, `--worker` pointing to an independently
staged worker, and `--fixture` pointing to the authored DOCX fixture. Both inputs
must be inside repository scratch. The Word export creates a disposable native
profile; the preparation-crash cases themselves create no Windows profile.
This still requires the standing native-test authorization. Close other Context
Suite instances and do not invoke Explorer commands while the test runs.

The default is `--case all --layout full`. Individual case names are
`preparation-before`, `preparation-partial`, `preparation-retiring` and
`preparation-locked`. `--layout compact` retains the same tests and document but
uses shorter scratch directory names for a controlled path comparison. It is
not an application storage migration or a workaround silently enabled for users.

The wrapper records source, fixture, application and complete worker hashes,
build output, child logs and unchanged-input status. Case receipts record actual
checks, window types shown and elapsed time. Failure codes are recorded in the
isolated case directory when the application receives a media-worker exception.


Verified full-layout recovery
----------------------------

The corrected-worker run at
`.codex-temp/office-app-preparation/c7d13881daeb4e40a5e7ff2d9c4da392` exits zero,
records unchanged sources and binaries, and builds the Release test host with
zero warnings/errors. Its actual-app receipts are in
`.codex-temp/office-app-lifecycle/31b35b1e632a4b52a49d41f24b6f5dd3`:

| Case | Checks | Elapsed seconds |
| --- | --- | --- |
| Before copy | 14 | 2.95 |
| Partial copy and following Word export | 14 | 36.98 |
| Interrupted retirement | 14 | 2.97 |
| Locked partial copy | 13 | 6.44 |

This is one passing `--case all --layout full` run, including the 184-character
Office context path. It preserves original bytes and timestamps, publishes the
following named PDF copy, and verifies empty context storage after successful
work or release of the deliberately locked snapshot. Analyze remains available
while cleanup is pending; Office conversion waits or reports the retained review
requirement. Closing the application completes its bounded recovery lifetime.

These programmatic checks do not establish rendered layout, keyboard usability,
screen-reader delivery, theme/DPI behavior or installed-shell acceptance. The
following PDF receives production validator checks; this run adds no independent
text/pixel comparison or broader Office fidelity matrix. Packaging remains unchanged.


Earlier failing and comparison runs
----------------------------------

These are individual receipts, not one passing full-layout suite:

| Case | Actual checks | Lifecycle receipt beneath `.codex-temp/office-app-lifecycle/` |
| --- | --- | --- |
| Before copy, full layout | 14 | `2bfa403a88694b8286357b044b20f5dd/preparation-before/lifecycle.json` |
| Partial copy and following Word export, compact layout | 14 | `2c9fcd16e4a6/partial/lifecycle.json` |
| Interrupted retirement, full layout | 14 | `8734f4e6b2694008ac637e62905ef17a/preparation-retiring/lifecycle.json` |
| Locked partial copy, full layout | 13 | `773a1f5015e0421ea8bf0336148c81ad/preparation-locked/lifecycle.json` |

The compact Word case completes in 38 seconds with a validated PDF copy and an
empty Office context root. The original document bytes and timestamp remain
unchanged. The other successful cases finish in approximately 2.6-6.3 seconds.
Application builds complete with zero warnings or errors.

The full-layout runs at `2bfa403a88694b8286357b044b20f5dd` and
`3988b52566354f75b1c9149633310e6f` recover the abandoned partial copy but fail on
the following Word export after approximately two minutes in the Office host.
The first fixture added an undeclared padding part; the second uses the corrected
XML-comment fixture and still fails. Both failed export profiles and mappings
are confirmed absent, and their context roots are empty. The generated context
path is 184 characters in the longer layout and 152 in the successful comparison.
Those attempts implicated path length without establishing the failure stage.
The diagnosis below identifies the incorrect native environment mapping; the
accepted run above retains the 184-character reproduction.

A further corrected-fixture full-layout run at
`707fdb3847de4851aa79d51edb2ffcaf/preparation-partial` reproduces the failure after
the compact success. Its `worker-failures.log` records `TimedOut` at
`2026-09-16T01:15:25Z`. Wrapper receipt
`.codex-temp/office-app-preparation/7eb9b690cb264c1e901b416827beecf2/exit.json`
records a nonzero application exit with unchanged inputs. The three failed full-layout attempts
are retained as failures. Do not infer an exact supported maximum from the two
tested path lengths or weaken the native isolation policy to make them pass.
The final read-only Windows inventory reports zero Context Suite Office profile
folders, zero profile mappings and zero test application/worker/Office-host
processes. The last failed case's Office context root is empty.

These earlier default runs remain failed evidence. The correction does not add
a production path restriction, shorten application storage or replace the full
test layout with the compact comparison.


Native profile environment diagnosis
------------------------------------

The later probes locate the stall before the main Office library is loaded. The
unchanged host's existing stage marker remains `configuration`; `sal3.dll` is
loaded and `mergedlo.dll` is absent. Bounded samples of the busy thread locate an
instruction inside Windows `GetTempPathW`; stack memory contains DbgHelp code
addresses. These address candidates are not a fully unwound backtrace. Evidence
is under `.codex-temp/office-app-preparation/a83f514c04b644799ea4873f468179af`.
The observer briefly suspends only the named disposable host thread for context
capture and resumes it immediately. It changes no executable or process memory.

Inspecting the child environment explains the incorrect path: the caller supplied
the 192-character operation profile as `LOCALAPPDATA`, and Windows expanded that
into `<operation-profile>/Packages/<native-profile-name>/AC`, 268 characters long
in the diagnostic. Changing `TMP`/`TEMP`, then also `USERPROFILE`, did not fix
that mapping. The exact redirected path and the failed attempts are recorded in
`.codex-temp/office-path-diagnostics/01c2b37654f9437794c404e84ed85016` and
`.codex-temp/office-app-preparation/5c0a120ea53849a8a00b071734dfe8cb`.

Microsoft documents that [AppContainer creation](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer)
provides its profile through `LOCALAPPDATA`. Its
[temporary-path API](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-gettemppath2w)
does not itself establish that the returned directory exists or is accessible.
The adapter now supplies the real local-app-data base so Windows can derive the
existing native profile correctly. It leases that profile's ordinary `AC/Temp`
directory before launch. The earlier trial changes to `TMP`/`TEMP` and
`USERPROFILE` are removed. The explicit engine profile, original/candidate paths,
five grants, native capabilities, job limits and network restrictions stay intact.
The native profile remains owned and removed by the existing coordinator.

The corrected adapter exports the same XML-comment fixture successfully with a
184-character context path. Source-bound receipt
`.codex-temp/office-path-diagnostics/72bb56985d6e42e893f41c3f0da5a744` records exit
zero and unchanged inputs. Its acknowledged 44,319-byte PDF candidate and source
bytes/timestamp checks pass; the native profile removal receipt is complete.
This diagnostic does not publish a PDF or independently inspect its text/pixels.
The subsequent full-layout actual-app acceptance is recorded above.


Multi-family regression and cleanup
-----------------------------------

The corrected worker is retained at
`.codex-temp/office-execution/96d5edf8343543e3aaa1d6bce88cef7e/worker`.
The [direct command regression](office-direct-command.md) at
`.codex-temp/office-execution/cb8320577db3481aabe99f9fb1fcb3c0` passes all **15
checks**, exits zero and verifies unchanged source, fixture and binary inputs.
Five exports cover a mixed image/Word/Excel/PowerPoint command and deliberate
Word publication failure followed by one successful retry. All originals retain
their hashes and modification times. All five native profiles, mappings and
generated contexts are removed.

The same run's `inspection-5e585ae2cf144f05a9e5343d51a13c20` passes independent
structure, authored text, page geometry and exact control-pixel comparison for
the three normal Office PDFs. These are the existing authored controls, not new
variant or hostile-content coverage. Worker and contract-host Release builds
complete with zero warnings/errors.

The final Windows inventory records zero Office profile folders, zero mappings
and zero test application/worker/Office-host processes. The preceding **3,532
foundation contracts** remain matched to all 266 recorded source files in
`.codex-temp/office-preparation-foundation-f783e13187544accaf6adf576c9c6a8a`;
they were not rerun for this private environment correction. The current native
exports and application checks provide the new runtime evidence. No formal
production payload, installed state or visible acceptance was changed.
