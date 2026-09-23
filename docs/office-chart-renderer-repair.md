Office Chart Renderer Repair
============================

Status: revised source patch compiled and linked; twenty-two authored native cases
pass, including a separately reviewed package-namespace compatibility repair.
Four Excel and two Word/PowerPoint chart PDF comparisons pass through the
restricted host in the isolated Windows build.
The paired application now passes the four authored Excel chart/layout cases
and independent PDF comparisons, plus Word layout and fifteen font/style cases.
The [reconstructed build and source-kit replay](office-source-build.md#corrected-source-kit-and-reconstructed-host-replay)
now pass compilation, 22 native cases and scoped restricted-host chart/preview,
external-link and macro controls. All 148,225 fresh-kit source files match the
compiled tree. DDE retains its known refusal. The paired
[interruption replay](office-worker-lifetime.md#paired-source-built-interruption-replay-2026-09-23)
passes 90 safety checks but exposes missing PowerPoint text-box fill. That defect,
remaining file/process/network acceptance, final runtime identity and adoption
are open.
Reserved candidates retain their original failed chart behavior.
The [chart-loss regression](excel-layout-acceptance.md) keeps chart fidelity in the
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
- Require the configured chart document service before constructing the object.
  Missing, malformed or different service values must fail under the policy.
- Permit only `RUNNING` for that class and chart service, with no link URL.
  Read the object fields while holding the existing mutex.
- Keep the existing permission to return objects to `LOADED`. Do not permit
  active, in-place-active or UI-active states under the restrictive policy.

Existing-entry, media-descriptor and user-init import paths stay blocked.
Windows OLE factories, DDE, external-link update paths, macro controls and Python
controls are unchanged. The native cases below exercise the chart exception;
they do not establish enforcement of every unchanged path or the restricted host.

The upstream setting blocks both OLE and DDE. The separate `DisableOLEAutomation`
setting only controls its automation service entry point; it is not an equivalent
replacement. The existing document preflight is not a whole-package safety scan.
Neither turning off the broad control nor trusting the file extension is the
selected repair.

The second draft closes a source-traced initialization gap in the first draft.
`setPersistentEntry(TRUNCATE_INIT)` calls `InitNewDocument_Impl()` and sets the
object to `RUNNING` directly, without calling `changeState`. Checking the service
only in `changeState` was therefore insufficient: a chart class mapped to a
different document service could create that model first. The factory now checks
the service before construction. The unchanged `miscobj.cxx` and `persistence.cxx`
were verified against the full pinned source archive; their hashes and the
before/after factory identities are retained in
`G:\ContextSuite-OfficeBuild\evidence\chart-patch-v2.json`. This is a correction
to the unadopted proposal, not evidence of an installed-app vulnerability.


Static verification
-------------------

```powershell
python -B tools/office-engine/Test-OfficeChartPatch.py `
  --source-id 175b82e333ab4edcad90919390a6e749
```

The tool validates the exact source snapshot, applies the patch to a fresh owned
copy, checks the chart's configured factory/service and preconstruction guard
ordering, compares untouched refusal function bodies, and reverses a separate
copy back to the original bytes. It
does not compile, install, change production pins or load the renderer.

The first draft passed ten checks in
`.codex-temp/office-chart-patch-4725775edd8d4f008a3716a5a231530c/results.json`.
The tightened second draft passes twelve checks in
`.codex-temp/office-chart-patch-7f18d6093e7b4a0b93bb706798609f32/results.json`.
The additional checks inspect source ordering; they do not execute native code.
The earlier attempt `office-chart-patch-4244ed3959634eb29941f0905aa08a16`
failed exact restoration because Git converted upstream LF to CRLF. It remains
retained. Per-command Git settings now preserve upstream line endings; no global
Git configuration changed. The commit whitespace check also caught a trailing
blank context line in the patch; extending that hunk to the next source line
removed the warning without changing the proposed C++ bytes. The fresh static
replays above pass. These source checks do not prove native behavior.

Fifteen native Cppunit cases are authored in
`.codex-temp/office-chart-native-draft-04`; the prior eight-, ten- and thirteen-case drafts
are retained. They cover wrong factory/service mappings, configuration restoration,
existing-entry preservation, user initialization and in-memory chart media import,
with available-model positive controls. The existing-entry router deliberately
returns an inert placeholder: its test requires no model, only the `LOADED` state,
refusal of runnable/interactive states, unchanged `content.xml` bytes and successful
unrestricted loading through both entry points. This is not a whole-package byte
comparison or a hostile binary-OLE test.

The latest cases add linked-chart creation and user-initialization controls:
the authored local chart must run when unrestricted, then refuse `RUNNING` with
the policy restored while preserving its fixture bytes. They use an exact owned
profile URL; the runner must also verify the filesystem paths before execution.
The fifteen cases were added only to the owned evaluation source tree. Their
compile-only target used `gb_SUPPRESS_TESTS=1`, which skips the test execution
recipe. The source diff, original module file and hashes are retained
in `G:\ContextSuite-OfficeBuild\evidence\native-tests-01-preparation`.
That first test build exited 2 after 5,557.13 seconds on a C2665 type error in
the authored profile-prefix assertion; no native cases executed. Its last
footprint observation was 28,819,933,491 bytes, below the approved budget.
On 2026-09-22 the owner authorized correction and incremental resumption.
Draft 05 materializes the concatenated prefix as `OUString` before passing it
to the string-view overload. The same path assertion and all fifteen cases
remain; the production chart patch is unchanged. Before/after bytes, hashes
and the narrow diff are in `evidence\native-tests-02-preparation`. Attempt 2
completed with two jobs, the same space monitor and test execution suppressed;
the subsequent native execution is recorded below. Hostile binary-OLE cases,
interactive-state positive controls and separate restricted-host/application
acceptance remain open. Source inspection confirms the Cppunit recipe recreates its `.user`
profile and may recreate a `.core` directory; resolve both inside the owned
workspace and preserve earlier evidence before a repeat. Drafting and source
inspection are not native acceptance.


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
by this configuration. The `make -j2 Library_embobj` evaluation has completed.
Dependency downloads use upstream HTTPS locations and configured SHA-256 checks.
Its starting workspace size was 7,799,144,415 bytes; the launcher measures growth
and terminates its own build tree if the approved 100 GiB budget is exceeded.
The build command, process identity, log, footprint observations and eventual
exit receipts are retained as `evidence\build-01-*`, `evidence\build-02-*` and
later numbered attempts.
The fourth attempt compiled the component; this is not an accepted repair.
Twelve setup/build script snapshots and their
hashes are retained under `evidence\bootstrap-scripts`; later launcher revisions
must retain their own identity before use.

The first attempt produced `skialo.dll` and several core libraries, then was
deliberately stopped after identifying an unbounded nested MSBuild invocation
in the upstream Python dependency recipe. Its exit 1 is an operator stop, not
a compiler failure or disk-budget failure. A narrow owned-source patch changes
that invocation to `/maxcpucount:1 /p:MultiProcessorCompilation=false`, keeping
the nested work within one outer Make slot. The original recipe matched the
pinned source archive before editing. The diff, before/after hashes and reason
are in `evidence\python-build-worker-limit.patch` and `.json`.
Attempt 2 resumed from the retained build files with two outer jobs and began
at 10,934,909,627 workspace bytes. It later exited 2 after the bundled Python
UWP projects hit Visual Studio 2026's STL1011 experimental-coroutine deprecation
error. The last measured footprint was 11,775,159,577 bytes; the budget was not
exceeded. Completed artifacts and the terminal receipt remain retained.

A build-only adjustment supplies Microsoft's header-documented
`_SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS` definition only to the
Python dependency's MSBuild invocation through its compiler environment.
[Microsoft documents this compiler environment mechanism](https://learn.microsoft.com/en-us/cpp/build/reference/cl-environment-variables?view=msvc-170).
The exact installed header identity, recipe diff and before/after hashes are in
`evidence\python-coroutine-compatibility.patch` and `.json`. This acknowledges
that one deprecated API for the evaluation build; it does not edit Visual Studio,
suppress diagnostics generally or change renderer security policy. The nested
one-worker restriction remains in place.

After attempt 2 had exited and no owned compiler processes remained, the revised
chart patch and the Python compatibility adjustment were applied to the owned
source tree. Attempt 3 compiled the Python dependency with 10 warnings and zero
errors; its completion stamp and full log were retained before the build stopped.
That verifies the compatibility adjustment for this toolchain, not warning-free
redistribution readiness. The full build was deliberately stopped after its
compiler log revealed `/MP` still active: Python's project item metadata sets
`MultiProcessorCompilation=true`, overriding the intended property-based limit.
Attempt 3's exit 1 records that operator stop, not another compiler failure.

The Python-only compiler environment now appends `/MP1`, retaining the single
MSBuild worker and the coroutine definition. Microsoft's
[compiler process-limit documentation](https://learn.microsoft.com/en-us/cpp/build/reference/mp-build-with-multiple-processes?view=msvc-170)
distinguishes this limit from MSBuild's project count. A disposable two-unit
compile, including the experimental-coroutine header, passed with those options
confirmed after Visual Studio environment setup. It did not measure process
counts or run an executable. The first probe incorrectly expected an override
warning; that failed assertion and its successful compiler output remain retained.
Exact recipe changes and probe identities are in
`evidence\python-compiler-worker-limit.patch` and `.json`; the prior Python log
is `evidence\build03-python-build.log`.

Attempt 4 built with the revised chart patch, retained artifacts, two outer
jobs, one nested compiler worker and the same 100 GiB monitor. The live Python
parent-shell invocation supplies `_CL_` with `/MP1`, and its MSBuild child command
contains `/maxcpucount:1`; this observation is retained in
`evidence\build04-python-worker-invocation.json`. It is not a continuous process
trace. Python then completed in 2 minutes 52.55 seconds with 10 warnings and zero
errors, and its dependency completion stamp is present. `applied-chart.patch`
preserves the first draft; `applied-chart-v2.patch` identifies the revision used
by attempts 3 and 4. The matching-runtime and acceptance gates remain unchanged.

### Component build and runner checks (2026-09-20)

Attempt 4 exited 0 after 1,916.51 seconds without exceeding the space budget.
Its last footprint observation was 14,733,955,543 bytes. The resulting x64
`embobj.dll` is 401,408 bytes with SHA-256
`0E1FD05F81AC1388DD2C5713795CB6BBBB2B197C8D8D55917EE51EF8A8FC344B`.
The DLL, component registration and receipt are preserved under
`G:\ContextSuite-OfficeBuild\evidence\build04-component`. The receipt binds the
actual three patched sources, configuration, build adjustments and log. This
establishes compilation and linkage of patch v2, not chart rendering or security.
HarfBuzz's Ninja 1.13.2 log also confirms joining GNU Make's two-slot Windows
jobserver; the observation is retained in `evidence\build04-harfbuzz-jobserver.json`.

The authored test runner compiled with zero warnings and errors. Its apphost
passes all sixteen existing Office process-lifetime contracts, covering literal
arguments, diagnostics, timeouts, cancellation, descendants, owner crash and
cleanup. `evidence\chart-runner-process-contracts.json` records the exact apphost
identity and output. These checks exercise the harness, not chart code. The
pinned Cppunit source confirms the expected `OK (15)` success-line format; that
count will still require binding to the actual compiled suite and complete
matching runtime. Runtime inventory, command/path refusal checks and pre-gbuild
profile verification remain prerequisites to native execution. The runner is a
process-lifetime boundary, not a filesystem or network sandbox.

The runner's first .NET SDK startup unexpectedly added its workspace tools
directory to the user PATH and printed a development-certificate message.
Inspection found only development certificates dated July; without a pre-build
store snapshot this is not a complete before/after certificate comparison. No
certificates were changed or removed. The exact workspace PATH entry was removed,
with all other entries, ordering and registry value type preserved and readback
verified. Evidence is in `evidence\runner-dotnet-certificate-observation.json`
and `evidence\runner-dotnet-path-restoration.json`. Subsequent build/test child
environments explicitly set `DOTNET_GENERATE_ASPNET_CERTIFICATE=false` and
`DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false`; the older first-use skip variable was
insufficient. No machine PATH change or installed-app update was made.

### Native compilation and execution (2026-09-22)

Native-test build attempt 2 exited 0 after 2,666.87 seconds. Its last workspace
measurement was 37,557,262,593 bytes, below the 100 GiB allowance. The compiled
draft-05 test DLL is 304,128 bytes, SHA-256
`61CD0A86DB135E0BAA7A9A96DE088D5C27C3AD5EF64F8DA19EF157AA0E200668`.
`evidence\native-tests-build-02-artifacts.json` binds its source, object, DLL and
build log. The chart component retains the build-04 identity above.

The reviewed runner verifies the command, exact fifteen case names, fresh owned
profile and complete membership/hashes of seven runtime/template directories.
Ten command/inventory guard checks and ten synthetic transcript checks pass.
The revised process helper retains both bounded diagnostic streams on nonzero
exit; all seventeen process-lifetime checks pass through runner 07, built with
zero warnings/errors. These results are in `evidence\native-runner06-guards-8f27cbd296704a4c97ae4f3356529fad`
and `evidence\chart-runner07-process-contracts.json`. Reparse checks were inspected;
a live junction refusal test was not performed.

Initial launches exited before any case ran. The isolated `cppunittester`
exception handler now prints its caught startup exception to stderr as well as
the upstream logging path. This diagnostic-only source change and executable
rebuild are retained in `evidence\native-startup-diagnostic-01` and
`evidence\native-startup-build-01-exit.json`. It exposed the missing `SRC_ROOT`
environment variable. The successful launcher supplies that and
`WORKDIR_FOR_BUILD` from the recorded source/build locations. Earlier failed
receipts and profiles remain retained; they are not chart-case failures.

`evidence\native-chart-run-05\result.json` records all fifteen named cases
starting and completing, `OK (15)`, and exit 0. The run took 343 ms, observed two
processes and left zero active children after cleanup. Peak job memory was
34,697,216 bytes. The bounded runner uses a 120-second deadline, eight-process
limit and 1 GiB process/job memory limits. It is not a file/network sandbox.

The cases cover restricted native-chart creation and allowed state transitions,
wrong class/factory/service refusals, existing-entry inert preservation, linked
chart refusal and blocked media/user initialization with available-model
controls. Interactive-state refusals pass, but unrestricted interactive positive
controls with an authored client site remain needed; missing client support
must not masquerade as policy enforcement. Hostile binary OLE is also untested.

Post-run verification confirms all 5,175 inventoried runtime files, totaling
634,077,326 bytes, are unchanged. `evidence\native-chart-run-05-postverification.json`
also binds the managed runner, apphost, sources, launcher, command manifest,
diagnostic patch and retained successful profile. No installed runtime or
production engine identity changed. At that checkpoint, actual PDF rendering,
full D07 replay, Word/PowerPoint charts and restricted-host security remained open.

### Matching runtime and passive PDF exports (2026-09-22)

The isolated runtime now includes matching `soffice.bin`, `soffice.exe` and
`soffice.com` launchers, bootstrap/configuration packages, the full component
registry and upstream default-profile files. Export-build attempt 1 was stopped
after multiple outer goals launched independent recursive two-job makes; later
scripts run one target at a time. Attempt 2 linked all three launchers but exited
2 on `Package_setup`: the package's registered target is
`Package_instsetoo_native_setup`. Attempt 3 used the registered package names
and `Rdb_services`, exiting 0 after 111.97 seconds. These are build-directory
copies, not a Windows installation. All receipts remain under the approved
workspace's `evidence\native-export-build-*` paths.

The first PDF attempt exited 77 while initializing a fresh profile, before
opening a workbook. The partial runtime lacked `instdir/presets`, which the
pinned `desktop/source/app/userinstall.cxx` requires. Export-build attempt 4
built the five upstream extras packages owning that directory, exiting 0 after
151.91 seconds. No initialization bypass or reused customer profile was added.
The second PDF attempt exported its first workbook but failed the existing
saved-profile assertion for `AutoCheckEnabled`; that failure is retained.

The owned build has `ENABLE_ONLINE_UPDATE`, `ENABLE_ONLINE_UPDATE_MAR` and
`ENABLE_EXTENSION_UPDATE` unset. Its updater job configuration and component
are absent, so the two inapplicable update declarations disappear on profile
save. The evaluation-only runner now requires that exact build configuration,
no update-check library and no updater registration in the inventoried registry.
It checks every retained macro, active-content, Python, security-level, OpenCL
and calculation setting against the requested profile. Fourteen profile checks
cover valid output, removal/corruption of all six retained controls and an
unexpected updater entry. The shared shipping profile verifier is unchanged;
this is not evidence of network enforcement.

The final runner lives in `.codex-temp/office-chart-export-draft-02`, linked to
the unchanged authored `ExcelLayoutFixtures` and `ExcelLayoutOracle` sources.
Its isolated build has zero warnings/errors. It uses fresh profiles, bounded
ordinary jobs and `DisableActiveContent=true` for all four portrait/landscape
and saved-values/recalculated workbooks. The pinned renderer schema maps
saved values to 1 and recalculation to 0; the fixtures' values and caches agree,
so separate formula/date correctness controls remain required.

`G:\ContextSuite-OfficeBuild\evidence\chart-pdf-export-03\results.json` passes
all four independent comparisons: qpdf structure, one correctly oriented Letter
page, expected text and placement, three red bars with the original 1:2:3 height
tolerance, and category association. The existing negative oracle controls pass.
Conversions take 545-674 ms, excluding initialization and verification; these
are tiny diagnostic inputs, not the declared application performance benchmark.
Every conversion leaves zero active job processes. Original hashes/timestamps
and all 4,111 inventoried runtime/verifier input files remain unchanged.
The command, configuration hash, managed runner and source identities are retained
in the sibling preparation and launch receipts. The build-generated `version.ini`
has an empty `buildid` because this is an archive checkout; external source and
file hashes identify the evaluation. Production identity/adoption remains open.

Lossless PNG encodings of all four PDFium rasters are retained in `visual-review`.
Assistant inspection of the portrait cached and landscape recalculated pages
shows the three bars, labels, headings and end marker without apparent clipping.
This is limited artifact inspection, not owner, desktop or accessibility acceptance.

This establishes passive native PDF rendering with the restriction enabled.
The interim shipping chart guard remains. Actual application publication and
restricted-host replay, Word/PowerPoint charts, adoption and final-candidate
replay remain required. The later native OLE checks below cover component-level
refusal and preservation, not whole-document hostile-input enforcement.

### Interactive positive controls (2026-09-22)

The three interactive-state cases now use an authored client site backed by a
real Calc document, window, frame and layout manager. Each first requires the
restricted request to fail before any client callback and leave the chart in
RUNNING. With the restriction disabled, the same object must reach ACTIVE,
INPLACE_ACTIVE or UI_ACTIVE and call the client. It returns to RUNNING, then
must refuse activation again after the restriction is restored. The UI_ACTIVE
control also checks the real chart frame, docking area and nonempty menus.

Before creating a container, each case verifies that its thread belongs to
`LIBO_TEST_DESKTOP`. Upstream desktop creation can silently fail; these tests
must stop before creating UI on the user's desktop. The runner also removes
`LIBO_TEST_DEFAULT_DESKTOP` from the child environment. This is native API
coverage on a separate test desktop, not visible usability acceptance.

Build attempt 3 failed on a missing authored-test `rtl/ref.hxx` include; attempt
4 compiled the correction. Attempt 5 added the separate-desktop assertion.
Runs 6 and 7 passed ACTIVE/INPLACE_ACTIVE controls but failed unrestricted
UI_ACTIVE. Run 8 identified an empty container menu. The upstream
`registry-user-ui/user-ui.xcd` replaces installed UI paths with sparse unit-test
configuration. Run 9 retains the installed and common test configuration but
omits that UI-only override, supplying the matching runtime's actual menus.
The headless argument, policy assertions and production chart patch are unchanged.
Earlier failed runs and profiles remain retained.

`evidence\native-chart-run-09\result.json` records all fifteen cases passing,
including the strengthened controls: exit 0, `OK (15)`, 606 ms, two processes,
zero active children after cleanup and 41,988,096 bytes peak job memory.
The draft-10 source and build-07 result are bound by
`evidence\native-chart-execution-09-preparation\preparation.json`.
The reviewed command records the exact configuration layers; the draft makefile
alone still selects the upstream sparse UI override and is insufficient to
reproduce this successful invocation. Future integration must retain that
explicit menu configuration.

`evidence\native-chart-run-09-postverification.json` confirms unchanged membership
and hashes for all 5,202 runtime/template files, totaling 638,581,873 bytes, and
binds the source, runner, command, launcher and retained profile. This closes the
native interactive-positive-control gap. Binary OLE, actual restricted-host
enforcement and application/final-candidate replay remain open.

### Binary OLE boundary controls (2026-09-22)

Draft 11 adds two native cases to the existing fifteen. Its build-08 receipt
records successful compilation and linkage. The test makefile now selects
installed/common configuration explicitly, declares the Calc/chart UI packages
and links Windows structured-storage APIs. This removes the earlier mismatch
between the recipe's sparse test menus and the successful reviewed invocation.

The binary fixture case creates compound files entirely in memory using Windows
storage APIs. Each contains only an authored inert marker. The test reopens the
storage and verifies its class ID and marker before passing bytes to UNO. It
combines a random class ID and the exact native chart class ID with OLE and
chart media types, covering all four identity/label combinations. Both direct
factories refuse them; the router retains only a LOADED placeholder with no
component and refuses all four runnable/interactive states. Copying that
placeholder preserves the original binary payload exactly, as does the source.
The fixture uses no executable, external link, OLE server or installed Office app.

The second case instantiates the actual Windows OLE factory and exercises its
existing-entry, new-object, media, link and user-initialization paths. With the
restriction enabled, every call throws the policy refusal before touching an
authored storage barrier. With it disabled, each same call reaches that barrier
exactly once and throws its distinct sentinel exception. Restoring the
restriction again prevents all storage access. These positive controls prove
that available factory dispatch, rather than a missing component or malformed
object, distinguishes the refusal. They deliberately stop before OLE object
loading; they do not demonstrate successful OLE server activation or network
enforcement.

Runner 08 requires the exact seventeen names and `OK (17)`. It builds with zero
warnings/errors, passes ten synthetic transcript checks, ten command/inventory
guards and all seventeen existing process-lifetime checks. Its linked process
helper is unchanged. `evidence\native-chart-run-10\result.json` records all
seventeen native cases passing in 621 ms, two processes, zero active children
after cleanup and 42,033,152 bytes peak job memory. All 5,202 inventoried files
remain unchanged, totaling 638,649,457 bytes, as recorded in
`evidence\native-chart-run-10-postverification.json`. The preparation and
postverification receipts bind the draft, compiled DLL, runner and exact command.

This closes the scoped native binary-OLE refusal/preservation gap. Actual
restricted-host document controls, file/network/process enforcement, application
publication, Word/PowerPoint charts and final-candidate replay remain required.
The production host currently loads `mergedlo.dll`; this isolated build exports
the same embedding hook from `sofficeapp.dll` and Windows loop functions from
`vcllo.dll`. Export-table inspection alone does not establish host compatibility.
The production host, runtime pins and shipping chart refusal remain unchanged.

### Restricted-host chart exports (2026-09-22)

The four existing Excel comparisons now also pass through the actual
`OfficeSandboxProcess` launcher and `OfficeSandboxOwner` profile owner. The
evaluation harness links those sources unchanged. It uses a fresh, capability-free
AppContainer for each workbook, the existing suspended-start/token checks,
eight-process and 1 GiB job limits, a 120-second deadline, bounded protocol
channels and whole-job cleanup. Only the copied runtime/input and owned
output/profile/temp directories receive the required grants.

The evaluation host is a scratch copy of the private host with one explicit
linkage adaptation: load the embedding hook from `sofficeapp.dll` and the two
Windows loop functions from `vcllo.dll`. Its token, source/path identity,
profile and terminal-protocol checks are unchanged. An ordinary-token control
exits 2 with the required AppContainer refusal. The host and managed harness
build with zero warnings/errors. Production host sources and runtime pins remain
unchanged; this is evidence for the isolated unmerged build, not automatic
adoption of that configuration.

`G:\ContextSuite-OfficeBuild\evidence\chart-host-export-01\results.json` records
all four portrait/landscape and saved-values/recalculated cases passing with
active-content blocking enabled. The unchanged qpdf/PDFium oracle verifies
structure, page geometry, text/placement, the three bars and category association.
Sources retain their hashes and timestamps, and terminal replies match output
PDF and calculated-workbook hashes/sizes. Host calls take 635-744 ms; this
excludes profile preparation and independent verification and is not the full
application performance benchmark.

The preparation receipt binds the private source copy, library adaptation,
compiler, runner sources and input manifest. The copied evaluation payload has
3,819 files totaling 541,260,391 bytes; this is only the host/runtime, not the
whole product footprint. `evidence\chart-host-export-01-postverification.json`
confirms unchanged membership/hashes for that payload, all 4,112 verifier/runtime
inputs and the linked runner sources. All four native profiles and their Windows
registry mappings are absent after cleanup, with individual removal receipts.

This closes the four-case passive Excel fidelity check through the restricted
host. It does not close whole-document hostile-input or file/network-denial
checks, application publication, Word/PowerPoint charts, engine adoption or
final-candidate acceptance. The shipping chart refusal remains in place.

### Word and PowerPoint chart controls (2026-09-22)

[OfficeChartFixtures](../tools/office-engine/Probe/OfficeChartFixtures.cs) adds
one authored DOCX and one single-slide PPTX. Both embed a passive DrawingML chart
with literal ALPHA/BETA/GAMMA values 10/20/30; neither has a workbook, external
data target, macro or executable object. The fixtures reuse the independently
authored package scaffolding and Excel chart XML, replacing cell references with
literal data. Word uses a Letter page and PowerPoint a 720-by-405-point slide.

[OfficeChartOracle](../tools/office-engine/Probe/OfficeChartOracle.cs) checks
the exact text tokens, heading/page bounds, chart placement, category-to-bar
association and the existing three-pixel 1:2:3 height rule. Three unchanged
Excel oracle helpers are now internal so the new checks share their established
pixel and glyph logic. A synthetic positive control and six negative controls
cover missing/extra text, missing bars, incorrect values, displaced categories
and clipping. Expectations were fixed before the first export.

Attempt `chart-host-export-02` produced the Word PDF but failed the harness's
Excel-only `OOXMLRecalcMode` expectation. The actual host deliberately writes no
calculation setting for Word/PowerPoint. Draft runner 03 matches that declared
policy, retaining mandatory macro, active-content, Python, security-level and
OpenCL checks. Its 25 profile contracts cover the existing spreadsheet setting
checks and removal/corruption of all five non-spreadsheet controls. Shipping
profile verification and production host sources are unchanged.

`G:\ContextSuite-OfficeBuild\evidence\chart-host-export-03\results.json` passes
both restricted-host conversions and independent qpdf/PDFium comparisons. Host
calls take 645 ms for Word and 595 ms for PowerPoint, excluding profile setup and
independent verification. Both builds have zero warnings/errors. Preparation
receipts bind the generated-fixture/oracle sources, runner and unchanged copied
runtime. The postverification receipt confirms all 4,112 inventoried inputs and
runner sources unchanged, with native folders and registry mappings absent for
both successful profiles and the earlier failed attempt's profile.

Retained lossless PNGs in `visual-review` were inspected by the assistant: both
pages show the three bars, labels and title without apparent clipping. This is
limited artifact inspection, not owner acceptance or broad chart compatibility.
These two passive chart controls close the scoped Word/PowerPoint rendering gap;
whole-document hostile-content enforcement, application publication, wider Office
regressions, engine adoption and final-candidate replay remain required.

### Local external-workbook controls (2026-09-22)

[OfficeExternalLinkFixtures](../tools/office-engine/Probe/OfficeExternalLinkFixtures.cs)
adds four authored XLSX link cases and a direct-target control. The linking
workbooks save `123456` in both the formula cell and external-link cache. A real
generated local target contains `987654`. Cases cross saved-values/recalculated
conversion with the target directory withheld or explicitly granted read access
to that disposable AppContainer. No network address, macro or executable content
is present. The target is outside the source snapshot and ordinary output grants.

`G:\ContextSuite-OfficeBuild\evidence\external-host-export-01\results.json`
passes all five actual restricted-host exports. Every linked PDF retains `123456`
and its expected label on one Letter page. Independent inspection of the host's
calculated workbook requires the same value, a retained formula and external-link
part, distinguishing saved-data handling from flattening away the expression.
The direct-target PDF and workbook both contain `987654`, confirming the target
is a valid Office input with the different value. The granted-target cases show
that lack of a filesystem grant alone does not explain retaining the saved value.

Host calls take 581-724 ms, excluding setup and independent qpdf/PDFium inspection.
The source, copied source and target retain their hashes/timestamps. The linked
evaluation runner builds with zero warnings/errors. Its preparation receipts bind
the unchanged actual launcher/profile owner, evaluation host and fixture sources.
The postverification receipt confirms all 4,112 runtime/verifier files and runner
sources unchanged, with all five native profile directories and registry mappings
removed. The native host, production runtime pins and shipping guards are unchanged.

This is evidence of no observable external-value refresh for these generated
local-workbook cases in both calculation modes. It is not proof that the renderer
made no read attempt, a positive test of unrestricted link refreshing, universal
external-link coverage or network denial. DDE, macro, embedded-object document
controls and the remaining process/file/network evidence stay open.

### DDE fidelity failure and targeted refusal (2026-09-22)

[OfficeDdeFixtures](../tools/office-engine/Probe/OfficeDdeFixtures.cs) generates
four XLSX controls: named and direct DDE formulas under saved-values and
recalculated export. Each has a saved value of `123456`. The independently
authored [data-only DDE service](../tools/office-engine/Probe/OfficeDdeCanary.cs)
returns `987654` for one random service name and fixed topic/item; it refuses
execute, poke and advise, performs no file/network action and has a bounded
lifetime. Ordinary client requests before and after export confirm availability.

`G:\ContextSuite-OfficeBuild\evidence\dde-host-export-02\results.json`
records **failed acceptance**, not a security pass. Both saved-values PDFs retain
`123456`; recalculation produces `#NAME?` for the named formula and `#N/A` for the
direct formula. The exported workbooks omit the DDE link part, so native import
recognition is not established. No renderer connection or request reached the
service in any case, but that observation does not distinguish enforcement from
an unavailable DDE path. The ordinary controls each connect and request data.
An earlier attempt stopped after its first export because the harness assumed
the missing link part existed; the corrected harness records all four outcomes
without weakening the original acceptance conditions.

The postverification receipt confirms all 4,112 runtime/verifier inputs and runner
sources unchanged, service disposal and removal of all five native profiles from
both attempts. These disposable evaluations do not change the production host,
runtime identities or active-content policy.

The shared [source preflight](../src/ContextSuite.Core/Analysis/OfficeSourcePreflight.cs)
now refuses recognized DDE declarations and formulas before either calculation
mode, with guidance to export from Excel and continued Analyze availability.
The bounded [inspection](../src/ContextSuite.Core/Analysis/ExcelDdeInspection.cs)
scans typed external-link, workbook and worksheet parts, including strict and
transitional namespaces, defined names and direct DDE calls. Quoted literals and
identifiers are excluded. Malformed or over-budget inspection remains unknown;
this positive-risk guard is not a complete security scanner or a claim that
unrecognized packages are safe. General renderer isolation remains mandatory.

All **4,490 foundation contracts** pass, including 51 new DDE checks. Running
`python -B tools/office-engine/Test-OfficeDdeRefusal.py` passes **28 actual-worker
checks** with zero-warning/error Release builds. Eight DDE requests (four fixtures
in both modes) stop before native runtime access, create no output/profile and
preserve originals/snapshots. A DDE-free control reaches the deliberately absent
runtime and the same worker remains usable. Evidence is retained under
`.codex-temp/office-dde-refusal/545f4d7630ef4fa0a5b8a51e2c8600b9`.

The refusal prevents the demonstrated conversion defect. It does not turn the
failed native DDE matrix into a pass or close whole-document macro/OLE/DDE,
file/network/process, application chart replay or engine-adoption gates.

### Native DDE policy control (2026-09-22)

Draft 15 adds an eighteenth native case to the existing seventeen. It creates a
random, data-only DDE service on the same isolated Windows test desktop, serving
only the authored topic/item and value `987654`. Execute, poke and advise remain
refused. A real Calc UNO DDE link starts with saved value `123456`. With
`DisableActiveContent` enabled, explicit refresh retains that value and makes no
service connection/request. With the policy disabled in this disposable profile,
refresh reads exactly `987654` and both service counters increase. Restoring the
restriction and saved value prevents further service calls and preserves `123456`.
The service shuts down and unregisters before the case completes.

Earlier attempts remain failed evidence. Run 11 hit the runner's 120-second
limit because the test waited for DDE service startup without processing Windows
messages. Draft 13 pumps messages during startup/shutdown. Runs 12 and 13 then
completed but inspected the initial empty asynchronous result too early. Pinned
`SvDDEObject::GetData` schedules a DDE request before returning that empty value;
draft 15 processes callbacks and waits at most five seconds for the exact expected
response. It accepts only the exact authored text or numeric value, not partial
text or permissive parsing. Both failed completed runs retain unchanged-input
receipts. None of these harness corrections changes a production source patch or
loosens active-content restrictions.

Native build 12 compiles and links successfully. The unchanged runner 09 requires
all eighteen exact case names and `OK (18)`; its ten synthetic transcript checks
and ten command/inventory guards pass. Its process helper still matches the
previous seventeen lifetime checks. The fixed receipt
`G:\ContextSuite-OfficeBuild\evidence\native-chart-run-14\result.json`
passes all eighteen cases in 664 ms with two processes, zero remaining children
and 42,307,584 bytes peak job memory. The DDE case takes 86 ms. Its retained trace
records successful assertions and service shutdown. The postverification receipt
confirms unchanged membership and hashes for all 5,202 inputs, totaling
638,692,977 bytes. Prior test profiles are retained in their preparation folders.

This establishes a functioning native DDE path and its policy-controlled refusal
for the authored link. It does not establish OOXML DDE import/fidelity or the
whole-document AppContainer result: those failed spreadsheet cases above remain
failed and the source refusal remains required. Macro/embedded-object document
controls, file/network/process boundaries, application chart replay, engine
adoption and final-candidate acceptance remain separate gates.

### Automatic document-macro control (2026-09-22)

Native draft 22 adds a nineteenth case using an authored Writer document whose
Basic macro only changes its own body from `MACRO BLOCKED` to `MACRO EXECUTED`.
It performs no file, network or process operation. The saved document binds
`OnLoadFinished` to a document-local `StarBasic` macro. Four fresh hidden loads
check restricted settings, unrestricted automatic execution, `NEVER_EXECUTE`
with otherwise permissive settings, and restored restrictions. Imported macro
source, event binding, effective execution permission and exact body text are
checked. Direct invocation is only a diagnostic: it cannot substitute for the
required automatic positive control.

Earlier native runs 15, 17, 18 and 19 retain failed automatic-trigger evidence.
The pinned engine suppresses `OnLoad` for hidden loads; the later Script binding
also relies on a visible frame for document dispatch. The final fixture uses
the load-finished event and document-bound Basic dispatch. Intermediate compile
and link failures are retained. These corrections change the test fixture and
its build recipe, not the engine patch or production security policy.

Build 19 succeeds, and
`G:\ContextSuite-OfficeBuild\evidence\native-chart-run-20\result.json`
passes all nineteen cases in 1,267 ms with two processes, zero remaining children
and 47,890,432 bytes peak job memory. Runner 10 requires all nineteen exact names
and `OK (19)`; its ten transcript and ten command/inventory guards pass. The
unchanged process helper retains its prior lifetime evidence. Postverification
confirms all 5,202 runtime/template inputs unchanged, totaling 638,727,281 bytes.

`G:\ContextSuite-OfficeBuild\evidence\macro-host-export-01\results.json`
then passes the same saved fixture through the unchanged real AppContainer
launcher/profile owner and the evaluation host. A fresh copied runtime matches
the native control. Independent qpdf/PDFium checks require one Letter page and
exact text `MACRO BLOCKED`; the host call takes 623 ms. The source/snapshot and
all 4,112 runtime/verifier inputs retain their identities. Required macro,
active-content, Python, security-level and OpenCL settings are retained. The
disposable native profile directory and registry mapping are verified absent.

The fixture contains ODF bytes with a misleading `.docx` name. Actual public
preflight identifies ODT and refuses conversion; only this isolated engine probe
deliberately bypasses that admission layer. This establishes a functioning
automatic document macro and its refusal under the actual restricted host. It
does not admit ODF/macro-enabled conversion or prove OOXML VBA compatibility,
arbitrary macro coverage, embedded OLE behavior, network denial, application
publication or final-candidate acceptance. Those separate gates remain open.

### Word embedded-object document controls (2026-09-22)

Three authored DOCX fixtures compare an ordinary image with two binary OLE
objects carrying the same saved preview. One compound file has a random class
ID; the other claims the exact chart class ID. Both contain only an inert marker
stream, with no executable, macro or external target. Windows structured-storage
APIs independently read their class IDs and marker bytes without activating an
OLE server. Their preview is a fixed red/green 144-by-72-point rectangle on a
Letter page, alongside fixed text.

Native draft 24 imports the actual DOCX packages. The image control has no
embedded object; each OLE document retains exactly one object and its replacement
graphic. Both objects remain `LOADED`, have no component, expose only that state
and refuse `RUNNING`, `ACTIVE`, `INPLACE_ACTIVE` and `UI_ACTIVE`. The prior native
factory availability/storage-boundary positive controls remain in the same suite.
Build 21 and all twenty native cases pass in
`G:\ContextSuite-OfficeBuild\evidence\native-chart-run-22\result.json`.
The native job finishes with zero remaining children. All 5,208 inventoried
runtime/template/fixture files remain unchanged, totaling 638,773,803 bytes.

`G:\ContextSuite-OfficeBuild\evidence\embedded-host-export-02\results.json`
passes all three exports through the actual restricted host. Public preflight
admits the ordinary DOCX packages. Independent qpdf/PDFium checks require exact
text, one Letter page, correctly sized/positioned red/green regions and identical
full-page pixels to the image-only control. The oracle also rejects missing,
swapped, shrunken and shifted previews. Host calls take 510-580 ms. Sources,
snapshots and all 4,112 runtime/verifier files remain unchanged; all three native
profile directories and registry mappings are removed.

The first host attempt remains failed raster acceptance. Its image-only VML
shape had an implicit border absent from the embedded previews. Independent PDF
inspection found identical image data and placement, but an extra stroke on the
control. Fixture 02 explicitly disables border/fill for all three shapes. The
unchanged exact-pixel requirement then passes; the native suite is repeated for
those exact corrected documents. All three first-attempt profiles are also gone.

`embedded-word-native-host-binding-01.json` verifies the same 3,818 renderer
files and six fixture/support files across native and restricted-host evidence.
It also verifies the actual runner's twenty required case names against native
draft 24, explicitly correcting stale descriptive labels retained in the runner
draft metadata without rewriting prior receipts.

This closes the scoped Word binary-object inactivity and saved-preview check.
It does not establish installed OLE-server activation, every object type, Excel
or PowerPoint embedded-object import, file/network/process enforcement, actual
application publication or final-candidate acceptance. Production host sources,
runtime pins and interim refusals remain unchanged.

### Excel and PowerPoint embedded-object controls (2026-09-22)

Six authored XLSX/PPTX fixtures extend the same ordinary-image, random-class
binary object and chart-class binary object comparison to Calc and Impress.
They reuse the independently checked inert compound bytes and 96-by-48-pixel
red/green preview. Excel requests automatic object loading/updating, with no
external target. The host matrix exports each spreadsheet in both calculation
modes, for nine outputs including the three presentations.

Native draft 28 imports all six documents. It reads
`EmbeddedObjectNoNewClient`, which avoids requesting a running model or attaching
a UI client. Each object remains `LOADED`, has no component, exposes only that
state and refuses all four running/interactive transitions. The original
96-by-48-pixel replacement graphic must exist; a generic placeholder icon cannot
satisfy that assertion. Image-only controls contain no embedded object. Build 25
and all twenty-one cases pass in
`G:\ContextSuite-OfficeBuild\evidence\native-chart-run-24\result.json`:
1,754 ms, two processes, zero remaining children and 58,458,112 bytes peak job
memory. All 5,215 runtime/template/fixture inputs remain unchanged.

`G:\ContextSuite-OfficeBuild\evidence\drawing-host-export-03\results.json`
passes all nine actual restricted-host exports. Each requires exact text, page
geometry, preview colors/size/placement and full-page pixels identical to the
corresponding ordinary-image control in the same calculation mode. Host calls
take 493-608 ms. Postverification binds all 3,818 renderer files and seven fixture
files to the successful native replay, verifies all 4,112 host/verifier inputs
and runner sources unchanged, and confirms all nine native profile directories
and registry mappings removed. Prior failed host attempts also have verified
cleanup: one profile in attempt 01 and nine in attempt 02.

The failed attempts remain distinct. The initial presentation serializer emitted
namespace-prefixed elements in `[Content_Types].xml`; both native Desktop loading
and the actual host refused the file. The pinned package reader's
`comphelper/source/xml/ofopxmlhelper.cxx` compares raw element names with
`Types`, `Default` and `Override`. Equivalent default-namespace declarations load.
This is a retained renderer compatibility failure, not an invalid expanded XML
name or a passed isolation check; its engineering disposition remains open.

The next fixture had an incorrect PowerPoint OLE `graphicData` URI ending in
`presentationml/2006/main/ole`, producing placeholder icons. The corrected URI
ends in `presentationml/2006/ole`. The preview oracle also incorrectly required
540 raster rows despite accepting the exported 405.014-point page: at 96 DPI,
that page rounds up to 541 rows. It now accepts 540 or 541 under the unchanged
405-point plus 0.1-point geometry check. Size/color/placement and exact-pixel
requirements remain. The native replay uses the corrected fixtures and the
stronger original-preview-size assertion. An earlier diagnostic-string compile
failure is retained separately.

These results close the scoped embedded-object import/inactivity and saved-preview
checks across Word, Excel and PowerPoint. They do not establish OLE-server
activation, arbitrary object compatibility, namespace-prefix compatibility,
process/file/network enforcement, actual application publication, engine adoption
or final-candidate acceptance. Production permissions and source/runtime pins
remain unchanged.

### Package namespace compatibility repair (2026-09-22)

The [separate one-file patch](../tools/office-engine/patches/package-namespace-resolution.patch)
repairs the prefix-dependent metadata reader found above. Its
[source manifest](../tools/office-engine/patches/package-namespace-source.json)
pins the original file to the same upstream build; the original bytes match the
retained full source archive. Element names now resolve through scoped namespace
declarations before matching the content-type or relationship vocabulary.
Wrong, missing or unbound namespaces are refused. Required attributes, allowed
elements and the existing two-level structure remain enforced. Active-content
permissions and the chart exception are unchanged.

Native draft 30/build 27 passes all twenty-two cases in
`G:\ContextSuite-OfficeBuild\evidence\native-chart-run-25\result.json`:
2,553 ms, two processes, zero remaining children and 58,290,176 bytes peak job
memory. New parser controls check equivalent prefixes, child declarations,
sibling scope restoration, wrong/unbound namespaces, unknown elements, missing
required attributes and excessive nesting. All nine Word/Excel/PowerPoint object
fixtures repeat with prefixed content-types and relationships, preserving their
inactivity and previews. All 5,225 inventoried inputs remain unchanged. Build 26
failed on a missing generated type header in the test; the corrected header and
successful repeat are retained without weakening assertions.

The matching `namespace-drawing-host-export-01` and
`namespace-word-host-export-01` runs pass twelve restricted-host exports,
including both Excel modes. Each PDF passes text, geometry and preview checks;
its full-page pixels exactly match the earlier ordinary-namespace baseline.
`G:\ContextSuite-OfficeBuild\evidence\package-namespace-repair-01-acceptance.json`
binds the outputs to the same 3,818 renderer files, verifies all 4,112
runtime/verifier inputs and confirms removal of twelve native profiles and their
registry mappings. Only `program/comphelper.dll` differs from the preceding
native renderer inventory. A fresh source replay applies the public patch to the
exact original and reverses it byte-for-byte.

This resolves the observed namespace compatibility failure in the evaluation
renderer. It does not adopt that renderer, remove interim application guards or
close the remaining file/network/process, application-publication and final
candidate gates. Earlier failed evidence remains retained.

### Ownership and packaging preparation (2026-09-22)

A fresh `Test-OfficeProfileOwnership.ps1 -CreateDisposableProfile` replay passes
all 140 checks at
`.codex-temp/office-owner/066d49062b3d4c40b4efb95404570a0c`.
The actual private launcher and application owner permit input reads/output
creation and return Windows access-denied errors for input writes and withheld
reads. Recreating the same profile SID after cleanup leaves the old input/output
paths denied. Worker loss stops the retained native child; sharing-conflict,
residual-child-access and reopened-journal recovery checks also pass.

`chart-host-binding.json` in that evidence directory verifies current sources
and binaries and binds six owner/job/launcher sources to each namespace-host
runner. All six distinct test profile folders and registry mappings are absent;
the suite intentionally creates some identities more than once. This refreshes
the scoped file-access and ownership/recovery evidence. It does not prove network
denial or substitute for real-render interruption and application publication.

The partial native build lacked its upstream notice files. Three sequential
upstream packaging targets now generate/install `license.txt`, `LICENSE.html`,
`NOTICE`, `CREDITS.fodt` and the English readme inside the isolated `instdir`.
`G:\ContextSuite-OfficeBuild\evidence\native-notices-01-acceptance.json`
verifies those five files against their source/generated originals and confirms
all preceding 3,818 runtime files are unchanged. The complete runtime now has
3,823 files totaling 544,076,961 bytes. Existing host-test copies remain unchanged;
their original inventories do not include these later notice additions.

`G:\ContextSuite-OfficeBuild\tools\office-review-runtime-01` is a fresh complete
copy with the evaluated unmerged host and a sorted, hash-pinned file inventory.
`evidence\office-review-runtime-01.json` records its identities and one empty
directory. It is prepared for adoption review, not selected by production pins.
The upstream archive build still has an empty `version.ini` build ID; source,
configuration, patch and file hashes currently identify it. Resolve the durable
distribution identity and source kit before final composition.

The matching ZIP is 222,190,663 bytes; its 3,825 files expand to 544,675,255
bytes including the host and inventory. Every archived file was read back and
matched to its reviewed hash, with the empty directory retained. The receipt is
`evidence\office-review-runtime-01-archive.json`. This is an engine-only footprint,
not the size or acceptance of the final combined application.

All 140 retained external dependency inputs match the pinned `download.lst`
hashes; `evidence\dependency-archives-01.json` distinguishes source archives,
the OpenSymbol font and Java tools. Twelve declared inputs are not downloaded;
the inventory alone does not establish the final runtime's complete source
closure or redistribution readiness.

The complete source/archive audit subsequently accounts for all 148,166 regular
files, with no missing files. Eight modified files divide into four runtime-repair
files, two build-compatibility files and two native-test files. Three additions
provide version metadata and the native test source/makefile. All 56 materialized
archive links match their resolved source targets. The receipts are
`evidence\source-correspondence-01\results.json` and
`evidence\source-classification-01\results.json`.
`evidence\source-delta-replay-01\results.json` verifies byte-exact forward and
reverse application of all eight modifications against retained archive originals.
The existing `configure.ac` copy also contains UTF-8-to-Windows-1252 punctuation
changes in help/comments alongside the intentional antivirus-preflight change.
That incidental encoding change is recorded, not silently omitted from the
patches; normalize and revalidate the canonical recipe before source-kit release.
Source-kit assembly, dependency closure and a reproducible build remain open.
The subsequent [source-build checkpoint](office-source-build.md) verifies the
canonical UTF-8 recipe, the exact 140-input upstream fetch selection and standalone
preparation from an assembled source kit. Complete rebuild/adoption remain open.

Static inspection of the first review payload found five imported Microsoft
runtime DLLs available on this machine but absent from that payload. The new
`tools\office-review-runtime-02` preserves the first copy and adds unmodified
x64 Visual Studio redistributables, version `14.51.36247.0`, with valid Microsoft
signatures. Three startup dependencies sit beside the host; all five sit in the
explicitly searched `runtime\program` directory. Host and preceding engine bytes
remain unchanged. The five files' transitive imports require no additional MSVC
DLLs, and static inspection of all 325 native files finds no unbundled MSVC
imports. System32/API-set observations still describe this machine; this is not
clean-machine or dynamic-loader provenance evidence.

The app-local deployment option and redistribution conditions are described in
[Microsoft's deployment guidance](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files?view=msvc-170)
and [Visual Studio 2026 redistribution list](https://learn.microsoft.com/en-us/visualstudio/releases/2026/redistribution).
Compiler eligibility, license compliance and servicing remain part of adoption
review. No system runtime installer was run.

The revised engine-only archive contains 3,833 files and expands to 546,730,852
bytes. Its compressed size is 222,972,095 bytes and SHA-256 is
`493A044BA2F5C6F8F50DE72295185DE78E70CA81440CE8E00E80A16956AA8B30`.
Every archived file, including the three host-adjacent runtime DLLs, matches its
reviewed hash. The archive receipt is
`evidence\office-review-runtime-02-archive.json`.
The paired `review-word-host-export-02` and `review-drawing-host-export-02`
runs pass all twelve exports, including both Excel calculation modes. Text,
geometry and previews pass; every full-page raster is byte-identical to its
preceding namespace-host baseline. All runtime/verifier/fixture/runner inputs
remain unchanged and all twelve profile folders and registry mappings are absent.
`evidence\office-review-runtime-02-acceptance.json` binds these results to the
archive and import inspection. This remains isolated host evidence, not actual
application publication or affirmative network-denial acceptance.
No compiler installation, production engine adoption, installed-app change or
reserved-candidate mutation occurred.

### Runtime linkage constraint

Inspection of the expanded combined candidate's
`office-engine/runtime/program/services/services.rdb` confirms that
`EmbeddedObjectCreator`, `OOoEmbeddedObjectFactory` and
`OOoSpecialEmbeddedObjectFactory` load from `mergedlo.dll`. That payload has no
standalone `embobj` library. The pinned upstream merged-library list also includes
`embobj` alongside `chart2`, `comphelper` and `vcl`. The observed registrations and
file membership are retained in
`G:\ContextSuite-OfficeBuild\evidence\runtime-linkage-inspection.json`.

The current standalone `Library_embobj` build can establish compilation and
linkage of the proposed source change, but its output is not a replacement for
the shipped merged library. Native integration and D07 replay require a matching
rebuilt runtime with a reviewed configuration and component registry. Do not
copy a standalone DLL into the current candidate or infer ABI compatibility from
a successful component build. Keep the reserved candidate and current engine
identities unchanged throughout this evaluation.


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
4. The unchanged four-case D07 application/PDF matrix now passes with the paired
   isolated engine and restrictive profile; see
   [application evidence](excel-layout-acceptance.md#paired-application-acceptance-2026-09-23).
   Word/PowerPoint charts have restricted-host evidence. Complete their required
   application/final-candidate replay and relevant calculation, revision, font,
   image and interruption regressions. Word D04 and fifteen style cases pass.
5. Review source delivery/notices, packaging impact, performance and human pages
   before final engine adoption or composing the final combined candidate. The
   isolated application selection has its own explicit source/runtime pins.

If this proposal needs broader object permissions to render, stop and review the
design rather than expand the exception to arbitrary embedded content. Other
acceptance work can continue while the native build and security checks proceed.
