Windows Explorer Integration
============================

Status: behavioral design with an implemented x64 sparse-package prototype. The
prototype registration and activation approach is recorded in decisions 0001,
0002, and 0003; production packaging remains open.


Menu Contract
-------------

Context Suite contributes three peer top-level commands to Windows Explorer:

```text
Analyze
Convert  >
Optimize >
```

There is no required **Context Suite** parent menu. Each root has a distinct icon
and Convert and Optimize contain no more than one level of child commands.

- **Analyze** directly opens the details surface for the complete selection.
- **Convert** offers compatible target formats and a planning action.
- **Optimize** offers compatible loss policies and a configuration action.

Commands should appear in the modern Windows 11 context menu when supported by
the chosen deployment model. A fallback experience may exist, but it must retain
the same behavioral boundaries and command names.


Contextual Capability Rules
---------------------------

The shell surface queries a small capability snapshot for the selection.

- A root is hidden when no useful action applies to any selected file.
- A direct batch child is enabled only when it applies safely to every selected
  file.
- Mixed selections may offer a planning action even when no one-click child is
  common to every file.
- Converter does not offer a file's current format as a meaningful conversion.
- Optimizer offers only policies that retain each file's current format.
- Analyzer can offer full details for a mixed selection when every path can be
  safely handed to the host, even if some formats later report unsupported.

Extension mappings are hints used for fast discovery. The host validates file
contents and recalculates capabilities before executing anything.


Explorer Process Boundary
-------------------------

Explorer-facing code must remain exceptionally small and reliable. During menu
enumeration it must not:

- Load media engines or large managed dependency graphs.
- Decode media or scan complete files.
- Read unbounded metadata.
- Access the network.
- Display windows, notifications, or errors.
- Wait for long-lived locks, services, or child processes.
- Modify files, settings, caches, or logs.

The shell component may normalize the selection, consult compact registration
data, and perform a strictly bounded capability lookup. Full analysis and all
media work run in the Context Suite host or worker process.


Activation Contract
-------------------

Invoking **Analyze** or a Convert or Optimize child command creates an immutable
activation request containing:

- A schema version and unique request identifier.
- The root operation and selected child action.
- Selected paths in Explorer order.
- Invocation context needed for result placement or window ownership.
- No trusted media properties; the host discovers those again.

The request crosses the process boundary through a deliberate activation or IPC
mechanism. Large selections must not depend on fitting every path into one
command-line string. If a temporary manifest is used, it must have a unique name,
restrict access to the current user, validate its schema and size, and be cleaned
up after a bounded lifetime.

The host treats every request as untrusted. It validates operation identifiers,
path counts, path lengths, file existence where required, supported schemes, and
all requested options before planning.

For the planned commercial application, the host also resolves trial or purchase
access before admitting a new operation. Account and activation UI belong in
that process, never Explorer menu enumeration. Website services must not receive
the selected paths or file contents, and an admitted batch may finish if trial
access expires. See [decision 0006](decisions/0006-trial-and-purchase-access.md).


Command Identity
----------------

- Root and child commands have stable identifiers independent of localized text.
- Visibility, enablement, labels, icons, and invocation use the same capability
  definitions.
- Built-in presets have stable identifiers and separately versioned policy
  definitions.
- Unknown or retired command identifiers fail safely in the host.
- File associations and registration state are owned by installation rather than
  media-processing code.


Analysis Details
----------------

**Analyze** has no Explorer submenu. Invoking it immediately hands the complete
selection to the host, which performs analysis out of process and opens the
details surface. Concise snapshots may appear inside that surface, but Explorer
must not parse media or construct property summaries during menu enumeration.


Result Experience
-----------------

The shell component does not own progress or results. The host provides:

- A progress surface for operations long enough to need one.
- Cancellation and per-file state.
- A final summary for success, no-change, cancellation, and partial failure.
- Actions to open output locations, copy paths, retry, or revise settings.

Short successful analysis may open directly to its details. One-click
optimization or conversion should still provide discoverable results without
interrupting the user with a modal success box for every file.


Installation And Recovery
-------------------------

- Installation and uninstallation must register and remove shell commands
  predictably without asking media adapters to modify the registry.
- A missing host, missing engine, or incompatible activation schema disables only
  the affected action and offers repair outside Explorer.
- Updating command definitions must not leave stale duplicate menu entries.
- Clearly document whether Explorer restart, sign-out, or package repair is
  needed.
- Registration, packaging identity, update strategy, and any classic-menu
  fallback require recorded architecture decisions before implementation.


Current Prototype
-----------------

The repository contains a Windows 11 x64 discovery prototype with:

- Three native `IExplorerCommand` COM classes with stable CLSIDs and the titles
  **Analyze**, **Convert**, and **Optimize**.
- Three sparse identity packages, each containing one application and one verb,
  so Windows attributes each root independently instead of aggregating every
  suite verb into every attributed flyout.
- Direct activation for **Analyze** and one-level prototype subcommands for
  **Convert** and **Optimize**, each with a distinct generated icon.
- One immutable, UTF-8, versioned activation request for the complete Explorer
  selection. The request is capped at 4 MiB and 4,096 paths.
- A separate native host that distrusts and revalidates the schema, operation,
  action, selection count, absolute paths, and file existence.
- Contract tests that invoke all three COM classes with one three-file
  `IShellItemArray` and verify one complete host activation per command.

This is deliberately an integration spike rather than the final desktop UI or
installer. The host displays an activation summary and performs no media work.
The final UI framework, production package identity, signing, and update model
remain separate decisions.


Verification
------------

Shell integration requires tests for:

- Single, repeated-type, mixed-type, unsupported, missing, and large selections.
- Three peer root commands with no required suite parent.
- Direct **Analyze** activation with no submenu.
- One-level **Convert** and **Optimize** child menus.
- Correct visibility and enablement for every registered capability.
- Stable command identity across labels, icons, invocation, and localization.
- Menu enumeration within a documented performance budget.
- Host activation with Unicode paths, long paths, spaces, and large selections.
- Rejection of malformed, oversized, stale, and unknown-schema requests.
- Missing host or media engine behavior without destabilizing Explorer.
- Installation, upgrade, repair, and uninstallation without duplicate commands.
- End-to-end execution from each root through host-side revalidation.
