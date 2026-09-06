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

# Complete foundation, requiring compatible private source at proprietary/.
./tools/Build-Production.ps1 -Configuration Release
./tools/Test-Foundation.ps1 -Configuration Release -Integration
./artifacts/production/Release/ContextSuite.Application.exe
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


Implementation Boundaries
-------------------------

- Core: immutable operation data, activation parser, typed policies/facts/results,
  and bounded JSON framing. No UI, account service, or codec dependency.
- Application: WPF window/view model, bounded in-memory selection queue, activation
  forwarding, request-file handling, and on-demand worker ownership.
- Worker: authenticated local connection and real private capability discovery.
- Private: production composition with an empty media catalog until real adapters
  are implemented. No fake encoder or licensing bypass is supplied.
- Shared source: Windows pipe identity/security code compiled into its process
  owners and contract tests without adding an infrastructure assembly.

This is one unfinished production application, not a demonstration edition.
Selections currently resolve to unsupported/not-implemented results. No media
bytes are inspected or transformed. The access-policy contract has no permissive
shipping implementation; real operation admission awaits the commercial slice.
There is no settings UI or persisted user preference yet; versioned JSON storage
is the accepted approach when the first real preference is introduced.

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

Worker startup and request timeouts are 10 seconds; shutdown permits 2 seconds
before terminating the owned process tree. Cancellation tears down the worker
connection; a later request starts a fresh worker. Parent exit or pipe closure
also ends the worker. No persistent job recovery or worker pool is implemented.


CI And Private Source
---------------------

Public CI never checks out private source. It runs public contracts, native
contracts, documentation checks, and expected missing-private build failures.
Private CI is manually dispatched from `context-suite-private` with an approved
immutable public commit SHA. It checks out both repositories, tests integration,
and exports only their revision IDs. Do not run private integration against
untrusted public contributions or export whole workspaces, source, or symbols.

Workflow files must be committed/pushed before hosted CI can execute; adding
them locally does not establish a hosted CI pass. Parent commits do not commit
the nested private repository. No source-license or payment-provider choice is
made by this scaffold.


Verification Status
-------------------

Local Release builds and 50 managed foundation contracts pass, including full
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

The computer-use helper was unavailable during implementation, so actual window
appearance, keyboard navigation, screen-reader presentation, and the installed
Explorer-to-WPF path are not manually verified. The existing installed packages
were not changed. Before installation/release, verify direct Analyze activation,
both submenus, repeated activations into one window, readable batch rows, and safe
window closure. Automated protocol tests are not a substitute for that smoke test.
