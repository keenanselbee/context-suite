Office Context Preparation
==========================

Status: application preparation implemented; customer Office conversion remains
disabled pending orchestration, independent validation and publication.

`OfficeContextPreparation.CreateAsync` now constructs the source context used by
the Office worker. It takes an existing context root, the runtime directory, an
original path, an explicit DOCX/XLSX/PPTX format and the explicit calculation
policy. Excel requires `cached` or `recalculate`; this does not select a customer
default. The original, runtime and context locations must remain separate.

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

Only after source/snapshot verification succeeds does preparation create the
version-three [ownership journal](office-ownership-journal.md), outside all grant
directories. At this point its only entry is profile intent: preparation creates
no native profile, grants no permissions and starts no worker. The original file
is never copied into a directory granted write access to the renderer.

The coordinator must create the profile, apply the five recorded grants, bind
worker dispatch, confirm process exit and complete profile cleanup before
disposing preparation. Disposal refuses while a confirmed profile lacks a
completed deletion record, retaining its journal and file leases for retry.
`VerifyAsync` checks original and snapshot identities, bytes and snapshot
protection again before future publication. It does not authorize publication.

Preparation failures release their handles and retain any partial files; the
exception includes the exact context directory for diagnosis. They never sweep
directories or create a Windows profile. Power-loss recovery of an incomplete
copy, confirmation-write failures and automatic retention cleanup remain
separate acceptance work. The existing journal recovery handles profiles only
after durable confirmation and live identity verification.

Windows documents the relevant
[sharing, new-file and reparse-point behavior](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew).
These checks are intended to prevent accidental substitution and unsafe file
operations; they do not authenticate local state against hostile full-trust
software running as the same user.


Verification
------------

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

The customer command, startup recovery coordinator, production PDF validation,
transactional copy publication, mid-copy interruption and the remaining engine
adoption/fidelity gates remain open. This adds no visible, keyboard, screen-reader,
theme/DPI or installed-shell acceptance.
