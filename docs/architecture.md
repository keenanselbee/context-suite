Context Suite Architecture
==========================

Status: production process foundation implemented under decision 0007.
Magick.NET with the pinned curated native engine is integrated into development
packaging. Decision 0011 adds pinned CPU DirectXTex in the worker and public
bounded DDS header analysis. Bounded DDS local acceptance passes; customer deployment remains pending.
Decision 0013 adds lossless PNG recompression via a pinned, Windows-job-owned
oxipng child of the worker. The application remains the sole publication owner.


Architecture Goals
------------------

- Keep Explorer stable and responsive.
- Make Analyze, Convert, and Optimize separate product capabilities over shared
  infrastructure.
- Keep file-format behavior testable without UI or shell integration.
- Preserve inputs and publish only validated outputs.
- Add formats through capability registration rather than central switch growth.
- Isolate external engines and make their versions observable.
- Support batches, cancellation, and partial failure from the beginning.


Logical Components
------------------

The initial solution should separate these responsibilities. Project boundaries
may combine some components while the codebase is small, but dependency direction
must remain clear.

| Component | Responsibility |
| --- | --- |
| `ContextSuite.Core` | Operation requests, plans, results, capabilities, policies, errors, progress, and shared value models |
| `ContextSuite.Analysis` | Analyzer contracts, format detection, typed facts, and report models |
| `ContextSuite.Conversion` | Conversion planning and converter contracts |
| `ContextSuite.Optimization` | Optimization policies, planning, and optimizer contracts |
| `ContextSuite.Media.*` | Format- or engine-specific implementations such as DDS, PNG, image codecs, and FFmpeg |
| `ContextSuite.Application` | Desktop UI, activation handling, queue coordination, settings, and result presentation |
| `ContextSuite.Shell` | Minimal Explorer command declarations, capability snapshots, and host activation |
| Test projects | Unit, contract, integration, fixture, shell, and end-to-end coverage parallel to their owners |

`Core` must not depend on the desktop UI, shell APIs, or a particular media
engine. Format adapters may depend on `Core` and their narrow operation contract.
The application composes implementations and owns user-facing orchestration.


Public And Product Composition
------------------------------

The main repository owns contracts, shell integration, UI, batch coordination,
output safety, production media adapters, optimization policies, and tests.
The independent proprietary/ checkout is retained as historical material.
The existing ContextSuite.Private assembly/namespace names are preserved during
the migration; their source now belongs to this repository.

One production build connects those implementations through ordinary project
references. Core contracts remain independent of media engines; only the worker
links implementation/engine assemblies. The documented pinned native artifacts,
source companions, and notices remain required build inputs. Current release
receipts identify one application source commit and the selected engine identities;
historical dual-repository receipts remain preserved. See
[decision 0021](decisions/0021-free-product-and-public-implementation.md).

The native solution retains the shell, prototype host, and shell contract tests.
Production scripts additionally build Core, Application, Worker, and the media
implementation project. Managed contract tests cover the new boundaries. Accepted
[decision 0007](decisions/0007-production-ui-and-processes.md) selects Windows 11
x64, C#/.NET 10 WPF with MVVM, one application per interactive user session, and
one on-demand worker processing files sequentially. Retain bounded request-file
shell activation; use local named pipes for app forwarding and worker messages.
The app owns final publication; the worker produces and validates temporary
outputs. Settings use versioned JSON in local application data. Typed image
probe/preview/conversion IPC and validated application publication now have
real-worker integration tests, and the conversion UI now uses that path. The
capability registry advertises twenty ordinary-image conversion pairs, seven bounded DDS
conversion pairs and one lossless PNG optimization capability, subject
to per-file probing and planning. The app does not reference engine assemblies;
only the worker links them. A test-only host compiles the real windows with isolated storage,
without adding a shipping override or a public review edition.
See [development status](development.md) for verification limits.


Operation Model
---------------

All tools receive one selection and validate it in application orchestration.
Confirmed transformation plans retain executable-item checks and batch identity
before worker execution. There are no trial or paid-access conditions. Explorer
menu enumeration remains bounded and independent of media parsing and network
access. Analyze performs read-only bounded analysis and presents typed facts and
warnings without entering a transformation/publication transaction.

Convert and Optimize use this flow after admission:

```text
selection
  -> host-side validation
  -> capability resolution
  -> media analysis
  -> typed plan
  -> user decisions when required
  -> cancellable execution
  -> semantic validation
  -> transactional publication
  -> per-file and aggregate results
```

The shared request identifies the operation, action, paths, and user-selected
policy. Plans add detected properties, engine selection, consequences, required
choices, and output intent. Results record actual outcomes and never overwrite
the original plan.


Capability Registry
-------------------

Each implementation declares capabilities such as:

- Operation: analyze, convert, or optimize.
- Supported detected inputs, not just filename extensions.
- Supported outputs for conversion.
- Preset or policy identifiers for optimization.
- Single-file and batch-selection rules.
- Required engine features and availability.
- Planning requirements such as transparency or animation decisions.

Explorer consumes a compact projection suitable for quick menu decisions. The
application consumes the authoritative registry and validates capabilities again
after analyzing the actual files.

Capabilities are executable contracts. An advertised combination must have a
representative success fixture, relevant boundary fixtures, and result
validation.


Analysis Boundary
-----------------

Analyzers perform bounded reads and return typed facts plus warnings. They do
not render UI, decide conversions, invoke unrelated engines, or mutate state.

Converter and Optimizer planners may consume analysis results, but they add
their own policy decisions. Shared facts must not become a mutable bag of
engine-specific strings.


Execution And Concurrency
-------------------------

- Use asynchronous operations and pass cancellation tokens through every
  cancellable boundary.
- Coordinate batches through a bounded queue rather than creating an unbounded
  task or thread per file.
- Track explicit states such as pending, planning, waiting for input, running,
  validating, publishing, succeeded, unchanged, cancelled, and failed.
- Isolate per-file failures while respecting batch-wide cancellation.
- Serialize operations that conflict over one source or output path.
- Report monotonic progress where an engine provides meaningful progress; use an
  indeterminate state rather than fabricated percentages otherwise.


External Tool Boundary
----------------------

All command-line media engines use one hardened process service that owns:

- Executable resolution from an installation manifest.
- Structured argument lists.
- Standard output and error capture without deadlock.
- Machine-readable progress parsing where supported.
- Cancellation, graceful termination, forced termination, and timeout policy.
- Exit code, duration, engine version, and diagnostic capture.
- Environment and working-directory isolation.

Adapters interpret process results in their own domain. Stderr is diagnostic
output, not proof of failure. An exit code alone is also insufficient when the
operation promises a validated media result.

Production tools must have pinned versions, integrity hashes, source records,
packaging locations, and upgrade tests. Nothing under `reference/` participates
in executable resolution.


Output Transaction
------------------

Output safety is a shared service rather than duplicated adapter behavior:

1. Normalize and validate the intended destination.
2. Reserve a collision-safe final name.
3. Create a unique temporary output on the destination volume.
4. Let the adapter write only to that temporary path.
5. Analyze and validate the completed output against the plan.
6. Publish with an atomic move when possible.
7. For explicit replacement, retain a recoverable prior file until publication
   succeeds.
8. Clean up temporary data without turning cleanup failure into input loss.

[Decision 0008](decisions/0008-output-naming-settings-and-replacement.md) defines
the implemented naming and replacement policy. Copies use
Windows-style operation suffixes and collision numbering. Same-path replacement
uses an explicit unique original backup, then attempts recycling only after
publication succeeds. Different-extension conversion publishes first and then
recycles the unchanged original. Recycling failures preserve originals/backups;
there is no permanent-delete fallback or application recovery browser.

The publisher must distinguish failed publication from successful publication
with failed cleanup, reconcile Windows partial replacement failures, and protect
interrupted-operation evidence from routine temporary cleanup. Do not claim
unconditional atomicity across replacement, recycling, and power loss. Enable
replacement only on verified paths/platforms; unsupported cases remain copy-only.

The current gate permits Windows build 26200 x64 and ordinary single-link local
NTFS files only. Publication records under
`%LOCALAPPDATA%/ContextSuite/Publications` preserve paths, fingerprints, and stage;
startup reports their presence without resuming jobs or deleting artifacts.
Per-file results distinguish retained originals from unconfirmed recovery, and
aggregate warnings are a subset of completed items, not extra completed files.
This is not protection against malicious same-user filesystem manipulation or
a guarantee against hardware/power-loss failures.

Validation checks semantic requirements such as format, dimensions, streams,
duration, alpha, animation, metadata policy, and loss policy. Requirements vary
by operation and adapter.


Settings And Presets
--------------------

- Store typed, versioned settings with documented defaults.
- Save local JSON safely, preserve unknown newer schemas, and snapshot settings
  per admitted batch. Credentials and trial records are not ordinary preferences.
- Expose one shared Convert/Optimize settings window. Replacement permission is
  off per tool by default and never replaces explicit batch consent; quick
  actions keep originals. See decision 0008 for the complete policy.
- Give built-in actions stable identifiers independent of displayed labels.
- Treat presets as complete policies over their governed fields.
- Reject invalid persisted values and migrate intentionally when schemas change.
- Keep arbitrary engine command text outside the initial product.
- Keep advanced settings secondary and expose important consequences in the
  plan regardless of where a setting lives.


Free Operation Admission
------------------------

The application validates and admits work without licensing storage, clocks,
credentials, or provider requests. Admission still binds the confirmed batch
identity and enforces plan validity and executable work. Cancellation, recovery,
format safety, worker boundaries, and publication rules remain independent of
product pricing. Legacy trial/license records are neither read nor modified by
the free runtime. Ordinary settings and ownership/recovery state remain active.


Diagnostics And Privacy
-----------------------

- Give every activation and batch a correlation identifier.
- Record operation, policy, adapter, engine version, duration, result category,
  and safe reproduction details.
- Avoid full paths, metadata values, or file contents unless the user explicitly
  exports diagnostics that require them.
- Keep raw engine output out of the primary UI but available for troubleshooting.
- Bound log size and retention.
- Exclude authentication tokens and license payloads from diagnostic logs.


Testing Strategy
----------------

- Unit-test parsers, planners, policies, naming, and validation without external
  engines.
- Contract-test every adapter capability with small deterministic fixtures.
- Test external process cancellation, output capture, timeouts, and abnormal
  exits with controlled helper processes.
- Test output transactions under collision, cancellation, validation failure,
  locked files, and insufficient permissions.
- Use semantic comparisons for encoded media unless byte stability is an
  intentional contract.
- Test shell activation separately from media behavior, then add a
  small number of complete Explorer-to-result paths.
- Test the complete application from the main source checkout with reviewed engine
  inputs and no private checkout; preserve source/engine provenance and fail clearly
  when a required pinned engine is unavailable.
- Test access states independently of codecs, including expiry during an
  admitted batch and the selected offline/service-failure policies.


Decision Status And Deferred Work
---------------------------------

Decision 0007 settles the platform, UI, application lifecycle, worker boundary,
initial IPC approach, settings storage, and initial public/private allocation.
Core, Application, Worker, private composition, and focused tests now exist.
Decision 0008 settles output naming, settings UX, and replacement direction;
the [completed safety slice](image-output-safety-goal.md) records local verification
of those mechanisms before codecs.

Resolve these remaining choices before their dependent implementation:

- DDS acceptance passes under [decision 0011](decisions/0011-dds-engine-and-texture-policies.md). Curated
  production-development integration and bounded BMP/TGA policies now have
  [passing evidence](bmp-tga-and-engine-integration.md); complete the separate
  [redistribution gates](release-redistribution.md) before release.
- FFmpeg distribution and update model for audio.
- `oxipng` and `pngquant` distribution and update model.
- Installer, signing, updates, and crash diagnostics.
- Complete the verified free-product source and release transition under decision
  0021 while retaining existing adapter/policy and engine identities.

Shell prototype decisions 0001–0004 remain scoped to their recorded evidence.
Historical public/private and commercial directions are recorded in
0005–0006; the production foundation in 0007 is accepted. Resolve the
choices needed by each implementation milestone before building that surface;
Decision 0021 supersedes commerce; engine redistribution and output safety remain
independent release requirements.
