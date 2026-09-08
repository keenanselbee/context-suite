Context Suite
=============

Context Suite is a local Windows application that makes common media
file operations available directly from File Explorer through three peer
context-menu commands:

- **Analyze** explains how a selected file is stored and which properties matter.
- **Convert** creates a deliberately selected format or texture representation.
- **Optimize** reduces file size while retaining the current format by default.

The project focuses on image and audio workflows that are understandable
without codec expertise. It should never hide meaningful quality, transparency,
metadata, or compatibility consequences.


Project Status
--------------

Product design and technical discovery are in progress. A native x64 shell
prototype now registers **Analyze**, **Convert**, and **Optimize** as independent
Windows 11 Explorer commands and hands the complete selection to a separate host
through a bounded, versioned request file. The standalone native prototype host
only confirms activation; production WPF activation supports the implemented
analysis/conversion workflows below.

Shared image-output safety and settings are implemented and locally verified.
The [goal evidence](docs/image-output-safety-goal.md) records the tested scope and
replacement restrictions. The completed PNG/JPEG/WebP conversion slice has tested
adapter/worker execution, a system-themed conversion planner, previews and
trial-gated safe publication and a passing bounded media/failure matrix.
The curated native engine is now integrated into production development builds,
with bounded BMP/TGA conversion bringing the catalog to twenty cross-format pairs.
The completed bounded [DDS slice](docs/dds-conversion-goal.md) adds public header analysis,
pinned CPU DirectXTex conversion, mip/color policies and safe publication: 27
conditional conversion pairs total. All 33 focused image/DDS desktop checks pass.
This is not release-ready: PNG optimization and paid activation remain planned.
An Inno Setup offline installer candidate now compiles. The
[upgrade/repair recovery backend](docs/installer-recovery.md) passes automated
failure tests; installer integration, trusted signing and clean-machine
acceptance remain pending. See the
[packaging goal](docs/release-packaging-goal.md),
[integration evidence](docs/bmp-tga-and-engine-integration.md)
and [remaining redistribution gates](docs/release-redistribution.md).
See the [conversion goal](docs/image-conversion-goal.md) for verified scope.


Developer Commands
------------------

Run these commands from PowerShell at the repository root:

```powershell
.\tools\Build.ps1 -Configuration Debug
.\tools\Test-ShellPrototype.ps1 -Configuration Debug -SkipBuild
.\tools\Install-ShellPrototype.ps1 -Configuration Debug -SkipBuild
.\tools\Test-InstalledShellPrototype.ps1
.\tools\Open-ShellPrototypeTestFolder.ps1
.\tools\Uninstall-ShellPrototype.ps1
```

The build requires Visual Studio 2026 with the x64 C++ desktop tools and Windows
11 SDK 26100. Generated binaries and package assets are written under
`artifacts/`. Installation registers three development-only sparse identity
packages, one for each Explorer root; it does not modify media-file associations.

See [shell prototype validation](docs/shell-prototype-validation.md) for the
automated evidence, observed layout, and optional manual host-dialog check.


Production Foundation
---------------------

The WPF application, shared core, on-demand worker, bounded activation queue,
and private-project composition now build. Convert has a working PNG/JPEG/WebP/BMP/TGA/DDS
planner and private worker adapter, with trial-gated safe publication. Analyze
reports bounded DDS headers; Optimize remains unimplemented. The bounded conversion acceptance
matrix passes; commercial release checks remain unfinished.

```powershell
./tools/Test-Foundation.ps1 -Configuration Release
./tools/dds-engine/Build-DdsEngine.ps1
./tools/Build-Production.ps1 -Configuration Release
./tools/Test-Foundation.ps1 -Configuration Release -Integration
./artifacts/production/Release/ContextSuite.Application.exe
```

The full build requires the compatible private checkout and staged, hash-verified
curated native inputs; see the [staging workflow](tools/curated-engine/README.md).
These commands do not
install or replace Explorer packages. See [development and validation](docs/development.md)
for prerequisites, public-only checks, IPC limits, and the remaining manual smoke
checks. CI workflows are authored; hosted execution requires publication.
See [foundation validation](docs/milestone-1-validation.md) for the local evidence.


Public Source And Commercial Direction
-------------------------------------

Context Suite is intended as both a co-op portfolio project and a paid Windows
utility. Most engineering is publicly reviewable; selected production
implementations live in the separate `context-suite-private` repository,
checked out at the ignored `proprietary/` path.

There will be one production application build, requiring the compatible private
checkout. A public checkout alone will not build the complete app. There is no
separate review/demo edition: portfolio evaluation will use the public code,
architecture, tests, and planned screenshots and demo video. A downloadable
commercial trial is planned for people who want to run the application.

Public components may be built and tested independently where supported. The
native prototype commands remain independent. Production composition now builds;
paid licensing is not implemented. The image conversion flow and its bounded
media/failure acceptance matrix are verified locally.

The commercial direction is a three-day trial followed by Polar license-key
activation, using hosted checkout without custom website accounts. Polar account
approval is user-confirmed; app integration remains planned. Media processing
remains local. Decision 0009 settles a 72-hour local trial starting at the first
confirmed valid conversion; its store, execution gate and conversion UI are tested
with isolated trial data. Paid offline-license
policy, pricing and source-license terms remain open.
See [build ownership](docs/decisions/0005-public-and-proprietary-builds.md) and
[commercial access](docs/decisions/0006-trial-and-purchase-access.md).
The [Polar integration plan](docs/polar-integration.md) records setup information
still needed and the implementation and release checks.


Product Boundaries
------------------

The accepted production foundation is Windows 11 x64, WPF on .NET 10 with MVVM,
one application per interactive user session, and one on-demand sequential
media worker. It retains the native Explorer extension and bounded request-file
activation, with local named pipes for application forwarding and worker
communication. Settings use versioned local JSON and immutable batch snapshots.
Process foundations are implemented; see [decision 0007](docs/decisions/0007-production-ui-and-processes.md)
and [development status](docs/development.md) for verification limits.

- Analysis is read-only.
- Conversion changes format only after the destination is explicit.
- Optimization retains the current format unless the user separately chooses a
  conversion.
- Source files are preserved by default.
- New output names use Windows-style suffixes, such as `gamma - Converted.png`
  and `gamma - Optimized (2).webp`. Explicit replacement and recycle-only cleanup
  are verified for ordinary local NTFS files on Windows build 26200 x64; other
  platforms/locations remain copy-only. See
  [decision 0008](docs/decisions/0008-output-naming-settings-and-replacement.md).
- Completed outputs are validated before they are published or replace anything.
- Explorer integration remains thin; media processing runs outside Explorer.


Documentation
-------------

- [Product design](docs/product-design.md)
- [Roadmap](docs/roadmap.md)
- [Release packaging and clean-machine goal](docs/release-packaging-goal.md)
- [Image output safety and settings: implementation evidence](docs/image-output-safety-goal.md)
- [Analyzer design](docs/analyzer-design.md)
- [Converter design](docs/converter-design.md)
- [Optimizer design](docs/optimizer-design.md)
- [Shell integration](docs/shell-integration.md)
- [Architecture](docs/architecture.md)
- [Desktop smoke tests](docs/desktop-smoke-tests.md)
- [Repository style guide](docs/style-guide.md)
- [Commit style](docs/commit-style.md)
- [Architecture decisions](docs/decisions/README.md)


Initial Scope
-------------

The first release path is intentionally narrow:

1. Shared settings, safe copies, and optional replacement: implemented and tested.
2. PNG/JPEG/WebP/BMP/TGA conversion and curated-engine integration: implemented
   and tested for explicitly bounded variants.
3. DDS analysis and bounded 2D conversion: implemented and locally verified
   under [decision 0011](docs/decisions/0011-dds-engine-and-texture-policies.md).
4. Optimize PNG files with explicit lossless and bounded lossy policies, then
   expand image coverage through tested capabilities.
5. Add common audio analysis and conversion after the image workflow is
   reliable.

Video, documents, cloud processing, AI editing, CD ripping, and arbitrary media
engine commands are not part of the initial scope.
