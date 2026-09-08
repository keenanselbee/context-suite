Architecture Decisions
======================

Use this directory for decisions that constrain implementation, packaging,
public behavior, or long-term maintenance. Do not create a decision record for a
routine code choice that is obvious from the implementation.


When To Record A Decision
-------------------------

Create a record when choosing or changing matters such as:

- Desktop UI framework or application lifecycle.
- Explorer integration and deployment identity.
- Inter-process activation and request schemas.
- Media engines and binary distribution.
- Supported Windows versions or architectures.
- Persisted settings and preset schemas.
- Output replacement guarantees.
- A significant change to product scope or tool boundaries.


Naming
------

Use a four-digit sequence and a short lower-kebab-case title:

```text
0001-select-desktop-ui-framework.md
0002-select-shell-registration-model.md
```


Template
--------

```markdown
Decision Title
==============

Status: proposed | accepted | superseded
Date: YYYY-MM-DD


Context
-------

What problem or constraint requires a durable decision?


Decision
--------

What was selected?


Consequences
------------

What becomes easier, harder, required, or intentionally unsupported?


Alternatives Considered
-----------------------

Which credible alternatives were rejected, and why?
```

Keep records concise, link superseding decisions in both directions, and update
design documents when an accepted decision changes their assumptions.


Current Decisions
-----------------

- [0012: Inno offline installer](0012-inno-offline-installer.md) — accepted; internal first-install candidate, signing and lifecycle acceptance pending

- [0011: DDS engine and texture policies](0011-dds-engine-and-texture-policies.md) — accepted; bounded local implementation and acceptance completed

- [0001: Shell prototype platform](0001-shell-prototype-platform.md)
- [0002: Shell activation request](0002-shell-activation-request.md)
- [0003: Separate shell identity packages](0003-separate-shell-identity-packages.md)
- [0004: Analyzer product language](0004-analyzer-product-language.md)
- [0005: Public and proprietary builds](0005-public-and-proprietary-builds.md) — accepted
- [0006: Trial and purchase access](0006-trial-and-purchase-access.md) — local trial settled by 0009; paid policies open
- [0007: Production UI and processes](0007-production-ui-and-processes.md) — accepted; process foundation implemented
- [0008: Output naming, settings, and replacement](0008-output-naming-settings-and-replacement.md) — implemented; replacement restricted to verified platforms/paths
- [0009: First image engine and local trial](0009-first-image-engine-and-trial.md) — bounded local acceptance passed; release packaging refined by 0010
- [0010: First release formats and curated image engine](0010-first-release-formats-and-curated-engine.md) — accepted; curated development packaging and bounded BMP/TGA conversion implemented; DDS refined by 0011, release clearance pending
