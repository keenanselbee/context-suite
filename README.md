Context Suite
=============

Context Suite is a local Windows application that makes common media
file operations available directly from File Explorer through three peer
context-menu commands:

- **Analyze** explains how a selected file is stored and which properties matter.
- **Convert** creates a deliberately selected different format.
- **Optimize** reduces file size while retaining the current format by default.

The project focuses on image and audio workflows that are understandable
without codec expertise. It should never hide meaningful quality, transparency,
metadata, or compatibility consequences.


Project Status
--------------

Product design and technical discovery are in progress. A native x64 shell
prototype now registers **Analyze**, **Convert**, and **Optimize** as independent
Windows 11 Explorer commands and hands the complete selection to a separate host
through a bounded, versioned request file. The host currently confirms activation
only; it does not analyze or transform media yet.

The first media vertical slices will analyze DDS files, optimize PNG files, and
convert common PNG, JPEG, and WebP images.


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
commands above currently build only the native shell prototype; production
composition, licensing, and media processing are not implemented yet.

The commercial direction is a three-day trial followed by website login and
purchase validation. Media processing remains local. Trial start timing and
offline-license policy are still open, as are pricing and source-license terms.
See [build ownership](docs/decisions/0005-public-and-proprietary-builds.md) and
[commercial access](docs/decisions/0006-trial-and-purchase-access.md).


Product Boundaries
------------------

The accepted production foundation is Windows 11 x64, WPF on .NET 10 with MVVM,
one application per interactive user session, and one on-demand sequential
media worker. It retains the native Explorer extension and bounded request-file
activation, with local named pipes for application forwarding and worker
communication. Settings use versioned local JSON. These choices are accepted,
not implemented; see [decision 0007](docs/decisions/0007-production-ui-and-processes.md).

- Analysis is read-only.
- Conversion changes format only after the destination is explicit.
- Optimization retains the current format unless the user separately chooses a
  conversion.
- Source files are preserved by default.
- Completed outputs are validated before they are published or replace anything.
- Explorer integration remains thin; media processing runs outside Explorer.


Documentation
-------------

- [Product design](docs/product-design.md)
- [Roadmap](docs/roadmap.md)
- [Analyzer design](docs/analyzer-design.md)
- [Converter design](docs/converter-design.md)
- [Optimizer design](docs/optimizer-design.md)
- [Shell integration](docs/shell-integration.md)
- [Architecture](docs/architecture.md)
- [Repository style guide](docs/style-guide.md)
- [Commit style](docs/commit-style.md)
- [Architecture decisions](docs/decisions/README.md)


Initial Scope
-------------

The first release path is intentionally narrow:

1. Analyze DDS headers and report exact format information without guessing.
2. Optimize PNG files with explicit lossless and bounded lossy policies.
3. Convert PNG, JPEG, and WebP while handling transparency and metadata safely.
4. Add common audio analysis and conversion after the image workflow is
   reliable.

Video, documents, cloud processing, AI editing, CD ripping, and arbitrary media
engine commands are not part of the initial scope.
