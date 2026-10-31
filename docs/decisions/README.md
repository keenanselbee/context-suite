Architecture Decisions
======================

Latest: [0021: Free product and public implementation](0021-free-product-and-public-implementation.md) --
accepted; one free build, no trial or licensing, and retirement of the private
implementation boundary. Local migration and candidate verification are complete;
live retirement and release acceptance remain separate. See
[transition evidence](../free-access-goal.md) and [project status](../../README.md#project-status).

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

Decision status describes the selected policy. Dated implementation and acceptance
claims remain evidence of their checkpoints, not current payload status.

- [0021: Free product and public implementation](0021-free-product-and-public-implementation.md) - current product/access and source boundary.
- [0019: Broad file analysis and media expansion](0019-broad-file-analysis-and-media-expansion.md) - accepted scope; capability and acceptance details are in the owning designs and goal.
- [0018: Context-menu utility and output preference](0018-context-menu-utility-and-output-preference.md) - implemented with owner acceptance; License surface superseded by 0021.
- [0016: Fixed optimization recipes](0016-fixed-optimization-recipes.md) - implemented bounded recipe policy; refines 0015.
- [0015: Simple context-menu workflows](0015-simple-context-menu-workflows.md) - accepted UX direction, refined by 0016 and 0018; commercial access superseded by 0021.
- [0014: PNG precision presets](0014-png-precision-presets.md) - implemented policy; planner presentation refined by 0015/0018.
- [0013: Lossless PNG optimization](0013-lossless-png-optimization.md) - implemented bounded policy; planner/access portions superseded by 0018/0021.
- [0012: Inno offline installer](0012-inno-offline-installer.md) - accepted installer direction; native lifecycle and signing acceptance remain separate.
- [0011: DDS engine and texture policies](0011-dds-engine-and-texture-policies.md) - accepted bounded media policy.
- [0010: First release formats and curated engine](0010-first-release-formats-and-curated-engine.md) - engine policy retained; launch ordering broadened by 0019.
- [0009: First image engine and local trial](0009-first-image-engine-and-trial.md) - engine/media policy retained; trial superseded by 0021.
- [0008: Output naming, settings, and replacement](0008-output-naming-settings-and-replacement.md) - safety policy retained; consent model refined by 0018.
- [0007: Production UI and processes](0007-production-ui-and-processes.md) - process foundation retained; private/commercial boundary superseded by 0021.
- [0004: Analyzer product language](0004-analyzer-product-language.md) - accepted tool language.
- [0003: Separate shell identity packages](0003-separate-shell-identity-packages.md) - accepted shell identity policy.
- [0002: Shell activation request](0002-shell-activation-request.md) - prototype contract and history; production IPC refined by 0007.
- [0001: Shell prototype platform](0001-shell-prototype-platform.md) - prototype policy; package count superseded by 0003.


Superseded Product Policies
---------------------------

Preserve these decisions' original reasoning and evidence; decision 0021 now
owns product access and the implementation boundary.

- [0020: Seven-day trial and pricing](0020-seven-day-trial-and-pricing.md) - superseded by 0021.
- [0017: Paid access and release candidate](0017-paid-access-and-release-candidate.md) - paid access superseded by 0021; dated candidate evidence retained.
- [0006: Trial and purchase access](0006-trial-and-purchase-access.md) - superseded by 0021.
- [0005: Public and proprietary builds](0005-public-and-proprietary-builds.md) - private build requirement superseded by 0021; repository history remains separate.
