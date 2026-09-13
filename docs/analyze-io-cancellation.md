Analyze File I/O Cancellation
============================

The 2026-09-11 reader now starts its five-second read budget before scheduling
file inspection. Previously, path metadata, native opening and initial file
state queries ran before that timer existed. A blocked open could therefore
ignore both the intended deadline and a user's cancellation request.

Implementation boundary
-----------------------

`SynchronousFileIo` holds a real handle to the current thread only for a synchronous
delegate. On cancellation it requests Windows I/O cancellation immediately and
every 50 ms until the scope ends. Repetition covers a request issued just after
the first cancellation attempt. A lock disables callbacks before scope disposal
can release the thread for unrelated work; registration, timer and thread handle
are disposed. No scope spans an await, and no timed-out task is abandoned.

The reader uses this scope around path checks, opening, handle validation and
length/write-time queries. It checks cancellation between phases, retains the
disk-file/reparse/offline restrictions, and closes a newly opened handle on any
failure. The existing asynchronous byte reads use the same read token. Optional
workers retain their own budgets. Final source-state verification after a deeper
probe gets a separate five-second metadata budget and the original caller token.
User cancellation remains cancellation; deadline failures have readable text.

Microsoft documents [CancelSynchronousIo](https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-cancelsynchronousio)
as a request that can race with normal completion. Its
[cancellation guidance](https://learn.microsoft.com/en-us/windows/win32/fileio/canceling-pending-i-o-operations)
also requires careful thread ownership and notes that driver support varies.
The reader waits for actual completion and cleanup. This is not a guaranteed
five-second return bound for an unresponsive driver, remote filesystem or device.

Actual evidence
---------------

`AnalysisIoContracts` authors local disposable files and holds a native
[level-1 opportunistic lock](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_request_oplock_level_1).
The signaled break event proves the competing open reached the filesystem; the
uncompleted reader proves it is waiting for acknowledgment. The tests then cover:

- User cancellation and the production five-second deadline while the lock is
  still held. The final run observed 1.70 ms and 5,015.01 ms respectively from
  reader invocation, without releasing the blocking lock to manufacture success.
- A positive release control that completes the real read, source hash/time
  preservation, exclusive reopening after each outcome and successful fresh reads.
- An already-canceled/disposed scope followed by a blocked open on that same
  thread. The later open stays blocked until its own lock is released.
- Cancellation before a native request exists, followed by a blocked open inside
  the still-active scope. Repeated cancellation closes this issuance race.

All 2,349 foundation contracts pass, including 29 new checks. Retained evidence:
`.codex-temp/foundation-tests/analysis-io-f6d7b928b91f4fa19dbb8cab2477f0f3`
and `.codex-temp/analysis-io-foundation-final.log`. An initial test compilation
error was fixed before execution; the final race checks supersede the earlier run.

The refreshed [Analyze benchmark](analyze-performance.md) passes all existing
reference-machine timing/allocation review budgets with original hashes/times
unchanged. Fresh Release stage
`artifacts/production-staging/070dd01a49c14d5db5458aa8a5fccb01` builds with zero
warnings/errors and passes curated image, optional audio/PDF identity, inventory,
notice, dependency and allowlist checks using `-SkipShell`. Its log is
`.codex-temp/analysis-io-production.log`. No private implementation changed.

Remote storage, hostile/non-cooperative drivers, an individually blocked metadata
query, broader selections and actual
visible/accessibility acceptance remain separate checks. Existing private
real-worker suites were not repeated for this public reader change. No installed
state, Explorer registration, native recycling or live commerce was changed.

Admission follow-up (2026-09-11)
--------------------------------

Analyze admission no longer calls `File.Exists` in the managed request validator
or `GetFileAttributesW` in the native prototype host. Both still validate schema,
operation/action, count and absolute-path syntax. This reaches the activation
parser, application router and view-model admission through their shared validator.
File availability and regular-file restrictions are checked per row by the
cancellable reader. A missing or inaccessible member cannot reject valid members
of the selection; folders receive an unsupported row rather than being read.
Transformations retain their existing availability checks and publication rules.

All 2,358 foundation contracts pass. Added/updated cases exercise parser admission
for unavailable paths, continued Convert/Optimize refusal, a real mixed Analyze
batch with missing/file/folder/file members, duplicate-request handling, readable
failure guidance, no licensing/worker access and no output/retry authority.
The earlier blocked-open and cancellation-race tests also pass in this run.

The native [host-only checks](shell-integration.md#host-only-validation)
pass three manifest inspections, three valid batches, six mixed-availability
cases and unknown-schema refusal. They use `--validate-only` with generated
repository-local requests, never COM invocation or application routing.

Fresh Release stage `artifacts/production-staging/27cef67c42a0418c826910a124b28bcd`
passes managed/native builds and payload verification, this time including the
native shell build. The separate prototype host is tested from
`.codex-temp/native-staging-27cef67c42a0418c826910a124b28bcd`; it is not added to
the production application payload. Logs are `.codex-temp/analyze-admission-foundation.log`,
`.codex-temp/analyze-admission-host.log` and `.codex-temp/analyze-admission-production.log`.
The preceding performance refresh remains dated evidence, not a new benchmark run.
Actual installed-shell and visible acceptance remain unverified.

Failure guidance follow-up (2026-09-11)
--------------------------------------

The results model now distinguishes a read deadline, missing file, access denial
and sharing/locking conflict. Previously all four became the same stable-header
failure message, hiding the cause established by the reader. Messages now direct
the user to check the drive/network, locate the moved/deleted file, check access
permissions or close the file in the other program. Other read errors retain the
existing fallback, and unsupported paths retain their regular-file explanation.

`FileAnalysisTimeoutException` preserves the existing `IOException` contract
while letting the UI distinguish a read-budget failure without matching text.
Only the ordinary failure message changes; no automatic retry, output or paid
admission is added. Other members of the Analyze batch continue after a failure.

All 2,367 foundation contracts pass, with nine new checks and a strengthened
typed-timeout assertion. Evidence uses a real sharing violation, an authored file
with read access temporarily denied, and an actual five-second blocked-open
timeout through the batch handler. The access-denial fixture's original security
descriptor, bytes and write time are verified after restoring its DACL. The
timed-out batch input retains its bytes/time, and the next file succeeds before
the blocking test lock is released. Missing-file and folder guidance is also
checked through the existing mixed-selection case.

Logs are `.codex-temp/analyze-failure-guidance-foundation.log` and
`.codex-temp/analyze-failure-guidance-production.log`. Fresh Release stage
`artifacts/production-staging/f3c475e2de144a9789ebd7df46353f4e` includes catalog
2026-09-11.11 and the native shell build; managed/native builds and payload checks
pass with no reported warnings/errors. No private implementation, installed state,
Explorer routing, recycling or live commerce changes were made. This proves
result-model delivery, not visible layout, screen-reader announcement or wider
driver/network acceptance. The earlier performance measurement was not repeated.


Rejected test-oplock cleanup (2026-09-13)
----------------------------------------

A foundation run during PDF resource verification stalled in the test helper,
not in an admitted Analyze operation. An isolated trace captured
`DeviceIoControl` refusing a level-1 oplock with error 300 while the supplied
OVERLAPPED structure still contained `STATUS_PENDING`. The helper incorrectly
called blocking `GetOverlappedResult` during cleanup of that rejected request.
There was no submitted operation whose completion could release that wait.
The [Windows API documentation](https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-getoverlappedresult)
defines pending submission by the call's `ERROR_IO_PENDING` return, not by reading
the structure's internal member after a failed request.

The helper now tracks actual pending submission and waits only for those requests
before freeing their native storage. A real competing read handle deliberately
refuses an oplock; two regression assertions require prompt return and released
input handles. Ordinary setup retries only error 300, at most 20 attempts separated
by 50 ms. This occurs before the measured analysis begins; failed reader,
deadline, cancellation and same-thread assertions are never retried or skipped.
Production `SynchronousFileIo` and all reader deadlines remain unchanged.

The focused mode is:

```powershell
dotnet run --project tests/ContextSuite.Core.ContractTests -c Release -- --analysis-io '<repository scratch directory>'
```

Five consecutive isolated runs pass **36 checks each**, including the refused
request regression and the existing real reader/mixed-batch/cancellation races.
Two runs also encounter transient setup refusals, recovering after one and two
retries respectively. Evidence is
`.codex-temp/analysis-io-repeat-a0c0a000a490461ca33e377e8e1259c2/runs.json` and its
five logs. The preceding bounded reproduction times out and terminates its owned
child after 30 seconds; its trace is
`.codex-temp/analysis-io-repeat-0b886e3058de4266a228bd2a002e2e4b/0.log`.
The earlier stalled foundation log, `.codex-temp/image-pdf-resource-foundation.log`,
is retained as a failed run; only its verified owned test process was stopped.

The final complete foundation run passes **2,391 contracts** with recorded exit
code 0 in `.codex-temp/image-pdf-resource-foundation-final.log`. This is current
worktree evidence, including separate trial-policy edits; it is not a claim that
those edits were present in the earlier staged PDF payload. Native wait-chain
inspection did not identify the cause; the explicit failed-request trace did.
These tests do not close remaining remote-driver, metadata-stall or visible
Analyze acceptance.
