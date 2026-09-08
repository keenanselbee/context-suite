Reproducible Packaging And Clean-Machine Goal
============================================

Status: in progress; internal candidate tooling and Inno lifecycle integration
implemented; signing, native upgrade/repair and isolated-machine acceptance pending.
No commercial release approval.

The [installer lifecycle integration](installer-recovery.md) passes 38 orchestration
checks plus 52 recovery checks without Sandbox or a VM. Versioned extraction,
active launch/uninstall and recovery are wired; native existing-install admission
remains closed rather than being inferred from mocked platform tests.

Scope and boundaries
--------------------

Make production builds repeatable from identified public/private source and
approved engine inputs, produce a narrowly inventoried package, and verify the
customer installation lifecycle. Do not introduce new formats, Optimize, paid
activation, signing credentials, publishing or a second application edition.
Repeatable inputs and recorded outputs do not promise bit-identical native builds.

Implemented workflow
--------------------

1. Export the already reviewed image engine with
   `tools/curated-engine/Export-ProductionEngine.ps1`. The resulting
   `curated-win-x64.zip` contains exactly the pinned native DLL and its notice.
   This is a transport artifact, not a substitute for the archived source,
   link-map and redistribution evidence. No upload is performed by the script.
2. A new checkout imports it with
   `tools/curated-engine/Import-ProductionEngine.ps1 -Archive <zip>`.
   Reject extra entries, paths, duplicates, excessive sizes and any mismatch
   with the independently committed DLL/notice hashes. Never approve a new
   engine hash merely to make CI pass.
3. Build DDS from pinned source using `tools/dds-engine/Build-DdsEngine.ps1`.
   Retain its source/bridge/build-definition identity and native hash.
4. Run `tools/release/New-ReleaseCandidate.ps1`. Both repositories must be clean;
   the script builds Release and copies only the approved payload minus symbols
   and the obsolete development inventory into a new candidate directory.
   `-AllowDirty` is for local verification only and visibly records dirty source
   provenance; such output is not an exact committed-source release.
5. `release-manifest.json` identifies both commits, SDK/toolset, package
   dependencies, engines and every payload file's length and SHA256. The ZIP
   hash is printed and written to `archive.sha256` separately. The finished ZIP
   is extracted into fresh scratch output and revalidated. Hashes establish
   consistency, not authenticity.
6. `tools/release/Test-ReleaseCandidate.ps1 -Candidate <directory>` verifies the
   inventory and source-linked engine/notice guards. It requires the matching
   public/private checkout; it is not a customer-side signature validator.

All output stays under ignored `artifacts/` or `.codex-temp/`. Nothing is
installed, signed, published or registered. The ZIP is an unsigned internal
candidate, not a finished installer or approved customer download.

Private CI
----------

The private workflow takes an immutable approved public commit and a private
release tag containing `curated-win-x64.zip`. GitHub's job token reads that
private asset; no credential is embedded in the payload. A moved/replaced asset
cannot bypass the separately committed native/notice hashes. No arbitrary URL
or public pull-request input can supply an engine implementation.

The workflow imports the reviewed image engine, builds DDS, runs public/private
contracts and packaging checks, then exports only the candidate ZIP and revision
receipt within the private repository. It does not export source or PDBs.
Interactive UI and Explorer acceptance remain separate from hosted CI.

The scripts/workflow must first be reviewed, committed and pushed. The owner
must explicitly authorize uploading the engine artifact to the private release
before a hosted run can succeed. No upload or hosted run has occurred here.

Installer and runtime handoff
----------------------------

The current candidate retains the existing framework-dependent deployment:
Windows 11 x64 build 26100+, .NET 10 Windows Desktop Runtime x64, and a Visual C++
x64 runtime at least as recent as the recorded DDS toolset (the shell and
curated image engine use static C++ runtimes). The included
`requirements.json` links official Microsoft prerequisite sources;
`Test-Prerequisites.ps1` checks the machine without installing anything. This
is a prerequisite check, not proof of loading every native dependency.
See [Microsoft's deployment options](https://learn.microsoft.com/en-us/dotnet/core/deploying/)
and [Visual C++ runtime guidance](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist).

`tools/release/New-ShellIdentityCandidates.ps1` accepts an explicit candidate,
identity prefix, publisher and four-part version and uses SDK MakeAppx to build
three unsigned sparse identity packages. It preserves the existing peer-root
model without registering anything or choosing a final publisher. Generated
packages are separate from the candidate ZIP and record its manifest hash.
MakeAppx success is not installation/upgrade acceptance.

The owner approved Inno Setup with per-user installation, offline prerequisites
and manual installer-based updates. Website distribution comes first; Microsoft
Store is a possible later target. See [decision 0012](decisions/0012-inno-offline-installer.md)
for the accepted direction, license review and staged implementation boundary.

The publisher must match the eventual trusted signing certificate. Customer
installation must not enable Developer Mode, import a development certificate,
remove prototype identities implicitly, reset trials or delete user settings.
Microsoft documents [signed external-location packages](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps).
Production publisher/identity and signing remain open. Upgrade/repair remains
disabled until lifecycle recovery is implemented and verified.

Inno installer candidate
------------------------

```powershell
# Read a licensed, reviewed VC redistributable; downloads pinned Inno/.NET inputs.
./tools/release/Get-InstallerInputs.ps1 -VisualCppRedistributable '<vc_redist.x64.exe>'
./tools/release/Test-InnoInstaller.ps1
./tools/release/Test-InstallerLifecycle.ps1
./tools/release/Test-InnoInstaller.ps1 -Candidate '<candidate>' -IdentityDirectory '<identities>'
./tools/release/Build-InnoInstaller.ps1 -Candidate '<candidate>' -IdentityDirectory '<identities>' -UnsignedInternal
```

`installer-inputs.json` pins the signed Inno 7.1.0 installer, .NET Windows Desktop
10.0.11 x64 offline installer and Visual C++ 14.51.36247.0 x64 redistributable.
The Inno archive digest comes from its immutable GitHub release; the .NET SHA512
comes from Microsoft's release metadata. VC input is supplied explicitly from
the reviewed Visual Studio distribution and verified by hash and Microsoft
signature; there is no floating VC download. Existing files with mismatched
hashes cause failure rather than silent replacement. Review and repin updates.

The provisioning command uses official Inno portable mode under ignored
`artifacts/installer-inputs/`. It does not install runtimes, register the compiler,
create shortcuts or change PATH. The pinned archive's license permits commercial
use under its conditions; upstream requests voluntary commercial-license
purchase. The compiler's unlicensed banner is not substituted for the actual
license text or upstream FAQ. Microsoft runtime redistribution entitlement remains
part of the separate release review.

The builder verifies the candidate, all three identity manifests and hashes,
runtime pins and compiler identity, then builds a single offline EXE under
`artifacts/installer-candidates/`. It records the exact staged files and installer
SHA256 in `installer-receipt.json`. The installer-specific preflight is separately
inventoried and may be newer than the preflight archived in the input candidate.
The compiler is not part of the customer payload.

This output is visibly an **internal lifecycle candidate**, not a customer
release. `-UnsignedInternal` allows compile verification only; the resulting
installer refuses unsigned/untrusted MSIX packages. Without that flag the builder
also rejects unsigned identities. Signing the installer/application, stable
identities and signed-output provenance remain unimplemented release gates.

Setup detects existing/prototype registrations before prerequisites, performs
first-install registration with compensation on failure, and aborts uninstall
file removal if unregistering owned packages fails. Registration failure retains
the installed files and uninstaller, displays an incomplete result and exits 20.
No broad deletion or user-data cleanup is authored. Existing directories/packages
are rejected: rerunning setup is not yet repair or upgrade. Native Windows
acceptance is required before relaxing these restrictions.

Acceptance checklist
--------------------

- [x] Exact reviewed image-engine transport and fail-closed import.
- [x] Private CI provisions required engines before production compilation.
- [x] Clean-source gate, unsigned candidate ZIP, file inventory and no symbols.
- [x] Explicit runtime prerequisites and read-only host preflight.
- [x] Unsigned sparse identity packaging verified with the Windows SDK.
- [x] Fresh local source/output directories build using only approved engine
  input and pinned DDS download; image/DDS contracts pass. This includes local
  working changes and shares the installed SDKs/NuGet cache.
- [ ] Clean committed checkouts/hosted CI reproduce a candidate using the private asset.
- [x] Inno Setup, per-user/offline installation and manual updates approved.
- [x] Internal EXE compilation and mocked registration/failure contracts.
- [x] Versioned Inno extraction, active launch/uninstall and recovery orchestration;
  repository-only integration tests. Native existing-install admission stays closed.
- [ ] Stable production identities and signing provider selected.
- [ ] Isolated clean Windows install, missing-runtime handling and app startup.
- [ ] Installed modern Explorer roots, image/DDS conversion and source safety.
- [ ] Upgrade, interrupted/failed installation, repair and uninstall; preserve
  settings/trial/history and leave no duplicate commands or broken old install.
- [ ] Final signatures, notices/redistribution review and release-only inventory.

Local evidence (2026-09-07)
--------------------------

Release compilation succeeds with zero warnings/errors. A dirty-marked internal
candidate is produced under `artifacts/release-candidates/` and passes inventory
validation. Fourteen packaging checks pass (valid engine/candidate, missing or
duplicate entries, traversal, wrong casing, additional entries, wrong native
hash, extra candidate file, altered candidate hash, missing worker, duplicate
inventory, unsafe inventory path and required Explorer asset removed together
with its inventory entry). The runtime preflight
passes on the development machine only. No clean VM/Sandbox runner was found
through the available local tooling; Windows features were not enabled.
SDK MakeAppx also successfully builds all three unsigned identity packages using
an explicitly temporary `ContextSuite.PackagingTest` identity and test publisher.
No certificate is created or trusted and these packages are not installed.

`tools/release/Test-FreshSourceBuild.ps1 -EngineArchive <zip>` archives both
committed source trees into a new ignored workspace, imports the reviewed engine,
downloads/builds pinned DDS, compiles production and runs real image/DDS adapter
contracts. `-IncludeWorkingChanges` overlays authored local edits for verification
before committing and records actual source-file hashes. Incidental untracked
root logs are not included. It makes no commits or registration changes and
retains source/evidence only under `.codex-temp/`, not a distributable artifact.
The successful local run is
`.codex-temp/fresh-source-build/8ea7dcdd7fad489bb87b0034e45577f6/verification.json`;
339 image/adapter contracts and the DDS suite pass with freshly built binaries.
This proves no dependence on the original output/cache directories, not an
independent machine, clean committed-source release or hosted CI pass.

Signing and [redistribution gates](release-redistribution.md) remain separate.
The overall goal is not complete until installer and clean-machine evidence exist.

Inno evidence (2026-09-07)
-------------------------

Inno 7.1.0 compiles the offline installer, including the Pascal-script lifecycle
hooks. Twenty-five installer contracts pass: fresh/partial registration,
failure at each addition, mutation-before-error compensation, rollback failure,
foreign/newer package protection, removal failure, per-user/theme/no-cleanup
defaults, independent prerequisite checks and rejected unsigned/mismatched
build inputs. Appx calls are mocked: these are not native lifecycle passes.
The compiler and offline inputs were staged inside `artifacts/`; no runtime,
Context Suite installation, certificate trust or Explorer registration changed.
The earlier first-install EXE is retained under
`artifacts/installer-candidates/504e2bd245cd49c5bd26ee2061202100/` (84,328,063 bytes);
its receipt records SHA256
`87967ECFC9FFEA712EBED23F9D46F9BDB4D6B36781B0D7766CE76DAD69A9680A`.
The existing fourteen release-packaging checks and 36-document repository
validation also pass. Desktop/VC/combined prerequisite detection passes on the
development host; missing-runtime execution remains an isolated-machine test.

Next acceptance requires an isolated Windows environment and explicitly
authorized test signing/trust there, or production-signed packages. Do not trust
a test certificate on the development machine to bypass this gate.

The subsequent lifecycle integration candidate compiles at
`artifacts/installer-candidates/cffbe0cfd639417cb1062c69c0cd6a89/`.
Its receipt inventories the new helper scripts, versioned release template and
application/package files, and records `nativeUpgradeAdmission: false`.
See [integration evidence and limits](installer-recovery.md). This wraps the
previous dirty-marked application candidate; it is not a clean-source release.
