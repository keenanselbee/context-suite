Office Process Isolation Evaluation
===================================

Status: native lifetime/control and resource-limit preflight passed; AppContainer file/network
matrix prepared but not executed with a registered profile. No production
isolation claim and no Office document execution in this probe, updated 2026-09-14.

Purpose
-------

Required Office-to-PDF rendering needs stronger execution isolation than the
existing profile/headless flags. The [passive engine experiment](office-engine-evaluation.md)
does not provide it. This independently authored native experiment tests the
operating-system boundary separately before admitting hostile documents.

Windows [AppContainer isolation](https://learn.microsoft.com/en-us/windows/win32/secauthz/appcontainer-isolation)
provides access control for files, processes and networks. Microsoft's
[launch documentation](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer)
describes a per-user profile plus process security attributes and capabilities.
The candidate requests no network or filesystem capabilities; it grants only
explicit scratch ACL entries. Ordinary AppContainers can still read some shared
system resources. This is not a claim that every path outside scratch is unreadable.

Implemented experiment
----------------------

[Test-OfficeIsolation.ps1](../tools/office-engine/Test-OfficeIsolation.ps1) uses the
repository's existing Visual Studio/CMake discovery pattern and builds a static-CRT
native probe into fresh `.codex-temp/office-isolation/<guid>` scratch. No external
binary or document is accepted. All native code is independently authored.

The launcher creates its child suspended, allows inheritance only of empty input
and the diagnostic pipe, assigns an owned job, then resumes it. The job sets an
eight-process limit, 512 MiB per-process and 1 GiB aggregate memory limits, and
kill-on-close. It limits diagnostics to 64 KiB and normal execution to 30 seconds.
Cleanup explicitly terminates the job and checks that active process count reaches
zero within five seconds, including when the launcher itself already exited.
The initial preflight configured resource limits; the follow-up below now tests
commit-memory and process-count enforcement. This native experiment has not
replaced a shipping process launcher. The later, separate
[Office evaluation launcher](office-process-lifetime.md) now uses creation-time
job assignment and has its own lifetime contracts plus passive export evidence.
Neither launcher establishes the pending AppContainer access boundary.

Passed preflight tests:

- An unrestricted control can open both generated readable/withheld fixtures,
  request both write handles, and connect to the generated IPv4 loopback listener.
  The token reports it is not an AppContainer.
- A launcher spawns a sleeping descendant and exits. The job retains the
  descendant, then terminates all remaining members. Windows may add a console
  host, so the contract does not assume an exact helper-process count.
- A sleeping child exceeds a 250 ms test deadline; cleanup observes zero remaining
  job members.
- Excessive diagnostic output triggers the 64 KiB limit and the same complete
  owned-job cleanup.

Preflight evidence is retained under
`.codex-temp/office-isolation/954245986d184261815ef2fff8a24f63/case`:
`control.json`, `orphan.json` and `lifetime.json`. The combined attempt then failed
at AppContainer process creation with Windows error 2. Its log is
`.codex-temp/office-isolation-preflight-final.log`. Earlier test iterations exposed
a diagnostic-drain race after process exit and an incorrect exact process-count
assumption; both are corrected in the current probe. Those runs remain retained.

The final default preflight passes under
`.codex-temp/office-isolation/2e35174d4007452c85a8c582f1207287`, with build/source
hashes in `build.json` and log `.codex-temp/office-isolation-default.log`. It
explicitly skips the AppContainer access matrix. The x64 native build uses
warnings-as-errors and passes; no Office, product or UI regression is implied.

Resource enforcement follow-up (2026-09-11)
------------------------------------------

The default native preflight now runs three resource tests with matching positive
controls. Test-only budgets are smaller than the normal launcher defaults, so
the test does not need to exhaust the machine's available memory. `VirtualAlloc`
requests committed private memory; these are commit limits, not physical working
set measurements. Windows documents separate
[process and job commit limits](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_extended_limit_information)
and an [active-process limit](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_limit_information).

| Test | Positive control | Restricted result |
| --- | --- | --- |
| Per-process commit | A 128 MiB process limit allows a 64 MiB allocation | A 32 MiB limit refuses it with error 1455; measured private commit stays unchanged |
| Aggregate commit | A 256 MiB job allows simultaneous 40 MiB allocations in two processes | A 64 MiB job refuses the second allocation with error 1455 while the first remains live; the requesting process's private commit stays unchanged |
| Process creation | A 32-process job creates all twelve requested sleeping helpers | An eight-process job creates seven live helpers plus their launcher, then refuses another with error 1816 |

The aggregate holder publishes its readiness record by renaming a fully written
file, avoiding a partial-file race. The launcher verifies that holder is still
live after the second allocation attempt. Process pressure retains actual handles
and verifies every created helper is live. Each job's configured limits are read
back and compared with the request. Cleanup terminates the complete owned job and
waits for its accounting to reach zero, including retained memory holders and
sleeping helpers. Existing timeout/diagnostic/launcher-exit checks also pass.

The tests exposed two incorrect assumptions in the first assertions. Windows
reported a peak job value above the configured memory limit even though it
refused the allocation; the final restricted run records 88,616,960 peak bytes
against a 67,108,864-byte limit. The API allocation result and measured unchanged
private commit establish the refusal; the high-water counter remains diagnostic
evidence and is not treated as a ceiling. The cause of that counter behavior is
not established by this test.

Likewise, accounting reports sixteen total processes and fifteen active before
cleanup in the restricted process case, beyond its seven explicit live helpers
and launcher. The earlier lifetime test observed additional Windows console hosts.
The final assertion verifies helper handles and quota refusal at the requested
application-process count, while retaining the complete accounting values. This
does not prove that the limit is a ceiling on every platform helper PID. Include
that overhead in further Office sizing/containment review.

Final evidence is
`.codex-temp/office-isolation/c1b89ee1cb2140e9a5c78d79c22fce37`:
`build.json`, the existing `case/control.json`, `orphan.json`, `lifetime.json`,
and six resource JSON reports with corresponding child logs. All six reports
parse, show zero active processes after cleanup, and retain no pending readiness
files. The full log is `.codex-temp/office-resource-limits-ready.log`.
The x64 `/W4 /WX` build and repository boundary/theme/73-document/whitespace checks
pass. This tools-only change does not rerun media, worker or application UI suites.

Failed assertions remain at `3a755c267a864a709dc24c39b8b242b8` (peak-memory ceiling)
and `ae8782dac055406799e802994fd87bc6` (total process count). The subsequent
`d93747c02bff47bd9ea154969f99fbaa` passes the resource checks; the final run above
also includes atomic readiness publication. No failed attempt is counted as a
passed preflight. No AppContainer profile or Office document was executed.


Forced owner termination (2026-09-11)
------------------------------------

The default preflight now additionally starts a disposable job owner outside the
test's observed job. That owner uses the actual `Run` launcher to start a child,
which starts a sleeping grandchild. An atomically published readiness record
identifies both descendants by PID and creation time. The observer opens handles,
checks those times and verifies that each process runs this case's unique probe
copy before observing or performing fallback cleanup.

All three application processes must remain live through a 300 ms control
interval. The observer then calls `TerminateProcess` only on the owned launcher
and requires exit code 71. Its child and grandchild must both signal exit within
five seconds. The observer holds process handles, not a job handle; it cannot
keep the owner's job alive. This tests closure of the abruptly terminated owner's
job handle, rather than graceful `TerminateJobObject` cleanup. Failure guards can
terminate the verified owned processes, but only after the assertions; they
cannot make the test report success.

The run at `.codex-temp/office-isolation/9bc758adcaa041b4818ff33910e629c3` passes,
including all earlier control/lifetime/resource tests. `case/owner-crash.json`
records all three PIDs, descendant creation times, owner exit 71, descendant
exits 0 and both descendant handles signaled. The recorded cleanup interval is
0 ms at the clock's resolution, not proof of zero latency. Both helpers were
programmed to sleep for sixty seconds; their observed early exits are not normal
completion. No unrelated process was selected or terminated.

The full log is `.codex-temp/office-owner-crash.log`; `build.json` retains source,
recipe and executable hashes. The x64 warnings-as-errors build and repository
boundary/theme/73-document/whitespace checks pass. This proves the two explicit
application descendants stop after this owner crash. It does not inventory every
Windows helper process after a crash, exercise AppContainer tokens or establish
Office engine behavior. Repeat the relevant lifetime tests inside the eventual
renderer boundary before production adoption.


Prepared access matrix and authorization
----------------------------------------

The 2026-09-11 network preflight now includes independent IPv4 and IPv6-only
loopback listeners. Each has its own ephemeral port and remains owned by the
probe. The unrestricted child must connect successfully to both; the prepared
isolated child must return `WSAEACCES` for both. A missing/unreachable listener or
disabled IPv6 cannot count as successful isolation. Each nonblocking connection
has a two-second deadline within the enclosing bounded job.

The updated x64 warnings-as-errors build and default preflight pass at
`.codex-temp/office-isolation/17656800e8594b1383b0b417cccb4487`, with log
`.codex-temp/office-ipv6-preflight-final.log`. `case/control.json` records
`loopbackConnect: 0`, `ipv6LoopbackConnect: 0` and `appContainer: 0`.
The existing descendant/owner-crash cleanup, diagnostic, timeout and resource
checks also pass. No AppContainer profile was created; the actual IPv4/IPv6
denial assertions remain unexecuted pending the authorization below. This does
not test external network destinations or DNS.

The initially attempted unregistered SID could not launch an AppContainer child
on this machine. That failure occurs before file/network assertions and is not
evidence of either access denial or engine incompatibility. The default now runs
preflight only, avoiding repeated unregistered attempts.

The separate `-CreateDisposableProfile` opt-in creates exactly one profile named
`ContextSuite.Office.Evaluation.<scratch-guid>` using CreateAppContainerProfile.
It refuses an existing-name collision, creates a zero-capability process using
that SID, and deletes only the profile it successfully created after job cleanup.
A destructor also attempts cleanup on failure; cleanup failures report the
retained name for recovery. The name is recorded before creation.

This opt-in is **not yet authorized or executed**. Windows places profile data
under `C:\Users\Keenan\AppData\Local\Packages` and creates per-user AppContainer
profile registry metadata. At preparation time, the repository's [AGENTS.md](../AGENTS.md) said:
"Do not create, edit, move, delete, or overwrite files outside the repository
unless the user explicitly asks for a specific external path." Specific
authorization for the disposable profile was requested and remains unanswered.
The quotation records that earlier rule, not the current file's text. The prepared
opt-in remains pending. It does not require installing
a package, changing Explorer registration or modifying another application's profile.

The prepared child checks its actual AppContainer token, allowed reads/writes,
denial of withheld fixture access, denial of writes to read-only inputs, and
WSAEACCES when connecting to known reachable IPv4 and IPv6 loopback listeners. Control success
precedes denial assertions to avoid mistaking a missing file or dead listener for
isolation. The isolated case never falls back to an unrestricted token.

Actual fixture-content verification (2026-09-14)
------------------------------------------------

Review before the pending profile run found that the unrestricted write-access
control opened its readable input with `CREATE_ALWAYS`, truncating it. The later
read check only requested a handle, so it could not distinguish that empty input
from the intended fixture. Earlier results establish handle-access behavior,
not successful transfer of the original fixture contents.

Write-access-only checks now use `OPEN_ALWAYS` and preserve existing inputs.
The child reads and compares the known input bytes, writes and flushes a distinct
control/isolated output marker, and reads that marker back. The parent independently
checks both original inputs and the exact expected output after each successful
child. A retained control output cannot substitute for the isolated child's marker.
Withheld read and write attempts still require actual access denial in the prepared
isolated mode; they never treat missing files or other errors as success.

The child also queries `TokenCapabilities` through a bounded 64 KiB buffer and
records its count alongside `TokenIsAppContainer`. Both control and prepared
zero-capability isolated assertions require a zero count. The
[Windows token information contract](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-token_information_class)
defines this as the token's capability-group inventory, not a guarantee of total
filesystem isolation or absence of ordinary shared system access.

The revised default preflight passes with an x64 `/W4 /WX` build at
`.codex-temp/office-isolation/85c4339bba634afbb65157afe3f4353a`.
`case/control.json` records `capabilityCount: 0`, `outputReadback: 0`, all expected
successful access codes and both successful loopback connections. Input bytes and
the control output pass the parent's exact comparisons. Existing timeout,
diagnostic, descendant, owner-crash and resource-enforcement controls also pass.
`build.json` records the source/build/executable hashes; the log is
`.codex-temp/office-access-content-preflight.log`.

This run created no AppContainer profile. The isolated capability count, actual
content transfers and access-denial assertions remain unexecuted until the
separate profile authorization. No Office engine or document ran, no product
payload changed and no image/audio/UI regression is claimed by this tools-only fix.


Remaining gates
---------------

Run and verify that access matrix after authorization, including profile cleanup
and retained scratch ACL scope. Then evaluate Office engine startup, font/runtime
access, profile paths, explicit environment, output validation, and independent
rendering inside the same boundary. Carry the verified resource tests into that
boundary, including owner-crash cleanup. Execute the prepared IPv6 denial check
and test further network cases, active-content/external-reference denial, hostile
documents, resource budgets and mixed-batch recovery before production adoption.
The full broad-file goal remains active; this preflight is not a substitute for
the selected Office conversions or other launch requirements.
