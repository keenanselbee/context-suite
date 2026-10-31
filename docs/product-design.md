Context Suite Product Design
============================

Status: one free production application with implementation in the main repository.
The [free transition record](free-access-goal.md) owns migration and exact-candidate
verification; [project status](../README.md#project-status) distinguishes source,
candidate, installed, and release state. Local verification does not establish
installation, signing, remote CI, publication, or live retirement acceptance.

Use [Analyzer design](analyzer-design.md), [Converter design](converter-design.md),
[Optimizer design](optimizer-design.md), and [document design](document-design.md)
for current behavior and safety boundaries. The [launch matrix](launch-capability-matrix.md)
and [file coverage](file-type-coverage.md) distinguish recognition from executable
capabilities. [Dated implementation history](product-implementation-history.md)
preserves prior feature, candidate, and installation checkpoints.

Analyze uses a one-time opening layout at 82% of the monitor work-area height.
Collapsed and expanded views share the expanded layout's starting top. Explicit
Technical details changes shrink or restore normal height while preserving
position and width; expansion fits below the current top where possible.
Maximized windows retain their geometry. The disclosure updates immediately,
remembers its preference, and preserves ordinary movement and resizing.

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
implemented direction: Auto first, direct presets, best-effort processing within the
chosen quality limits, a completion chime, and UI only for useful progress,
necessary decisions or problems. The [simplification goal](context-menu-simplification-goal.md)
records owner acceptance of the focused utility; broader accessibility and
environment acceptance remain separate. See the [quiet-first UX history](quiet-first-ux-goal.md).


Tool Boundaries
---------------

The suite exposes three equal top-level Explorer commands rather than a single
**Context Suite** parent menu:

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
- Direct app opening is a Settings/help landing with Explorer instructions.
  Necessary prompts and progress/problems provide relevant previews and file
  details; there is no manual processing workspace.
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
Direct child actions remain available when useful for at least one selected file.
Content inspection outside Explorer determines support; inapplicable items get
per-file explanations while independent supported files continue. The selected
command and captured settings apply across the coordinated batch.

Necessary choices, such as a matte for transparent PNG-to-JPEG conversion,
open a focused decision dialog before processing. Routine actions do not open
a general planner or partition the selection into unrelated workflows.

The target Optimize submenu is Auto, Lossless, Balanced, Smallest, separator,
Settings. Each preset runs directly without Choose preset or a success dialog.
Fast batches stay hidden; longer work may show cancellable progress. No smaller
result is a normal completion. Required consent, processing problems, partial failure
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

The application validates confirmed plans before admitting work, independently
of any trial or purchase state. Analyze validates the selection, reads bounded
media facts, and opens its report; it does not create or publish transformed files.

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

Current implemented scope spans image conversion, common audio conversion,
PDF/Office document actions, and format-specific optimization. Exact admitted
variants, engines, preservation limits, and acceptance gaps belong to the
[Converter design](converter-design.md), [Optimizer design](optimizer-design.md),
[document design](document-design.md), and [launch matrix](launch-capability-matrix.md).
Recognition alone never enables a transformation.

Bounded image paths include ICO and still SDR AVIF output, classic TIFF
conversion, static SVG conversion, HEIC/AVIF input, and GIF frames to numbered
PNG copies. GIF animation encoding, camera RAW, and HEIC output are outside the
implemented scope. Selected MP4/MOV/WebM/MKV audio extraction is an explicit
exception to the video exclusion; general video conversion remains outside scope.
All additions retain their format-specific safety limits and mandatory copy
exceptions. Do not expand scope merely because an upstream engine supports more.

The [roadmap](roadmap.md) records remaining priorities and release gates;
[implementation history](product-implementation-history.md) preserves earlier
vertical-slice ordering. Each addition needs demonstrated use cases, independent
validation, and its required acceptance before advertised support expands.


Free Product And Source Availability
------------------------------------

Context Suite is a free Windows utility and a co-op portfolio project. All
supported features and future official updates are free, with no trial,
purchase, activation, installation limit, or licensing network requests.
The reviewed application implementation and tests live in the main repository;
the separate private checkout is retired historical material.

One production build composes the same media adapters and pinned engine inputs.
Complete application builds require the documented toolchain and reviewed engine
artifacts; public source availability does not remove those prerequisites.
Keep source-available terms, third-party notices, and source-delivery material.
See [decision 0021](decisions/0021-free-product-and-public-implementation.md)
and [transition status](free-access-goal.md) for implementation and verification.

Old trial and license records remain untouched and cannot block new operations.
Validation of plans, selected tools, formats, quality choices, source/output
identities, and safe publication remains mandatory. Media contents and selected
paths remain local. Existing historical commercial decisions retain their
original evidence, with their current policy superseded by decision 0021.


Distribution Direction
----------------------

Website downloads use one Inno Setup installer with Analyze, Convert, and Optimize
selected by default. Users may deselect any tools but must keep at least one.
Only selected commands and their required shared payload are installed; see
[optional tool installation](optional-tool-installation.md) for dependencies,
size estimates, and current acceptance limits. Installation is per-user, with
bundled offline Microsoft runtime prerequisites that may require administrator
approval. Settings and legacy trial/license data must survive uninstall. Manual installer-based updates come first;
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
- Remote media processing, cloud media storage, accounts, purchases, or activation.
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
