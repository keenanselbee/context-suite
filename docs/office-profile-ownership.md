Office Profile And Grant Ownership
==================================

The application now contains an `OfficeSandboxOwner` component that owns a fresh
Windows AppContainer profile and explicitly granted directories. It is separate
from the private worker's [managed process job](office-managed-launcher.md).
The component has native file-access, worker-crash and typed worker-export
evidence. Context construction still belongs to the isolated harness; the
customer Office command remains incomplete.


Lifetime and access policy
-------------------------

Profile names use the owned Office namespace and a nonempty lowercase GUID. The
evaluation namespace is supported for explicitly authorized disposable tests;
it does not change access policy. Creation never adopts an existing profile.
Only an owner whose creation succeeded can remove that profile, and a disposed
owner cannot later delete another profile created with the same name.

The caller supplies only owned snapshot/output directories and a verified runtime.
Read grants allow reading/execution; writable grants add ordinary creation,
modification and deletion inside their directory. They do not grant arbitrary
capabilities or change a parent's ACL. Grant roots must be ordinary absolute
local directories with no overlapping grants, ambiguous syntax or reparse
ancestors. Directory handles are retained along the full path. Existing children
are inspected under held read handles, with a 65,536-entry bound, and reparse or
hard-linked files are refused before ACL changes.

The owner inserts an explicit inheritable allow entry for its new SID while
retaining other entries. Windows propagates it to existing children, and later
files inherit it. Cleanup rechecks the tree, removes the owner's exact entry
from the current ACL, verifies no entry for that SID remains at the grant root,
releases handles and deletes the Windows profile. It does not restore a stale
whole-ACL snapshot over intervening unrelated entries. Microsoft's
[inheritance documentation](https://learn.microsoft.com/en-us/windows/win32/secauthz/automatic-propagation-of-inheritable-aces)
and [SetSecurityInfo contract](https://learn.microsoft.com/en-us/windows/win32/api/aclapi/nf-aclapi-setsecurityinfo)
describe propagation and inherited-entry removal.

Before granting access, the owner also verifies that none of the held children
already contains an entry for the profile SID. After root revocation, it checks
every held child again before releasing the grant or deleting the profile. Root
revocation alone does not establish removal below protected or explicitly
permissioned children. The child-access follow-up below records that boundary.

Workers must stop before profile cleanup. If tree/access cleanup cannot be
verified, the owner retains its remaining grants and profile and reports their
locations/name for a retry; it does not claim cleanup succeeded. This is an
in-memory retry boundary, not persisted application-crash recovery. Cleanup of
hostile linked trees, concurrent external ACL/tree tampering, cleanup failure
recovery and full-runtime grant costs still need dedicated acceptance.


Asynchronous sharing-conflict recovery
--------------------------------------

The [active worker interruption tests](office-path-boundary.md) exposed a
temporary Office cache file that could not be opened during cleanup after its
native process handle was signaled. The file was subsequently absent. The
specific remaining lock holder was not established.

`DisposeAsync` now retries only aggregate cleanup failures whose underlying
native errors are sharing or lock violations (32 or 33). It permits at most 50
attempts, separated by 100 ms asynchronous delays. This bounds retry count and
added delay, not the duration of individual Windows filesystem calls. Each
attempt retains the same owner and repeats its existing checks; it does not
relax sharing, bypass linked-file checks or replace unrelated ACLs. Other errors
propagate immediately. Exhausted retries preserve unresolved grants and the
Windows profile for later recovery. Synchronous `Dispose` remains available.

The focused suite passes **56 checks** at
`.codex-temp/office-owner/1f72e692322145479812de30b70d9618`, with its matching
fixture/result stage beneath `.codex-temp/office-isolation`. A held exclusive
cache-file handle verifies exact diagnostics, retry exhaustion and retained
ownership. Releasing that handle lets the same pending asynchronous cleanup
complete; source bytes and unrelated ACL entries remain preserved.

The real interruption harness now awaits profile-owner disposal after worker
exit. Replay `.codex-temp/office-worker/556f332dfd514885bd3e00575a710fa4`
passes all three cancellation/recovery cells and independent inspection of the
three recovery PDFs. It then fails in the test's process-identification observer,
before its first worker-loss stop. All seven profiles are removed. The observer
now tolerates unavailable main-module information while a process starts.
The [worker-lifetime follow-up](office-worker-lifetime.md) subsequently corrects
early client return after worker crashes and verifies all nine stop/recovery
cases. Production context journals and Office application-crash recovery remain
separate.


Residual child-access recovery
------------------------------

The restart-recovery review found that the previous owner verified revocation
only at each grant root. A child's explicit profile entry or protected inheritance
could therefore survive root revocation without preventing profile deletion.
The owner now checks the bounded, held child handles for any entry naming its SID
before applying a grant and after removing it. A residual entry retains the
profile and unresolved grant; it is reported with the affected child path. The
owner does not remove an unexpected child entry or restore a prior whole ACL.

The expanded ownership suite passes **72 checks** at
`.codex-temp/office-owner/ffe221767f7e4299b239c51612065024`; the matching stage
under `.codex-temp/office-isolation` contains the actual Windows access evidence.
Two new cases cover an explicit file entry and a protected child directory:

- Pre-existing child access refuses before root or child ACL mutation.
- Removing root access leaves the unexpected child entry intact and prevents
  profile removal. Unrelated permissions and authored file bytes are preserved.
- After the test resolves only its injected child entry, the same retained owner
  completes cleanup and removes the profile folder and registry mapping.

This is verified in-memory recovery. It does not establish concurrent tree/ACL
tampering resistance, power-loss durability or persisted restart recovery. The
earlier 90 interruption checks across nine cases are not a new run of this changed
owner.

The changed owner also passes **39 real-worker export checks** at
`.codex-temp/office-worker/34310b1d93a0439cb320d8984812535f`. All three generated
Word/Excel/PowerPoint documents export and clean up successfully. Independent
inspection at `inspection-5d5ccd76d31147a983df8cb8eb8ba0a6` passes qpdf structure,
authored text, page geometry and exact PDFium control pixels for all three PDFs.
The application and private-harness Release builds have zero warnings/errors.

`.codex-temp/office-child-access-verification.json` reconciles current source and
worker identities, source/candidate hashes and independent inspection. All four
distinct test profile identities are absent from Windows folders/mappings and
22,050 checked runtime/context ACLs. The ownership suite intentionally recreates
its one identity several times; four is a distinct-identity count, not a count
of profile-creation calls. Production candidate 1.1.0 was not modified. The
broader image/audio/PDF suites were not rerun for this Office-only owner change.


Directory sharing correction
----------------------------

The first ownership run let its disposable grant directory be renamed despite
an open attributes-only handle. Cleanup still addressed the opened directory
object, but the intended rename guard was false. Directory leases now request
`FILE_LIST_DIRECTORY` without delete sharing; the native host's configured-path
handles now request `GENERIC_READ`. The full-trust evaluation path helper is
corrected too. A handle identity check alone is not a demonstrated rename guard.

The first failed ownership stage is
`.codex-temp/office-isolation/14d187ee0eaa4d5589eadb0ffc83eb8b`.
The generated directory move stayed inside that stage. Its owned profile folder
and registry mapping were verified absent after exception cleanup. It is retained
as failed evidence. Following intermediate stages passed 39 and 42 checks before
the final junction-refusal cases; those counts are not combined with the final run.


Repeatable verification
-----------------------

With explicit authorization for disposable Windows profile metadata, run:

```powershell
.\tools\office-engine\Test-OfficeProfileOwnership.ps1 -CreateDisposableProfile
```

Without the opt-in flag the command refuses before creating a profile. It requires
the separate private checkout, restored build dependencies, Python and the existing
Windows x64 C++/.NET toolchain. It snapshots and builds the authored process probe,
builds the private harness linked to the exact public application owner source,
checks focused source stability and records binary/source hashes. It never loads
Office, installs an application, registers Explorer commands or contacts licensing.

The canonical run passes **45 checks**, covering:

- New profile creation, collision refusal, actual folder/mapping lifetime and
  idempotent disposal without affecting a later profile with the same name.
- Readable input, denied input writes, denied withheld-file reads and permitted
  output creation, verified by a real AppContainer child. Inputs have ordinary
  writable attributes, so the write refusal tests the access grant.
- Inheritance on existing/later files, hard-link and junction refusals without
  changing the other path's permissions, non-overlapping grants, and blocked
  directory replacement while leases are active.
- Preservation of unrelated ACL changes made after granting access, revocation
  on native-created files and released directory handles after cleanup.
- Abruptly terminating the worker alone while holding the exact live native
  child's process handle. Its job closes and the child stops; the profile remains
  owned by the surviving application test process until that process cleans it.
- Recreating the same deterministic SID with only binary access: earlier input
  and output locations remain denied, demonstrating actual grant revocation.

Canonical build/log evidence is
`.codex-temp/office-owner/cd62ab5d687548beb02be19f36d3ee23`;
the matching stage under `.codex-temp/office-isolation/` contains `results.json`,
native access results, the child identity marker and the profile name/SID. The
preceding final scratch run `ef9a92028c0240709289f27b70e676d4` also passed 45 checks.
Both the private harness and application Release builds have zero warnings/errors.

The native host's stronger path handle has a separate real-Office regression:
host build `.codex-temp/office-host/6cce2b202f5647b8bfbacfbb5c2dfefd`, supervisor
`lease-regression-5aa37468bcf94673a2311186b4ea51ee`, and case `cs68` beneath the
retained `1a880d93be994b9a9a3467bb77aa1ba3` isolation stage. All three exports pass
completion hash checks and independent structure/text/page geometry/exact-pixel
comparisons against retained `cs41` controls. Independent results are
`inspection-335477cdb16a4f1ba8b885deb0e11f94/results.json`;
completion evidence is `.codex-temp/office-lease-completions.json`.
This regression uses the evaluation grants, not the new production ownership
composition. Existing source/runtime and profile cleanup must remain verified
separately from successful rendering.

`.codex-temp/office-owner-final-verification.json` reconciles the canonical
source/build hashes, profile-folder/mapping removal, refusal without the opt-in
flag, three Office regressions, nine exclusive source/settings/PDF opens after
exit, all 19,332 runtime source/copy hashes and exact copied membership.


Next integration work
---------------------

The later [worker integration](office-worker-export.md) tests this owner around
actual Office exports. It fixes cleanup of engine-created cache paths exceeding
260 characters and expands the ownership matrix from 45 to 50 checks. Current
worker exports clean their profiles successfully; production persisted recovery
is still required.

Construct the production profile/grant/path context around the pinned Office
runtime, pass it to the worker, and connect completion to independent PDF
validation and app-owned transactional copy publication. Add persisted recovery
for application loss, cleanup failures and hostile staging contents. Resolve
calculation/font policies, broader document fidelity, content/network enforcement
and runtime adoption before enabling customer Office conversion. Prior foundation
counts are unchanged evidence, not a new run. No visible UI, installed lifecycle,
screen-reader, theme/DPI, live commerce or release acceptance is claimed here.

The durable ownership record must precede grant mutation and distinguish intended
changes from verified completion. Keep it outside directories writable by Office.
Bind recovery to the recorded directory objects and successfully created profile,
not only a reusable profile name or paths supplied by a journal. Verify process
termination again before revocation, and retain uncertain creation, identity or
cleanup states for review. Profile/grant cleanup records remain separate from
output publication records; neither an export completion nor a recovered profile
authorizes publishing an unvalidated PDF. Native integration and restart recovery
must establish these requirements.

The subsequent [ownership journal component](office-ownership-journal.md) now
implements bounded persistence and transition checks, including actual writer-loss
tests. Its journaled owner now records native profile/grant mutations and actual
worker-export cleanup, with 93 ownership checks and 42 worker checks. Safely
reclaiming the recorded profile after restart remains unfinished.

The subsequent [version-two profile binding](office-ownership-journal.md#profile-directory-binding)
records the actual Windows profile-directory identity and checks it before grant
revocation. A disposable replacement-folder test verifies refusal without changing
the retained original, mapping or grants. All 97 ownership checks, 42 actual worker
checks and three independent PDF inspections pass; the test profiles and grants
are removed. Earlier version-one records remain readable for review only. This
does not supply recoverable engine-process evidence or automatic restart cleanup.

The later [reopened-journal recovery](office-ownership-journal.md#profile-recovery-from-a-reopened-journal)
now reconstructs a cleanup-only owner after creator death, profile/job verification
and held grant-directory identity checks. Five disposable owner crashes pass
within 138 native ownership checks; all profiles and grants are removed, including
the retained case from an initial fixture failure. These are generated ownership
fixtures; recovery during actual Office rendering remains unverified.
