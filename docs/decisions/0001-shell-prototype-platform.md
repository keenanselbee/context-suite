Shell Prototype Platform
========================

Status: accepted for the prototype; package count superseded by
[0003: Separate shell identity packages](0003-separate-shell-identity-packages.md)
Date: 2026-09-05


Context
-------

The product requires three peer commands in the modern Windows 11 File Explorer
menu. Registry-only legacy verbs cannot prove that experience, and Explorer
loads command implementation code on a stability-sensitive path. The production
installer and final desktop UI are not yet selected.


Decision
--------

Build the discovery prototype for Windows 11 build 26100 on x64. Implement each
command as a separate native C++ `IExplorerCommand` COM class in one statically
linked DLL. Register the DLL with a development sparse package and give Analyze,
Convert, and Optimize separate package application identities, each with one
verb.

Keep enumeration bounded and free of media engines, network access, UI, and file
mutation. Launch a separate native prototype host only from `Invoke`.

This decision selects the shell-spike platform, not the final application UI,
production packaging format, supported architecture set, or installer.


Consequences
------------

- Explorer can exercise the same `IExplorerCommand` mechanism required by the
  modern Windows 11 menu.
- Three application identities allow a direct test of Windows app-attribution
  grouping while retaining one product package and one implementation DLL.
- The shell component has no managed runtime or media-engine dependency.
- Developers must build an x64 DLL matching the Explorer process architecture.
- The development package is local, unsigned, and must not be treated as a
  release artifact.
- Other Windows versions, Arm64, signing, upgrades, and final installer behavior
  remain open decisions.


Alternatives Considered
-----------------------

- **Legacy registry verbs:** simpler, but they appear under **Show more options**
  and cannot validate the intended Windows 11 surface.
- **One application with three verbs:** supported, but Windows may group multiple
  verbs attributed to one app. Separate identities give the peer layout its
  strongest valid prototype.
- **Managed in-process shell extension:** rejected because Explorer-facing code
  should not load a managed dependency graph.
- **Full MSIX immediately:** deferred until installation, signing, update, and
  final application lifecycle decisions are ready.
