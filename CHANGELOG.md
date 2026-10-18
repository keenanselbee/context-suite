Context Suite Changelog
=======================

1.4.8 - Instant Technical details disclosure, 2026-10-16
-------------------------------------------------------

- Show and hide Technical details immediately, removing the sliding content and
  lingering scroll range when the Analyze window shrinks.
- Keep the themed full-width disclosure header, keyboard toggle, remembered
  state and existing window placement behavior.


1.4.7 - Keep one starting top for Analyze, 2026-10-16
---------------------------------------------------

- Open collapsed and expanded Analyze windows at the same top position, based
  on the expanded opening layout. Only their heights differ.
- Expand and collapse downward from that shared top, retaining free movement
  and resizing afterward.


1.4.6 - Resize Analyze when details are toggled, 2026-10-16
--------------------------------------------------------

- Shrink a normal Analyze window when Technical details closes and restore its
  previous expanded height when details reopen, keeping its position and width.
- Fit explicit expansion below the current top when space permits. Preserve
  maximized state and keep manual movement and resizing free of ongoing limits.


1.4.5 - Comfortable opening spacing for Analyze, 2026-10-16
---------------------------------------------------------

- Open expanded Analyze at 82% of the current monitor's usable height, with
  balanced space above and below; remembered collapsed details open compactly.
- Apply this geometry once, without ongoing move, resize, maximize, DPI or
  disclosure handlers that could override normal Windows handling.
- Keep long details scrollable and footer actions accessible.


1.4.4 - Initial Analyze sizing and revised icons, 2026-10-16
-----------------------------------------------

- Treat the taskbar work-area fit as an initial Analyze layout, releasing it
  when the owner starts moving, resizing or maximizing the window.
- Preserve the owner's subsequent position and size when Technical details
  opens or closes, rather than snapping the window back above the taskbar.
- Regenerate Windows application and taskbar icons from the owner's latest
  triangle PNG revisions, retaining their transparency.


1.4.3 - Updated Windows icons and window movement, 2026-10-16
------------------------------------------------------------

- Regenerate all Windows icon sizes from the owner's latest triangle PNGs,
  preserving transparency and the separate small-size artwork.
- Keep the post-layout taskbar bounds check for resizing and expansion without
  also forcing a position correction on every window move between monitors.


1.4.2 - Analyze details layout and preferences, 2026-10-16
---------------------------------------------------------

- Fit expanded Analyze windows into the space above the taskbar and recheck
  native bounds after layout, preserving scrolling and access to footer actions.
- Open Technical details by default and remember the user's open/closed choice
  across files, windows and application restarts.
- Put type-specific properties first, remove duplicate file facts and move
  supporting identification evidence to the end of Technical details.


1.4.1 - Streaming text line counts, 2026-10-16
---------------------------------------------

- Count recognized text beyond the 64 KiB identification prefix, using fixed-size
  buffers up to 256 MiB within the existing five-second read budget.
- Show exact totals when the complete file is counted; retain an accurate lower
  bound and inspected text-byte count when a limit or invalid tail stops reading.
- Preserve Unicode characters and CRLF endings across chunks, user cancellation,
  source-change checks, read-only behavior and the existing Windows artwork.


1.4.0 - Revised Windows triangle artwork, 2026-10-16
--------------------------------------------------

- Regenerate Windows icons from the owner's updated triangle PNGs, preserving
  the revised proportions, transparent surroundings and center opening.
- Retain the taskbar resource placement fix and the separate online-page artwork.
- This version rolls over from 1.3.9 under the project's version policy; it adds
  no compatibility change.


1.3.9 - Triangle-only Windows icons, 2026-10-16
----------------------------------------------

- Use the approved triangle-only PNGs for Windows application, taskbar, package
  and installer icons, preserving transparent surroundings and the center opening.
- Retain the emerald-background artwork for online pages and the corrected
  external resource index used by Windows taskbar icon selection.


1.3.8 - External taskbar resource lookup, 2026-10-16
--------------------------------------------------

- Stage the shared icon resource index in the application's external installation
  directory, where Windows resolves sparse-package visual resources.
- Check installed resources through Windows package lookup. The previous check
  only verified indexes beside registration manifests and missed this failure.
- Preserve the original PNG alpha, green tile, engine selection and app behavior.


1.3.7 - Transparent taskbar icon corners, 2026-10-15
---------------------------------------------------

- Remove the Analyze, Convert and Optimize package background colors that
  Windows painted behind transparent icon corners.
- Provide default, dark and light taskbar PNG resources at fourteen target
  sizes, retaining the approved green tile and the original source alpha.
- Generate package resource indexes for taskbar selection, and check package
  backgrounds and taskbar assets in addition to the executable ICO.
- Keep the existing engines and application behavior.


1.3.6 - Application icon, 2026-10-15
-----------------------------------

- Use the approved emerald folded-triangle artwork for the application,
  Windows package logos, installer and Start menu shortcut.
- Embed ten transparent icon sizes from 16 to 256 pixels, using the smaller
  source for compact views. Keep the distinct monochrome Explorer action icons.
- Include the 1.3.5 lecture PDF compatibility and expanding file-details UI,
  retaining the complete bundled engine selection and existing user settings.


1.3.5 - Consistent lecture PDF dark-mode copies, 2026-10-15
---------------------------------------------------------

- Recover bounded malformed JPEG/mask dimensions and drawing coordinates only
  when independent evidence establishes a consistent rendering interpretation.
  Preserve original documents, searchable text, geometry and document features.
- Handle separate transparent image regions and text over diagrams, including
  a disclosed original-color fallback when recoloring cannot preserve detail.
- Recover drawing operands across page content streams without changing shared
  attachment bytes. Refuse ambiguous numeric fragments and split literal text
  with an actionable source re-export message.
- Open Files and details automatically for conversion and let the file list use
  available window height. Distinguish source-recovery warnings from retained
  original-color images.
- Retain the complete bundled engine selection from 1.3.4. Prepare an unsigned
  internal candidate; installation and publication remain separate actions.


1.3.4 - Python-free bundled Office runtime, 2026-10-13
-----------------------------------------------------

- Keep Office conversion bundled using a source build configured without the
  Python runtime. Retain the matching service registry, Java deliverables,
  drawing resources, languages and Colibre theme.
- Save another 37.61 MiB of runtime files relative to 1.3.3. Preserve retained
  binary identities through the explicit SDK/theme reduction recipe.
- Bind the new source kit, exact source correspondence and dependency notices
  to the selected runtime. Reject Python components and stale Python notices
  in Python-free packages; retain earlier notice records for historical builds.
- Retain the current PDF dark-mode engine and existing Office fidelity limits,
  including native Word equation omissions. Prepare an unsigned internal
  candidate; installed 1.2.9 and reserved payloads remain unchanged.


1.3.3 - Smaller bundled Office runtime, 2026-10-12
------------------------------------------------

- Retain bundled Office conversion while excluding 19 alternative icon themes
  and 12 SDK development files. Keep Colibre, drawing resources, languages,
  Python, required notices and the matching original source kit.
- Reduce the Office runtime by 68.66 MiB with unchanged retained binaries.
  Record and verify the exact file exclusions and original build provenance.
- Include the latest PDF diagram-label readability changes, preserving source
  documents and the existing dark-mode admission limits.
- Remove successful release-archive verification extractions after validation;
  preserve failed extraction evidence and immutable prior release reservations.
- Prepare a new unsigned internal candidate. Installed 1.2.9 and reserved 1.3.2
  stay unchanged; no installation or publication is included.
- Office's existing active-content policy still omits native Word equations;
  this reduction does not add equation support or relax that policy.


1.3.2 - Broader lecture PDF compatibility, 2026-10-11
----------------------------------------------------

- Extend PDF (Dark mode) with bounded rounded panels, nested backgrounds, proved
  glyph and thin-outline overlap, and text partially hidden by later opaque cards.
- Preserve older direct links and null structure semantics; improve masked images,
  local photo overlays and validated image backings without flattening pages.
- Retain original screenshots when recoloring damages small labels or button text.
  Reject cases that would reveal originally hidden text; keep geometry and paint order.
- Report bounded affected-page diagnostics for unsupported or damaged PDFs.
- Validate five original COSC 310 lectures / 212 pages and regress the prior
  25-document / 746-page corpus. Intro, Requirements and the existing dark reference
  retain documented refusals; source repairs are not automatic.
- Prepare a fresh unsigned candidate using policy pdf-dark-7. Preserve installed
  1.2.9 and reserved 1.3.1; installation and release approval remain separate.


1.3.1 - Stable lecture PDF reading copies, 2026-10-11
----------------------------------------------------

- Preserve validated masked diagrams on unchanged paper, including rectangular
  clips, and classify thin rules consistently before and after recoloring.
- Avoid counting byte-identical image samples as another conversion. Add repeat
  checks for masked diagrams and thin accents on light/gray backgrounds.
- Supersede development candidate 1.3.0 after its earlier-lecture repeat check
  found these issues. Its reserved payload remains unchanged.


1.3.0 - Lecture PDF reading palette, 2026-10-11
----------------------------------------------

- Extend PDF (Dark mode) to validated tagged lecture documents, ordinary links,
  passive attachments and static optional-content layers, preserving their structure.
- Normalize navy paper and eligible tables, diagrams, gradients and translucent
  highlights; preserve photos, logos, searchable text, code and mathematical geometry.
- Validate actual glyph bounds and local contrast. Keep unsupported complex or
  protected content closed with concise, content-free failure categories.
- Use palette convergence for repeat-conversion no-ops and always publish a copy.
  Validate the 25-document, 746-page lecture corpus and the earlier 50-page lecture.
- Prepare an isolated unsigned development candidate; installation and release
  approval remain separate. This does not claim universal PDF support.


1.2.7 - PDF dark-mode reading copies, 2026-10-10
----------------------------------------------

- Add one PDF (Dark mode) Convert destination with local deterministic paint
  recoloring, searchable text and retained vector/page geometry.
- Always save a collision-safe dark-mode PDF copy, including when overwrite is
  enabled. Keep already-dark documents unchanged and isolate resources on mixed
  light/dark pages. Retain batch admission, cancellation and selective retry.
- Recolor admitted simple line art while preserving opaque unclassified imagery
  with disclosure. Enforce readable colored accents. Refuse ambiguous masks,
  contrast conflicts and unsupported protected, interactive or complex features.
  The complete lecture reference remains unsupported; no universal PDF or fully
  dark scanned-page support is claimed.
- Pin the independently built native host and its existing qpdf dependencies;
  add semantic, rendered-fidelity, failure-recovery and packaging guards.
- Package an isolated unsigned combined candidate and before/after review.
  Exact-candidate regression coverage and fresh extraction pass. Native UI and
  representative customer fidelity remain separate. Installed 1.2.5 and all
  earlier reserved payloads/receipts remain unchanged.


1.2.6 - Compatibility and audio extraction, 2026-10-09
----------------------------------------------------

- Accept validated JFIF as a JPEG input alias and extract the largest embedded
  ICO image to PNG with deterministic selection and retained transparency.
- Convert every PDF page to a numbered JPEG at 150 DPI, quality 90, on white
  paper; retry unfinished pages without replacing completed output.
- Export all displayed GIF frames as numbered PNGs in one collision-safe
  folder, including duplicate frames. Static GIF creates one PNG. Retry keeps
  completed frames; PNG output retains order but not animation timing.
- Add SVG-to-JPEG with proportional sizing, font notices, quality 90 and an
  explicit background choice.
- Extract supported MP4/MOV/WebM/MKV audio to WAV, MP3 or FLAC. Multiple audio
  tracks use a compact picker; retries retain the selected track and source
  identity. Require consent for surround downmix and chapter/artwork omission.
- Preserve originals for every new conversion, including when overwrite is
  enabled. Reuse existing destination labels/icons, one batch admission and
  bounded sequential execution. Keep existing image, audio, WEM and document
  engines, including the repaired WMA paths and Windows Voice provider.
- Package an isolated unsigned candidate with verified fresh extraction and
  exact-candidate regressions; provide prompt and output review artifacts.
  Engineering verification completed 2026-10-10. Native UI and owner acceptance
  remain separate. Installed 1.2.5, prior payloads and receipts remain unchanged.


1.2.5 - Image conversion expansion, 2026-10-09
--------------------------------------------

- Add ICO copies with automatic sizes, aspect-preserving fit and transparent
  padding, plus still SDR AVIF copies with fixed quality and retained alpha.
- Convert supported classic TIFF pages to numbered PNG/JPEG copies or one
  ordered PDF, with explicit JPEG background choices and selective page retry.
- Render bounded static SVG to PNG locally, with Automatic/custom width,
  proportional dimensions and necessary font substitution disclosure.
- Integrate distinct ICO/AVIF destination icons with the existing Compact set.
  TIFF and SVG reuse existing destination commands.
- Package pinned AVIF/SVG engines with independently sourced dependencies,
  licenses and build evidence. Retain source-preserving publication, bounded
  workers, batch admission and cancellation/retry safeguards.
- Prepare an isolated unsigned candidate for combined checks and owner review.
  Installation, native UI/fidelity acceptance and commercial release are separate;
  installed 1.2.3 and reserved baseline 1.2.4 remain unchanged.


1.2.4 - Combined icons and WEM baseline, 2026-10-09
-------------------------------------------------

- Compose the distinct Convert destination icons and bounded WEM decoder with
  the existing image, DDS, audio, HEIF, PDF and Office engines for isolated review.
- Encode supported PCM16/float32 WAV and MP3 using a compatible user-installed
  Wwise, with one batch quality decision and source-preserving publication.
- Recognize the optional WEM payload in the combined workflow verifier.
- Automated combined workflow, shell/icon and payload checks pass for this
  internal baseline; image expansion and human acceptance remain open.
  Installed version 1.2.3 and earlier payloads remain unchanged.


1.2.3 - Transparent Word figures and answer spacing, 2026-09-28
------------------------------------------------------------

- Preserve transparent paper around PDF checkmarks and figures without removing
  deliberately painted white details.
- Improve substituted Minion answer text widths; retain narrow-table fitting
  to avoid extra wrapping and shifted rows.
- Preserve source rule colors for printing. Word dark-mode line visibility
  remains a manual check; no dark-mode color override is introduced.
- Verify Word exports, 164 focused checks, 14 worker checks and 4,506 foundation
  contracts. Install locally with license, trial and settings unchanged.

1.2.2 - Native Word rules and paragraphs, 2026-09-28
--------------------------------------------------

- Replace simple PDF rules and long underscore blanks with editable Word lines,
  preserving their thickness and removing raster paper backgrounds.
- Group related prose, apply heading styles and reconstruct repeated footers.
  Keep positioned form content, editable tables and localized checkmark images.
- Verify blank and completed worksheets through Microsoft Word export, 152
  focused checks, 14 worker checks and 4,506 foundation contracts.
- Install locally with ordinary uncompressed copies; verify all three Explorer
  registrations and preserve license, trial and settings unchanged.

1.2.1 - Balanced Optimize icon, 2026-09-28
----------------------------------------

- Reduce the selected Optimize chevrons by 10% around their center, retaining
  the same icon box and proportions. Check native menu-size previews in both themes.
- Install locally with ordinary uncompressed copies; all three Explorer commands
  pass activation checks. Preserve license, trial and settings files unchanged.

1.2.0 - PDF-to-Word worksheet fidelity, 2026-09-28
------------------------------------------------

- Preserve filled worksheet text, answer rules, diagram labels and page positions
  in editable Word copies. Improve multiline table layout and font mapping.
- Preserve static stamp appearances as localized images; retain refusal for
  unsupported annotation content and actions. Report image fallback on completion.
- Verify the owner's three-page sample, all 42 editable table cells and seven
  checkmarks, plus 94 focused and 4,506 foundation checks.
- Installed locally using ordinary uncompressed copies. Verify all three Explorer
  commands and the installed PDF-to-Word worker; preserve license, trial and settings.

1.1.9 - Optimize chevrons, 2026-09-28
-------------------------------------

- Replace Optimize's arrows and centre bar with the owner's selected B chevrons:
  two solid inward shapes with slightly closer spacing. Match the approved
  mockup at small sizes in both themes.
- Install locally as an ordinary uncompressed copy, verify all three Explorer
  registrations and preserve license, trial and settings files unchanged.

1.1.8 - Clearer Optimize arrows, 2026-09-28
------------------------------------------

- Give Optimize smaller arrowheads, longer shafts and wider gaps around a shorter,
  thinner centre bar. Widen the glyph slightly for clearer small-size rendering.
- Install locally as an ordinary uncompressed copy after native icon and package
  verification. Preserve license, trial and settings files unchanged.

1.1.7 - Lighter Convert arrows, 2026-09-28
-----------------------------------------

- Make the Compact Convert arrow shafts slightly thinner, with matching lighter
  arrowheads. Preserve the overall width and direction of the glyph.
- Install locally after native icon and package verification; preserve license,
  trial and settings files unchanged.

1.1.6 - Windows icon encoding correction, 2026-09-28
--------------------------------------------------

- Encode Compact icons as explicit 32-bit DIB frames, preserving alpha without
  relying on display color depth. Verify 48 decoded frames during asset generation.
- Use eight icon sizes from 16 to 128 pixels. Install locally with all three
  Explorer commands resolving to the dark-menu icons on the owner's dark theme.
  Preserve license, trial and settings files unchanged. Native size/theme previews
  passed; live Explorer visual inspection was unavailable.
- Retain the 1.1.5 candidate: its structural checks passed, but native icon drawing
  exposed an encoding problem, so it was not installed.

1.1.5 - Compact Explorer icons, 2026-09-28
-----------------------------------------

- Replace colored letter tiles in Explorer with Compact solid glyphs: a document
  and magnifier, opposing conversion arrows, and inward compression arrows.
- Include light/dark variants and eight icon sizes from 16 to 256 pixels.
  Select appearance from Windows app theme or contrast-theme menu text.

1.1.4 - PDF conversion improvements, 2026-09-28
--------------------------------------------

- Add best-effort PDF-to-DOCX copies with editable text, ordinary tables and
  embedded images; retain complex figures as images and report unsupported input.
- Default PDF page PNGs to white, with optional transparency in Convert Settings.
- Filter Explorer targets using filename hints and use Manage license...
  consistently. Preserve prior payloads and reserved candidates.
- Install locally on the owner's request; keep license, trial and settings files
  unchanged and verify all three Explorer registrations.

1.1.3 - License purchase and management links, 2026-09-28
-------------------------------------------------------

- Add Purchase... and Manage license to the License window, opening the
  environment-specific Polar checkout and customer portal on explicit clicks.
- Enable owner-approved live checkout, retain key activation and pending work,
  and show a usable address if the browser cannot open. Checkout owns purchase
  and return instructions; trial dates and license storage remain unchanged.

1.1.2 - Unsigned expanded candidate preparation, 2026-09-23
----------------------------------------------------------

- Prepare one candidate with HEIC/AVIF to JPEG/PNG, AIFF/WMA to the existing
  six audio targets, and the separate Windows Voice provider.
- Include the reconstructed Office renderer, ordinary internal chart support,
  corrected shape resources, and tested recovery/resource/output protections.
  Retain refusals for unsupported charts, unsafe dates and external behavior.
- Add source-bound dependency notices and the offline notice index. Permit
  explicit compatible local HEIF/audio library replacements while preserving
  default native identities, media validation and copy publication.
- Isolate managed staging and reuse verified native engines. Preserve reserved
  1.1.1; exact-candidate suites, performance, human acceptance, network evidence
  and distribution/commerce decisions remain open. Signing stays deferred.


1.1.1 - Local combined candidate, 2026-09-16
-----------------------------------------

- Package the optional isolated Word/Excel/PowerPoint-to-PDF engine alongside
  image, audio and PDF engines for integrated local acceptance.
- Include document-specific failure guidance, reviewed font handling,
  calculated-date publication guards and corrected workbook capacity limits.
- Expand the catalog to 245 descriptions and 134 qualified MIME references;
  retain bounded analysis and copies by default.
- Keep Office date fidelity, network-isolation evidence, redistribution,
  listening, visible acceptance and commercial release gates open.


1.1.0 - Local candidate, 2026-09-14
---------------------------------

- Preserve short resampled audio using a bounded working extension and the
  original output interval. Validate duration independently of resampling.
- Retain ambiguous APK and RAR filename meanings in the reviewed 244-record
  catalog, without treating recognition as a conversion capability.
- Continue local audio/PDF evaluation. Required Office conversion, listening,
  visible acceptance and commercial release gates remain incomplete.


1.0.9 - Local candidate, 2026-09-14
---------------------------------

- Correct short MP3 duration declarations without changing encoded audio data.
  Keep exact decoded duration, metadata validation and safe copy publication.
- Retain a tiny-file automatic-format-detection compatibility gap for further
  player acceptance; this candidate does not close broad audio release gates.


1.0.8 - Local candidate, 2026-09-14
---------------------------------

- Convert recordings shorter than sixteen samples to FLAC without adding padding.
  Retain exact decoded samples and the existing metadata/publication safeguards.
- Report extra decoded output as a length-validation failure. Short MP3 padding
  remains an open defect; invalid output is not published.


1.0.7 - Local candidate, 2026-09-14
---------------------------------

- Preserve literal track, disc, language and copyright values in custom ID3
  text fields when they do not meet the corresponding typed frame's syntax.
  Keep valid typed fields and all sample, artwork and publication safeguards.
- Supersede candidate 1.0.6 while preserving its staged payload and receipt.


1.0.6 - Local candidate, 2026-09-14
---------------------------------

- Preserve supported Unicode descriptive tags when converting FLAC, MP3, M4A,
  Vorbis and Opus to WAV, including alongside artwork. Keep ordinary ASCII INFO
  fields and use explicit ID3 encoding for other admitted values.
- Validate the complete WAV output inventory even without artwork. Recognize
  both track-number INFO conventions while refusing conflicting values.
- Keep source files, exact decoded audio, copies by default and existing quality
  decisions. Reject unrepresentable text during conversion inspection.


1.0.5 - Local candidate, 2026-09-14
---------------------------------

- Preserve supported PNG/JPEG artwork when converting FLAC, MP3, M4A, Vorbis
  and Opus to WAV. Keep image bytes, picture fields, descriptive tags and exact
  decoded samples. Refuse picture metadata that ID3 cannot represent.
- Preserve the encoder's WAV language field and validate the complete output
  before publication. Copies and existing quality-consent policies remain unchanged.


1.0.4 - Local candidate, 2026-09-14
-----------------------------------

- Preserve supported embedded ID3 tags and PNG/JPEG artwork when converting WAV
  to FLAC, MP3, Vorbis, Opus and representable M4A. Retain picture bytes, labels,
  descriptions and order, source samples and required quality decisions.
- Keep duplicate metadata stores, unsupported chunks and malformed tags blocked.
  This adds WAV source handling; WAV artwork output remains unfinished.


1.0.3 - Local candidate, 2026-09-14
-----------------------------------

- Preserve supported PNG/JPEG artwork when converting FLAC, MP3, Vorbis and Opus
  to M4A. Keep image bytes and order, descriptive tags and validated audio.
  Refuse picture labels or descriptions that the M4A cover format cannot retain.
- Retain fast-start placement and repair audio chunk offsets after adding covers.
  Existing same-format no-op and required audio-quality prompts remain unchanged.


1.0.2 - Local candidate, 2026-09-14
-----------------------------------

- Apply PDF page scaling when converting to PNG at the fixed 150 DPI. Read each
  page's UserUnit through the selected qpdf parser and apply size limits to the
  scaled dimensions. Keep inherited crop/rotation behavior and original files.
- Package and verify qpdf's existing runtime dependencies with the PDF renderer.
  These remain optional local candidates; release adoption is separate.


1.0.1 - Local candidate, 2026-09-14
-----------------------------------

- Add PNG/JPEG cover preservation when converting FLAC, Vorbis, Opus and M4A to
  MP3. Retain picture bytes, type, description and order; refuse conflicting
  geometry, duplicate descriptions and unsupported metadata rather than changing
  them silently. Audio quality decisions remain explicit.
- Verify Word, Excel and PowerPoint PDF export interruption and same-profile
  recovery in the isolated evaluation harness. The customer Office converter
  and filesystem/network isolation remain incomplete.
- Establish `Version.props` as the product-version source. Managed assembly/file
  versions and the three package templates use its four-component Windows form.
  Candidate staging reserves each version before building and retains its receipt
  and inventory identity. Installation and registration are separate actions.


Earlier development snapshots
-----------------------------

Earlier managed development outputs used the SDK's implicit 1.0.0 version; the
three shell prototype manifests used 0.1.0.0. Multiple historical candidate
payloads were distinguished by staging GUID and inventory. Their directories,
inventories, test evidence and Git history remain unchanged. They are historical
development snapshots, not a claim that one released version had those differing
contents. Detailed feature history remains in Git and the linked goal documents.

The [broad support goal](docs/broad-file-support-goal.md) records completed work
and remaining acceptance. Signing, installer lifecycle and live commerce remain
separate release gates.
