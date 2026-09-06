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
automated evidence and the remaining Explorer smoke check.


Product Boundaries
------------------

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
