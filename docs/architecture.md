Context Suite Architecture
==========================

Status: production foundation accepted in decision 0007; implementation pending.
Media engines and production deployment details remain deferred decisions.


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

The public repository exposes contracts, shell integration, UI, batch
coordination, output safety, and tests. The separate private repository at
`proprietary/` supplies selected production implementations and commercial
integration. The DDS parser and its tests remain public; production media
adapters and the built-in optimization policy catalog are private. Public core
libraries must not depend on private types.

One planned production application build requires the compatible private
checkout and connects its implementations to public interfaces through direct
project references. Missing private implementations fail the build clearly.
There is no separate review/demo edition. Public CI checks independently
buildable public components, tests, documentation, and source boundaries without
private credentials; private release CI verifies the complete application and
records both repository revisions.
See [decision 0005](decisions/0005-public-and-proprietary-builds.md).

This production build is planned. The current solution contains only the native
shell, prototype host, and shell contract tests. Accepted
[decision 0007](decisions/0007-production-ui-and-processes.md) selects Windows 11
x64, C#/.NET 10 WPF with MVVM, one application per interactive user session, and
one on-demand worker processing files sequentially. Retain bounded request-file
shell activation; use local named pipes for app forwarding and worker messages.
The app owns final publication; the worker produces and validates temporary
outputs. Settings use versioned JSON in local application data. These choices
are ready for scaffolding, not implemented functionality.


Operation Model
---------------

All tools receive one selection, validate the request, and resolve access in the
application before admitting work. Access checks never run inside Explorer
menu enumeration or media parsers. Trial and paid access share the same
application; the commercial policy is defined in
[decision 0006](decisions/0006-trial-and-purchase-access.md).

Analyze then performs bounded media analysis and presents typed facts and
warnings. It does not enter a conversion plan or output-publication transaction.
Application-owned trial state is separate from the read-only analyzer.

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

Validation checks semantic requirements such as format, dimensions, streams,
duration, alpha, animation, metadata policy, and loss policy. Requirements vary
by operation and adapter.


Settings And Presets
--------------------

- Store typed, versioned settings with documented defaults.
- Give built-in actions stable identifiers independent of displayed labels.
- Treat presets as complete policies over their governed fields.
- Reject invalid persisted values and migrate intentionally when schemas change.
- Keep arbitrary engine command text outside the initial product.
- Keep advanced settings secondary and expose important consequences in the
  plan regardless of where a setting lives.


Commercial Access Boundary
--------------------------

The planned application admits work using trial or purchase state. Three-day
trial expiry may block new paid operations but must not interrupt an admitted
batch or hide its results. Media engines and output validation do not branch on
account credentials or contact the website.

Website login and purchase validation are allowed network operations. Media
contents and selected paths remain local. Signed offline licenses are proposed;
their lifetime, refresh, and outage behavior must be decided before real access
enforcement is implemented. Keep account credentials and license-signing private
keys out of both source repositories and out of distributed app secrets.


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
- Test independently buildable public components without the private checkout;
  verify the complete application against the compatible private revision and
  verify a clear build failure when required private implementations are absent.
- Test access states independently of codecs, including expiry during an
  admitted batch and the selected offline/service-failure policies.


Decision Status And Deferred Work
---------------------------------

Decision 0007 settles the platform, UI, application lifecycle, worker boundary,
initial IPC approach, settings storage, and initial public/private allocation.
Scaffolding can proceed with Core, Application, Worker, one private
implementation project, and focused tests alongside the existing shell.

Resolve these remaining choices before their dependent implementation:

- Image decoding and encoding engine.
- FFmpeg distribution and update model for audio.
- `oxipng` and `pngquant` distribution and update model.
- Installer, signing, updates, and crash diagnostics.
- Exact private engine adapters and optimization policy definitions.
- Trial timing, offline access, and recovery policies, following decision 0006.

Shell prototype decisions 0001–0004 remain scoped to their recorded evidence.
Public/private build direction and commercial access direction are recorded in
0005–0006; the production foundation in 0007 is accepted. Resolve the
choices needed by each implementation milestone before building that surface;
audio distribution and live payment services need not block the first DDS slice.
