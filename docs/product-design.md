Context Suite Product Design
============================

Version 1.4.8 uses an immediate Technical details disclosure. Its content,
chevron and scroll range update in the first layout without a sliding transition.
The native Expander state and themed ToggleButton retain keyboard interaction,
focus feedback and the remembered preference; window sizing remains unchanged.

Version 1.4.7, installed locally, gives collapsed and expanded Analyze windows the same starting top,
calculated from the expanded 82% opening height. The collapsed window is shorter,
not separately centered. Toggling details changes only height; ordinary movement
and resizing remain unrestricted.

Version 1.4.6, installed locally, adjusts height on explicit Technical details changes. Collapsing
shrinks a normal inspector to at most 570 DIPs; reopening restores its previous
expanded height, fitting below the current top when possible. Position and width
stay unchanged, maximized windows stay maximized, and movement and resizing have
no ongoing geometry corrections. The remembered disclosure state and 82% opening
layout remain.

Version 1.4.5, installed locally, replaces Analyze's temporary bounds with a one-time opening layout.
Expanded inspectors start at 82% of the current monitor's usable height, centered
vertically; remembered collapsed details retain a compact opening height. No
ongoing geometry handlers or maximum-height limit constrain movement, resizing,
snapping, maximizing, monitor changes or later disclosure changes. Long content
scrolls inside the chosen size while footer actions stay accessible.

Version 1.4.4, installed locally, narrows the Analyze taskbar fit to initial automatic layout. Moving,
resizing or maximizing the inspector releases its temporary monitor bounds;
later disclosure changes retain the owner's geometry. It is a starting layout,
not a permanent restriction on where the owner can place the window.
The same update incorporates the owner's further revisions to both triangle
PNGs, preserving the separate small-size artwork and transparent surroundings.

Version 1.4.3, installed locally, refreshes Windows icons from the owner's latest triangle PNGs on
2026-10-16. Preserve source colors, proportions and alpha, using the small artwork
through 48 pixels. It also retains post-layout window bounds checks without
forcing a position correction on every move between monitors.
Installed taskbar resource lookup, three packaged COM activations and all payload
hashes pass. Settings, trial and license data are unchanged by installation.

The 2026-10-16 Analyze refinement in 1.4.2 opens Technical details by default and
remembers subsequent disclosure choices in settings. Expansion fits below the
window's current position within the monitor work area, leaving a small gap above
the taskbar. Type-specific file properties appear before source/classification
information; supporting evidence is last. Duplicate generic file facts are removed.

The 2026-10-16 text-counting update in 1.4.1 reads recognized text in fixed-size
chunks, up to 256 MiB within the existing five-second read budget. Small format
identification remains bounded to 64 KiB. The Analyze window shows an exact line
total when counting completes, or a lower bound and inspected text-byte count
when a limit or invalid tail stops it. Counting runs off the UI thread and keeps
user cancellation and source-change checks. See [Analyzer design](analyzer-design.md)
and the [measured performance follow-up](analyze-performance.md#streaming-text-counts-2026-10-16).

On 2026-10-16 the owner selected `images/icon-tri.png` and
`images/icon-tri-small.png` for Windows branding in 1.3.9. Use the transparent
folded triangle for application, taskbar, package and installer icons. Retain
the emerald-background `icon.png` and `icon-small.png` artwork for online pages.
Keep the external resource index and transparent package backgrounds from 1.3.8.
Version 1.3.9 is installed locally. Generated icon alpha, installed resource lookup,
three packaged COM activations and the installed payload inventory pass; settings,
trial and license data and the online-page source images are unchanged.
The owner's revised triangle PNGs are installed in 1.4.0 on the same date, with
the same icon alpha, installed resource lookup, packaged COM and payload checks.
The version rollover introduces no compatibility change.

2026-10-15 source follow-up: conversion progress/results opens **Files and
details** automatically. Its file table fills the remaining window height;
the selected-file explanation scrolls separately and the bottom actions stay
reachable. Resizing or toggling details retains the chosen window height.
Quiet completion and the separate Analyze inspector retain their existing
lifecycle. This UI change is included in local version 1.3.6, installed on
2026-10-15 with the approved emerald folded-triangle application icon. Windows
uses ten transparent ICO sizes (16 through 256 pixels); package and installer
branding share the same artwork. Explorer action glyphs remain monochrome.

The 1.3.7 local update changes package-level taskbar transparency: all three
identities declare transparent backgrounds and include indexed default, dark and
light target-size assets. Keep the green rounded tile; its outer transparent
corners must not receive Windows' former blue, purple or green package fill.
The 1.3.8 follow-up stages the shared resource index at the external application
location. The 1.3.7 indexes beside registration manifests were not loaded by
Windows; installed resource lookup failed despite correct PNG alpha. Verification
now resolves each tool's icon through its installed package. On 2026-10-16 the
owner confirmed that the gray taskbar square is gone after reopening installed
1.3.8. Resource lookup, alpha, installed payload and packaged COM checks pass.

Analyze UI update, installed locally in 1.2.8 on 2026-10-11: Analyze now uses an independent
read-only inspector window, with an immediate single-file summary and a
multi-file comparison table. Convert and Optimize retain their shared compact
progress/results window, application process and sequential worker. Closing or
cancelling transformation results preserves the inspector and its analysis.
See [analyzer design](analyzer-design.md) for lifecycle and verification limits.
The installed inventory and three shell COM registrations are verified. Settings,
trial and license files remain unchanged; installed UI acceptance remains separate.

The 1.2.9 refinement, installed locally on 2026-10-11, puts text line counts and
encoding alongside file size. Identification evidence remains under Technical
details. Counts from partially inspected files are explicitly lower bounds.

Owner-approved PDF dark-mode conversion, 2026-10-10: add one **PDF (Dark mode)**
destination that creates a local deterministic reading copy, preserving searchable
text, vector geometry and supported document features. Preserve photos; recolor
simple diagrams only under a validated policy. Always keep originals. AI/OCR
reconstruction and cloud processing are excluded. This is an explicit appearance
conversion exception to format-only destinations, not an Optimize action. See the
[implementation goal](pdf-dark-mode-goal.md). The bounded native engine, worker,
direct action and shell target are verified in unsigned
[candidate 1.2.7](pdf-dark-mode-review.md). A subsequent source-only policy update
converts all 50 lecture pages with independent structural and rendered review and
actual-worker validation. It adds local contrast relationships and bounded
transparent-image handling; some preserved images retain light backgrounds.
That update is not packaged or installed; reserved candidate 1.2.7 is unchanged.
The source-only `pdf-dark-3` follow-up adds bounded recoloring of compressed
monochrome and colored diagram images, including the four requested lecture
pages, with contrast checks and preservation of unhandled image panels.
The subsequent `pdf-dark-4` update improves colored-panel ink contrast and
compression handling without changing geometry or the existing admission guards.
The source-only `pdf-dark-5` follow-up preserves colored-ink edge coverage to
reduce pale annotation outlines, retaining the same command and bounded guards.

Owner-approved lecture PDF extension, 2026-10-11: normalize already-dark slide
backgrounds and supported embedded tables to the existing reading palette.
Support validated tagged structure, ordinary links, passive attachments, observed
static layers and wider font metrics while preserving their semantics. Retain one
PDF (Dark mode) command and mandatory copies. The
[lecture support goal](pdf-dark-lecture-support-goal.md) defines the 25-document,
746-page acceptance corpus and implementation sequence. This extension is planned;
installed 1.2.9 remains unchanged.

Owner-approved compatibility expansion, 2026-10-09, in progress: JFIF input,
largest-frame ICO to PNG, all PDF pages to JPEG, all GIF frames to PNG, static
SVG to JPEG, and audio extraction from MP4/MOV/WebM/MKV to WAV/MP3/FLAC. Reuse the
existing destination commands and icons. GIF has no first-frame command or
submenu. Video extraction is the approved narrow extension to the previous
video exclusion; general video conversion remains outside scope. The owner
explicitly added MKV on 2026-10-09; M4V and other containers remain outside scope.
All new conversions keep originals even when overwrite is enabled. Preserve
one batch admission, sequential worker execution, immutable choices and
selective retries. See the [converter design](converter-design.md) for behavior
and current verification. The installed 1.2.5 payload remains the baseline;
these source changes are not installed or packaged as a new release yet.

Owner-approved image expansion, 2026-10-09: ICO and still SDR AVIF output,
classic TIFF to PNG/JPEG/PDF, and static SVG to PNG. Validate the combined icons,
WEM and existing-engine baseline first, then evaluate and implement bounded
converters, distinct destination icons and a combined review candidate. Keep
the direct-command UX and existing publication guarantees. These additions are
implemented in the unsigned [1.2.5 review candidate](image-expansion-review.md).
Native UI, representative fidelity and commercial acceptance remain separate;
local version 1.2.5 is now installed, with the previous 1.2.3 payload retained.

Owner-approved extension, 2026-10-08: [Convert icons and WEM conversion](wem-conversion-goal.md).
Integrate distinct destination icons and add loose WEM/WAV conversion through a
bundled decoder and user-installed Wwise Authoring. Preserve the direct-command
UX; soundbank editing is outside this goal. Both directions now have bounded
real-engine verification in source. Installed/native UI and in-game acceptance
remain separate gates; WEM is not yet an advertised launch capability.
The approved input extension also accepts MP3 and float32 WAV through bounded
PCM16 preparation, with one explicit batch quality decision and no silent loss
of unsupported tags, artwork or out-of-range samples.

Completed UX direction: [context-menu simplification](context-menu-simplification-goal.md)
under [decision 0018](decisions/0018-context-menu-utility-and-output-preference.md).
Remove the general customer workspace and routine planners; retain focused
decision prompts, Analyze, progress/problems, Settings and License. Output defaults
to copies, with explicit Settings consent for safe replacement on future commands.
The implementation follows this scope with owner manual acceptance. Additional
accessibility/theme/DPI and installed lifecycle coverage remain release gates.

License provides **Purchase...** and **Manage license...** actions opening the configured Polar
checkout and customer portal. The owner cleared live checkout on 2026-09-28.
Checkout owns purchase/return instructions; the app retains key activation.
The [bundled third-party notices](bundled-notices.md) remain available without
activation or an online request. Source delivery and redistribution are separate.

Previously accepted expansion: [broad file support](broad-file-support-goal.md) under
[decision 0019](decisions/0019-broad-file-analysis-and-media-expansion.md).
Analyze must give useful read-only results for every readable regular file,
including a reviewed offline explanation of common types. Add common audio
analysis/conversion and lossless FLAC optimization, plus PDF/Office/OpenDocument
analysis. Selected launch actions include images-to-PDF, PDF pages-to-images,
PDF optimization and Word/Excel/PowerPoint-to-PDF. The owner-approved
[PDF improvements](pdf-conversion-improvements-goal.md) add best-effort editable
[PDF-to-Word copies](pdf-to-docx.md), white-default PNG pages with optional
transparency, and filename-filtered conversion targets. These changes are
included in local version 1.1.9, which also adds Compact monochrome Explorer icons.
Optional PDF optimization has
direct worker/publication coverage; PDF-to-PNG has direct PNG-command coverage
with per-document summaries and partial-page retries. Renderer release adoption
and visible acceptance remain pending. Optional Convert > PDF now combines images
through a focused order review, one validated copy and whole-document retry.
Bounded legacy DOC/XLS/PPT analysis now supplements package analysis; its
[supported headers and limits](legacy-document-analysis.md) are explicit.
[Optional Office-to-PDF dispatch](office-direct-command.md) now creates separate
document copies, sharing one admission with combined-image PDF work. Spreadsheet
calculation requires a focused choice; retention, fidelity and integrated
acceptance remain open. Retain the simple customer surfaces above. Shared Analyze/fallback,
bounded JSON/XML structure analysis and a 245-entry catalog are implemented with automated
evidence; see [current coverage](file-type-coverage.md). The
[launch capability matrix](launch-capability-matrix.md) separates recognition,
analysis, executable pairs, packaged engines and remaining acceptance. Signing is deferred.
The [combined 1.1.1 candidate](combined-office-candidate.md) packages all selected
engine families for isolated integration; that build does not establish release
adoption or complete Office fidelity and visible acceptance.

On 2026-09-16 the owner added HEIC/AVIF input to JPEG/PNG and AIFF/WMA input
to the existing audio targets to the required local candidate goal. These input
additions are now implemented in the optional
[expanded scratch payload](combined-expanded-inputs.md), with recorded image/audio
worker and copy-publication checks. They are absent from reserved candidate
1.1.1. The [launch matrix](launch-capability-matrix.md) defines admitted variants
and refusals; broader fidelity, human review, engine adoption and final integrated
acceptance remain open. ALAC remains a separate evaluation. The
[current completion plan](broad-file-support-goal.md#current-objective-and-completion-plan)
defines scope freeze, implementation, customer acceptance and combined packaging
milestones, including reviewed commits in both repositories.

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
ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã¢â‚¬Å“Context SuiteÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â parent menu:

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
requires a meaningful decisionÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Âsuch as choosing a matte for transparent PNG to
JPEG conversionÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Âopens the appropriate planning surface before processing.

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
The 2026-10-09 image expansion selects ICO and still SDR AVIF output, classic
TIFF to PNG/JPEG/PDF and static SVG to PNG, within tested variant boundaries.
GIF and camera RAW remain outside that scope. HEIC/AVIF input to JPEG/PNG is
required by the owner's 2026-09-16 scope decision and implemented in the expanded
scratch payload, subject to the declared variant and acceptance limits above.
Existing HEIF input support does not admit every HEIF-family variant or enable
HEIC output; the separately selected AVIF output remains planned. Decision 0019
supersedes the former image-release-before-audio ordering: broad Analyze, common
audio and agreed document capabilities are now part of the next launch goal.
See [broad file support](broad-file-support-goal.md) for that expansion's sequence;
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
