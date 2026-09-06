Context Suite Roadmap
=====================

Status: Milestone 0 in progress. Milestones are ordered by dependency and proof
of value, not assigned calendar dates.


Current Position
----------------

As of September 6, 2026, the repository has completed a focused Windows 11 x64
shell-integration discovery prototype. It proves:

- Three independent top-level **Analyze**, **Convert**, and **Optimize** package
  identities backed by one native shell DLL and one out-of-process host.
- Direct Analyzer activation and isolated one-level Convert and Optimize menus.
- A bounded, versioned activation file carrying a complete multi-file selection.
- Host-side schema, action, path-count, absolute-path, and existence validation.
- Debug and Release builds plus automated three-file COM activation contracts.

This evidence resolves part of Milestone 0 but does not complete it. Desktop UI,
media engines, production packaging, settings, updates, signing, and diagnostics
remain open decisions. Milestone 1 has not started as defined: the repository
does not yet contain the planned application/core projects, domain contracts,
fixture conventions, or CI. Milestone 2 and later media milestones have not
started; the shell prototype proves batch handoff only, not the safe execution
pipeline or media behavior.


Roadmap Principles
------------------

- Build complete vertical slices from Explorer command to validated result.
- Keep Analyze, Convert, and Optimize behaviorally distinct.
- Establish file and process safety before expanding format count.
- Treat multi-file Convert and Optimize invocations as first-class batches, not
  repeated single-file operations.
- Make every advertised capability a tested contract.
- Prefer a polished image-focused first release, then add audio on the same
  foundation.
- Use milestone exit criteria to decide readiness; incomplete work moves forward
  only when an explicit scope decision accepts the remaining risk.


Release Shape
-------------

The roadmap targets two product releases:

- **Image MVP** — all three Explorer roots, DDS and common image analysis,
  batch PNG optimization, and batch PNG/JPEG/WebP conversion.
- **Audio expansion** — common audio analysis, batch conversion, and narrowly
  defined same-format optimization after the image foundation is reliable.


Milestone 0: Resolve Foundation Decisions
-----------------------------------------

Decide and record:

- Supported Windows versions and CPU architectures.
- Desktop UI framework and application lifecycle.
- Modern Explorer registration and deployment identity.
- Host activation and large-selection IPC.
- Image decoding and encoding engine.
- Distribution of FFmpeg, `oxipng`, and `pngquant` or selected alternatives.
- Settings location and schema format.
- Installer, signing, update, and diagnostics strategy.

Exit criteria:

- Each decision required to scaffold the solution has an accepted record under
  `docs/decisions`.
- The selected approach supports three peer Explorer roots and an out-of-process
  media host.
- Dependency packaging never resolves code or binaries from `reference/`.
- The supported platform can be reproduced on a clean development machine.


Milestone 1: Repository And Contract Foundation
-----------------------------------------------

Deliver:

- The initial .NET solution and projects following the accepted architecture.
- Canonical restore, build, test, format-check, and run commands.
- Continuous integration for build and automated tests.
- Shared operation, capability, media fact, warning, error, progress, and result
  models.
- Typed output and replacement policies.
- Test-data conventions and a small fixture provenance manifest.
- Developer setup and contribution documentation.

Exit criteria:

- A clean checkout restores, builds, and tests with documented commands.
- Core contracts do not depend on UI, Explorer, or a particular media engine.
- Reference projects remain ignored and are not build or runtime dependencies.
- CI enforces the same essential checks documented for local development.


Milestone 2: Safe Execution And Batch Infrastructure
----------------------------------------------------

Deliver:

- Immutable single- and multi-file operation requests.
- Host-side path validation and authoritative capability resolution.
- A bounded asynchronous work queue.
- Batch-wide cancellation plus per-file state and results.
- Aggregate progress and partial-failure summaries.
- A hardened external-process runner with structured arguments, output capture,
  cancellation, timeouts, and engine identity.
- Collision-safe output reservation, temporary output, semantic validation, and
  transactional publication.
- Test doubles that exercise the complete pipeline without media engines.

Exit criteria:

- A simulated multi-file Convert batch and Optimize batch run as one operation.
- Tests cover repeated paths, collisions, locked outputs, Unicode and long paths,
  cancellation, timeouts, validation failures, and partial success.
- Failed or cancelled operations never corrupt or replace a source.
- Aggregate results exactly reconcile with every per-file result.


Milestone 3: Analyze DDS Vertical Slice
---------------------------------------

Deliver:

- A bounded DDS parser independent of the local reference implementation.
- Typed reporting for legacy and DX10 headers, FourCC/DXGI format, BC
  compression, color-space certainty, dimensions, mipmaps, texture type, arrays,
  and alpha mode.
- Malformed and unknown-value handling.
- The Analyzer details surface.
- The first **Analyze** Explorer root with direct details activation and no
  submenu.

Exit criteria:

- Unit fixtures cover every advertised DDS variant and malformed boundary.
- Legacy color-space uncertainty is reported without guessing.
- Analysis does not modify file bytes or timestamps intentionally.
- Explorer-to-details activation works without performing full parsing inside
  Explorer.


Milestone 4: Batch Lossless PNG Optimization
--------------------------------------------

Deliver:

- A pinned and packaged lossless PNG engine adapter.
- **Optimize > Lossless** for one or many selected PNG files.
- Pixel-equivalence, dimensions, alpha, metadata-policy, and decodability
  validation.
- Sibling output by default and explicit recoverable replacement.
- Per-file sizes plus aggregate bytes and percentage saved.
- **Unchanged** results when no smaller valid output exists.

Exit criteria:

- A multi-file Explorer selection produces one batch, one progress surface, and
  one reconciled summary.
- Every successful output decodes identically under the lossless contract.
- Larger outputs are not published by default.
- Cancellation and individual failures leave all sources intact.


Milestone 5: Batch Lossy PNG Policies
-------------------------------------

Deliver:

- A pinned and packaged quantization engine adapter followed by lossless PNG
  cleanup.
- Versioned **Balanced** and **Smallest** policies with hard quality floors.
- Explicit metadata choices and preserved alpha, dimensions, and color behavior
  under each policy.
- Representative photographic, graphical, transparent, UI, and gradient
  fixtures with reproducible provenance.
- A comparison surface for consequential settings.

Exit criteria:

- No policy silently retries below its documented quality floor.
- “Skip if larger” produces **Unchanged** rather than weaker quality.
- Automated semantic and perceptual thresholds pass for the fixture corpus.
- Human-reviewed examples document the visible difference between policies.
- Single- and multi-file Explorer paths behave consistently.


Milestone 6: Batch Image Conversion
-----------------------------------

Deliver:

- A selected and packaged image conversion engine.
- PNG, JPEG, and WebP analysis needed for planning and validation.
- **Convert** targets compatible with the complete Explorer selection.
- One coordinated batch for multiple selected files.
- Transparency detection and required matte choice for opaque outputs.
- Orientation application, metadata policy, quality and maximum-dimension
  controls, and before/after preview.
- Destination-oriented recommendations and an advanced settings surface.

Exit criteria:

- Every advertised PNG/JPEG/WebP pair has successful, malformed, transparency,
  orientation, metadata, Unicode-path, and cancellation coverage where relevant.
- One selected target and policy apply predictably across a batch.
- File-specific warnings appear before execution without spawning per-file
  planning windows.
- Outputs validate against their plans and sources remain intact.


Milestone 7: Image MVP Integration And Polish
---------------------------------------------

Deliver:

- Final **Analyze**, **Convert**, and **Optimize** root behavior for the image MVP.
- Accessible keyboard, focus, contrast, scaling, and screen-reader behavior.
- Consistent queue, progress, cancellation, diagnostics, and result UX.
- Installer, clean upgrade, repair, and uninstall flows.
- Explorer performance and resilience measurements.
- User documentation, screenshots, demo media, and a concise portfolio case
  study explaining the design and engineering decisions.

Exit criteria:

- A clean machine can install, use, update, repair, and uninstall the suite.
- Menu enumeration remains within the documented performance budget.
- Missing engines or invalid activation requests cannot destabilize Explorer.
- The primary workflows are usable without advanced codec knowledge.
- Automated checks, manual release smoke tests, and accessibility review pass.


Milestone 8: Audio Analysis And Batch Conversion
-------------------------------------------------

Deliver:

- A pinned FFmpeg/ffprobe distribution or accepted alternative.
- Analysis for WAV, FLAC, MP3, M4A/AAC, and Ogg/Opus properties.
- Batch conversion among only the pairs verified by fixtures.
- Destination contexts for compatibility, speech, music sharing, editing, lossless
  archive, and game audio where their promises can be tested.
- Tag, artwork, channel, sample-rate, bit-depth, bitrate, and lossy-to-lossy
  planning policies.

Exit criteria:

- Every advertised audio pair has semantic fixtures and metadata expectations.
- Multi-file selection produces one bounded, cancellable batch.
- Lossy-to-lossy conversion is identified before execution.
- The UI never describes decoded lossy audio as restored or higher quality.
- Gapless or loop-sensitive limitations are documented for relevant outputs.


Milestone 9: Audio Optimization And Release Hardening
----------------------------------------------------

Deliver:

- Verified same-format lossless optimization where clear invariants exist,
  beginning with FLAC recompression if testing supports meaningful savings.
- Explicit bounded policies for any lossy same-format audio re-encoding accepted
  into scope.
- Audio batch summaries using the same success, unchanged, failure, and
  cancellation model as image optimization.
- Full regression, packaging, accessibility, performance, and recovery coverage
  across image and audio features.

Exit criteria:

- Audio optimization never changes container or codec implicitly.
- Lossless operations prove decoded-audio equivalence under a documented
  comparison contract.
- Lossy policies never become more aggressive after a no-change result.
- Metadata and artwork results match the selected policy.
- Installation and update tests cover every packaged media engine.


Deferred Backlog
----------------

After the image MVP and audio expansion, possible additions include AVIF, HEIC,
TIFF, BMP, GIF, SVG rasterization, ICO, richer comparison tools, clipboard
workflows, and reusable user contexts. Each requires a demonstrated user job,
an explicit scope decision, packaging support, and contract fixtures.

Video, PDF and Office conversion, cloud processing, AI editing, CD ripping,
automatic source deletion, and arbitrary media-engine commands remain non-goals
until the product design explicitly changes.


Roadmap Maintenance
-------------------

- Update this document when milestone scope or order changes.
- Record architecture choices in `docs/decisions`; do not turn roadmap bullets
  into implicit decisions.
- Mark implemented capabilities accurately in the README and tool designs.
- Split milestones when their exit criteria become too large to verify as one
  coherent result.
- Do not mark a milestone complete while required batch, safety, validation, or
  documentation criteria remain unmet.
