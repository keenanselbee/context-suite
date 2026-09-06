Context Suite Product Design
============================

Status: initial product direction with a working shell-integration prototype;
media analysis and transformation are not implemented yet.


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


Release Sequence
----------------

The intended vertical slices are:

1. DDS analysis, including DX10 and legacy header distinctions.
2. Lossless PNG optimization with measured before-and-after results.
3. Bounded lossy PNG optimization with representative visual fixtures.
4. PNG, JPEG, and WebP conversion with transparency and metadata handling.
5. Common image analysis.
6. Common audio analysis and conversion.
7. Additional formats supported by demonstrated use cases and reliable tests.

Each slice should work from Explorer through validation before the next format
family substantially expands the product surface.


Portfolio And Commercial Product
--------------------------------

Context Suite is both a co-op portfolio project and a potential paid utility.
Most engineering is intended to be publicly reviewable. The complete production
application requires private implementations from `context-suite-private`,
checked out under the public repository's ignored `proprietary/` directory.

One planned production application build requires the private checkout; a
public checkout alone cannot build the complete app. There is no separate
review/demo edition. Portfolio presentation uses public code, architecture,
tests, and planned screenshots and demo video; a downloadable commercial trial
will serve people who want to run it. Public components may be tested separately
where their dependencies permit. The current public build is only the shell
prototype, not the production application. See
[decision 0005](decisions/0005-public-and-proprietary-builds.md).

The commercial direction is a three-day trial followed by website login and
purchase validation. Login alone does not establish a purchase. All media
processing stays local; account services do not receive selected paths or media
contents. Access is checked by the application before work starts, never while
Explorer constructs a menu. An admitted batch may finish after trial expiry,
and its results remain available.

Trial start timing and exact elapsed-time rules remain open. Browser activation
with a signed offline license is proposed; license lifetime, refresh, service
outages, recovery, and post-trial feature availability require explicit policies.
Pricing and license terms are also undecided. Keep protection modest and accept
that determined users may reset local trials or modify binaries. See
[decision 0006](decisions/0006-trial-and-purchase-access.md).


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
- Automatic deletion of source files.
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
