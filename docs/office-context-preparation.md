Office Context Preparation
==========================

Status: preparation, live retirement and recorded retirement after restart are
implemented, including cleanup of recorded but unstarted preparation. Torn or
missing preparation records still require review. The
[direct Office command](office-direct-command.md) now owns preparation through
the application view model's lifetime.

The [actual-app acceptance follow-up](office-app-preparation.md) passes all 55
checks across interrupted preparation, recovery guidance, forwarded Analyze,
blocked Office work and cleanup after shutdown. Correcting the native profile
environment permits the following Word export at the original failing path
length; the full layout is accepted without shortening application storage.

`OfficeContextPreparation.CreateAsync` now constructs the source context used by
the Office worker. It takes a context root, the runtime directory, an
original path, an explicit DOCX/XLSX/PPTX format and the explicit calculation
policy. Excel requires `cached` or `recalculate`; this does not select a customer
default. The original, runtime and context locations must remain separate.

Callers normally supply an existing root. The application executor explicitly
allows preparation to create a missing root, after location, fixed-policy,
path-budget and source checks succeed. Creation walks missing ancestors from a
leased existing parent and retains each new directory before creating its child.
The executor does not create an unchecked root ahead of preparation. Rejected
locations, unsupported policies, invalid sources and pre-cancelled requests leave
missing roots absent. A later copy or journal failure may still retain a partial
context under the failure policy below.

Preparation holds ordinary local directory ancestors and opens the original leaf
without following a reparse point. It refuses multiple filesystem links, empty or
oversized sources, unsupported XML variants and a content/format mismatch before
creating a context. Source preflight identifies the variant; it is not permission
to execute embedded content or a complete security scan. The worker's admission
and verified runtime checks remain required.

The application creates a unique context with `input`, `output`, `profile` and
`temp` directories, refusing existing objects. It copies from the retained source
handle with cancellation and a byte limit, checks the copy's hash, flushes it and
sets the copied file read-only through its handle. Reopening must match the copied
file's native identity and bytes. Original and snapshot handles deny writes and
replacement; parent directory leases prevent ancestor replacement. Ordinary
readers can still open both files. Pure directory leases now request read/list
access, while actual permission grants retain their required ACL access.

After creating and binding the five generated directories, preparation creates
the version-four [ownership journal](office-ownership-journal.md) before copying
source bytes, outside all grant directories. Its only entry is profile intent:
preparation creates
no native profile, grants no permissions and starts no worker. The original file
is never copied into a directory granted write access to the renderer.

The coordinator must create the profile, apply the five recorded grants, bind
worker dispatch, confirm process exit and complete profile cleanup before
disposing preparation. Disposal refuses while a confirmed profile lacks a
completed deletion record, retaining its journal and file leases for retry.
`VerifyAsync` checks original and snapshot identities, bytes and snapshot
protection again before future publication. It does not authorize publication.

Cancellation or copying failure retires the recorded unstarted context after
verifying native profile absence and the bound directory identities. If cleanup
fails, the exception retains preparation and its leases for the executor's retry;
further Office work is blocked while cleanup remains pending. Failures before a
complete journal exists retain the exact location for review. No directory sweep
or native profile creation is part of this cleanup. Physical power-loss durability
and torn-record recovery remain separate acceptance work.

Before creating a context, preparation also reserves room within recovery's
512-entry and 256-record limits: at most 510 existing entries and 255 ownership
records are accepted. A full root is retained with an actionable review result,
and the executor blocks subsequent documents instead of accumulating more files.

Windows documents the relevant
[sharing, new-file and reparse-point behavior](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew).
These checks are intended to prevent accidental substitution and unsafe file
operations; they do not authenticate local state against hostile full-trust
software running as the same user.


Completed live-context retirement
---------------------------------

`Retire` removes generated files only after the live coordinator has stopped the
worker and completed native profile cleanup. It requires a completed journal,
captures the four generated directories, their parent context and the record's
filesystem identities while preparation leases still prevent replacement, then
opens them for deletion. The original, runtime and context-root leases remain
held until retirement succeeds or the caller explicitly disposes preparation.

The complete tree is inspected before any deletion: at most 4,096 entries,
32 directory levels and one million path characters. Unexpected context-root
children, replaced or missing owned directories, reparse points, multiple file
links and sharing conflicts stop the attempt. Open handles hold inspected objects
against writes and renames. Files are deleted through those handles, children
before parents, and the ownership record is removed last. Read-only snapshots
use Windows' handle-based
[extended deletion flags](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddk/ns-ntddk-_file_disposition_information_ex),
without changing attributes through a path. The
[handle information classes](https://learn.microsoft.com/en-us/windows/win32/api/minwinbase/ne-minwinbase-file_info_by_handle_class)
define the corresponding `FileDispositionInfoEx` operation.

An incomplete attempt retains the journal, original lease and live retirement
state for retry. A PDF already published remains a completed output and receives
a cleanup warning; it is not converted again merely to retry temporary cleanup.
No original, published PDF, runtime file or Windows profile directory is part of
this file-deletion tree. Native profile cleanup remains the owner's separate
responsibility and precedes retirement.

Live cleanup now records terminal retirement intent in the version-four journal
before deletion. [Restart recovery](office-startup-recovery.md#completed-context-retirement)
can finish that intent after original-owner death and native-state verification.
An unstarted version-four context can also retire after verified owner death,
native profile absence and inspection of all five bound directories. Recovery
records terminal intent before deletion; later attempts can resume that intent.
Earlier version-three records, missing/torn journals and interrupted exports without
retirement intent retain their existing review behavior. The implementation does
not infer deletion authority from a directory name or sweep old scratch folders.


Verification
------------

The interrupted-preparation follow-up passes all **3,532 foundation contracts**
in `.codex-temp/office-preparation-foundation-f783e13187544accaf6adf576c9c6a8a`.
The 60 added checks interrupt actual copying before the first write and after a
partial write, exercise cleanup failure and executor retry, enforce both storage
limits, and kill disposable writers for restart recovery. Live owners, locked
files and substituted directories are refused; retries remove owned temporary
files while preserving original bytes and timestamps. These foundation checks
create no native profiles. The crash observer retries bounded Windows sharing
errors after process exit; an earlier immediate-open attempt failed at that
boundary, before recovery could run.

All **40 native retirement checks** also pass at
`.codex-temp/office-retirement/51de183d43ea4a9a91f1b15dc54b178b`, with unchanged
source/fixture receipts and all seven final profile names and contexts removed.
This regression creates eight disposable profiles across those names and runs
no Office renderer. The Release application test host builds with zero warnings
or errors. Real document exports, visible acceptance and production packaging
were not rerun for this follow-up. Actual-app startup acceptance of the new
unstarted recovery path remains open, as do torn records and physical power loss.

The root-creation follow-up passes all **3,461 foundation contracts** in
`.codex-temp/office-preparation-foundation-d1c13d1093af4615a32c04db435add91`.
Its 27 additional checks cover relative and noncanonical roots, runtime overlap,
unsupported policy, mismatched/invalid/oversized input, missing runtime, path
limits, cancellation, explicit existing-root behavior and successful nested-root
creation. They verify absent directories after refusal, retained ancestor leases,
unchanged original bytes/timestamp and released source handles. These tests create
no native profiles and do not rerun real Office rendering or visible acceptance.

The retirement follow-up passes all **3,379 foundation contracts** in
`.codex-temp/office-preparation-foundation-975874f186df4b918f1cdb1bb3c0b395`.
Its 37 additional checks use authored completed journals, without creating native
profiles. They cover read-only snapshot removal, locked children, hard links,
unexpected root files, directory identity substitution, depth limits, retry and
original preservation. Refusals keep evidence before deletion starts. Reparse
points are rejected by the implementation but have no new dedicated retirement
fixture in this run. Mid-deletion interruption remains untested.

The [application transaction follow-up](office-pdf-validation.md#live-context-retirement-verification)
adds actual export/profile retirement and separate cleanup-retry evidence. The
preceding source-preparation evidence below describes its earlier retained-file
behavior and remains distinct from this follow-up.

All **3,221 foundation contracts** pass, including **48 new preparation checks**,
at `.codex-temp/office-preparation-foundation-7157cdc53f724cf399adab032a38854f`.
The source-bound receipt covers unchanged originals, exact read-only snapshots,
protected file/parent lifetimes, refusal when snapshot protection is removed,
restored verification, release of file leases, explicit Excel calculation,
content mismatch, cancellation before preparation, size limits, hard links and
overlapping roots. The generated original, snapshot and failed-admission files
remain in repository scratch; these checks create no native profile.

All **90 actual worker checks** pass at
`.codex-temp/office-worker/7e1df207d27248029c9dd5f924a3dc71`. Both the three
owner-crash fixtures and the following ordinary exports use application-created
contexts. The 63 ordinary checks include prepared source identity, refusal to
dispose before native cleanup and source/snapshot verification after rendering.
The other 27 checks confirm recovery after owner-only loss during live rendering,
preserved originals/snapshots and retained interrupted candidates.

Independent inspection at
`contracts/inspection-26921ac9c8c347de87181556ce8128ba` verifies all three following
PDFs through qpdf structure, authored text, page geometry and exact PDFium control
pixels. `.codex-temp/office-preparation-final-verification.json` matches current
sources and binaries, verifies 162 journal frames and 30 live directory identities,
and confirms that all six profile folders/mappings and named jobs are absent.
None of 23,045 checked runtime/context ACL entries contain the six profile SIDs.
The application and private harness build in Release with zero warnings/errors.
The earlier 138 native ownership checks were not rerun for this change; the
current replay exercises preparation, grants, journal reopening and actual worker
recovery. The reserved production package was not rebuilt.

The preceding replay at `.codex-temp/office-worker/095ee07d9d554cd299333bc9dc36140f`
also passes 90 checks and independent PDF inspection. It uses application
preparation only for the following exports; its crash fixture still makes its
own snapshot. The final replay above replaces that remaining fixture preparation.

At the time of the preceding worker replay, the customer command, startup recovery
coordinator, production PDF validation, transactional copy publication and mid-copy
interruption remained open; the follow-ups above and the direct-command document
record subsequent work. Engine adoption/fidelity gates remain open. This adds no visible, keyboard, screen-reader,
theme/DPI or installed-shell acceptance.
