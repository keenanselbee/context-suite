Curated Engine Prototype
========================

Native build and staging tooling for [decision 0010](../../docs/decisions/0010-first-release-formats-and-curated-engine.md).
The evaluated candidate is now integrated into production development packaging.
See the [integration evidence](../../docs/bmp-tga-and-engine-integration.md) and
the historical [evaluation report](../../docs/curated-engine-prototype.md).

Production staging
------------------

```powershell
./tools/curated-engine/Stage-ProductionEngine.ps1 -RunName prototype-2
./tools/Build-Production.ps1 -Configuration Release
./tools/curated-engine/Test-ProductionPackaging.ps1
```

Staging requires the prepared/built candidate and its source evidence. It checks
the exact DLL and notice hashes in `production.json`, then copies approved inputs
into ignored `artifacts/engines/curated-win-x64`. The private project excludes
NuGet native assets and fails if these inputs are missing; the production script
checks identity, notices and the file/package allowlist. There is no stock fallback.
New checkouts must build/review a candidate first. Native rebuilds are not
byte-identical: an unreviewed new hash cannot replace the recorded selection.
Production staging does not install software or clear commercial distribution.

Isolated candidate workflow
---------------------------

Requirements: compatible private checkout, existing Release production and test
outputs, Windows 11 x64, .NET 10 SDK, Visual Studio 2026 x64 C++ tools, curl.
Preparation downloads pinned upstream build inputs; it does not install tools.
All generated native files, caches, test payloads and evidence stay under
`.codex-temp/curated-engine/`. These candidate commands do not overwrite the
staged production engine; only the explicit staging command above changes it.

From the repository root, with a fresh run name:

```powershell
./tools/curated-engine/Prepare-CuratedEngine.ps1 -RunName candidate-3
./tools/curated-engine/Build-CuratedEngine.ps1 -RunName candidate-3
./tools/curated-engine/Prepare-CuratedTests.ps1 -RunName candidate-3 -TestName verified-output
./tools/curated-engine/Test-CuratedGuards.ps1 -RunName candidate-3
./tools/curated-engine/Test-CuratedEngine.ps1 -RunName candidate-3 -TestName verified-output
```

Close Context Suite first. Add `-Desktop` to the last command only on an unlocked
desktop to run the conversion UI suite after integration tests. It opens test
windows and may leave its disposable Explorer folder open; it does not restart
Explorer or close unrelated windows. Tests use their existing isolated trial
stores, not the real application trial. Do not run other app tests concurrently.
The runner holds the existing desktop-smoke lock. Interactive focus and Explorer
folder recognition have failed on reruns; see the report, and retain failures
instead of interpreting earlier green evidence as reliable unattended automation.
Logs and a success summary are retained under `verification-<id>`. A timeout stops
the owned test runner; inspect any remaining test child processes before retrying.

This recipe uses **previously built managed outputs**. It is not a substitute for
rebuilding changed application/tests using the documented development commands.
Record that managed revision/output provenance alongside any future release.
Preparation deliberately copies top-level files only: NuGet runtime folders can
otherwise load the stock engine instead of the candidate. Each prepared engine
and worker payload is checked for one native DLL and the curated notice hash.

For the separate loaded-module and BMP/TGA coder-availability probe:

```powershell
dotnet build ./tools/curated-engine/Probe/CuratedEngine.Probe.csproj -c Release
$run = Join-Path (Get-Location) '.codex-temp/curated-engine/candidate-3'
$probe = Join-Path $run 'probe'
New-Item -ItemType Directory -Path $probe
Get-ChildItem ./artifacts/managed/bin/CuratedEngine.Probe/Release/net10.0 -File |
    Where-Object Name -NotLike 'Magick.Native*' | Copy-Item -Destination $probe
Copy-Item (Join-Path $run 'verified-output/engine/Magick.Native-Q16-x64.dll') $probe
$hash = (Get-Content (Join-Path $run 'build-result.json') -Raw | ConvertFrom-Json).sha256
& (Join-Path $probe 'CuratedEngine.Probe.exe') (Join-Path $run 'coder-probe.json') $hash
```

The probe checks the actual loaded module hash and coder declarations, not BMP/TGA
variant correctness or application support. It intentionally does not expand the
application's format allowlist.

Build inputs and upkeep
----------------------

`Prepare-CuratedEngine.ps1` pins archive URLs and SHA256 hashes.
`dependency-sources.json` records the source inputs of the upstream prebuilt
dependency archive; these static libraries are **not rebuilt locally**.
Preparation copies this inventory into new workspaces. `preparation.json` records
archive identity and original/patched link-header hashes. The build records DLL
identity in `build-result.json`, with configuration, linker map and verbose logs
retained beside it. The build status stays `built-not-verified`; separate test
records supply verification rather than rewriting historical build evidence.

Only 29 hard-coded native link lines are removed. Generated build configuration
keeps Q16, no HDRI/OpenMP, static runtime, and the existing OpenCL option; the
small MSBuild overlay enables map evidence and sets the native library name.
Codec algorithm sources are unchanged. An upstream update requires fresh pins,
review of this overlay and dependencies/notices, then two fresh builds and tests.
The script discovers installed C++ tools rather than provisioning a hermetic
toolchain. Record their version; successful rebuilds are not a promise of
byte-identical DLLs.

`New-CuratedNotices.ps1` combines upstream Apache licensing, the curated generated
ImageMagick/dependency notices and a hash-pinned WebP patent grant. Never copy the
stock bundle's notice file into a curated release. Neither this file nor a passing
guard certifies licensing or patent clearance.

`Test-CuratedPayload.ps1` checks the known native binary hash, exactly one engine,
the static library map owners, configured delegates and notices. It is a bounded
native-component guard, not a complete application SBOM, signature verifier or
proof against maliciously forged build evidence. An eventual release pipeline
must also inventory every packaged file and bind evidence to the final artifact.
