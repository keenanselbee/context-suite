Production Foundation Development
=================================

Prerequisites: Windows 11 x64, .NET 10 SDK, and Visual Studio 2026 x64 C++ tools
with Windows SDK 10.0.26100.0 for the native shell. `global.json` permits installed
stable .NET 10 feature bands. Run commands from the repository root.

Follow [repository maintenance](repository-maintenance.md) for agent task
closeout, temporary output, retained evidence and the cleanup/commit plan.


The free transition under [decision 0021](decisions/0021-free-product-and-public-implementation.md)
consolidates production source here. Legacy private/commercial evidence in the
[historical record](foundation-development-history.md) retains its original scope;
current verification is in [the transition goal](free-access-goal.md).


Build And Test
--------------

```powershell
# Public checkout only; no private checkout needed.
./tools/Test-Repository.ps1
./tools/Test-Foundation.ps1 -Configuration Release
./tools/Test-ProductionSource.ps1
./tools/Test-ShellPrototype.ps1 -Configuration Release
./tools/release/Test-InstallerSelections.ps1 # Mocked modern/classic lifecycle; no installation.
./tools/release/Test-InstallerComponents.ps1 # All seven payload selections; no engine inputs needed.

# Real image-engine/adapter contracts, requiring staged native inputs.
./tools/Test-ImageConversion.ps1 -Configuration Release
./tools/Test-PngQuantization.ps1 -Configuration Release # Research only; generates an offline comparison report.
# Optional: -Corpus '.codex-temp/png-quantization/screenshots-<id>' for pre-copied research inputs.
./tools/Test-DdsCodec.ps1 -SkipNativeBuild
# Opt-in desktop test of the actual conversion UI with isolated settings.
./tools/Test-DesktopSmoke.ps1 -Configuration Release -Images -WorkerPath '<verified isolated stage>/ContextSuite.Worker.exe'
# Opt-in native publication test with newly generated disposable inputs.
./tools/Test-PublicationWindows.ps1 -Configuration Release -Recycle
```

Build commands restore required SDK projects automatically. `dotnet restore`
against an individual `.csproj` is also supported. `Test-Repository.ps1` is the
initial read-only whitespace/link/source-boundary check; it is not a full C#
style analyzer. Managed builds treat compiler/analyzer warnings as errors.
The contract harness is a console executable with a nonzero failure exit,
matching the native harness convention; use the script, not `dotnet test`.

Public CI runs documentation/source-boundary checks separately from public code
contracts, so a documentation failure does not suppress code-test results.
Public checks use a fresh checkout without private source or ignored local
evidence. Keep linked deliverables in public source; describe scratch evidence
with a plain path labelled local-only. `tools/Test-RepositoryLinks.ps1` exercises
this rule with disposable fixtures, including an ignored file that exists locally.

For CI or build changes, replay the relevant commands from a clean public source
snapshot and report exactly what ran. Keep expensive engine integration,
native installation and interactive desktop acceptance in their explicit local
workflows; release verification applies to the exact packaged payload.
After an authorized push, inspect the workflow for that commit and distinguish
remote success, failure or a pending run from local results. Triage failures
before dependent work; keep ordinary workflow history. Commits, pushes and
release actions retain their separate authorization requirements.

Close Context Suite before running activation-router contracts. They acquire
the same per-user/session pipe to verify single ownership and forwarding.
Do not run two foundation test harnesses concurrently in one session.

Foundation runs keep a log and result under `.codex-temp/ft-<12hex>`. The short
run name keeps nested Office test paths within the existing application limit.
Successful runs remove their own disposable `f` directory after recording
the result; failed runs retain fixtures and diagnostics. Pass `-KeepScratch`
when investigating a successful run. Existing historical runs remain untouched.
The focused cleanup guards are `tools/maintenance/Test-FoundationScratch.ps1`
and `python -B tools/maintenance/Test-WorkspaceHistory.py`; their fixtures are
isolated from release history.

Open `ContextSuite.Production.slnx` for the managed projects in the IDE.
Production output is under `artifacts/production/<Configuration>`. The worker
must remain beside the application. The native prototype continues to build
independently through `tools/Build.ps1` and `ContextSuite.sln`. No build or test
installs packages or restarts Explorer. When deployed beside the production app,
the shell DLL chooses it; otherwise it retains the native prototype host.
Installed packages are not automatically switched to this new output directory.
However, rebuilding an already registered development directory updates its files.
`New-ReleaseCandidate.ps1` uses fresh isolated staging instead, subject to the
version reservation rules below.

The default `Build-Production.ps1` output is a reusable development directory,
not an isolated candidate. On this workstation `artifacts/production/Release`
is installed: do not refresh it as routine verification. `Test-Foundation.ps1
-Integration` also rebuilds that configuration's production directory. Use these
commands only in a disposable source checkout or a confirmed unregistered,
unused development configuration. Stage the exact curated engine first using
the [engine guide](../tools/curated-engine/README.md), then build DDS and stage
PNG inputs before production compilation. Required engine inputs remain separate
from the complete authored source; missing pins must fail rather than use stock
native assets.

Only in that disposable checkout or unused, unregistered configuration, the
ordinary development sequence is:

```powershell
./tools/dds-engine/Build-DdsEngine.ps1
./tools/png-engine/Stage-PngEngine.ps1
./tools/Build-Production.ps1 -Configuration Release
./tools/Test-Foundation.ps1 -Configuration Release -Integration
```

Do not run this sequence against the installed Release directory in this
checkout. The optional `Test-PublicationWindows.ps1 -Recycle -Images` mode also
rebuilds that production configuration: it requires the same output safeguards
and separate explicit authorization to recycle its disposable test originals.

Fresh candidate staging uses `Build-Production.ps1 -StagingId <new-guid>` or
`tools/release/New-ReleaseCandidate.ps1`. A new GUID isolates files but also
reserves the product version; it is not a disposable compile option. Follow
[product versioning](product-versioning.md) and the
[packaging workflow](release-packaging-goal.md), including clean source and all
selected engine inputs. Failed reservations remain protected. Existing verified
candidates must be reused as preserved inputs, never rebuilt in place.

Installation/registration and Explorer acceptance are separately authorized
workflows in [shell integration](shell-integration.md). Normal build and test
work does not switch registered identities or restart Explorer.

Some engine documentation is itself part of an immutable reviewed source kit.
In particular, the AVIF/SVG engine READMEs and KIT documents and HEIF/audio
distribution BUILD documents retain historical private-source wording. Current
authored native projects are under `src/ContextSuite.*.Native` in this checkout;
use the current repository build scripts for fresh builds. Historical packaged
recipes still name their original source layout. Do not rewrite their bytes or
repin engines as documentation cleanup: preserve old kit identities and add
current guidance outside the selected recipe, or review a new kit separately.


Implementation Boundaries
-------------------------

- Core: immutable operation data, activation parser, typed policies/facts/results,
  and bounded JSON framing. No UI, account service, or codec dependency.
- Application: WPF window/view model, bounded in-memory selection queue, activation
  forwarding, request-file handling, and on-demand worker ownership.
- Worker: authenticated local connection and real media capability discovery.
- Admission: validates confirmed plans, cancellation, and batch identity without
  licensing storage or network dependencies.
- Media implementation: authored adapters and production catalog under src/;
  the existing ContextSuite.Private project name is retained. The WPF app does
  not load engine assemblies; the worker owns them.
- Shared source: Windows pipe identity/security code compiled into its process
  owners and contract tests without adding an infrastructure assembly.

This is one free production application. Routine Convert and Optimize commands
run directly with captured settings; necessary matte, DDS, quality, and document
choices use focused prompts. Standalone launch opens Settings/help. Analyze is
read-only. The general workspace and routine planners are removed. See the
[simplification goal](context-menu-simplification-goal.md) and
[launch matrix](launch-capability-matrix.md) for current scope; automated checks
do not establish outstanding visual, accessibility, or release acceptance.
Validated free admission precedes reserving, encoding, and publishing. UI tests
compile the same app sources into a test-only host with isolated paths. The free
runtime does not read or write legacy files under
%LOCALAPPDATA%/ContextSuite/Access, including trial.json and protected license
records. Existing files are preserved through upgrades and uninstall. Public
contracts use disposable legacy fixtures and retain plan/cancellation/batch
identity refusal checks. See [the transition goal](free-access-goal.md).
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
The explicit per-tool Overwrite originals choice in Settings supplies standing
consent for future commands. Legacy permission-only settings migrate to copies.
Each operation still validates safety and mandatory-copy exceptions; no routine
per-batch replacement confirmation is required.
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


CI And Source
-------------

Both workflows live in the main repository and require no private checkout.
`public.yml` runs documentation/source-boundary, managed, hidden-view, installer
mock, and native shell contracts. `Test-ProductionSource.ps1` checks complete
main-source references and rejects private/reference dependencies.

`production.yml` is manually dispatched with an approved full source SHA and a
public engine-release tag. It imports the exact curated engine and palette
assets, builds DDS, stages PNG inputs, runs integration, and exports an unsigned
inventoried candidate plus its source revision. The hosted runner is a fresh
checkout; its default development output is not this workstation's installed
directory. See the [packaging goal](release-packaging-goal.md) for provisioning,
runtime preflight, installer commands, and remaining signing/lifecycle gates.
`tools/release/Test-InnoInstaller.ps1` uses mocked platform calls and does not
install or unregister packages. Never export whole workspaces, private historical
configuration, or symbols as release artifacts.

Workflow files must be committed/pushed before hosted CI can execute; adding
them locally does not establish a hosted CI pass. Parent commits do not commit
the nested historical private repository. Product/source terms are recorded in
[LICENSE.md](../LICENSE.md); free operation requires no payment provider.


Verification Status
-------------------

Current free-source and unsigned-candidate evidence is maintained in the
[transition goal](free-access-goal.md). Original process/UI observations remain
in [the historical record](foundation-development-history.md). They do not
establish current visual, accessibility, installed Explorer, or native installer
acceptance.
