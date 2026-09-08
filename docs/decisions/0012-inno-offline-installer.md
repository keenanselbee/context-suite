Inno Setup And Offline Website Distribution
===========================================

Status: accepted direction; internal lifecycle integration implemented, native acceptance pending
Date: 2026-09-07

Context
-------

The owner approved Inno Setup after comparing conventional installers and full
MSIX. Website distribution comes first; Microsoft Store is a possible later
channel. Preserve one app, three peer Explorer identities, modest licensing and
simple deployment. Do not create a separate Store edition or account system.

Decision
--------

- Use pinned Inno Setup 7.1.0 x64, per-user installation under
  `%LOCALAPPDATA%\Programs\Context Suite`, with one application uninstall entry.
- Preserve the three sparse identities from decision 0003, pointing at one
  shared application payload. Final package names/publisher must be fixed against
  the signing identity before customer installation is enabled.
- Keep framework-dependent .NET deployment for this iteration. Bundle the
  official offline .NET 10 Windows Desktop x64 and Visual C++ x64 installers;
  detect each independently and run only missing prerequisites. Prerequisite
  installation may need administrator consent; the application and Appx
  registration remain in the original user's non-elevated context.
- Never download dependencies during customer setup, import a certificate, enable
  Developer Mode, restart Explorer, forcibly close apps or reset user data.
- Target installer-driven manual updates first; do not add an auto-updater.
  Upgrade/repair is not enabled until its rollback and interruption tests pass.
- Use system-selected light/dark installer appearance. The application's live
  theme behavior remains independent and unchanged.

Implementation boundary
-----------------------

The initial internal installer deliberately supports only a fresh installation.
It rejects existing installation directories, any of its registered identities,
and known prototype packages before installing runtimes or application files.
It does not remove development registrations automatically.

The [upgrade/repair backend](../installer-recovery.md) uses separately staged
immutable release directories, an active-release pointer and a durable recovery
journal. Inno now stages versioned files and calls the shared lifecycle coordinator.
The Start menu resolves the active pointer; uninstall uses the active release's
identity/version, not the most recently attempted installer version. A bootstrap
journal covers failed first registration and an uninstall journal supports retry.
The mocked integration/failure matrix passes without Sandbox or a VM. Admission
still rejects existing directories: native acceptance is required before enabling
upgrades or rerunning incomplete setup. No automatic legacy-layout migration.

Registration verifies all three results and compensates newly added packages if
one fails, including an Add operation that changes state before returning an
error. This is not an atomic Windows transaction. Inno's post-install phase is
past its file rollback boundary: registration failure returns exit code 20,
shows an incomplete-install message, and retains files plus the uninstaller for
recovery. If compensation fails, retain files supporting remaining registrations.
Uninstall removes exact current-user names/publisher/version only, before file
removal; an unregistration error aborts file cleanup. User settings, trial state,
license data and media are outside its cleanup rules. Inno's accumulated file
ledger owns versioned payload removal; only five explicitly named installer
metadata files use `UninstallDelete`. Unknown files/temporary journal remnants
are retained rather than swept up. A per-user process-lifetime mutex serializes
setup and uninstall; the helper's exclusive lock protects registration/pointers.

Abrupt process termination, power loss, in-use shell DLLs and native uninstall
exception handling still require isolated Windows evidence. Do not advertise
transactional upgrade/repair or zero-leftover uninstall from mocked tests.

The compiler can produce an explicitly unsigned internal artifact using unsigned
test identities. This is not an install-time bypass: setup still rejects
untrusted identities. No release signing or commercial-clearance switch exists.

Consequences and alternatives
----------------------------

One offline EXE can later serve website and Store distribution. Store EXE
submission requires trusted signing of the installer and included PE files,
silent setup and immutable versioned HTTPS URLs; it does not provide automatic
updates. Full MSIX offers Store-managed distribution but requires separate
validation of our multi-identity deployment. WiX adds complexity we do not need
for the initial consumer installation; an update framework can be considered
later if manual updates become insufficient.

Signing changes executable hashes. A future signing stage must preserve the
unsigned engine provenance and record/revalidate signed output separately; do
not replace the curated native source pins with post-signing hashes merely to
pass today's candidate checker.

Sources
-------

- [Inno Setup download and signed publisher](https://jrsoftware.org/isdl.php)
- [Inno license](https://jrsoftware.org/files/is/license.txt) and
  [commercial-license FAQ](https://jrsoftware.org/isorder.php): commercial use is
  permitted under the license conditions; purchase is requested, not strictly
  required. The pinned archive's license was also read. No purchase was made.
- [Inno installation order](https://jrsoftware.org/ishelp/topic_installorder.htm)
- [Microsoft EXE/MSI requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements)
- [Microsoft distribution paths](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path)

See the [packaging goal](../release-packaging-goal.md) for commands and evidence.
