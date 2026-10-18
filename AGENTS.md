AGENTS.md
=========

These instructions apply to Context Suite.

- For this checkout under `C:\Repositories`, read `C:\Repositories\System\AGENTS.md` for shared working rules and project routing. The rules below are project-specific additions and overrides.
- If the shared file is unavailable in another environment, report that and use the available local instructions and project documentation.


Project Guidance
----------------

This file provides working rules for coding agents in the Context Suite
repository. Read `README.md`, `docs/product-design.md`,
`docs/style-guide.md`, and the relevant tool design before making nontrivial
product or implementation changes.


Commit Style
------------

- Follow `docs/commit-style.md` and the repository's existing commit history.


Development Cleanup
-------------------

- Follow [repository maintenance](docs/repository-maintenance.md) for task
  closeout, retained evidence, historical cleanup and the implementation plan.
- Put new disposable output in a unique task/run directory under `.codex-temp`.
  Do not reuse another task's directory or move existing engine inputs merely
  to fit this convention. Ignored and untracked files are not automatically disposable.
- As part of authorized implementation or testing, clean only disposable output
  created by that task after its processes stop and verification succeeds.
  Confirm the exact resolved path stays inside the owned directory and reject
  reparse points. Read-only requests still prohibit cleanup writes.
- Keep active inputs, required evidence and failed/interrupted runs until
  classified. For retained output, record its purpose, reproduction command and
  retention reason in an existing receipt or a short local note. Revisit it when
  its purpose ends; save required evidence before disposing of transient output.
- Avoid unnecessary storage; there is no fixed GB target or budget process.
  Reuse useful build inputs and caches, avoid redundant copies, investigate
  unexpected growth and explain significant leftovers. Measure when useful for
  cleanup or large builds, not every turn; never remove needed material to shrink
  a number.
- Scripts should clean their own successful transient output and report retained
  failures. Do not add age-based deletion, background sweepers or cleanup of
  shared roots. Existing retained history requires a separate reviewed selection.
- Preserve source, private-repository boundaries, reference material, engine
  pins, source/notices, version receipts, installed and rollback payloads. Never
  infer deletion permission from size, age, a passing test or a Git commit.
  Before historical deletion, recheck current dependencies and protections as
  well as source/archive identities; a changed or incomplete selection must stop.
- End substantial work with the relevant checks, a review of the task's diff,
  and a brief report of retained large output or cleanup failures. Keep current
  status separate from historical milestones. Do not commit or push implicitly.
- Review correctness before cosmetic cleanup: preserve originals and validated
  publication, check changed workflows and contracts, and keep evidence tied to
  the source/engine tested. A clean tree or smaller checkout is not proof of
  correctness. Fix demonstrated issues without unrelated refactoring.


Repository-Specific Notes
-------------------------

- Context Suite is a local Windows application with three peer Explorer tools:
  Analyze, Convert, and Optimize.
- Treat `proprietary/` as a separate private Git repository with its own
  instructions, history, and remote. Keep it untracked by the parent; parent
  staging or commit requests do not authorize private-repository commits.
- Follow decisions 0005 and 0006 for public/private builds and commercial
  access. Maintain one production application build requiring the private
  checkout, with a clear error when required implementations are unavailable.
  Do not add a separate public review/demo edition or shipping access bypass.
  Independently testable public components do not require a second application.
  Use tools/Build-Production.ps1 for the full foundation; the native
  shell-prototype commands remain independently supported.
- Follow accepted decision 0007 for the production foundation: Windows 11 x64,
  WPF/.NET 10 with MVVM, one app per interactive user session, one on-demand
  sequential worker, and bounded local named-pipe communication. The app owns
  final output publication. Do not add a worker pool or resident service without
  a new scope decision. Bounded PNG/JPEG/WebP/BMP/TGA conversion and local trial admission are
  implemented with a passed bounded acceptance matrix; paid activation remains planned.
- Keep private source out of public source archives through explicit packaging
  rules. Never store payment credentials or signing private keys in either repo.
- Polar-hosted checkout and license-key validation are selected; do not add
  custom website accounts or a signing backend without a new decision. Follow
  docs/polar-integration.md; account approval is not proof of working licensing.
  Purchase validation is permitted; media contents and
  selected paths stay local. Keep access checks in application orchestration,
  never in Explorer enumeration or parsers. Trial expiry must not interrupt an
  admitted batch. Do not add extensive anti-piracy machinery.
- Treat `docs/product-design.md` as the suite-wide product boundary and the
  relevant tool design as that tool's current boundary. Update the owning
  document when an accepted decision changes either boundary.
- Keep the tool responsibilities distinct: Analyzer is read-only, Converter
  requires an explicit target format, and Optimizer retains the current format
  unless the user separately selects a conversion.
- Treat a multi-file Explorer invocation of Convert or Optimize as one
  coordinated batch with shared settings, bounded concurrency, combined
  progress and cancellation, per-file validation, and an aggregate result. Do
  not launch independent UI or worker flows for each selected file.
- Preserve source files by default and publish only validated outputs through a
  collision-safe, transactional output workflow.
- Follow decision 0008 for Windows-style output names, immutable settings
  snapshots, and explicit replacement consent, with decision 0018 superseding
  per-batch consent: a new explicit per-tool Settings choice authorizes future
  replacement. Legacy permission-only settings must migrate to copies, never
  silently become standing overwrite consent. Recycle originals/backups only
  after publication; never fall back to permanent deletion. Replacement remains
  gated on Windows failure tests. Do not add a custom backup manager or app Undo.
- Use `tools/Test-PublicationWindows.ps1 -Recycle` only for explicit opt-in native
  safety verification of disposable test inputs. Never empty the Recycle Bin or
  change its configuration. Keep replacement's platform allowlist restricted to
  environments with equivalent native evidence; see docs/image-output-safety-goal.md.
- Never silently discard transparency, orientation, metadata, tags, artwork,
  animation, color information, or another meaningful media capability.
- Accepted raster-conversion exception: Automatic handles ordinary metadata
  quietly on a copy, preserving supported information and applying orientation
  and color correctly. Unsupported extras/stale thumbnails may be omitted; this
  forces copy output even with replacement enabled. A format selection accepts
  ordinary encoding/precision limits on that copy, not silent alpha/HDR/animation
  loss. Strict preservation and descriptive removal stay secondary options.
- Require an explicit matte choice before converting transparent media to an
  opaque image format.
- Warn before lossy-to-lossy audio or image processing, state the selected loss
  policy, and never imply that a lossless target restores lost quality.
- Separate media analysis, planning, execution, validation, and publication so
  each can be tested independently.
- Treat stderr as diagnostics rather than proof of failure. Use exit codes plus
  validated semantic results to determine completion.
- Keep common workflows understandable without codec expertise and place
  advanced controls behind a secondary surface.
- Follow accepted decision 0015 as the next product priority: broad-audience
  simplicity, context-menu-first direct actions, Auto first, best-effort results
  within fixed quality limits, quiet success and compact progress/problem UI.
  Decision 0018 now narrows the customer UI to direct commands, necessary prompts,
  Analyze, progress/problems, Settings and License. Remove the general workspace
  and routine planners. Copies remain default; explicit Settings consent will
  allow safe replacement on future direct actions, retaining mandatory copy
  exceptions. Track implementation in docs/context-menu-simplification-goal.md. Keep planned
  behavior distinct from implemented and verified behavior in status reports.
- Keep Explorer-facing code minimal and bounded. Do not load media engines,
  perform unbounded parsing, access the network, or display errors while Explorer
  enumerates commands.
- Follow decision 0019 and docs/broad-file-support-goal.md for the next priority:
  useful read-only Analyze for every readable regular file, an offline common-type
  catalog, common audio conversion/FLAC optimization and PDF/document analysis.
  Selected launch document actions are images-to-PDF, PDF pages-to-images, PDF
  optimization and Word/Excel/PowerPoint-to-PDF. Evaluate exact variants, engines
  and fidelity under docs/document-design.md before enabling them.
  Recognition does not authorize conversion. Keep the completed decision 0018 UX.
- Do not add video conversion, archive extraction, cloud processing, AI editing,
  CD ripping, arbitrary engine commands or obscure transformations without a new
  product-scope decision. Signing remains deferred; product expansion does not
  authorize installation, registration, live commerce, commits or publication.
- Use `tools/Build.ps1` for the canonical native x64 build.
- Use `tools/Build-Production.ps1` for production composition and
  stage the selected curated engine first via `tools/curated-engine/Stage-ProductionEngine.ps1`.
  Follow `docs/bmp-tga-and-engine-integration.md`; never fall back to stock native assets.
  Use
  `tools/Test-Foundation.ps1` for public contracts; add `-Integration` to test the
  real private worker. Close the application before router tests. Use
  `tools/Test-PrivateBoundary.ps1` for expected missing-private failures and
  `tools/Test-Repository.ps1` for documentation and public-source boundaries.
- Use `tools/Test-ShellPrototype.ps1` for host schema, manifest, COM class, and
  multi-selection activation contracts.
- Use `tools/Install-ShellPrototype.ps1` and
  `tools/Uninstall-ShellPrototype.ps1` only for local sparse-package testing.
  Installation state is not part of a normal build or test.
- Use `tools/Test-InstalledShellPrototype.ps1` to verify the registered package
  and packaged COM activation without opening Explorer.
- Use `tools/Open-ShellPrototypeTestFolder.ps1` to prepare the three-file manual
  Explorer layout and activation smoke test.
- Use `tools/Test-DesktopSmoke.ps1` for opt-in WPF UIA checks on an unlocked
  desktop with Context Suite closed. Explorer mode is experimental; follow
  docs/desktop-smoke-tests.md and never report an unavailable/failed run as a
  pass. Do not run it concurrently with foundation integration tests. It must
  not install packages, restart Explorer, or close unrelated windows/tabs.
- Documentation-only changes require whitespace and link validation; do not
  scaffold or build the application solely to verify documentation.
- Keep public documentation links usable in a fresh public checkout. Describe
  ignored local evidence as plain paths, explicitly labelled local-only.
- Report local verification and remote CI separately. For CI/build changes, verify
  relevant checks from a clean source snapshot without private or ignored inputs
  unless the check explicitly requires them. After an authorized push, inspect
  the matching workflow result and report failures or a still-pending run; do not
  call an unobserved remote run passed. A failing run needs triage before further
  dependent work. See [development verification](docs/development.md).
- Treat everything under `reference/`, including `reference/reference.md`, as
  repository-local, ignored, read-only implementation research.
- Use reference projects to understand algorithms, workflows, capability
  boundaries, edge cases, and test scenarios. Reimplement those ideas in the
  repository's own architecture and style.
- Do not copy source text, project structure, binaries, libraries, assets,
  installer material, or generated files from `reference/` into production
  code.
- Do not add build-time or runtime dependencies on files under `reference/`.
  Production dependencies must be selected, versioned, and packaged
  independently.
- Do not execute reference binaries unless the user explicitly requests that
  exact action. Static inspection and checksum verification are allowed.
- Never edit a reference project to support Context Suite. Turn useful behavior
  and discovered edge cases into independently authored implementation and
  tests outside `reference/`.
