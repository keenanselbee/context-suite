Optional Tool Installation
==========================

Analyze, Convert, and Optimize are independent setup choices, selected by default.
At least one must remain selected, including in silent setup. One offline installer
contains all three choices. Shared files are installed once, and only selected
Explorer identities or classic commands are registered.

Implementation and automated verification are complete. Signed native installation,
upgrade, and repair acceptance remain pending. Existing-install admission stays
closed; this change does not enable in-place component changes. Inno remembers
the previous component selection, but an existing installation still encounters
that admission gate. See [release packaging](release-packaging-goal.md).


Payload Policy
--------------

The application, worker, managed dependencies, assets, audio engine, and qpdf are
shared. Analyze needs audio/PDF probes; Optimize needs PNG, FLAC, and PDF engines.
Keep these complete verified groups instead of trimming individual DLLs by guess.
Convert additionally installs Office, PDF rendering/validation/dark-mode, AVIF,
HEIF, SVG, and WEM engines. Unknown engine directories stop packaging until their
component dependency is classified in `packaging/inno/InstallComponents.ps1`.

The complete candidate is verified before component projection. The builder
derives seven hash-bound release inventories and small installation markers from
that catalog. Generated Inno file entries use the same dependency policy, with
one matching descriptor and marker per selection. Selected release schema 2
requires explicit tools; legacy schema 1 always means all three. New installers
reject candidates without the installed-tool capability recorded in their
requirements. Original candidates and version receipts remain immutable.

The app reads its marker once at startup, rejects omitted direct commands before
queuing work, and hides settings for omitted processing tools. Shared probes stay
available to installed tools. Missing markers retain legacy/development behavior;
malformed markers fail with a reinstall message. The marker is installation
configuration, not a licensing or anti-tampering boundary. Licensing is unchanged.

The managed installation launcher validates the marker before starting the app.
Uninstall uses the release descriptor and verified signed packages to establish
ownership, so a missing, malformed, or changed marker does not trap an installed
copy. New installation still verifies the complete staged payload before
registration. Existing-install upgrade/repair admission remains gated.


Approximate Installed Sizes
---------------------------

These estimates use the preserved 1.4.8 application payload, before this feature's
small managed-code, marker, identity, and installer-metadata overhead. They are
planning estimates, not a newly packaged release. The builder records exact
payload bytes for every selection in each new installer receipt.

| Selection | Application payload | Practical estimate |
|---|---:|---:|
| Analyze | 43.6 MiB | 45-50 MiB |
| Optimize | 43.6 MiB | 45-50 MiB |
| Analyze + Optimize | 43.6 MiB | 45-50 MiB |
| Convert | 546.5 MiB | 545-555 MiB |
| Analyze + Convert | 546.5 MiB | 545-555 MiB |
| Convert + Optimize | 546.5 MiB | 545-555 MiB |
| All three | 546.5 MiB | 545-555 MiB |

Convert's Office engine accounts for about 417 MiB. Deselecting Analyze or Optimize
while retaining Convert saves little because their dependencies are shared.
The offline download still contains all payloads. Microsoft runtimes are separate
shared prerequisites: roughly another 171 MiB for the measured .NET runtime pair
when absent, plus Visual C++ runtime and installer/cache overhead. These are not
repository storage budgets or fixed product size limits.


Verification And Remaining Acceptance
-------------------------------------

Public tests cover selection parsing, command admission, component inventories,
and mocked modern/classic registration and recovery. All seven combinations remain
covered. `Test-InstallerSelections.ps1` passes 132 selected-install checks plus
104 legacy checks; `Test-ClassicInstaller.ps1` adds 30 classic checks.
`Test-InstallerComponents.ps1` passes 77 projection checks, or 79 with `-Compile`
and the pinned local Inno compiler. The latter compiles inert fixtures and never
runs the resulting installer. The installer suites also passed from an isolated
public-source snapshot without private or ignored build inputs.

The app has 79 focused selection checks, and the public foundation suite passed
5,015 contracts after the test scheduling correction below. A production build
passed without warnings or errors. A reduced
development payload built from current managed source and preserved verified
engines contains 256 files totaling 44,541,181 bytes (42.5 MiB), before installer
metadata. Real audio Analyze, FLAC optimization, and PDF optimization checks
passed against it, as did 98 PNG optimization and interruption checks. This is
development evidence, not a new reserved release. A repeat run exposed a console
test scheduling assumption in the existing Analyze/cancel contract; that test
now uses the existing UI dispatcher helper while retaining its cancellation and
source-preservation assertions. Five focused repetitions passed.

New engine families must declare their dependency group and extend the relevant
payload checks; do not silently drop an unclassified folder. Run these tests from
the repository root:

```powershell
./tools/Test-Foundation.ps1 -Configuration Release
./tools/release/Test-InstallerSelections.ps1
./tools/release/Test-ClassicInstaller.ps1
./tools/release/Test-InstallerComponents.ps1
# Optional local compilation only; does not execute or install its output.
./tools/release/Test-InstallerComponents.ps1 -Compile
```

Native acceptance must still cover signed first installation, Explorer command
visibility, installed-tool operations, uninstallation, prerequisite handling,
and interrupted setup in a disposable Windows environment. Upgrade/repair and
changing component choices remain behind their existing separate admission gate.
Mocked registration tests and an Inno compile do not establish native acceptance.
