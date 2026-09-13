Context Suite Roadmap
=====================

Status: image foundation and simplified context-menu UX implemented with owner
manual acceptance. The active priority is [broad file support](broad-file-support-goal.md)
under [decision 0019](decisions/0019-broad-file-analysis-and-media-expansion.md).
Signing is deferred. Native installer lifecycle, live commerce and remaining
accessibility/environment checks remain separate release gates.
Historical milestone numbers below retain their original scope identifiers;
the new goal defines its own milestones and current completion criteria.


Current Position
----------------

Current implementation priority (September 9): universal read-only Analyze,
an offline common-file catalog, common audio and PDF/document support. The first
Analyze/fallback, bounded JSON/XML structure analysis and 237-entry catalog are
implemented; [coverage and limits](file-type-coverage.md) record verified scope.
Broader analysis and audio remain pending. Images-to-PDF, PDF pages-to-images,
PDF optimization and Word/Excel/PowerPoint-to-PDF are selected for launch;
their engines, exact variants and implementation remain pending.
The [new goal](broad-file-support-goal.md) records the current image baseline and
latest checks. The following foundation history retains its original test scope;
older pending UI items must be read alongside the completed
[simplification evidence](context-menu-simplification-goal.md).

As of September 6, 2026, the repository has completed a focused Windows 11 x64
shell-integration discovery prototype. It proves:

- Three independent top-level **Analyze**, **Convert**, and **Optimize** package
  identities backed by one native shell DLL and one out-of-process host.
- Direct Analyzer activation and isolated one-level Convert and Optimize menus.
- A bounded, versioned activation file carrying a complete multi-file selection.
- Host-side schema, action, path-count and absolute-path validation; transformation
  availability checks and cancellable per-row Analyze availability checks.
- Debug and Release builds plus automated three-file COM activation contracts.

This evidence resolves the shell-discovery portion of Milestone 0. Production UI,
platform, lifecycle, worker/IPC boundaries, settings storage, and initial source
allocation are now accepted in decision 0007. Magick.NET is selected for general
image conversion; decision 0009 selects Magick.NET-Q16-x64 14.17.1, with actual
native payload verified locally and curated development packaging integrated.
Redistribution review, customer packaging,
updates, signing, and diagnostics remain pending. Milestone 1 now has
Core, WPF Application, Worker, private composition, managed contracts, fixture
conventions, build/test scripts, and authored CI workflows. Local builds and
protocol tests pass. WPF UI automation passes, and user screenshots confirm
manual classic Explorer handoff for all three tools with complete selections
and cross-tool accumulation in one window. Automated Explorer tests remain
experimental; modern-menu/accessibility review and hosted CI remain unverified.
See [development status](development.md).
The bounded foundation goal's evidence is summarized in
[Milestone 1 validation](milestone-1-validation.md); this does not certify release readiness.
Milestone 2 now includes queue/cancellation plumbing, settings, naming, validated
output publication, and restricted-platform replacement/recycling. The completed
Milestone 6 image slice has tested private codec execution, worker IPC and
trial-gated publication and a tested system-themed planner; its bounded media
and failure acceptance matrix passes.
This does not complete Milestone 2 or the other media milestones.

The public/private repository direction and seven-day commercial trial are now
recorded in decisions 0005 and 0006. The single production foundation builds;
trial storage/admission is now connected to the tested conversion UI;
Polar activation is locally implemented with synthetic-provider contracts;
live-provider and interactive acceptance remain pending. Decision 0007 accepts the
WPF application and worker foundation, including application reuse, sequential
worker execution, IPC, publication ownership, settings, and source allocation.
The private catalog advertises twenty tested PNG/JPEG/WebP/BMP/TGA cross-format pairs
plus five ordinary-image-to-DDS pairs, DDS-to-DDS and selected-color-mip DDS-to-PNG;
the real adapter is used by the Convert planner and worker. Per-file probing and
explicit planning still determine whether an individual variant is supported.
The catalog also advertises bounded lossless PNG optimization (preserve pixels,
representation and metadata; save only smaller verified output). Its automated
acceptance is separate from the still-pending interactive optimization UI checks.
The user-approved [expanded evaluation](png-quantization-improvements.md) led to
[bounded RGB precision presets](png-lossy-presets.md), now implemented through
the planner and private worker. Interactive production acceptance, held-out
quality coverage and broader comparison/metadata controls remain open.


Next Goal And Current Execution Order
------------------------------------

Follow the [broad file support milestones](broad-file-support-goal.md):

1. Define the capability matrix, resource limits and fixture provenance. Resolve
   document action scope while independent Analyze/audio work proceeds.
2. Deliver generic Analyze results and route existing DDS facts into one compact
   report. Unknown and misleadingly named files must remain useful outcomes.
3. Build the reviewed offline catalog and tested identification rules, keeping
   recognition separate from supported conversion and optimization.
4. Add bounded image/audio/PDF/Office and selected other structural analysis.
5. Deliver fixed audio conversion presets and lossless FLAC recompression through
   an independently evaluated engine and explicit container/codec matrix.
6. Implement the selected document actions after fidelity/dependency decisions.
7. Complete real-engine, failure, performance, manual UI and isolated packaging
   acceptance. Preserve separate release gates and defer signing.

The following records existing milestone history/dependencies. They do not
require an image-only public release before the broader goal can proceed.

The [image output safety and settings goal](image-output-safety-goal.md) is locally
implemented and verified, a bounded Milestone 2 slice.
[Decision 0008](decisions/0008-output-naming-settings-and-replacement.md)
accepts Windows-style output naming, safe-copy defaults, per-batch settings, and
optional replacement followed by recycling, with retained originals/backups on
failure. Settings, naming, publication, result reporting, Settings activation, and
failure contracts pass. Native replacement, recycle-only deletion guards, and
forced-termination tests pass. Replacement is available only on verified Windows
build 26200 x64 with ordinary local NTFS files, and permission remains off by
default. See the goal brief for evidence and remaining manual coverage.

Historical image implementation sequence (retained milestone references):

1. Completed bounded Milestone 2 settings/naming/publication safety foundation.
2. Completed bounded Milestone 6 PNG/JPEG/WebP batch conversion using Magick.NET, adding
   the needed Milestone 2 worker execution and validation contracts alongside it.
3. Completed curated-engine development-packaging integration and bounded
   BMP/TGA conversion under Milestone 6. The completed bounded [DDS goal](dds-conversion-goal.md)
   adds Milestone 3 header analysis and DDS conversion under decision 0011;
   codec, worker, failure, packaging and 33 image/DDS desktop checks pass locally.
4. In-progress [reproducible packaging and clean-machine goal](release-packaging-goal.md)
   under Milestone 7: candidate/CI tooling and an approved Inno offline
   lifecycle candidate now exist. The [installer integration](installer-recovery.md)
   passes automated staging, launch, upgrade/repair and uninstall-orchestration
   checks without a VM. Native upgrade admission, stable signing identities and
   clean-machine verification remain gated.
   No release certification yet.
5. Milestones 4 and 5 PNG optimization, reusing the output/settings foundation.
   The bounded [lossless PNG slice](png-optimization-goal.md) is now implemented
   under decision 0013, with automated engine/worker/publication checks. Interactive
   planner acceptance and a direct Lossless menu action remain pending; Milestone 5
   bounded lossy policies are implemented; interactive acceptance and broader
   comparison/metadata controls remain open. Product work can proceed while signing and
   native installer acceptance await owner decisions.
6. The former image-release-before-audio ordering is superseded by decision 0019.

The completed brief is [PNG/JPEG/WebP batch conversion](image-conversion-goal.md).
[Decision 0009](decisions/0009-first-image-engine-and-trial.md) settles the initial
package, media policies and implemented 168-hour local trial. Live Polar integration remains deferred and no
shipping bypass is permitted. Robust behavior takes priority over advertised
format count. Actual codec, worker, isolated conversion-UI and production-window
tests pass; the [acceptance audit](image-conversion-goal.md) records scope and
release limits. The isolated curated-engine prototype in
[decision 0010](decisions/0010-first-release-formats-and-curated-engine.md) passes
its bounded acceptance; see the [evaluation](curated-engine-prototype.md).
The subsequent [production integration and BMP/TGA slice](bmp-tga-and-engine-integration.md)
also pass their bounded checks; stock native copying is disabled and packaging
verifies the selected curated hashes and file allowlist. Complete the separate
[release redistribution gates](release-redistribution.md) before distribution.
Decision 0011 settles the dedicated CPU DirectXTex engine and BC/linear/sRGB/mipmap
policies; bounded implementation and focused verification pass locally. The accepted first image
release targets PNG/JPEG/WebP/DDS/TGA/BMP, not the entire upstream format catalog.


Roadmap Principles
------------------

- Build complete vertical slices from Explorer command to validated result.
- Keep Analyze, Convert, and Optimize behaviorally distinct.
- Establish file and process safety before expanding format count.
- Treat multi-file Convert and Optimize invocations as first-class batches, not
  repeated single-file operations.
- Make every advertised capability a tested contract.
- Preserve the tested image foundation while adding broad Analyze, common audio
  and agreed document capabilities under decision 0019.
- Use milestone exit criteria to decide readiness; incomplete work moves forward
  only when an explicit scope decision accepts the remaining risk.


Release Shape
-------------

The former image MVP followed by audio expansion is superseded by decision 0019.
The next launch target combines the existing image utility with useful universal
Analyze, a reviewed format catalog, common audio and agreed PDF/document support.
The [active goal](broad-file-support-goal.md) separates required capabilities,
open decisions and acceptance. Historical component scopes were:

- **Image MVP** — all three Explorer roots, DDS and common image analysis,
  batch PNG optimization, and batch PNG/JPEG/WebP/TGA/BMP conversion, with first-class
  DDS representation conversion developed before broader image expansion.
- **Audio expansion** — common audio analysis, batch conversion, and narrowly
  defined same-format optimization after the image foundation is reliable.

Public source, documentation, tests, screenshots, and demo video support
portfolio evaluation; there is no separate review/demo application. The one
production build requires the private checkout. A downloadable trial will
provide hands-on evaluation when ready. A paid
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

- Curated Magick native build and redistribution configuration under decision
  0010; package/precision are settled by 0009. CPU DirectXTex and the bounded DDS
  matrix are selected/pinned under 0011; complete their acceptance and redistribution gates.
- Lossless PNG uses pinned oxipng under decision 0013; its transitive release
  inventory remains to review. Select distribution of FFmpeg and the lossy PNG
  engine before their dependent slices.
- Installer, signing, update, and diagnostics strategy.
- Exact private engine adapters and optimization policy definitions.
- Access-policy boundary for the seven-day trial and purchase workflow
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
distribution need not delay the shared safety and first image-conversion work.


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
  states; defer real key activation, payments, and trial persistence to the commercial
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
- Versioned settings, immutable batch snapshots, and the shared settings UI.
- Decision 0008 naming, explicit replacement consent, and verified recycle-only
  cleanup with original/backup retention on failure.
- Test doubles that exercise the complete pipeline without media engines.

Exit criteria:

- A simulated multi-file Convert batch and Optimize batch run as one operation.
- Tests cover repeated paths, collisions, locked outputs, Unicode and long paths,
  cancellation, timeouts, validation failures, and partial success.
- Pre-publication failure/cancellation preserves originals. Committed results
  remain completed; later cleanup failure preserves a backup and reports a warning.
- Replacement remains unavailable until actual Windows safety checks pass;
  copy-only support does not certify the replacement contract.
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

Current slice: [Balanced RGB7 and Smallest RGB6](png-lossy-presets.md) implemented
with exact alpha, preserved accepted metadata and smaller-than-lossless gating.
This does not complete the broader comparison UI or release acceptance below.

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

- A pinned and packaged Magick.NET image conversion adapter.
- PNG, JPEG, and WebP analysis needed for planning and validation.
- Implemented BMP/TGA input and output for tested variants under decision 0010,
  with explicit alpha, orientation, bit-depth, and metadata/color-loss policies.
- **Convert** targets compatible with the complete Explorer selection.
- One coordinated batch for multiple selected files.
- Transparency detection and required matte choice for opaque outputs.
- Orientation application, metadata policy, quality and maximum-dimension
  controls, and before/after preview.
- Destination-oriented recommendations and an advanced settings surface.

Exit criteria:

- Every advertised PNG/JPEG/WebP pair has successful, malformed, transparency,
  orientation, metadata, Unicode-path, and cancellation coverage where relevant.
- BMP/TGA additions meet the same applicable safety and per-pair contracts;
  compiled coder availability alone never enables a capability.
- One selected target and policy apply predictably across a batch.
- File-specific warnings appear before execution without spawning per-file
  planning windows.
- Outputs validate against their plans and sources remain intact.

DDS expansion follows the first common-image slice and reuses Milestone 3's
public parser for independent header validation. Target BC1/DXT1 through BC7
and selected uncompressed formats, with explicit applicable linear/sRGB and
signedness behavior. Preserve supported mip/face/layer structures, distinguish
pixel conversion from reinterpretation, and reject unsupported combinations.
Decision 0011 selects the CPU DirectXTex bridge and initial ordinary-2D matrix;
BC6H/HDR and cube/array/volume conversion remain deferred. See the active
[DDS goal](dds-conversion-goal.md); general-image engine support does not cover DDS.


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

Decision 0017 settles elapsed-time rules, feature access after expiry, lifetime
updates, 30-day offline grace/daily refresh, outage behavior and transfer recovery.
Verify actual refunds/revocation, pricing and source/distribution terms,
independently of media-engine selection. Polar is selected and account approval
is user-confirmed; sandbox configuration is recorded and local integration is
contract-tested, but live activation remains unverified. Follow the
[Polar integration plan](polar-integration.md). Do not build custom website
accounts. Commercial integration need not block component development. Resolve
production admission before exposing executable paid media operations; there is
no separate portfolio application or shipping access bypass.

Deliver and verify:

- The seven-day trial with clear expiry information and completion of batches
  admitted before expiry.
- Polar hosted checkout, key activation with one active installation, and the
  chosen local storage and verification policy. Redirects cannot grant access.
- Invalid-license, unavailable-service, license-recovery, and selected offline
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

Outside the first image-release target, possible additions include AVIF, HEIC,
TIFF, GIF, SVG rasterization, camera RAW, ICO, richer comparison tools, clipboard
workflows, and reusable user contexts. Each requires a demonstrated user job,
an explicit scope decision, packaging support, and contract fixtures.

Video, PDF and Office conversion, cloud processing, AI editing, CD ripping,
unrequested source removal, permanent-delete fallback, and arbitrary media-engine
commands remain non-goals until the product design explicitly changes.


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
