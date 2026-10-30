Free Access And Repository Transition
=====================================

Status: in progress. Decision accepted 2026-10-30 under
[0021](decisions/0021-free-product-and-public-implementation.md).

The owner authorized one free application, retirement of the private build
boundary and commercial selling, reviewed milestone commits, and a verified
release candidate. The unfinished PDF dark-mode changes in both repositories
are explicitly included and must be verified with the migration.


Milestones
----------

1. Preserve the starting working changes and identify source, installed,
   candidate, and engine versions separately.
2. Import only reviewed required noncommercial implementations, native bridges,
   and tests. Keep exact pinned source bytes and retain private history.
3. Remove commercial runtime and UI while preserving operation validation and
   media/publication safeguards. Old access files must remain untouched.
4. Make build, test, CI, packaging, and current receipts independent of the
   private checkout, retaining historical receipts and required engine inputs.
5. Verify contracts, real media/PDF workflows, free admission with legacy state,
   candidate payloads, and isolated clean-source builds. Record exact outcomes;
   do not infer installed or hosted acceptance from local checks.
6. Review diffs and commit independent milestones. Prepare an immutable free
   candidate with its source/engine receipts; preserve existing installations.
7. Update product/website copy. Prepare exact storefront and remote-archive
   retirement actions after verifying the migration; deployment and live account
   changes are separate from the local candidate.


Starting Evidence
-----------------

Public baseline: 275a6f9b56132d1e4d90808f763e6ee41cab360c.
Private baseline: 33ba1255e2ffcfd386dd29d6b181c6a016fc3763.
Both had authorized unfinished PDF changes. Local-only snapshots, file inventory,
and subsequent evidence belong under .codex-temp/free-transition-20261030.
The initial import copies 237 selected tracked noncommercial private files,
including current PDF modifications; it does not import commercial configuration.

Current working version is 1.5.5; HEAD metadata is 1.5.3. The existing README
records 1.5.5 as installed and verified; this task has not re-established that
installation claim. Choose a new release version only after checking reservations.


Completion Evidence
-------------------

Pending. No free candidate, clean-source pass, remote archival, storefront
retirement, or website deployment is claimed yet. Preserve failed/interrupted
runs until classified and retain required source, pins, notices, and receipts.
