Production UI And Processes
===========================

Status: accepted; process foundation implemented, media work pending
Date: 2026-09-06


Context
-------

The native shell prototype proves command layout and complete-selection handoff.
At acceptance, the production UI, application lifetime, and media worker had not
been built. The foundation implementation is now described in
[development status](../development.md); media implementations remain absent.
The next foundation should support one small Windows utility built from public
source and selected private production implementations.


Decision
--------

Use C# on .NET 10 with WPF and MVVM for the desktop application. Retain the small
native C++ Explorer extension. Begin with a few projects and feature folders
rather than creating a separate assembly for every logical component.

| Component | Responsibility |
| --- | --- |
| Native shell | Fast command enumeration and selection handoff |
| Desktop application | Windows, activation, access policy, queue, settings, progress, and results |
| Processing worker | Isolated codec execution and external engine invocation |
| Shared core | Typed requests, facts, plans, policies, and results independent of UI and engines |

The production build composes selected private implementations through public
contracts, without a separate demonstration edition. The worker
boundary contains processing failures; it is not automatically a security
sandbox or an anti-piracy mechanism.


Accepted Foundation
-------------------

These defaults are approved for the initial production foundation.

| Choice | Selected starting point | Main tradeoff |
| --- | --- | --- |
| Platform | Supported Windows 11 x64 releases, .NET 10 LTS | No initial Windows 10 or native ARM64 support |
| UI | WPF with MVVM; retain the native C++ shell | Less built-in Windows 11 visual styling than WinUI 3 |
| Application lifetime | One application per interactive user session, with one shared queue | Requires activation forwarding and startup-race handling |
| Worker | One on-demand worker, one active file at a time initially | Less batch throughput than parallel workers, simpler cancellation and recovery |
| Activation | Retain the bounded shell request-file contract initially | Requires private storage permissions, consumption rules, and stale-file cleanup |
| Process communication | Versioned, bounded JSON messages over local named pipes | Requires framing, timeouts, identity checks, and disconnect handling |
| Output ownership | Application reserves destinations and publishes; worker processes and validates temporary outputs | Requires explicit completion acknowledgements across the process boundary |
| Settings | Versioned JSON under the user's local application-data directory | Schema validation and safe replacement are still required |

Microsoft recommends WinUI 3 for new Windows applications. WPF is selected here
as a project-specific simplicity tradeoff, not because WinUI is unsupported or
unsuitable. Avalonia would be worth reconsidering if cross-platform delivery
became a real requirement; it would not remove the Windows-specific shell work.
Avoid a web UI runtime and a custom design system for the initial utility.

Repeated invocations should forward the complete request to the existing app
and acknowledge receipt before the forwarding process exits. Deduplicate request
IDs and test simultaneous startup. Do not run a tray resident or login-started
service. Closing the last window exits when idle; with work pending, ask whether
to keep the window open or cancel and exit safely. Do not lose queued work
silently or promise persistent job recovery in the first version.

Start the worker when media work is first needed and stop it with the app.
Sequential files still constitute a batch: one selection, shared policy,
aggregate progress, and per-file outcomes. Bound engine threads and resources
as well as file concurrency. On worker failure, fail the active file without
publishing its temporary output, report it, and start a fresh worker for pending
files. Do not automatically retry the failed file. Cancel cooperatively, then
terminate the owned process tree after a bounded timeout if necessary.

Restrict pipes to the current user and interactive session, reject remote
clients, and validate peer identity, protocol version, message size, and request
IDs. A user restriction alone is not protection against other software running
as that user. Keep the shell out of the worker protocol. The app must validate
request files before forwarding them, acknowledge consumption, and clean up only
its own validated request paths. Preserve the existing three-file activation
tests while replacing the native prototype host with production activation.

The application owns final publication and result accounting. The worker may
only produce the reserved temporary output and validation evidence for the
matching operation. A worker exit code alone never authorizes publication.
Application shutdown, pipe loss, or cancellation must not publish a partial
output; completed publications remain completed rather than being rolled back
as part of batch cancellation.

Begin with Core, Application, Worker, and one private implementation project,
plus the existing shell and focused tests. Use feature folders for analysis,
conversion, optimization, and infrastructure rather than an assembly per tool.
Do not add a plugin loader, dependency package feed, database, or separate
application edition. Keep credentials separate from ordinary settings JSON.


Initial Source Allocation
-------------------------

The accepted allocation is public contracts, UI, queue, output safety, DDS parser,
and their tests; private production media adapters, built-in optimization policy
catalog, and commercial service integration. This keeps substantial engineering
visible without introducing artificial secret-based compilation checks. Exact
private adapters follow engine selection. This refines the ownership boundary
in [decision 0005](0005-public-and-proprietary-builds.md).


Deferred Decisions And Implementation Work
------------------------------------------

Scaffolding may proceed using this decision. Specify exact IPC schemas, timeout
values, startup coordination, request-file cleanup, and settings fields as their
contracts are implemented and tested. Preserve the native shell tests during
the production-host transition. Acceptance does not mean these mechanisms have
already been implemented or verified. Consult the development status for the
current evidence and remaining visual/installed-package smoke checks.

Media-engine selection remains separate. Magick.NET and ImageSharp are image
candidates; oxipng and pngquant are PNG candidates; FFmpeg/ffprobe are audio
candidates. Pin versions only after capability, redistribution, and packaging
evaluation. No media engine is accepted by this decision. Installer/update
technology and detailed commercial-access policies also remain deferred to
their dependent work; parallel file processing requires measured justification.


Alternatives Considered
-----------------------

- WinUI 3: a candidate when native Windows presentation takes priority.
- Avalonia: a candidate if cross-platform desktop delivery becomes a goal.
- Codec work inside the desktop process: fewer processes, but weaker fault
  isolation for the UI.


Sources
-------

- [Microsoft desktop framework guidance](https://learn.microsoft.com/en-us/windows/apps/get-started/)
- [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Named-pipe security](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights)
- [.NET pipe options](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0)
