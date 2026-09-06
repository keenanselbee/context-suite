Analyzer Product Language
=========================

Status: accepted
Date: 2026-09-06


Context
-------

The original read-only tool was named **Context Inspector**, with **Inspect** as
its Explorer command. Windows 11 controls the relative order of independently
packaged top-level context-menu commands and rendered the suite alphabetically
as Convert, Inspect, Optimize. The tool also needs to explain encoded facts,
warnings, compatibility, and useful next steps rather than merely list metadata.


Decision
--------

Rename the tool **Context Analyzer** and its Explorer command **Analyze**. Use
analysis, analyzer, and the stable operation identifier `analyze` throughout
product language and implementation contracts.

Analyze remains strictly read-only. It opens the details surface directly and
does not expose an Explorer submenu. The existing COM CLSID remains stable while
the pre-release sparse-package identity changes from Inspect to Analyze.


Consequences
------------

- Explorer naturally renders the intended Analyze, Convert, Optimize sequence.
- The name supports both technical facts and deterministic recommendations.
- Product copy must not imply AI or probabilistic analysis.
- Old development Inspect packages must be removed during installation and
  uninstallation.
- The pre-release activation operation changes from `inspect` to `analyze`.


Alternatives Considered
-----------------------

- Keep **Inspect** and accept alphabetical ordering. This is technically valid
  but weakens the intended Analyze-to-action workflow.
- Prefix commands with numbers or invisible characters. This is visually or
  accessibly poor and depends on unsupported ordering behavior.
- Place all tools under one parent menu. This permits explicit child order but
  violates the accepted three-peer-root experience.
