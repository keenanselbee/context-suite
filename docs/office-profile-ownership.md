Office Profile And Grant Ownership
==================================

The application now contains an `OfficeSandboxOwner` component that owns a fresh
Windows AppContainer profile and explicitly granted directories. It is separate
from the private worker's [managed process job](office-managed-launcher.md).
The component has native file-access and worker-crash evidence, but is not yet
connected to a customer Office command or production worker request.


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

Workers must stop before profile cleanup. If tree/access cleanup cannot be
verified, the owner retains its remaining grants and profile and reports their
locations/name for a retry; it does not claim cleanup succeeded. This is an
in-memory retry boundary, not persisted application-crash recovery. Cleanup of
hostile linked trees, concurrent external ACL/tree tampering, cleanup failure
recovery and full-runtime grant costs still need dedicated acceptance.


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

Construct the production profile/grant/path context around the pinned Office
runtime, pass it to the worker, and connect completion to independent PDF
validation and app-owned transactional copy publication. Add persisted recovery
for application loss, cleanup failures and hostile staging contents. Resolve
calculation/font policies, broader document fidelity, content/network enforcement
and runtime adoption before enabling customer Office conversion. Prior foundation
counts are unchanged evidence, not a new run. No visible UI, installed lifecycle,
screen-reader, theme/DPI, live commerce or release acceptance is claimed here.
