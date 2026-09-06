Context Suite Roadmap
=====================

Status: Milestone 1 foundation implemented locally; UI/installed-shell smoke and
hosted CI verification remain pending. Remaining
Milestone 0 engine and release decisions are deferred to their dependent work.
Milestones are ordered by dependency and proof of value, not calendar dates.


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

This evidence resolves the shell-discovery portion of Milestone 0. Production UI,
platform, lifecycle, worker/IPC boundaries, settings storage, and initial source
allocation are now accepted in decision 0007. Media engines, production packaging,
updates, signing, and diagnostics remain deferred decisions. Milestone 1 now has
Core, WPF Application, Worker, private composition, managed contracts, fixture
conventions, build/test scripts, and authored CI workflows. Local builds and
protocol tests pass; hosted CI and manual production-window/installed-shell
smoke checks remain unverified. See [development status](development.md).
The bounded foundation goal's evidence is summarized in
[Milestone 1 validation](milestone-1-validation.md); this does not certify release readiness.
Milestone 2 has early queue/cancellation plumbing but no output transaction or
media execution. Milestone 3 and later media features have not started.

The public/private repository direction and three-day commercial trial are now
recorded in decisions 0005 and 0006. The single production foundation builds;
trial handling and website activation are not implemented. Decision 0007 accepts the
WPF application and worker foundation, including application reuse, sequential
worker execution, IPC, publication ownership, settings, and source allocation.
The private catalog advertises no media capabilities until real adapters arrive.


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

Public source, documentation, tests, screenshots, and demo video support
portfolio evaluation; there is no separate review/demo application. The one
production build requires the private checkout. A downloadable trial will
provide hands-on evaluation when ready. A paid image
release additionally requires the commercial access gate under Milestone 7;
the first useful media slices can be developed before live purchase services.


Milestone 0: Resolve Foundation Decisions
-----------------------------------------

Accepted foundation choices:

- Windows 11 x64 and WPF/.NET 10 with MVVM.
- One application per interactive user session and one on-demand sequential worker.
- Existing three-root native shell approach and bounded request-file activation;
  bounded local named pipes for app forwarding and worker communication.
- Application-owned output publication and versioned local JSON settings.
- Public/private allocation and one production build under decisions 0005/0007.

Resolve before the dependent media or release work:

- Image decoding and encoding engine.
- Distribution of FFmpeg, `oxipng`, and `pngquant` or selected alternatives.
- Installer, signing, update, and diagnostics strategy.
- Exact private engine adapters and optimization policy definitions.
- Access-policy boundary for the three-day trial and purchase workflow
  (direction accepted in decision 0006; timing and offline policy still open).

Exit criteria:

- Each decision required to scaffold the solution has an accepted record under
  `docs/decisions`.
- The selected approach supports three peer Explorer roots and an out-of-process
  media host.
- Dependency packaging never resolves code or binaries from `reference/`.
- The supported platform can be reproduced on a clean development machine.

Resolve decisions when their dependent work begins. Production UI, project
ownership, and activation lifecycle precede application scaffolding. Exact
engine choices precede their media slices; live payment services and audio
distribution need not delay the first DDS analysis feature.


Milestone 1: Repository And Contract Foundation
-----------------------------------------------

Deliver:

- Extend the existing solution with production application/core/worker projects
  under accepted decision 0007; preserve native shell contract tests.
- Canonical restore, build, test, format-check, and run commands.
- Continuous integration for build and automated tests.
- Shared operation, capability, media fact, warning, error, progress, and result
  models.
- Typed output and replacement policies.
- Test-data conventions and a small fixture provenance manifest.
- Developer setup and contribution documentation.
- One production application build with direct private project references only
  at the composition boundary. Document which public components and tests can
  run independently; do not scaffold a separate demo app or substitute engines.
- A public access-policy contract with trial/paid/expired/unavailable test
  states; defer real login, payments, and trial persistence to the commercial
  access gate. Test access states without adding a shipping bypass mode.
- Public CI for independently buildable components, tests, documentation, and
  source boundaries without private credentials; a private integration workflow that
  records compatible public/private revisions and prevents publishing private
  source in public artifacts.

Exit criteria:

- A clean public checkout runs its documented component tests and checks without
  private credentials. The complete application builds with the documented
  compatible private checkout and fails clearly when it is absent. There is no
  requirement for a standalone public application build.
- Core contracts do not depend on UI, Explorer, or a particular media engine.
- Reference projects remain ignored and are not build or runtime dependencies.
- CI enforces the same essential checks documented for local development.
- Parent-repository checks reject tracked private paths. Production integration
  checks exercise real private implementations rather than sample substitutes.


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
- The commercial access gate below, required before charging for the release.
- User documentation, screenshots, demo media, and a concise portfolio case
  study explaining the design and engineering decisions.

Exit criteria:

- A clean machine can install, use, update, repair, and uninstall the suite.
- Menu enumeration remains within the documented performance budget.
- Missing engines or invalid activation requests cannot destabilize Explorer.
- The primary workflows are usable without advanced codec knowledge.
- Automated checks, manual release smoke tests, and accessibility review pass.


Commercial Access Gate For The Paid Image Release
------------------------------------------------

Before implementation, settle trial start and elapsed-time rules, feature access
after expiry, offline-license duration/refresh, outage behavior, recovery/device
transfers, and refunds/revocation. Confirm pricing, source/distribution terms,
and the identity/payment providers independently of media-engine selection.

Deliver and verify:

- The three-day trial with clear expiry information and completion of batches
  admitted before expiry.
- Browser login, authoritative purchase checks, and the chosen local license
  storage and verification policy. An unpaid login must not grant paid access.
- Invalid-license, unavailable-service, account-recovery, and selected offline
  behavior tests without exposing credentials or media data in diagnostics.
- Private release signing with no private keys in either repository or the app.
- Install, upgrade, and recovery checks that preserve access state according to
  the documented policy.

Extensive anti-piracy work is outside this gate. Public portfolio documentation
and media development can proceed before commercial services are ready.


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
