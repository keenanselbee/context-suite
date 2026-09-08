Installer Upgrade And Repair Recovery
=====================================

Status: recovery backend implemented; 52 local automated checks pass.
Native installation and Inno upgrade integration remain gated.

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

The recovery module is an independently tested backend, not a command that runs
against installed packages during routine verification. The current Inno layout
still uses `app/` and refuses upgrades. Enabling it requires versioned staging,
active-pointer-aware shortcuts/uninstall, migration from the initial layout and
native verification of Appx re-registration and file-in-use behavior. Do not
remove the guard or add an installer bypass on the strength of mock tests.

See [decision 0012](decisions/0012-inno-offline-installer.md) and the
[release checklist](release-packaging-goal.md).

Automated verification
----------------------

```powershell
./tools/release/Test-InstallerRecovery.ps1
./tools/release/Test-InnoInstaller.ps1
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

Remaining checks are explicit: native signed Appx behavior, registration target
location, stopped/running app and Explorer surrogate behavior, process-kill and
power-loss durability, full Windows user-profile/registry preservation and the
complete Inno install/update/repair/uninstall sequence. File-system fault
injection does not establish hardware power-loss guarantees.
