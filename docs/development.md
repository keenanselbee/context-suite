Production Foundation Development
=================================

Prerequisites: Windows 11 x64, .NET 10 SDK, and Visual Studio 2026 x64 C++ tools
with Windows SDK 10.0.26100.0 for the native shell. `global.json` permits installed
stable .NET 10 feature bands. Run commands from the repository root.


Build And Test
--------------

```powershell
# Public checkout only; no private credentials or implementation needed.
./tools/Test-Repository.ps1
./tools/Test-Foundation.ps1 -Configuration Release
./tools/Test-PrivateBoundary.ps1
./tools/Test-ShellPrototype.ps1 -Configuration Release

# Complete foundation, requiring private source and staged curated inputs.
# See tools/curated-engine/README.md for the reviewed native staging workflow.
./tools/dds-engine/Build-DdsEngine.ps1
./tools/Build-Production.ps1 -Configuration Release
./tools/Test-Foundation.ps1 -Configuration Release -Integration
./artifacts/production/Release/ContextSuite.Application.exe

# Real image-engine/adapter contracts, requiring the private checkout.
./tools/Test-ImageConversion.ps1 -Configuration Release
./tools/Test-DdsCodec.ps1 -SkipNativeBuild
# Opt-in desktop test of the actual conversion UI with isolated trial/settings.
./tools/Test-DesktopSmoke.ps1 -Configuration Release -Images
# Opt-in native replacement; recycles only two newly generated image fixtures.
./tools/Test-PublicationWindows.ps1 -Configuration Release -Recycle -Images
```

Build commands restore required SDK projects automatically. `dotnet restore`
against an individual `.csproj` is also supported. `Test-Repository.ps1` is the
initial read-only whitespace/link/source-boundary check; it is not a full C#
style analyzer. Managed builds treat compiler/analyzer warnings as errors.
The contract harness is a console executable with a nonzero failure exit,
matching the native harness convention; use the script, not `dotnet test`.

Close Context Suite before running activation-router contracts. They acquire
the same per-user/session pipe to verify single ownership and forwarding.
Do not run two foundation test harnesses concurrently in one session.

Open `ContextSuite.Production.slnx` for the managed projects in the IDE.
Production output is under `artifacts/production/<Configuration>`. The worker
must remain beside the application. The native prototype continues to build
independently through `tools/Build.ps1` and `ContextSuite.sln`. No build or test
installs packages or restarts Explorer. When deployed beside the production app,
the shell DLL chooses it; otherwise it retains the native prototype host.
Installed packages are not automatically switched to this new output directory.

For an explicitly requested local Explorer smoke test, switch the existing three
development identities with `./tools/Install-ShellPrototype.ps1 -Production
-Configuration Release` (on one line). This builds production and registers
generated manifests targeting the WPF executable. Then run
`./tools/Test-InstalledShellPrototype.ps1` and
`./tools/Open-ShellPrototypeTestFolder.ps1 -Production`. Restart Explorer if it
retains the old shell DLL. To return to the native prototype, run the installer
without `-Production`. Neither mode is a release installer.


Implementation Boundaries
-------------------------

- Core: immutable operation data, activation parser, typed policies/facts/results,
  and bounded JSON framing. No UI, account service, or codec dependency.
- Application: WPF window/view model, bounded in-memory selection queue, activation
  forwarding, request-file handling, and on-demand worker ownership.
- Worker: authenticated local connection and real private capability discovery.
- Private: real image adapter and production composition; the catalog advertises
  twenty tested ordinary-image pairs and seven bounded DDS pairs. No fake encoder or licensing
  bypass is supplied.
- Shared source: Windows pipe identity/security code compiled into its process
  owners and contract tests without adding an infrastructure assembly.

This is one unfinished production application, not a demonstration edition.
Convert selections now open the image planner and can produce validated
PNG/JPEG/WebP/BMP/TGA/DDS copies after confirmation. Analyze reports DDS headers;
Optimize remains unimplemented.
The image worker, application executor and real conversion UI have focused
integration tests; final goal acceptance and release checks remain separate.
Trial admission precedes reserving/encoding/publishing.
UI tests compile the same app sources into a test-only host with isolated paths
and real trial enforcement. No permissive shipping access implementation exists;
live Polar access remains deferred. Only the worker links private image engines;
the app retains the mandatory-private build check without a runtime engine reference.
The production trial record is `%LOCALAPPDATA%/ContextSuite/Access/trial.json`,
separate from settings. Status reads do not start/create a trial. Only a confirmed
executable immutable conversion plan can call admission, and a successful atomic
save must precede admission. Public contracts use isolated scratch directories and
test clocks, not the user's real record. There is no shipping reset switch.
The shared settings UI now stores versioned JSON at
`%LOCALAPPDATA%/ContextSuite/settings.json`. It opens from the app or pathless
Convert/Optimize `settings` activation. Output-folder preferences and replacement
permission are separate per tool. Permission defaults off; availability is gated
to verified Windows build 26200 x64, with each replacement plan restricted to an
ordinary single-link local NTFS file. Each admitted batch captures immutable
preferences. Unknown schemas
are read-only, invalid settings fall back safely, and failed/stale saves do not
silently truncate or replace the previously loaded preferences. Credentials and
trial records are not stored here. The application-owned publisher implements
validated copies, explicit replacement, and recycle-only cleanup. Its tests use
controlled temporary bytes plus real worker integration, never a permissive access provider.
The image planner supplies semantic validation and per-batch replacement
confirmation; enabling the preference alone never authorizes replacement.
See the [safety goal evidence](image-output-safety-goal.md).

Publication recovery evidence is stored separately at
`%LOCALAPPDATA%/ContextSuite/Publications`. Startup displays the record location
when records remain. It does not resume jobs, purge backups, or offer app Undo.
Records contain paths, byte fingerprints, and publication stages, not credentials.

For explicit native filesystem/Shell verification on an unlocked Windows desktop:

```powershell
./tools/Test-PublicationWindows.ps1 -Recycle
```

This opt-in test builds public contracts and runs a bounded hidden child process.
It creates disposable inputs beneath `.codex-temp/publication-windows`, verifies
same-path and different-extension publication, reads the original bytes back from
the Recycle Bin, and proves the callback vetoes a native permanent-delete proposal.
It also checks cancellation and changed-source rejection. It never empties the
Recycle Bin or changes its settings; recycled test originals remain recoverable
there. Logs and result JSON remain under the test directory. Ordinary foundation
tests instead cover injected disk-full/permission failures, actual file locks,
and six forced-termination stages without performing Shell recycling.

Activation messages use protocol version 1 and bounded length-prefixed UTF-8
JSON (32 MiB maximum to accommodate escaping a 4 MiB shell request). Requests
retain the native line-oriented shell schema and its 4,096-path limit. Pipes
reject remote clients, restrict the current user, and verify process/session
identity. Same-user malicious software is not treated as a solved threat model.

Queue/history limits are 16,384 files and 1,024 activation IDs per app session.
Duplicate IDs are acknowledged without adding a second batch. Full queues and
closing instances reject requests without deleting their activation file.
Request files are deleted only after positive receipt. Forwarding uses a final
client receipt before disconnecting, because Windows pipe disconnection discards
unread response data. Cleanup examines at most
256 files per launch and removes only GUID-named request/temp files older than
24 hours in the owned request directory, excluding reparse points.

The worker's connection deadline is 10 seconds; application requests allow 30
seconds for probe/preview and 120 seconds for conversion. Shutdown permits 2 seconds
before terminating the owned process tree. Cancellation tears down the worker
connection; a later request starts a fresh worker. Parent exit or pipe closure
also ends the worker. No persistent job recovery or worker pool is implemented.


CI And Private Source
---------------------

Public CI never checks out private source. It runs public contracts, native
contracts, documentation checks, and expected missing-private build failures.
Private CI is manually dispatched from `context-suite-private` with an approved
immutable public commit SHA. It checks out both repositories, tests integration,
and imports the exact reviewed image-engine archive from an owner-provisioned
private release before building DDS. It exports revision IDs and an unsigned,
inventoried internal candidate ZIP, never source or symbols. See the
[packaging goal](release-packaging-goal.md) for the required asset, scripts,
runtime preflight, Inno offline installer commands and unverified signing/lifecycle
gates. `tools/release/Test-InnoInstaller.ps1` runs registration contracts with
mocked platform calls; it never installs or unregisters packages. Do not run private integration against
untrusted public contributions or export whole workspaces, source, or symbols.

Workflow files must be committed/pushed before hosted CI can execute; adding
them locally does not establish a hosted CI pass. Parent commits do not commit
the nested private repository. No source-license or payment-provider choice is
made by this scaffold.


Verification Status
-------------------

The original foundation evidence comprised Release builds and 50 managed
contracts; the completed conversion goal now records 280 passing integration
contracts and expanded engine/UI evidence. Foundation coverage includes full
selection transport, malformed clients, duplicate IDs, queue bounds, cancellation,
worker reuse, and restart after cancellation/crash. Native Release contracts also
pass for all three commands and complete three-file selections. Both production
projects fail with the documented error when private implementations are absent.
A public-only snapshot without `proprietary/` also passes component contracts.
The actual WPF process smoke verifies startup, three successive three-file
activations routed into one owner, request consumption, an on-demand worker,
unchanged sources, and worker exit after abrupt parent termination. The smoke
uses temporary GUID request files in the normal local activation directory and
removes them afterward; it does not install packages or automate Explorer UI.

The computer-use helper was unavailable during foundation implementation.
The later [desktop smoke harness](desktop-smoke-tests.md) adds real WPF UIA
assertions and an optional installed Explorer path. The WPF smoke has passed
locally, including row contents, focus, resize, and close/reopen. Production
Explorer automation is experimental and has not passed end to end. Subsequent
user-provided screenshots confirm the manual classic Explorer-to-WPF handoff:
all three tools receive complete three-file selections and accumulate 12 rows
across four batches in one window. The local development shell
registration was explicitly switched for local smoke work after the original
foundation validation. Neither registration nor UI test success certifies a
release installer. Window appearance, modern menu icons/order, high-DPI layout,
and screen-reader usability still require manual review.
