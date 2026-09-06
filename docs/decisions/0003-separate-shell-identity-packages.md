Separate Shell Identity Packages
================================

Status: accepted
Date: 2026-09-05
Supersedes: [0001: Shell prototype platform](0001-shell-prototype-platform.md),
only for the number of sparse package identities


Context
-------

Decision 0001 registered three application identities and three Explorer verbs
inside one sparse package. A real Windows 11 observation showed three attributed
root flyouts, but every flyout contained all three verbs. Windows groups modern
Explorer commands at the package boundary rather than isolating verbs by the
manifest application that declared them.

The product requires three peer roots—**Analyze**, **Convert**, and **Optimize**—
whose children remain behaviorally isolated.


Decision
--------

Register three sparse identity packages for the shell prototype, one for each
root. Every package contains exactly one application, one
`windows.fileExplorerContextMenus` verb, and its matching COM class registration.
All three packages reference the same externally located native shell DLL, host,
and generated assets.

The application display names are the short root labels **Analyze**, **Convert**,
and **Optimize**. Future prototype child actions for one tool must be registered
only in that tool's identity package.


Consequences
------------

- Windows can attribute and group each tool independently while the repository,
  binaries, and product remain one Context Suite.
- Installation, repair, update, and removal must treat the three identities as
  one transaction and detect partial registration.
- Stable COM CLSIDs and the shared activation contract remain unchanged.
- The development package count increases, and production installer design must
  hide that implementation detail from users.
- This evidence replaces the assumption that separate applications inside one
  package provide isolated Explorer grouping.


Alternatives Considered
-----------------------

- **Three applications in one package:** rejected by observed Windows 11
  behavior because every attributed root received every package verb.
- **One Context Suite root:** technically simpler, but contradicts the product's
  three-peer menu contract.
- **Three independently implemented products:** rejected because only deployment
  identity needs separation; code, host orchestration, settings, and branding
  should remain shared.
