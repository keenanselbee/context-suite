Context Suite Product Design
============================

Status: shell/process and output-safety foundations implemented; PNG/JPEG/WebP/BMP/TGA
conversion has passed its bounded local acceptance matrix. Analysis, optimization and paid activation
remain planned.


Product Summary
---------------

Context Suite is a local Windows application for understanding, converting, and
optimizing media files from File Explorer. It serves people who know what they
need to accomplish but may not know which codec, quality setting, or
compatibility tradeoff applies.

The product promise is:

> Understand and prepare media files without hidden quality, metadata, or
> compatibility surprises.


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
- Recommend actions, but keep irreversible consequences visible until execution.
- Preserve source files by default.
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

Explorer code must remain responsive. It performs only bounded capability work
and delegates analysis, planning, conversion, optimization, validation, and
result presentation to an out-of-process host.

Convert and Optimize each end with a separated **Settings...** action opening
their section of one shared settings window. Quick actions preserve originals;
replacement requires an explicit planning choice and confirmation.

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
for naming, settings snapshots, consent, and the required Windows verification.


Release Sequence
----------------

The accepted first image-release format target is PNG, JPEG, WebP, DDS, TGA,
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
import require separate scope and redistribution decisions. Audio follows the
image release rather than expanding its format target.


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

The commercial direction is a three-day trial followed by Polar license-key
activation. Hosted checkout and the customer portal replace custom website
accounts. A checkout redirect alone does not establish paid access. All media
processing stays local; account services do not receive selected paths or media
contents. Access is checked by the application before work starts, never while
Explorer constructs a menu. An admitted batch may finish after trial expiry,
and its results remain available.

The local trial starts at the first confirmed valid conversion and lasts 72
elapsed hours; [decision 0009](decisions/0009-first-image-engine-and-trial.md)
defines clock and failure handling. Browser activation
is replaced by in-app key activation; paid offline grace, refresh, service
outages, recovery, and post-trial feature availability require explicit policies.
Polar is selected; pricing and license terms are still undecided. See the
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
- PDF or Office conversion.
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
