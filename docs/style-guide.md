Style Guide
===========

This guide defines repository-specific conventions for Context Suite. Follow an
established local style when it conflicts with a general preference, and record
intentional architectural exceptions in a decision document.


Product Language
----------------

- Write the suite name as **Context Suite** in user-facing text.
- Write the tool names as **Context Analyzer**, **Context Converter**, and
  **Context Optimizer** when a full name is needed.
- Use the short commands **Analyze**, **Convert**, and **Optimize** in Explorer.
- Use `ContextSuite` as the root namespace and project-name prefix.
- Describe user goals before implementation details.
- Prefer plain media terms such as **format**, **quality**, **dimensions**,
  **file size**, **metadata**, and **output**.
- Explain codec or metadata terminology where the user must make a decision.
- Distinguish **lossless**, **visually lossless**, and **lossy** accurately.
- Never claim that conversion to a lossless format restores lost quality.
- Use **source** for the original file and **output** for a created result.


Repository Layout
-----------------

Use this layout unless an architecture decision records a better alternative:

```text
src/
tests/
docs/
tools/
assets/
```

- Name .NET projects and namespaces with dotted PascalCase.
- Name additional documentation with lower-kebab-case.
- Keep test projects parallel to the source projects they cover.
- Keep distributable media fixtures small and place them under an explicitly
  named test-data directory.
- Keep generated packages, logs, temporary files, and operation results out of
  source directories.
- Keep ignored implementation research under `reference/`; production code must
  not depend on it.
- Keep the separate private repository under ignored `proprietary/`. Follow
  [decision 0005](decisions/0005-public-and-proprietary-builds.md) for public
  interfaces, private implementations, the production build, and source packaging.


C# And .NET
-----------

- Enable nullable reference types and implicit global usings in new projects.
- Use the repository-supported .NET and C# versions; do not upgrade them
  incidentally.
- Use PascalCase for types, methods, properties, events, and public constants.
- Use camelCase for parameters and local variables.
- Use `_camelCase` for private instance fields.
- Prefix interfaces with `I`.
- Suffix asynchronous methods with `Async`.
- Name Boolean members affirmatively with `Is`, `Has`, `Can`, or another clear
  predicate.
- Prefer one public top-level type per file and match its filename to the type.
- Prefer records for immutable value models when value semantics are intended.
- Prefer collection interfaces in public contracts and concrete collections in
  implementations.
- Use `var` when the assigned type is obvious from the right-hand side;
  otherwise state the type.
- Keep methods focused and return early when it reduces nesting.
- Use dependency injection at process, filesystem, settings, clock, and media
  engine boundaries; do not introduce interfaces for every class.
- Pass `CancellationToken` through cancellable asynchronous operations.
- Do not block asynchronous work with `.Result`, `.Wait()`, sleeps, or manual
  polling.
- Treat file paths as untrusted external input and use platform path APIs rather
  than string concatenation.


Operation Design
----------------

- Keep analysis, planning, execution, output validation, and publication as
  separate responsibilities.
- Represent a requested operation with immutable typed data.
- Return explicit per-file results rather than communicating success through
  mutable global state.
- Distinguish cancellation, unsupported input, invalid input, engine failure,
  validation failure, and publication failure.
- Preserve source files by default.
- Write outputs to temporary files and publish them only after validation.
- Build process arguments as structured values and escape them only at the
  process boundary.
- Use exit codes and validated results as primary completion signals; stderr
  text alone does not imply failure.
- Keep format-specific behavior close to its owning analyzer, adapter, planner,
  or validation policy.
- Make capability declarations testable contracts rather than informal
  extension lists.
- Keep trial and purchase admission in application orchestration. Media parsers
  and engines do not own account state, and admitted batches finish safely even
  when trial access expires.


Analyzers
---------

- Analyzers must not mutate files, metadata, timestamps, settings, or external
  state.
- Use bounded reads and parse only the data required by the analysis contract.
- Preserve raw identifiers for unknown or future format values.
- State when a property is unknown, absent, inferred, or not encoded; do not
  guess.
- Keep report formatting outside binary parsers.


Converters And Optimizers
-------------------------

- A converter must have an explicit target format.
- An optimizer must retain the source format unless it hands an explicit choice
  to Converter.
- Never silently discard transparency, orientation, metadata, tags, artwork,
  animation, or color capabilities.
- Require an explicit matte before converting transparent media to an opaque
  format.
- Warn before lossy-to-lossy processing and state the selected quality policy.
- Reject an optimization output that is larger than its source unless another
  requested transformation explains the increase.
- Never overwrite a source by default. Explicit replacement must be recoverable
  and transactional.


Shell Integration
-----------------

- Keep Explorer-facing code minimal, deterministic, and non-blocking.
- Do not load media engines, parse large files, access the network, or open UI
  while Explorer is enumerating commands.
- Use stable command identifiers and apply capability rules consistently across
  visibility, enablement, and execution.
- Validate all activation data again in the out-of-process host.
- Keep user-facing errors and diagnostics outside the Explorer process.


XAML And MVVM
-------------

Apply this section if the accepted desktop UI decision uses XAML and MVVM.

- Name views `<Feature>View` or `<Feature>Window` and matching view models
  `<Feature>ViewModel`.
- Keep presentation state and user commands in view models; keep media rules and
  process execution outside them.
- Use commands for user actions and data binding for state.
- Keep code-behind limited to view-only behavior that is awkward or
  inappropriate to express through binding.
- Reuse styles and resources for repeated visual decisions.
- Application windows follow the Windows app appearance automatically through
  WPF's built-in Fluent `ThemeMode="System"` at application scope. Keep controls
  on theme resources so light/dark, accent, and contrast-theme changes propagate
  to existing windows. Do not hard-code light backgrounds or add per-window theme
  overrides. No separate stored theme preference is currently needed.
- Provide accessible names, keyboard navigation, visible focus states, and
  sufficient color contrast.
- Do not rely on color alone to communicate success, warnings, or failures.
- Keep primary workflows usable without opening advanced settings.


Errors And Diagnostics
----------------------

- Write user-facing errors in plain language and include the affected filename
  when safe.
- Offer a useful next action such as retrying, selecting another format,
  changing an output folder, or viewing diagnostics.
- Keep technical process output in diagnostics rather than the primary UI.
- Avoid logging full paths when a filename provides enough context.
- Never log file contents, secrets, or private metadata values.
- Preserve causal exceptions when translating infrastructure failures into
  domain errors.
- Record enough engine identity and operation policy to reproduce failures.


Tests
-----

- Name tests `Method_Scenario_ExpectedResult` or follow another equally readable
  established pattern.
- Cover planning and validation with unit tests before relying on end-to-end
  engine tests.
- Include focused fixtures for transparency, orientation, metadata, malformed
  files, Unicode names, unknown formats, and output collisions.
- Verify that cancellation never replaces or corrupts a source.
- Mark tests requiring external media binaries clearly and keep them
  deterministic.
- Compare semantic media properties instead of unstable byte-for-byte output
  when encoders may vary.
- Give every advertised capability at least one successful contract fixture and
  one relevant failure or boundary fixture.


Documentation
-------------

- Treat `docs/product-design.md` as the suite-wide product boundary.
- Treat each tool design as the boundary for that tool.
- Mark future work as planned; do not describe it as implemented.
- Record meaningful architecture choices under `docs/decisions` once an actual
  decision has been made.
- Update supported-format and behavior documentation with the implementation
  that changes it.
- Keep repository-local reference research out of public documentation unless
  its conclusions have been independently incorporated into the product design.
