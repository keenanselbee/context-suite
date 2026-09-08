Installer Upgrade And Repair Recovery
=====================================

Status: Inno lifecycle integration implemented; 42 orchestration checks and 52
recovery checks pass locally. Native existing-install admission remains gated.

The owner approved implementing and automatically testing recovery without
Sandbox or a manually maintained VM. Windows Home is not a development blocker.
Mocked Appx tests cannot establish clean-machine or native lifecycle acceptance.

Recovery contract
-----------------

- Stage a new payload in a different immutable `releases/<id>` directory. Never
  overwrite the active payload. IDs are 32 hexadecimal characters, not paths.
- Each release has a bounded descriptor with the same three package names and
  publisher, a four-part version, a complete application/package file inventory
  and SHA256 values. Verify the next payload before registration. Retain signed
  previous MSIX inputs for compensation; repair can replace damaged application
  files using a separate verified copy of the exact same release.
- Higher versions are upgrades. Equal versions are repairs only if inventories
  and package hashes are identical. Reject downgrades and identity changes.
- Write a recovery journal before changing Appx state. Use an exclusive lock
  and atomic JSON replacement for the active-release pointer and journal.
  Pin both descriptors' hashes in the journal and reject changed descriptors on
  recovery. Package manifests must match descriptor names/version/publisher.
- Remove only exact owned old/new package versions for the current Windows user.
  Register the next three identities against the new application directory,
  verify them, then commit the active pointer. Do not force-close processes.
- On failure before commit, restore the previous registrations and retain both
  payloads. If recovery fails, keep a pending journal and report that repair is
  required. Retrying recovery must be idempotent. Restoring a previously damaged
  payload's registrations is not a claim that the application is healthy.
- If interrupted after pointer commit, verify the next release and registrations
  before finalizing the journal; do not blindly downgrade a committed update.
- Never delete user settings, trial/license state or media. Payload cleanup and
  native Inno upgrade/uninstall integration remain separately gated.

Integration boundary
---------------------

Inno now extracts into `releases/<id>/app` and `releases/<id>/packages`, writes a
bounded release descriptor, and invokes the shared lifecycle coordinator. Each
setup process derives a stable 32-hex stage ID from its unique temporary directory;
this is a directory identifier, not a security hash. Existing stage directories
are never overwritten. The builder inventories the exact staged application and
three package files and includes the recovery/lifecycle helpers.

A stable Start menu shortcut requests a hidden `Launch-Active.ps1` helper,
which resolves the active executable, checks its hash and refuses pending recovery
or uninstall. It does not repair automatically; actual launch appearance still
needs native verification. Explorer
continues using the selected release's sparse-package external location.

First installation records `bootstrap.json` before file extraction. Registration
commits `active.json` only after all three roots verify. The coordinator can retry
an interrupted first registration; incomplete extraction cannot register anything.
Uninstall uses the active version (even after an attempted newer installation),
or bootstrap identity when no pointer exists. `uninstall.json` blocks launching
and enables retries after partial unregistration without resurrecting packages.
Inno's own file ledger removes installed files; only `active.json`, `bootstrap.json`,
`recovery.json`, `recovery.lock` and `uninstall.json` have exact metadata cleanup
entries. There is no recursive sweep, media cleanup or old-payload pruning.

Setup and uninstall acquire one per-user named mutex for their entire process
lifetime. Registration and launch also use the recovery file lock. These guards
do not claim to prevent an already running app/Explorer from holding DLLs open.

The shipping Check/Stage admission function still rejects every existing root,
including a partially installed root, with an actionable explanation. There is
no command-line bypass. The coordinator's retry/update behavior is verified below,
but rerunning the compiled setup is deliberately NOT yet an upgrade/repair path.
Legacy flat-layout and prototype registrations require explicit removal, not
automatic migration. Native signing, re-registration, file-in-use behavior and
Inno cancellation/uninstall-log behavior must be verified before opening this gate.
An interruption before Inno creates its uninstaller may require owner-assisted
cleanup; neither automatic recovery nor zero-leftover uninstall is promised.

See [decision 0012](decisions/0012-inno-offline-installer.md) and the
[release checklist](release-packaging-goal.md).

Automated verification
----------------------

```powershell
./tools/release/Test-InstallerRecovery.ps1
./tools/release/Test-InnoInstaller.ps1
# Superset: runs the recovery matrix once, then lifecycle orchestration checks.
./tools/release/Test-InstallerLifecycle.ps1
```

The recovery suite uses actual files, package ZIPs, atomic JSON replacement,
exclusive file locks and a junction rejection fixture under a unique
`.codex-temp/installer-recovery/` directory. Appx and signature validation calls
are replaced by script-scoped mocks. No test certificate is created or trusted,
no package is registered and no installed application is changed. Fixtures are
retained for diagnosis, including a junction whose target is inside the same
temporary test tree.

Fifty-two checks pass: upgrade, exact-release repair from a damaged old app copy,
partial registration repair, faults before/after every removal/addition,
pointer/journal commit failures, rollback failure/retry, reconstructed interrupted
transactions, idempotent recovery, invalid metadata/signatures/hashes/manifest,
foreign ownership/version, downgrade, inconsistent committed/rolled-back journals,
changed same-version payload, concurrent
operations, reparse paths and unchanged user data. Local evidence:
`.codex-temp/installer-recovery/c0689920bb524e93ba9d61559a7427d8`.
The public workflow now invokes the safe installer/recovery tests; hosted
execution has not been run here.

The integration superset adds 42 checks: first install, immutable staging,
active launch, upgrade, repair after later app damage, failure compensation,
uninstall retry, older-installer/active-newer-version handling, pending launch,
exclusive locks, corrupted/incomplete extraction, foreign identity, bootstrap
cleanup authorization, legacy rejection, unchanged user data, and source wiring.
The staging coordinator now validates incoming publisher/package names before
pending recovery, then checks the resolved active identity again afterward.
Four regression cases cover both identity mismatches with the pointer before or
after commit, asserting no Appx calls or changes to registrations, file/directory
inventory, file hashes, last-write times or attributes. See current
[hardening evidence](release-packaging-goal.md#installer-hardening-evidence).
The stage copier simulates Inno extraction using real fixture files; it does not
execute Inno's native file ledger. The test explicitly keeps Appx/signature calls
mocked. Public CI calls the superset instead of running the backend matrix twice.
Inno 7.1.0 successfully compiles the updated Pascal hooks and offline payload;
see the [packaging receipt location](release-packaging-goal.md).

Implementation references: [Inno lifecycle callbacks](https://jrsoftware.org/ishelp/topic_scriptevents.htm),
[exact uninstall cleanup entries](https://jrsoftware.org/ishelp/topic_uninstalldeletesection.htm),
and [Windows named mutex semantics](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-createmutexw).

Remaining checks are explicit: native signed Appx behavior, registration target
location, stopped/running app and Explorer surrogate behavior, process-kill and
power-loss durability, full Windows user-profile/registry preservation and the
complete Inno install/update/repair/uninstall sequence. File-system fault
injection does not establish hardware power-loss guarantees.
