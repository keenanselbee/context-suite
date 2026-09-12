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
