Context Suite
=============

Context Suite is a free, local Windows application with three File Explorer tools:

- **Analyze** explains a file's properties without changing it.
- **Convert** creates an explicitly selected format or texture representation.
- **Optimize** reduces size while retaining the current format by default.

Common actions use saved settings and complete quietly. Focused prompts handle
necessary choices; standalone launch opens Settings/help. There is no trial,
purchase, activation, or network licensing. Third-party notices are in Settings.

The source-available implementation lives in this repository. See the
[license terms](LICENSE.md) for permitted use; source availability is not an
open-source license. The ignored historical private repository is not a build
input. Selected engines and their source/notices remain required.


Project Status
--------------

Current status as of 2026-10-30:

| Area | State |
| --- | --- |
| Source | Free 1.6.2 application, built from the main repository |
| Verified candidate | Unsigned 1.6.2, built from clean source and archive-verified; 811 packaged PDF/Optimize checks passed |
| Existing local installation | 1.5.5, unchanged; all 2,915 installed payload files matched during migration verification |
| Release acceptance | Signing, native installer/upgrade lifecycle, clean-machine checks, and broader visible/accessibility acceptance remain open |
| External retirement | Free-product website copy is live; local private history is preserved under `z backup/proprietary/`; Polar retirement and remote archival remain open |

The [free transition record](docs/free-access-goal.md) owns exact source/archive
identities, test results, limits, and retained evidence. See the
[current roadmap](docs/roadmap.md) for remaining work and the
[capability matrix](docs/launch-capability-matrix.md) for supported format boundaries.
Source checks, candidate checks, and installed behavior are separate claims.
Historical milestones are in [project history](docs/project-history.md) and
[CHANGELOG](CHANGELOG.md).


Product Boundaries
------------------

- Analyze is read-only; media contents and selected paths stay local.
- Copies are the default. Explicit per-tool Settings consent permits future
  replacement only on verified paths/platforms; mandatory-copy exceptions remain.
- Only validated outputs are published. Original recycling happens after safe
  publication, with no fallback to permanent deletion.
- Transparency, color, animation, metadata, and document fidelity follow each
  workflow's stated limits. Unsupported cases receive an explicit result.
- Explorer integration stays minimal. One application and one on-demand worker
  coordinate each interactive user session.

The [product design](docs/product-design.md) owns the full product contract.


Developer Commands
------------------

Development targets Windows 11 x64, .NET 10, and the native toolchain documented in
[development](docs/development.md). From the repository root:

```powershell
./tools/Test-Repository.ps1
./tools/Test-ProductionSource.ps1
./tools/Test-Foundation.ps1 -Configuration Release
```

Close Context Suite before router tests and follow the documented test-concurrency
restrictions. These commands do not install the application.

For production builds, integration tests, and release candidates, follow the
[development workflow](docs/development.md) first. Default production output can
be the installed payload; use the documented isolation and fresh-version rules.
A command that does not register packages can still overwrite application files.
Installation, native recycle tests, desktop interaction, and publication remain
separate opt-in workflows.


Documentation
-------------

Start with the [documentation index](docs/README.md). Key guides:

- [Product](docs/product-design.md), [Analyze](docs/analyzer-design.md),
  [Convert](docs/converter-design.md), [Optimize](docs/optimizer-design.md), and
  [documents](docs/document-design.md).
- [Development](docs/development.md), [release packaging](docs/release-packaging-goal.md),
  and [repository maintenance](docs/repository-maintenance.md).
- [Decisions](docs/decisions/README.md), [style](docs/style-guide.md), and
  [commit conventions](docs/commit-style.md).
