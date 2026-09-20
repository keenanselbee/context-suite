Office Chart Renderer Repair
============================

Status: draft source patch prepared and statically checked; not compiled,
executed, adopted or accepted. The [chart-loss regression](excel-layout-acceptance.md)
still fails on the current candidate. This proposal keeps chart fidelity in the
[active goal](broad-file-support-goal.md) and keeps `DisableActiveContent=true`.


Evidence and proposed change
----------------------------

The retained runtime's `program/version.ini` identifies build
`8221e31b3ac356a1623c672912a3d2b492f7e3d1`, LibreOffice 26.2.6. The
[source manifest](../tools/office-engine/patches/chart-source.json) records nine
independently downloaded upstream files at that exact commit with byte lengths
and SHA-256 identities. The originals remain in
`.codex-temp/office-chart-source-175b82e333ab4edcad90919390a6e749/originals`.
No reference repository or private implementation source is included.

The pinned configuration declares one current internal chart class,
`12DCAE26-281F-416F-A234-C3086127382E`, with the internal embedded-object factory
and `com.sun.star.chart2.ChartDocument` service. The
[draft patch](../tools/office-engine/patches/allow-native-chart-rendering.patch)
changes three upstream embedded-object files:

- Allow only that exact chart class through the **new-object** factory path.
  Require the internal factory; do not allow a Windows OLE fallback.
- Permit only `RUNNING` for that class and chart service, with no link URL.
  Read the object fields while holding the existing mutex.
- Keep the existing permission to return objects to `LOADED`. Do not permit
  active, in-place-active or UI-active states under the restrictive policy.

Existing-entry, media-descriptor and user-init import paths stay blocked.
Windows OLE factories, DDE, external-link update paths, macro controls and Python
controls are unchanged. This describes the proposed source change, not tested
runtime enforcement. A forged class ID, unexpected factory/service, linked chart
or indirect activation must still fail in the eventual native tests.

The upstream setting blocks both OLE and DDE. The separate `DisableOLEAutomation`
setting only controls its automation service entry point; it is not an equivalent
replacement. The existing document preflight is not a whole-package safety scan.
Neither turning off the broad control nor trusting the file extension is the
selected repair.


Static verification
-------------------

```powershell
python -B tools/office-engine/Test-OfficeChartPatch.py `
  --source-id 175b82e333ab4edcad90919390a6e749
```

The tool validates the exact source snapshot, applies the patch to a fresh owned
copy, checks the chart's configured factory/service, compares untouched refusal
function bodies, and reverses a separate copy back to the original bytes. It
does not compile, install, change production pins or load the renderer.

Ten checks pass in
`.codex-temp/office-chart-patch-4725775edd8d4f008a3716a5a231530c/results.json`.
The earlier attempt `office-chart-patch-4244ed3959634eb29941f0905aa08a16`
failed exact restoration because Git converted upstream LF to CRLF. It remains
retained. Per-command Git settings now preserve upstream line endings; no global
Git configuration changed. The commit whitespace check also caught a trailing
blank context line in the patch; extending that hunk to the next source line
removed the warning without changing the proposed C++ bytes. The fresh static
replay above passes. These source checks do not prove native behavior.


Build prerequisites and authorization
------------------------------------

The pinned upstream README documents a Windows build using WSL as a helper and
Visual Studio. Its configure script also contains a Cygwin route and recognizes
Visual Studio 2022 and 2026. This machine has both Visual Studio generations, but
`wsl --list --quiet` reports WSL is not installed. No Cygwin was found on PATH or
at `C:\cygwin64`; this is not an exhaustive search of other drives.

The earlier proposed WSL/Ubuntu route is superseded by the owner's 2026-09-20
request to evaluate a less invasive native Windows route. WSL is not a product
requirement. The owner authorized prerequisite investigation, then approved the
concrete native setup below on 2026-09-20. Cygwin and standalone build tools are
now in the approved G: workspace. WSL and Windows features have not been changed.

The earlier investigation kept sources in repository-owned scratch. The proposed
external build workspace below addresses the reduced space on C: and has specific
owner authorization. Do not delete retained evidence to make room or modify
the installed Context Suite app, Explorer registrations, the reserved 1.1.1
candidate or existing engine payloads.


Windows prerequisite review (2026-09-20)
---------------------------------------

Read-only inspection supports a Cygwin/MSVC build attempt. It does not establish
that the patched source compiles or that this combination passes native tests.

| Item | Observed state |
| --- | --- |
| Visual Studio Community 2026 | Complete instance 18.9.12120.119; MSVC 14.51.36231, x64 ATL header/library, CMake, Ninja and vcvars64 present |
| Visual Studio Build Tools 2026 | Complete instance, but its selected MSVC installation lacks ATL; use Community without modifying either installation |
| Visual Studio Build Tools 2022 | Installer reports incomplete/not launchable; do not repair it as part of this setup |
| Windows SDK | Include directories 10.0.22621.0 and 10.0.26100.0 present; compilation still validates the complete selected SDK |
| Java | Existing Java 21.0.9 and javac command present; configure must validate the resolved JDK |
| Cygwin | Not on PATH or in C:\cygwin/C:\cygwin64; HKCU/HKLM Software\Cygwin keys absent at inspection |
| Storage | C: about 66.2 GB free; G: local NTFS NVMe about 1.335 TB free; proposed G:\ContextSuite-OfficeBuild does not exist |

The exact renderer `configure.ac` maps Visual Studio 2026 and contains the
Cygwin build path. The separate upstream LODE commit
`88b29c1d97be9ef33544f400fa119b3e29b89a9b` supplies package/bootstrap guidance.
Four downloaded text files and SHA-256 provenance are retained in
`.codex-temp/office-windows-prerequisites/d5af2213af904ffa88c9bedd3246dc7f`.
No downloaded script or binary was executed. The Windows build wiki was
unavailable to the research tool; this review does not claim it was read.

### Approved setup boundary

- Create only the dedicated external workspace `G:\ContextSuite-OfficeBuild`,
  with `cygwin`, `downloads`, `tools`, `source`, `build`, `temp` and `evidence`
  beneath it. Retain small audit receipts in the repository scratch directory.
  G: avoids consuming the system drive's remaining space. This external path
  received explicit authorization under the shared repository rules.
- Install a current-user Cygwin subset: upstream-listed autoconf, automake,
  bison, bsdtar, cabextract, flex, gettext-devel, git, gnupg, gperf,
  libxml2-devel, libpng12-devel, make, nasm, openssl, patch, perl, pkg-config,
  readline, rsync, unzip, wget, zip, perl-Archive-Zip, perl-Font-TTF,
  perl-IO-String and Python, plus required base/dependency packages. Record
  resolved package versions before use; omit optional editors and terminal UI.
- Use explicit setup root/cache paths, `--no-admin`, `--no-write-registry`,
  `--no-shortcuts` and `--no-replaceonreboot`. Verify current installer options
  before invoking setup. Keep HTTPS and package signature verification enabled.
  Do not execute LODE's install script unchanged: it selects a global C:\cygwin
  root, uses an HTTP mirror and passes `-X` to disable metadata signature checks.
- Acquire standalone native GNU Make 4.4.1 and pkgconf 2.4.3 from the upstream
  build-tool location, and other required build-only tools such as Ant/Meson
  beneath `tools`. Reuse the existing compiler, SDK, Java, CMake and Ninja.
  Pin downloaded identities and inspect configuration failures before adding
  further dependencies. Do not run the all-purpose LODE setup, which provisions
  additional optional components beyond this review.
- Keep build environment changes in child processes. Add no services, Explorer
  registration, shortcuts, global PATH changes, Windows features or antivirus
  exclusions. Do not elevate setup or modify the installed application.
- Allow an initial **100 GiB working-space budget**, starting with **two build
  jobs**. This is an approved resource allowance, not a measured upstream minimum
  or guaranteed final size. Measure download/source/build growth and stop for
  review if more space or tool changes are needed. Full source/dependency setup
  and a first compile may take hours; no duration has been measured here.

This is not a zero-trace portable environment. Cygwin's setup flag prevents its
setup-root registry write, but the runtime itself records its installation path
for diagnostics, normally under `HKCU\Software\Cygwin\Installations` for an
unelevated user. This is explicit in the
[upstream discussion](https://sourceware.org/pipermail/cygwin-apps/2025-July/044476.html)
and [runtime source](https://github.com/cygwin/cygwin/blob/main/winsup/cygwin/mm/cygheap.cc).
The approval covers that limited user-registry entry as well as the external
workspace. Do not patch Cygwin or change registry permissions to conceal it.
Build tools are not a security sandbox; Windows can also retain ordinary process
and file activity records. No promise of zero machine traces is made.

After setup, verify the exact upstream source and apply the chart patch only in
the fresh owned copy. Determine whether the changed `embobj` component and its
dependencies can be built against the retained runtime or require a complete
matching build; do not assume ABI compatibility. Inspect changes before using
any result in an isolated test payload. The execution/adoption gates below stay
unchanged. No new production identity or version is approved by this setup plan.

### Setup execution (2026-09-20)

The official Cygwin setup 2.937 signature verified with the published signing
fingerprint `56405CF6FCC81574682A5D561A698DE9E2E56300`. Setup exited 0 with
the approved flags, HTTPS mirror and normal package verification. Its UI stayed
hidden. Inspection found one runtime installation-path value under
`HKCU\Software\Cygwin\Installations`; that value points to the approved workspace.
The corresponding HKLM key was absent. The resolved package inventory is retained in
`G:\ContextSuite-OfficeBuild\evidence\cygwin-package-versions.txt`.

Native GNU Make 4.4.1 and pkgconf 2.4.3 came from the upstream LibreOffice tool
location. Meson 1.7.2 came from its release archive. The LODE-listed Ant 1.10.17
URL returned 404; the current official Ant 1.10.18 archive was acquired instead
and matched its published SHA-512. Download URLs, lengths and hashes are in
`evidence\standalone-tool-downloads.json` beneath the approved workspace.

The exact renderer source archive is 346,964,940 bytes, SHA-256
`7844FC4BD214147AB219012F57F2458A38D544DC8C0A154AF956882D724AF890`.
All nine separately pinned chart/configuration files match. The first extraction
stopped safely at an unhandled symbolic link before writing source files. Review
found 56 links to regular files within the archive; the successful extraction
materialized those target bytes as files without Windows symlink privileges.
`evidence\renderer-source.json` records every such path and target. The original
archive and failed-attempt evidence remain retained.

The draft chart patch applied only to this fresh extracted tree. Evaluation
configuration requests Visual Studio 2026, existing Java 21, two build jobs and
the owned dependency cache. Java support remains enabled; optional Java JUnit
tests, help and dictionaries are excluded from this component-build evaluation.
Those exclusions do not replace the native/security or application acceptance
requirements below. Configuration and compilation results are recorded separately;
setup success alone is not native acceptance or permission to adopt a runtime.

The first configuration attempt failed because Windows Python uppercased the
`ProgramFiles(x86)` environment key, which Cygwin Perl reads case-sensitively.
The launcher now preserves that key's spelling in the child process. The second
attempt found Visual Studio Community 2026, MSVC 14.51.36231, Windows SDK
10.0.26100.0 and Java 21.0.9, then stopped because Clang was absent. The existing
Visual Studio LLVM directory contains formatting/analysis tools, not its compiler.

The official standalone LLVM 22.1.8 Windows x64 archive was therefore extracted
under the approved `tools` directory. Its 862,053,924 bytes match the release's
SHA-256 `D96C2CC1736F4EB7FA43CB9BBDF56D93551A9AE0A9AADB9C99C3C3B2B712A234`.
GPG verified its detached signature using the LLVM-published release key
`FFB3368980F3E6BB5737145A316C56D064CACBA5`. A native compiler/header probe passed;
its actual include-prefix output is supplied with explicit compiler paths.
This does not modify Visual Studio, install LLVM globally or disable the
renderer graphics/PDF dependencies. Receipts and the probe log are retained in
the workspace evidence directory. Existing .NET and native Python paths are
also exposed only to the build child; .NET/NuGet caches are directed into the
approved workspace.

The third configuration attempt reached the upstream antivirus preflight, which
creates a harmless EICAR test file and fails when antivirus blocks it. That test
was blocked. No exclusion or Defender setting was changed. A separate recorded
patch to the owned `configure.ac` omits only the EICAR/exclusion preflight and
states that antivirus remains enabled. Its exact diff and before/after source
hashes are retained as `evidence\antivirus-enabled-build.patch` and `.json`.
This is a build-environment adjustment; it does not relax the renderer's active
content policy or count as document isolation evidence.

Configuration attempt 4 passed with no `config.warn` entries. The GitHub source
archive lacks release-generated `sources.ver`, so that metadata was generated
from the pinned `AC_INIT` version, 26.2.6.3; no extra source submodules are required
by this configuration. The first `make -j2 Library_embobj` attempt is underway.
Dependency downloads use upstream HTTPS locations and configured SHA-256 checks.
Its starting workspace size was 7,799,144,415 bytes; the launcher measures growth
and terminates its own build tree if the approved 100 GiB budget is exceeded.
The build command, process identity, log, footprint observations and eventual
exit receipt are retained as `evidence\build-01-*`. This is an in-progress build,
not a compiled or accepted repair. Twelve setup/build script snapshots and their
hashes are retained under `evidence\bootstrap-scripts`; later launcher revisions
must retain their own identity before use.


Required execution before adoption
---------------------------------

1. Compile the exact patched source with a recorded toolchain/configuration and
   retain a reproducible source/runtime identity. A clean patch application does
   not prove compilation, linkage or redistribution readiness.
2. Exercise factory and state transitions through actual native APIs: the exact
   internal chart is allowed only on the intended path; wrong IDs, service or
   factory mappings, links, imported binary objects and interactive states fail.
   Retain positive controls so unavailable components cannot masquerade as denial.
3. Verify generated DDE/OLE/external-link and macro controls under the real
   restricted host, including process/file/network boundaries and cleanup.
   Preserve the unresolved network-enforcement gate rather than treating a
   timeout as denial.
4. Run the unchanged four-case D07 application/PDF matrix with the patched
   isolated engine and restrictive profile. Add Word/PowerPoint chart checks;
   repeat relevant calculation, revision, font, image and interruption regressions.
5. Review source delivery/notices, packaging impact, performance and human pages
   before changing native identities or composing the final combined candidate.

If this proposal needs broader object permissions to render, stop and review the
design rather than expand the exception to arbitrary embedded content. Other
acceptance work can continue while the native build and security checks proceed.
