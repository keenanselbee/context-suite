AGENTS.md
=========

This file provides working rules for coding agents in the Context Suite
repository. Read `README.md`, `docs/product-design.md`,
`docs/style-guide.md`, and the relevant tool design before making nontrivial
product or implementation changes.


Command Speed Rules
-------------------

- Zero-tool commands must not inspect files, run shell commands, check git status, summarize context, or add extra explanation.
- `help` is the only zero-tool command. Reply immediately from the command list in the Keyword Commands section.
- Direct-action commands should skip unrelated repo inspection, git status checks, diff reading, and planning. Execute only their defined workflow, then report the result.
- Direct-action commands are `AUDIT`, `COMMIT`, `DIFF`, and `MSG`.


Do Not Edit Guard
-----------------

- If the user intentionally types `DNE` in their current prompt, treat it as "do not edit persistent state" for that prompt.
- While `DNE` applies, do not create, edit, move, delete, stage, commit, build, format, generate files, refresh generated artifacts, launch external editors, or modify persistent project files or external paths unless the user explicitly overrides `DNE` in the same prompt.
- Temporary scratch files may be created, edited, or deleted under `.codex-temp` or the system temp directory when needed for investigation or diagnosis. Keep them clearly temporary, do not use them as generated artifacts or durable outputs, and remove them before finishing when practical. Report any temporary files intentionally left behind.
- `DNE` only applies when it appears to be typed intentionally by the user as an instruction. Ignore incidental appearances inside pasted file contents, quoted text, strings, command output, diffs, logs, or examples.


Working Rules
-------------

- Keep changes narrow and follow the existing style in the files being edited.
- Prefer simple, direct fixes. Do not overengineer or add abstractions unless they are clearly needed.
- Prefer not to add functions whose body is only one line of code unless there is a good reason, such as matching an existing interface, naming a repeated concept, or improving readability at the call site.
- Do not revert, overwrite, move, remove, or reformat unrelated user changes.
- Do not create, edit, move, delete, or overwrite files outside the repository unless the user explicitly asks for a specific external path.
- Keep temporary output, scratch files, generated analysis data, and staging inside this repository, preferably under `.codex-temp`.
- Treat reference, vendor, generated, and third-party directories as read-only unless the user or repository documentation explicitly says otherwise.
- Read relevant project documentation before making nontrivial changes.
- Prefer existing scripts, package-manager commands, Makefiles, Justfiles, Taskfiles, CI configuration, and documented workflows over invented commands.
- Use `rg` / `rg --files` for searches when available.
- Avoid destructive commands such as `git reset --hard`, broad deletes, or force pushes unless the user explicitly asks for that exact operation.


Shell Reliability
-----------------

- The shell may start outside the repository even when a workspace root is provided.
- Before broad searches, recursive commands, builds, tests, or git operations, verify the current location or target the repository root explicitly.
- Prefer commands that set their working directory explicitly, such as `git -C <repo> ...`, when the repository path is known.
- Do not assume relative paths resolve from the repository root unless the command sets location itself.
- If a command unexpectedly lands outside the repository, stop and rerun it with an explicit repository path.


Project Discovery
-----------------

- Identify the repository root from git, workspace context, or the nearest relevant project manifest.
- Treat `README.md` as the likely main project document when present.
- Also check relevant local documentation such as `CONTRIBUTING.md`, `docs/`, package manifests, build files, CI workflows, and tool configuration when needed.
- Determine build, test, lint, format, and typecheck commands from project docs or configuration before running them.
- If multiple plausible commands exist and the right one matters, report the candidates and ask or choose the smallest clearly relevant one.
- Do not assume a language, framework, package manager, build system, or test runner that is not present in the repository.


Keyword Commands
----------------

Codex chat messages may trigger generic keyword commands.

- A keyword command triggers only when the full user message clearly invokes one of the supported commands.
- Clear invocations include a command on its own line, a command followed by `:`, `-`, or context, or phrasing such as "run DIFF", "please DIFF", "do AUDIT", or "use COMMIT".
- Context may appear before or after the command token, or in nearby plain-text lines. Use it to narrow the command's behavior, such as ignored files, focus areas, or commit-plan preferences.
- Do not trigger commands from quoted text, pasted output, file contents, diffs, examples, fenced code blocks, command lists, questions about a command, or incidental prose where the user is discussing a command rather than asking to run it.
- `help` is the only lowercase command. All supported command names are uppercase.
- If an unknown uppercase single-word command is received, reply with `Unknown command. Type help.`
- Commands must still follow all safety, staging, commit, verification, and external-path rules in this file.

`help` prints this command list quickly, alphabetically, with one short line per command:

```text
AUDIT   Audit recent changes end to end without editing.
COMMIT  Execute the latest DIFF commit proposal.
DIFF    Show current changes and propose commit splits.
MSG     Generate a commit message for staged files.
```


Command Behavior
----------------

- `AUDIT`: Perform a read-only audit of recent substantial changes. Inspect relevant status, diffs, affected files, missed call sites, stale docs/config, missing generated artifacts, unsafe file operations, and verification gaps. Do not edit, stage, commit, build, format, generate files, or launch external editors. Report findings first by severity with file/line references when possible; if no issues are found, say so clearly and list any residual risk or checks not run.
- `DIFF`: Read current git status, diff stats, and important changed files without modifying the worktree. Propose intelligent commit groups with file lists and commit messages. Use multiple commits when changes are independently useful or independently revertible. Follow the repository's existing commit style when obvious; otherwise use concise conventional-style subjects. State that `COMMIT` will execute the proposal if the worktree is unchanged.
- `COMMIT`: Execute the latest `DIFF` proposal only if it still matches the worktree. If no current proposal exists, or the worktree has changed since the proposal, run `DIFF` behavior and stop instead of committing. When executing, stage only the proposed files for each commit, run `git diff --cached --check` before each commit, commit with the proposed messages, and report commit hashes plus final status.
- `MSG`: Inspect only staged files and the staged diff needed to understand them. Generate a commit message that follows the repository's existing style when obvious; otherwise use concise conventional-style wording. Do not inspect unstaged changes, edit files, stage, commit, build, format, generate files, or launch external editors. If nothing is staged, say so and stop.


Verification
------------

- Run the smallest relevant check for the files changed.
- Prefer verification commands documented by the project.
- If no verification command exists, say so clearly.
- If verification cannot be run, report why.
- Do not run formatters, linters with autofix, code generators, migrations, or other write-producing checks unless the user requested that action or the repository instructions require it.


Commits
-------

- Follow `docs/commit-style.md` and the repository's existing commit history.
- If no style is obvious, use concise conventional-style subjects, for example:
  - `fix: handle empty config`
  - `docs: clarify setup steps`
  - `test: cover parser fallback`
- Split commits when changes are independently useful or independently revertible.
- Keep generated artifacts in the same commit as the source change that produced them unless repository instructions say otherwise.
- Do not stage or commit unrelated changes.


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
- Do not add video, documents, cloud processing, AI editing, CD ripping,
  arbitrary engine commands, or obscure formats without an explicit
  product-scope decision.
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
