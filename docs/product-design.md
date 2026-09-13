Context Suite Product Design
============================

Completed UX direction: [context-menu simplification](context-menu-simplification-goal.md)
under [decision 0018](decisions/0018-context-menu-utility-and-output-preference.md).
Remove the general customer workspace and routine planners; retain focused
decision prompts, Analyze, progress/problems, Settings and License. Output defaults
to copies, with explicit Settings consent for safe replacement on future commands.
The implementation follows this scope with owner manual acceptance. Additional
accessibility/theme/DPI and installed lifecycle coverage remain release gates.

Accepted next priority: [broad file support](broad-file-support-goal.md) under
[decision 0019](decisions/0019-broad-file-analysis-and-media-expansion.md).
Analyze must give useful read-only results for every readable regular file,
including a reviewed offline explanation of common types. Add common audio
analysis/conversion and lossless FLAC optimization, plus PDF/Office/OpenDocument
analysis. Selected launch actions include images-to-PDF, PDF pages-to-images,
PDF optimization and Word/Excel/PowerPoint-to-PDF. Optional PDF optimization has
direct worker/publication coverage; PDF-to-PNG has direct PNG-command coverage
with per-document summaries and partial-page retries. Renderer packaging and
visible acceptance remain pending. Optional Convert > PDF now combines images
through a focused order review, one validated copy and whole-document retry.
Bounded legacy DOC/XLS/PPT analysis now supplements package analysis; its
[supported headers and limits](legacy-document-analysis.md) are explicit.
Office-to-PDF remains planned;
retain the simple customer surfaces above. Shared Analyze/fallback,
bounded JSON/XML structure analysis and a 243-entry catalog are implemented with automated
evidence; see [current coverage](file-type-coverage.md). Signing is deferred.

Status: shell/process and output-safety foundations implemented; PNG/JPEG/WebP/BMP/TGA
conversion and bounded DDS analysis/conversion have passed local acceptance.
Lossless and bounded lossy PNG optimization are implemented with automated contracts;
broader analysis and live-provider paid activation remain pending. The owner
accepted the simplified image UI; this does not certify all accessibility environments.
See [PNG precision presets](png-lossy-presets.md) for exact scope and remaining checks.


Product Summary
---------------

Context Suite is a local Windows application for understanding, converting, and
optimizing supported files from File Explorer. It serves people who know what they
need to accomplish but may not know which codec, quality setting, or
compatibility tradeoff applies.

The product promise is:

> Understand and prepare media files without hidden quality, metadata, or
> compatibility surprises.

The overarching UX goal is simplicity for a broad audience: **select files,
right-click, choose, done**. Routine successful work should rarely require the
application UI. Direct, understandable menu actions are the primary interface;
Settings, technical details and focused decision prompts are secondary.

[Decision 0015](decisions/0015-simple-context-menu-workflows.md) establishes the
next direction: Auto first, direct presets, best-effort processing within the
chosen quality limits, a completion chime, and UI only for useful progress,
necessary decisions or problems. Direct Optimize and safe common Convert implement this flow with
local automated evidence; broader UI/compatibility work and human acceptance
remain open. See the [quiet-first UX evidence](quiet-first-ux-goal.md).


Tool Boundaries
---------------

The suite exposes three equal top-level Explorer commands rather than a single
“Context Suite” parent menu:

- **Analyze** reads the selected file and explains its properties. It never
  changes the file.
- **Convert** changes format because another representation is required or more
  suitable. The target format must be explicit.
- **Optimize** reduces file size while retaining the current format by default.

These boundaries are behavioral contracts, not just menu labels. Convert may
apply sensible encoding optimization while producing its requested format, but
same-format reduction belongs to Optimize. Optimize may explain that another
format could be smaller, but it must not perform that conversion implicitly.

An explicit DDS representation change (such as BC1 to BC7 or a chosen color-space
conversion) belongs to Convert even though the extension remains `.dds`.
Optimization retains the selected representation and loss policy.


Target Users
------------

- Students and job applicants meeting upload requirements.
- General Windows users handling unsupported images or audio.
- Web developers and designers preparing assets for a known destination.
- Content creators preparing audio for sharing, publishing, or editing.
- Indie developers analyzing and preparing game assets.


Primary Jobs
------------

1. Determine how a file is encoded and whether important capabilities such as
   transparency, animation, metadata, or color information are present.
2. Convert an unsupported file into a broadly compatible format.
3. Convert media for a named destination without learning codec terminology.
4. Reduce file size under an explicit loss policy.
5. Convert or optimize multiple selected files as one coordinated batch with
   consistent settings, safe output names, and clear partial-failure results.
6. Understand exactly what changed between source and output.


Design Principles
-----------------

- Describe user goals before implementation details.
- Prefer a single context-menu choice over opening a planner. Do not make users
  understand PNG variants, metadata chunks or encoder settings for ordinary work.
- Use fixed, versioned optimization recipes with quality and effort limits.
  Auto selects one recipe through bounded preflight; named presets do not search
  across methods. Allow at most one safe fallback, then retain the original.
  See [decision 0016](decisions/0016-fixed-optimization-recipes.md) and the
  [implemented PNG scope](fixed-preset-integration.md).
- Keep fast success quiet except for one optional completion sound per batch.
  Progress and exception UI must be compact and actionable, not an engineering dashboard.
- Recommend actions, but keep irreversible consequences visible until execution.
- Preserve source files by default.
- Ordinary raster conversion uses automatic image-information handling: apply
  orientation, retain/transform color correctly, preserve supported EXIF/XMP and
  resolution, and quietly omit incompatible extras from a new copy. Such omission
  forces copy output even with replacement enabled. No routine review checkbox,
  warning sound or failure status. Strict preservation and privacy removal remain
  secondary choices; automatic handling is not a personal-data scrub.
- Convert's menu format selects the tested default encoding on a copy. Prompt for
  actual ambiguity (such as transparency background or DDS interpretation), not
  ordinary metadata or encoding consequences. Keep failure reporting per file.
- Direct app opening is a Settings/help landing with Explorer instructions;
  previews, file tables and manual processing are secondary, not the first screen.
- For the researched PNG `fdEC` exception, removal is explicitly approved without
  a warning or forced-copy restriction. Follow the normal selected copy/replacement
  policy, validation and recovery safeguards. Use ordinary quiet success and the
  optional success chime. This does not authorize removing other unknown or
  meaningful metadata.
- Explain meaningful quality and compatibility tradeoffs in plain language.
- Never silently discard transparency, orientation, tags, artwork, animation,
  color information, or other meaningful capabilities.
- Never imply that converting lossy media to a lossless format restores detail.
- Keep common operations fast and place expert controls behind a secondary
  surface.
- Validate completed files instead of treating process completion as proof of
  success.
- Make mixed-selection and partial-failure behavior predictable.
- Keep all media processing local unless a future product decision explicitly
  introduces another model.


Explorer Experience
-------------------

Explorer presents **Analyze**, **Convert**, and **Optimize** as peer commands.
Each command is visible only when at least one useful action applies to the
selection. **Analyze** is a direct command with no child menu and opens its
details surface immediately. Convert and Optimize child actions are contextual
to the complete selection rather than being a static catalog of every supported
format or preset.

When multiple files are selected, Convert and Optimize treat that Explorer
invocation as one batch. They apply one selected target or policy consistently,
show combined progress, and return both per-file results and an aggregate
summary. They must not launch an unrelated window or process for every file.
Direct child actions are available only when they apply safely to every selected
file; otherwise the planning surface can explain or partition the selection.

Common one-decision operations may start from a child command. Any action that
requires a meaningful decision—such as choosing a matte for transparent PNG to
JPEG conversion—opens the appropriate planning surface before processing.

The target Optimize submenu is Auto, Lossless, Balanced, Smallest, separator,
Settings. Each preset runs directly without Choose preset or a success dialog.
Fast batches stay hidden; longer work may show cancellable progress. No smaller
result is a normal completion. Required consent, access problems, partial failure
and recovery warnings remain visible; simplicity must not conceal consequences.

Explorer code must remain responsive. It performs only bounded capability work
and delegates analysis, planning, conversion, optimization, validation, and
result presentation to an out-of-process host.

Convert and Optimize each end with a separated **Settings...** action opening
their section of one shared settings window. Commands create copies by default.
Saving Overwrite originals is explicit consent for future commands of that tool;
mandatory copy exceptions and validated publication still apply. Schema-one
permission-only settings migrate to copies. Standalone launch opens Settings
with Explorer instructions; there is no manual file-picker/drop workspace.

Application and Settings windows follow Windows' app light/dark mode, accent,
and contrast theme automatically, including changes while windows are open.
Use the built-in WPF Fluent appearance rather than maintaining a separate theme
setting. Explorer menus remain Windows-owned.


Shared Operation Workflow
-------------------------

The application resolves trial or purchase access before admitting a commercial
operation. Analyze validates the selection, reads bounded media facts, and opens
its report; it does not create or publish transformed files. Application-owned
trial bookkeeping does not belong in the read-only analyzer.

Convert and Optimize follow the transformation workflow:

1. Receive the selected paths as one operation batch and validate each path.
2. Analyze enough media properties to determine applicable capabilities.
3. Build a typed operation plan and identify consequences or required choices.
4. Present those choices when the selected command is not self-contained.
5. Execute a cancellable operation outside Explorer.
6. Validate each completed output.
7. Publish outputs according to the selected output policy.
8. Present per-file and aggregate results with useful next actions.

Default names follow Windows copy formatting: `name - Converted.ext`,
`name - Optimized.ext`, or a meaningful DDS variant such as
`name - BC7-sRGB.dds`, with collisions numbered from `(2)`. Preserve source
basenames rather than stripping existing suffixes. Explicit replacement publishes
validated output before recycling the old file, retaining an original/backup
when recycling fails. No permanent-delete fallback or app-managed backup browser
is planned. See [decision 0008](decisions/0008-output-naming-settings-and-replacement.md)
for naming, settings snapshots and the required Windows verification, with
[decision 0018](decisions/0018-context-menu-utility-and-output-preference.md)
superseding per-batch replacement consent.


Release Sequence
----------------

The implemented image foundation targets PNG, JPEG, WebP, DDS, TGA,
and BMP. Bounded PNG/JPEG/WebP/BMP/TGA conversion is implemented today. Formats
do not imply support for every variant or operation. A curated general-image
engine is integrated into development packaging. Decision 0011 selects a pinned
CPU DirectXTex bridge; bounded DDS analysis/conversion passes local acceptance. See
[decision 0010](decisions/0010-first-release-formats-and-curated-engine.md).
Retain useful near-term capabilities while excluding unnecessary dependencies;
do not expand product scope merely because upstream bundles a codec.

The intended vertical slices are:

1. Shared settings and output safety, including replacement failure tests.
2. PNG, JPEG, and WebP conversion with transparency and metadata handling.
3. Curated-engine integration and bounded BMP/TGA conversion (implemented), then
   DDS analysis with explicit BC/linear/sRGB conversion and independent validation.
4. Lossless then bounded lossy PNG optimization with representative fixtures.
5. Broader image conversion and analysis, capability by capability.
6. Common audio analysis and conversion.
7. Additional formats supported by demonstrated use cases and reliable tests.

Each slice should work from Explorer through validation before the next format
family substantially expands the product surface.
TIFF, GIF, ICO, and AVIF remain later candidates; HEIC/HEIF, camera RAW, and SVG
import require separate scope and redistribution decisions. Decision 0019
supersedes the former image-release-before-audio ordering: broad Analyze, common
audio and agreed document capabilities are now part of the next launch goal.
See [broad file support](broad-file-support-goal.md) for the active sequence;
the image slices above retain their existing implementation and evidence scope.


Portfolio And Commercial Product
--------------------------------

Context Suite is both a co-op portfolio project and a potential paid utility.
Most engineering is intended to be publicly reviewable. The complete production
application requires private implementations from `context-suite-private`,
checked out under the public repository's ignored `proprietary/` directory.

One production foundation build requires the private checkout; a
public checkout alone cannot build the complete app. There is no separate
review/demo edition. Portfolio presentation uses public code, architecture,
tests, and planned screenshots and demo video; a downloadable commercial trial
will serve people who want to run it. Public components may be tested separately
where their dependencies permit. Public-only builds cover the shell prototype
and component contracts, not the complete production application. See
[decision 0005](decisions/0005-public-and-proprietary-builds.md).

The commercial direction is a seven-day trial followed by Polar license-key
activation. Hosted checkout and the customer portal replace custom website
accounts. A checkout redirect alone does not establish paid access. All media
processing stays local; account services do not receive selected paths or media
contents. Access is checked by the application before work starts, never while
Explorer constructs a menu. An admitted batch may finish after trial expiry,
and its results remain available.

The local trial starts at the first confirmed valid conversion or optimization and lasts 168
elapsed hours; [decision 0009](decisions/0009-first-image-engine-and-trial.md)
and its [PNG extension](decisions/0013-lossless-png-optimization.md)
define clock and failure handling. Browser activation
is replaced by in-app key activation. The accepted
[paid-access policy](decisions/0017-paid-access-and-release-candidate.md) grants
all future updates, one active installation with transfer, daily revalidation,
30-day offline grace and free Analyze access after trial expiry. Implementation
and actual activation/transfer verification remain in progress.
The accepted price is $5 CAD, one time, under
[decision 0020](decisions/0020-seven-day-trial-and-pricing.md). Final licence terms
and live checkout verification remain pending. See the
[Polar integration plan](polar-integration.md). Keep protection modest and accept
that determined users may reset local trials or modify binaries. See
[decision 0006](decisions/0006-trial-and-purchase-access.md).


Distribution Direction
----------------------

Website downloads use one Inno Setup installer for the application and three
Explorer identities. Installation is per-user, with bundled offline Microsoft
runtime prerequisites that may require administrator approval. Settings, trial
and license data must survive uninstall. Manual installer-based updates come first;
Microsoft Store distribution is a later option, not a separate app edition.
The internal installer uses versioned payloads and active-release launch/uninstall.
Upgrade/repair orchestration is integrated and tested with mocked platform calls;
existing-install admission stays closed until native acceptance. Signed customer
installation, upgrade and repair remain pending. See
[decision 0012](decisions/0012-inno-offline-installer.md).


Explicit Non-Goals
------------------

The initial product will not include:

- Video conversion.
- Document transformations outside the selected PDF/Office actions in decision
  0019 and their verified capability matrix.
- Photo editing, filters, retouching, or background removal.
- AI enhancement or generation.
- Remote media processing or cloud media storage. Accounts are limited to the
  planned purchase and activation workflow.
- Extensive anti-piracy mechanisms such as invasive hardware fingerprinting or
  anti-debugging systems.
- CD ripping or media-library management.
- Arbitrary FFmpeg or other engine command entry.
- Unrequested source removal or permanent-delete fallback; explicitly confirmed
  replacement may recycle originals after validated publication.
- Silent in-place lossy processing.
- Obscure formats added only to increase the advertised format count.


Success Measures
----------------

The initial product direction is successful when:

- A new user can choose the correct operation without understanding codecs.
- The three tools remain behaviorally distinct and predictable.
- Important destructive consequences appear before work begins.
- A failed or cancelled operation never damages its input.
- Mixed batches remain understandable when files differ or fail.
- Multi-file Convert and Optimize invocations remain one controllable batch with
  predictable settings, cancellation, progress, and results.
- Output files open correctly and match the properties reported by the suite.
- Context-menu discovery does not noticeably degrade Explorer responsiveness.
- The repository demonstrates tested planning, safe process execution,
  accessible desktop UX, and disciplined scope decisions.
