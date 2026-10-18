Repository Cleanup And Correctness Plan
=======================================

Status: implementation started on 2026-10-17. The first maintenance commit and
verified historical fixture cleanup are complete. Correctness review, follow-up
guards and feature commit proposals remain underway. This document is not an
executable commit or deletion proposal.

Keep the repository understandable, preserve working behavior and prevent
unnecessary output from accumulating. Use existing tools and short records.
There is no ongoing GB target, budget revision process or automatic deletion
threshold. Required inputs, useful caches and evidence may grow with development.


Scope And Starting Evidence
---------------------------

The October 17 scan measured about 50.37 GB of logical files, almost all in
`artifacts` and `.codex-temp`. It skipped reparse targets and is historical
evidence, not a list of disposable files. The local relocation receipts and
recovery instructions in `artifacts/workspace-archive-20261013` describe the
earlier archived history; preserve them when classifying subsequent output.

The checkout contains substantial unfinished source, tests and documentation.
Refresh its actual state before reviewing or committing. Existing documentation
checks do not establish application correctness. Public and private repositories
have separate changes and authorization; reviewing one does not review the other.
Git history does not preserve ignored engines, local evidence or rollback copies.


1. Establish The Current State
------------------------------

- Record the relevant repository roots, branches, HEADs, staged changes and
  untracked files. Preserve concurrent work and partial staging.
- Identify current source, installed and candidate versions separately. Check
  the applicable records before describing any version as installed or verified.
- Classify changed files as feature work, fixes, tests, documentation, required
  assets/notices or generated noise. Read contents before proposing exclusions.
- Identify active engine/build inputs, release reservations, installed/rollback
  payloads and evidence referenced by current work. Some are under scratch paths.
- Keep a short list of actual findings with file references, consequence and
  next action. Distinguish confirmed defects, missing evidence and preferences.
  Reuse an existing task record; do not create a new tracking system.


2. Review Correctness Before Cosmetic Cleanup
---------------------------------------------

Review the changed behavior and its callers against the owning product/design
documents. Work through the following areas in order of potential harm; this
is not a request to redesign or exhaustively retest every existing subsystem.

| Priority | Review and verification focus |
| --- | --- |
| Source and output safety | Original preservation, explicit replacement consent, collision handling, validation before publication, recoverable failure |
| Workflow correctness | Advertised conversion pairs, settings snapshots, batch results, cancellation, retries and partial success |
| Media fidelity | Transparency, orientation, color, metadata, animation and document layout within each supported workflow's stated limits |
| Integration | Public/private contracts, engine selection and hashes, source/notice correspondence, worker failures and bounded transport |
| Customer behavior | Direct actions, necessary prompts, useful errors, quiet success, settings migration and truthful Analyze results |
| Delivery and status | Package inventory, private-source exclusion, consistent version/resource metadata, accurate implemented/verified/pending claims |

Fix demonstrated problems in small changes, with meaningful regression coverage
where practical. Keep behavior changes explicit. Remove obsolete code only after
checking callers, tests, configuration and packaging. Do not reformat unrelated
files, add speculative abstractions or weaken checks to make cleanup pass.
Do not change pinned hashes or preserved notices merely to silence a mismatch.

Use the smallest relevant checks, then verify the combined result when shared
contracts or composition changed:

| Changed scope | Existing verification starting point |
| --- | --- |
| Documentation/instructions | `tools/Test-Repository.ps1` |
| Core or application contracts | `tools/Test-Foundation.ps1` and the affected focused harness |
| Real worker/engine composition | Documented integration checks and the owning engine's verifier |
| Explorer commands/resources | `tools/Test-ShellPrototype.ps1`; installed checks only when that scope is authorized |
| Public/private boundary | `tools/Test-PrivateBoundary.ps1` and source/package boundary checks |
| Packaging or cleanup | Relevant release/payload checks and focused filesystem guard tests |

Follow [development instructions](development.md) for prerequisites and command
options. An integration command can rebuild output: check whether that path is
registered or in use before running it, and use supported isolated staging when
needed. Do not install, register, restart Explorer, run opt-in desktop/recycle
tests or exercise live commerce as an incidental check. Preserve restrictions
on concurrent foundation/router/desktop harnesses.

Record which source/engine or candidate was tested and the limits of the result.
If a required check is unavailable, keep the affected readiness claim open.
A matching hash proves identity, not behavior; mocked installer tests do not
prove native installation; old passing results do not validate changed code.


3. Commit Coherent, Verified Work
---------------------------------

Prepare the normal `DIFF` review from current contents, following the shared
command procedure and [commit style](commit-style.md). This plan supplies an
order of work, not approved file lists or commit messages.

Group by independently useful behavior: Analyze/UI, individual conversion
workflows, audio, PDF/Office, shell/resources, packaging and maintenance are
starting points. Keep matching tests, documentation, notices and intentionally
tracked generated companions together. Shared contracts may require a larger
coherent group; do not force a split that leaves broken intermediate states.

Review and fix first, run required checks, then execute authorized commits.
Keep public/private commits separate and record compatible revisions when needed.
Preserve unrelated work. Refresh the proposal if reviewed contents, HEAD or the
index changes; never absorb another agent's edits silently. Push is separate.
Do not create one catch-all cleanup commit or rewrite history to manufacture
a tidier development sequence.

Ordinary task-owned scratch cleanup can happen throughout. Broad historical
cleanup follows source review and preservation. Commit and validate one coherent
slice at a time; the whole backlog need not block ongoing development.


4. Clean Historical Output Safely
---------------------------------

Classify large or suspicious directories before acting:

| Material | Treatment |
| --- | --- |
| Source, selected engines, source kits, fixtures and notices | Preserve through the owning workflow |
| Useful build caches | Reuse while they save meaningful rebuild cost; revisit when superseded |
| Successful disposable intermediates | Remove when ownership, lack of consumers and reproducibility are established |
| Failure/acceptance evidence | Keep what supports diagnosis or the claim; reduce resolved retries to useful evidence |
| Installed, rollback and reserved release material | Protect; archive history only through an explicit reviewed selection |
| Unknown or active output | Leave intact until classified |

Age, size, ignore rules and a clean Git status are not deletion permission.
Reproducible means the required inputs, tools and command are available.
Do not rely on an assumption that a missing dependency can be downloaded again.

For a historical cleanup pass:

1. List exact roots, reasons and intended actions. Refresh protections from
   current build/engine inputs, installation/registration, version receipts,
   rollback records and active evidence. Check current consumers, not only
   an old protection manifest. Preserve uncertain dependencies.
2. Save needed history to a destination authorized for this scope. Reuse existing
   authorization where applicable. Verify archive membership and every restored
   file, or equivalent complete membership/hash checks for an uncompressed copy.
   Retain recovery instructions and never restore over existing work.
3. Confirm sufficient free space for copying and verification before starting.
   Recheck original identities, protections and absence of active writers before
   removing exact reviewed entries. Reject out-of-root paths, reparse points,
   protected overlaps and changed selections. Stop on uncertainty.
   Review read-only attributes before deletion; do not discover a known
   obstruction midway through a selection or clear attributes broadly.
4. Preserve receipts and diagnostics if partially completed; resume only after
   rechecking what remains. Verify protected source/payload identities and any
   affected workflow. Report local bytes reclaimed separately from archive
   storage retained. Moving data does not eliminate total storage use.

Use the existing [archive helper](../tools/maintenance/Archive-WorkspaceHistory.py)
and [guard tests](../tools/maintenance/Test-WorkspaceHistory.py) as the base.
It accepts only scratch descendants. New plans require an explicit protection
review bound to the exact selection file; protection contents are also bound to
the plan and receipts. Known protected inputs remain mandatory. Planning and
cleanup require `--reviewed-current-consumers`: this records the operator's
review, not automatic discovery of dependencies. Recheck consumers before
cleanup. Older receipts support recovery verification only, not new deletion.
See the helper's `--help` for the selection/protection schemas and commands.
Do not stretch it into a general artifact cleaner.

Never blanket-delete scratch/artifacts or use broad `git clean`. Preserve
immutable version receipts and reserved release bytes. Cleanup must not alter
installed behavior, registration, user settings, reference material or another
repository as a side effect.


5. Make Future Tasks Clean Up Their Own Output
----------------------------------------------

- Create a unique run directory when scratch is needed. Track paths this task
  creates; existing files and other agents' runs remain outside its ownership.
- Reuse verified engines/caches and avoid copying full payloads unnecessarily.
  Do not introduce permanent dependencies on disposable paths or move existing
  selected inputs merely to fit a naming convention.
- Save required results before removing successful transient output. After owned
  processes stop, check exact containment, contents, consumers and reparse points.
  Retain unexpected, locked, failed or interrupted output for review.
- Keep cleanup beside creation and verification in the producing script.
  Successful disposable output should disappear; evidence recording failures
  must retain the run. Cleanup errors must be reported without hiding the
  original test/build result. Do not blindly delete in a `finally` block.
- Explain significant leftovers briefly using an existing receipt or local note:
  purpose, reproduction command and reason to keep them. Revisit them when that
  purpose ends. No per-run budget, duplicate ledger or scheduled deletion.
- Review the task's diff, update the owning docs and report checks plus meaningful
  leftovers. Read-only requests prohibit cleanup writes. Never clean another
  agent's run; a resumed task rechecks ownership and active consumers.

Make the first script improvements where repeated runs produce the most waste.
The history-tool guard suite now removes its own fixtures after success and
retains a compact receipt. Foundation runs now use unique directories, retain
failure diagnostics and remove successful fixtures after recording a result.
Use `-KeepScratch` when successful fixtures are needed for investigation.
Unique directories alone do not make concurrent router tests safe.

Maintain focused tests for success cleanup, retained failures, failed receipt writes,
changed contents/consumers, protected paths, links, outside paths and safe retry.
Use disposable fixtures, never real release history.

Use small local helpers; share code only when several callers need it.
Add a read-only size report only if repeated investigations justify one.
No daemon, quota enforcement, automatic age-based pruning or framework migration.


6. Keep Storage And Documentation Understandable
------------------------------------------------

Avoid unnecessary storage rather than minimizing every byte. Growth from useful
capabilities, representative fixtures, required evidence and expensive caches is
reasonable. Investigate unexpected duplication or repeated successful leftovers.
Measure sizes when they help a substantial cleanup or growth investigation, not
on every turn. Check free space before expensive builds or restore verification.
Explain significant retained output; no fixed GB target or budget records.

Review narrow ignore rules for confirmed caches and incidental logs. Do not hide
source, notices, fixtures or evidence under broad patterns. Correct the producer
when it writes disposable output into source directories.

Keep one concise current-status block in README, distinguishing source, installed
and verified candidate versions. Keep current behavior in owning design documents
and dated history in CHANGELOG/goal records. Remove obsolete claims of "current"
without rewriting historical results. Keep private details out of public records.


Definition Of Done
------------------

- Reviewed source changes have clear dispositions and authorized coherent commits;
  unrelated and unfinished work remains identifiable and preserved.
- Confirmed correctness issues in scope are fixed and checked; unverified
  acceptance remains explicitly open.
- Protected inputs, source, releases and recovery evidence remain intact.
- Successful representative runs remove disposable output; failed runs preserve
  useful diagnostics and do not damage sibling/user files.
- Significant retained material has a clear purpose; unexpected growth has been
  investigated without imposing an arbitrary size limit.
- Current documentation matches the verified behavior and links pass validation.

Implement and verify correctness fixes and cleanup improvements in small
reviewed slices, then apply the guarded process to history. Keep verification
evidence separate from this plan; writing the plan does not establish acceptance.
