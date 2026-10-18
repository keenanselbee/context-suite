PDF Dark Palette Support for Lecture Documents
=============================================

Historical evidence relocation (2026-10-13): paths marked archived below now
reside under `Z:\Backup\Context Suite\workspace-history\20261013\payload`,
using the same repository-relative paths. Follow the
[workspace recovery instructions](repository-size-reduction-goal.md#uncompressed-workspace-relocation-2026-10-13)
before rerunning historical checks.

Current goal: completed in verified candidate 1.3.5 on 2026-10-15. The four
original rejected COSC 310 lecture PDFs and the regression corpus export through
the actual worker and publication path. Installed 1.3.4 remains unchanged;
installation and publication are separate actions. Earlier milestones below
remain historical evidence.


Consistent Export Follow-up
--------------------------

Required original inputs are Lecture 1 - INTRO.pdf, Lecture 5 - Requirements.pdf,
Lecture 8 - Software Modeling-2.pdf and Lecture 9 - Introduction to software
Architecture.pdf in the owner's COSC 310 Lecture directory. Work from disposable
copies and preserve original hashes. Do not solve the corpus with filename,
course, source-hash or page-number exceptions.

Implement general handling for disjoint transparent-image regions and text
over recolored diagrams. Investigate malformed image/mask declarations and
drawing coordinates against independent renderers, and admit a bounded recovery
only when appearance and semantics can be established. A reader's ability to
display a damaged PDF alone is insufficient evidence for an automatic repair.
Preserving a validated original-color diagram region is an acceptable reading
copy fallback with the existing disclosure; silently flattening pages, dropping
content, weakening contrast checks or removing meaningful features is not.

Execution and acceptance checklist:

- [x] Reproduce the four installed-engine failures and record source identities,
  affected pages, refusal categories and unchanged-original checks.
- [x] Add independently authored positive and negative fixtures for separate
  alpha regions, overlapping paint, image/text contrast, inconsistent image/mask
  dimensions and malformed path coordinates. Keep lecture files private.
- [x] Implement and validate separate alpha-region placement/backing support
  in the isolated native host; combined-worker release checks remain below.
- [x] Implement and validate glyph/composite contrast handling and a safe
  original-color region fallback where appropriate.
- [x] Resolve the damaged-input cases with a general validated interpretation
  or document the exact unresolved ambiguity and required source correction.
  Do not count a refusal or a repaired substitute as support for the original.
- [x] Convert all four complete original PDFs through the actual worker and
  application publication path to collision-safe dark-mode copies. Confirm that
  every page is present, readable and preserves its intended content.
- [x] Verify repeat-conversion no-ops, searchable text, glyph positions, geometry,
  images/masks, metadata, links, attachments, layers and applicable structure.
  Review independent rendered comparisons of every affected page.
- [x] Regress the existing supported lecture corpus and adversarial fixtures,
  including cancellation, failure cleanup, selective retries and original hashes.
- [x] Include the pending automatic, height-filling Files and details UI in a
  fresh versioned candidate, with current worker, engine and payload checks.
  Preserve reserved releases and user settings; installation and publication
  follow their existing authorization boundaries.

Completion requires demonstrated exports for the four original inputs and
regression evidence for the general policies. If an original remains genuinely
ambiguous, report that limitation explicitly and keep that acceptance item open;
do not declare the entire support goal complete based only on clearer errors.


Earlier Lecture Extension
-------------------------

Historical status: implemented and verified in isolated candidate 1.3.1, 2026-10-11.
The `pdf-dark-6` lecture extension is complete within the approved corpus scope.
All 25 topic PDFs (746 pages) pass native conversion, real-worker publication and
repeat-conversion no-op checks; all originals remain unchanged. Independent
structure and layer review and all-page rendered review pass. Installed 1.2.9
and its `pdf-dark-5` engine remain unchanged.

Extend **Convert > PDF (Dark mode)** to normalize light and already-dark lecture
documents to Context Suite's reading palette. Support the document features in
the owner's 25-PDF topic corpus through general policies, without file-name,
course-name or source-hash exceptions. Preserve searchable text, geometry,
accessibility structure, ordinary links, passive attachments and supported
layer behavior. Create validated copies and keep the existing direct-command UI.


Corpus findings
---------------

The local survey contains 25 PDFs and 746 pages, with 14 to 63 pages per PDF.
Every document is rejected by installed `pdf-dark-5` at its tagged-PDF guard.
Further structure inspection identified:

| Feature | Documents | Implication |
| --- | --- | --- |
| Accessibility structure | 25 | Preserve tags, reading order, role mappings and references to page content |
| Optional-content layer metadata | 21 | Inspect actual layer use and preserve visibility/configuration semantics |
| Font bounds beyond the current two-em limit | 18 | Handle real font metrics without changing source fonts or truncating bounds |
| Link annotations | 6 | Preserve ordinary URI/internal destinations and visible link appearances |
| File-attachment annotations | 6 | Preserve attachment contents, names and annotation placement without opening them |

The inspected layer examples describe a Background group used by marked page
content. Confirm the remaining layer configurations before choosing the admitted
subset. Larger font declarations include Times New Roman, Cambria Math, Tahoma
and Webdings; changing the limit only for Times New Roman would be insufficient.

Sampled renders show navy slide paper, white body text, yellow/green emphasis,
SQL and other code, mathematical notation, bright tables, diagrams, screenshots,
photographic covers and logos. The initial relational-model diagnostic would
skip every page as already dark after two gates were isolated in memory; that
experiment is not valid conversion evidence. Removing tags or editing font
metrics is not an implementation strategy.

The local inventory (`.codex-temp/pdf-topic-survey-s0wv4xss/inventory.json`, archived)
records source hashes, installed-engine identity, first refusals and a separately
labelled in-memory tag-gate diagnostic. The
catalog survey (`.codex-temp/pdf-topic-survey-s0wv4xss/catalog-details.json`, archived)
records layer and annotation observations. Original hashes were checked after
inspection. Local renders and corpus identities remain ignored review material;
do not redistribute the lecture PDFs, their attachments or screenshots as tests.
The surrounding programming examples are outside this PDF goal.


Implementation checkpoint
-------------------------

The combined native build is `pdf-dark-build-16f3aff058124181a941e06047b6fce8`,
policy `pdf-dark-6`, host SHA256
`C041A152487EDB4A2330B783C53F92BBA0CAE4DEB0B90ED977EFA679AE6648D1`.
Its [native replay](../.codex-temp/pdf-lecture-replay34/results.json) and
real-worker replay (`.codex-temp/pdf-worker131-corpus/results.json`, archived) pass all 25
conversions, all 746 pages, repeat no-ops and original-hash checks. Actual worker
workflow/publication/failure suites also pass, including live cancellation,
selective retry and cleanup after publication failure.

Implemented native policies include bounded structure/layer preservation,
embedded and exact locally matched font bounds, validated composite glyphs,
palette convergence, axial shading, ordinary translucent panels, and local
image/diagram placement checks. Smooth curves and actual glyph footprints avoid
false overlap refusals without changing document geometry. Compound arrow
annotations retain their shape and must retain contrast with Form text.

Complex transparent images may retain a matching opaque underlay. Opaque
screenshots may retain connected cross-edge annotations after local placement
and paint checks. These preserved regions are not counted as recolored diagrams;
existing poor contrast inside screenshots is not claimed to be repaired.
Lossless stream wrappers do not change JPEG diagram classification or preserved
photo bytes. Identity mask Decode arrays are admitted; inverted masks remain closed.

Twenty native suites pass on the final engine, including structure, font/glyph
bounds, forms, clipping, shading, opacity, convergence, diagrams and retained images.
Retained-image coverage includes 41 cases; diagram coverage includes 18 checks
with direct-DCT versus ASCII85-wrapped JPEG equivalence. Font negatives include
cycles, invalid indices, unsupported point attachment and understated bounds.
The private diagnostic suite passes 66 bounded, content-free mapping checks.

The all-page gallery (`.codex-temp/pdf-lecture-visual25/index.html`, archived) covers all
746 pages. The [independent audit](../.codex-temp/pdf-lecture-audit34/results.json)
binds every converted render pixel-for-pixel to the current native output and
compares tags, reading order, links, passive attachment bytes, bookmarks, metadata
and layer configuration. It checks 1,264 on/off page-state renders across the
21 layered documents. No structural or layer-state differences were found.
All pages received rendered overview review, with enlarged risky examples.
The [review record](../.codex-temp/pdf-lecture-audit34/visual-review.json) records
scope and limitations; it is engineering review, not owner acceptance.

An independent compositing calculation over all 90 translucent text panels found
a minimum nominal contrast of 4.6058:1. Original thin fonts and overlay opacity
remain intact; thumbnail antialiasing can make those strokes look dim. This is
not a claim of full-document accessibility compliance or OCR repair.
The earlier [50-page lecture](../.codex-temp/pdf-prior-lecture34/index.html)
passes independent text/glyph/path/mask checks and rendered review. Three
unclassified images intentionally retain their original appearance.

The full 1.3.1 candidate is built with all existing engines and has a verified
version receipt. Its exact worker passes all 25 corpus documents plus the earlier
50-page lecture, including mandatory-copy publication and repeat no-ops. The
[final candidate audit](../.codex-temp/pdf-candidate131-audit/results.json)
verifies all topic-page renders, logical structure and supported layer states.
All 50 earlier-lecture candidate renders also match the accepted review.

The [review handoff](../.codex-temp/pdf-lecture-final-review/index.html) links the
actual worker-produced copies and page galleries. Its
[verification record](../.codex-temp/pdf-lecture-final-review/verification.json)
binds engine identity, inventory and test evidence. The reserved stage is
`artifacts/production-staging/5eb53b6ee0e84ee4bf47640042faa839`; all 4,271 inventoried
payload files were hash-verified after testing. Exact-worker tests pass 51 public,
14 workflow, 9 cancellation/failure and 156 supplied-document checks. Public
foundation passes 4,893 contracts; bounded diagnostics pass 66 checks; PDF payload
guards pass 14 checks. Shell contracts pass 880 conversion and 86 Optimize/Analyze
availability checks plus COM activation batches. Source-boundary and documentation
checks pass. These are engineering results, not desktop or commercial acceptance.

Candidate 1.3.0 remains reserved and unchanged: its regression run exposed
repeat-conversion issues in the earlier lecture. The fix preserves validated
masked diagrams on unchanged paper, including rectangular clips, and makes thin
rule classification depend on geometry/paint relationships instead of changing
source contrast. Identical image samples no longer count as another conversion.
The earlier lecture now converts all 50 pages and repeats with zero output bytes.
Authored diagram/transparency suites and white/gray-paper rule fixtures enforce
that behavior. Native desktop and owner acceptance remain separate.


Implemented support boundaries
------------------------------

| Feature | Admitted behavior | Boundary |
| --- | --- | --- |
| Tags and reading order | Preserve logical structure, role mappings, MCIDs and ParentTree relationships | Validate bounded graphs; preserve existing admitted defects without claiming tag repair |
| Links and attachments | Keep ordinary URI/internal links, bookmark destinations and passive embedded bytes | No scripts, launch actions or attachment execution |
| Layers | Preserve validated static optional-content groups and configurations | Unsupported conditional/interactive layer behavior remains refused |
| Fonts | Use embedded TrueType glyph bounds or exact local PostScript font matches with width checks | No substitution, clamping or guessed glyph outlines |
| Gradients and opacity | Bounded axial RGB shading and locally proved ordinary translucent fills | No generic blending, image-backdrop or intersecting-text exception |
| Images | Recolor eligible diagrams; retain photos, logos and validated image regions with their alpha | No blanket inversion, AI, OCR or whole-page raster fallback |
| Repeated conversion | Revalidate target-palette content and return a no-op without publishing another copy | No producer-marker shortcut or whole-dark-page bypass |
| Errors | Six static PDF-specific feature categories plus existing invalid/resource/engine failures | Whitelisted bounded diagnostics never forward document text or native paths |


User-visible behavior
---------------------

- Retain the single **PDF (Dark mode)** destination and its existing icon. Do not
  add a second dark-mode menu, a routine palette prompt or an editing workspace.
- Normalize identified page paper, including navy backgrounds, to near-black;
  use readable off-white text and preserve meaningful accent-color distinctions.
  Colored data regions must not be mistaken for page paper.
- Treat embedded white tables and diagrams locally, changing paper and ink
  together where validated. Preserve photographic regions, logos and uncertain
  images. Preserve readable labels over translucent photographic overlays.
- Keep text, code spacing, math symbols, small labels, line widths, arrowheads
  and table boundaries intact. No OCR, AI reconstruction or whole-page raster
  fallback is part of this goal.
- Skip only documents that already satisfy the target appearance policy. A
  successful no-change result says **Already uses the dark palette**. A navy
  document is not automatically a no-op merely because it is dark.
- A second conversion of a generated copy must be a no-op with no palette drift,
  duplicate backgrounds, growing resource trees or additional output copy.
- Preserve originals even when overwrite is enabled. Keep shared admission,
  sequential work, cancellation, collisions and selective retry. Necessary
  unsupported-feature results name the actionable limitation, not a generic error.


Implementation sequence
-----------------------

1. **Freeze the corpus and define acceptance.** Extend the opt-in review tooling
   to inventory every page's fonts, layers, marked content, links, attachments,
   images and transparency. Record baseline text, geometry, bookmarks, tag
   relationships and passive payload digests. Select representative pages for
   large-scale inspection and retain an all-page gallery. Add independently
   authored fixtures for each feature; private lecture paths are optional inputs.
   Finish the feature inventory before declaring its compatibility boundary.

2. **Preserve tagged structure and annotations.** Implement bounded validation
   of the structure tree, parent tree, MCIDs, role mappings, alternate text and
   page/form/annotation references. Preserve logical relationships when streams
   or shared resources are cloned; added decorative paper must be an artifact.
   Validate the output independently rather than treating retained dictionaries
   as proof. Add supported URI/internal-link annotations and passive file
   attachments, preserving contents and metadata. Do not follow links, launch
   attachments or execute document actions during inspection/conversion. Retain
   refusal for unsupported active actions, encryption and signatures.

3. **Support the observed layer and font variants.** Admit the reviewed static
   optional-content configurations and retain layer identities, membership,
   default visibility and alternate visibility behavior. Ensure inserted paper
   does not cover content when a layer is toggled. Refuse unsupported conditional
   visibility rather than flattening it. Replace the arbitrary two-em rejection
   with finite, bounded, validated metrics and conservative transformed text
   extents; assess actual glyph bounds where needed. Do not clamp metrics or
   substitute fonts. Cover math, symbol fonts and nested forms in the fixtures.

4. **Normalize already-dark pages.** Replace whole-page dark skipping with a
   region-aware target-palette decision. Map page paper, text, colored emphasis,
   rules and filled regions using local background relationships. Preserve
   semantic color differences and readable black/white text on colored panels.
   Establish convergence/idempotence through content and rendered evidence;
   an unverified producer marker is not sufficient to skip checks.

5. **Handle diagrams and transparency.** Extend the existing bounded diagram
   policies to white panels on dark slides, tables, code screenshots and mixed
   vector/image figures. Preserve uncertain/photo regions. Support the observed
   ordinary alpha/blend cases only with composited contrast validation; inspect
   resources actually used by changed regions. Do not infer generic transparency
   support from unused resources or pages the old engine skipped. Review covers,
   masked logos, faint code, thin arrows and tiny table text at reading size and
   enlarged size. Keep ambiguous mixtures explicit.

6. **Integrate worker validation and results.** Update the policy identifier,
   protocol/facts as necessary, adapter and independently validated output
   contract. Keep shell eligibility cheap and unchanged. Replace blanket tagged,
   annotation and layer bans only for the positively validated new subsets.
   Distinguish normalized, already-in-palette, preserved-region and unsupported
   outcomes without adding routine prompts. Preserve originals and existing
   retry semantics. Keep diagnostics bounded and content-free.

7. **Verify the complete corpus and regressions.** Run all 25 documents through
   the real worker into repository-local disposable copies. Inspect all 746
   rendered page comparisons, with larger views for risky regions. Verify text,
   glyph positions, paths, page geometry, tag relationships, bookmarks, links,
   attachment payload hashes and layer configurations. Exercise supported layer
   states and repeated conversion. Repeat the earlier 50-page lecture and the
   relevant dark-mode, diagram, transparency, interruption, publication and
   mixed-batch regressions. Use malformed and unsupported fixtures to retain
   bounded behavior and honest refusals.

8. **Prepare the integrated candidate and review handoff.** Build and pin the
   revised native engine independently of reference projects. Compose a fresh
   verified production candidate with all existing engines, using the established
   version-reservation and packaging workflow. Bind final evidence to that exact
   worker/payload. Provide a corpus result matrix and before/after gallery; update
   capability and design documentation with actual supported bounds. Installation,
   signing, publication, commits and live commerce are not part of this goal.


Completion criteria
-------------------

- All 25 current corpus PDFs complete through the actual candidate worker with
  useful dark-palette normalization of eligible lecture regions. Preserved photos
  and uncertain image regions are explicit and are not counted as converted dark
  content. Merely removing a refusal or returning 25 no-ops does not meet the goal.
- All 746 pages retain searchable text, glyph placement, vector geometry, page
  order and meaningful content. Palette changes are visually reviewed; any
  structural comparator exclusions are narrow and independently justified.
- Reading order and tag references, URI/internal links, passive attachments,
  bookmarks and admitted layer behavior survive. No silent stripping, flattening
  or font substitution is permitted. Original source hashes remain unchanged.
- Every output passes repeat-conversion no-op checks and validated publication.
  Cancellation, worker failure, source changes, collisions and selective retry
  retain their existing guarantees.
- Authored fixtures demonstrate general support beyond the owner's filenames,
  plus negative controls for broken structures, active content, unsupported
  blending/layers and excessive resources.
- The final candidate has verified engine pins/inventory and exact-worker
  evidence. Human desktop/accessibility acceptance is reported separately; it is
  not inferred from hidden renders or engineering checks.
- If an unexpected corpus feature cannot be supported faithfully, record the
  exact remaining case and keep the goal incomplete rather than silently reducing
  scope or weakening preservation checks.


Difficulty and boundaries
-------------------------

This is a substantial, high-difficulty extension. Palette mapping itself is
smaller than preserving tagged structures, layer behavior and annotation content
through resource rewriting. The stages above allow those risks to be resolved
before tuning appearance. Time estimates should be refined after the complete
feature inventory and the first tagged/layered end-to-end fixture.

The selected corpus defines concrete acceptance, not a promise of arbitrary PDF
support. General form editing, signature rewriting, decryption, script execution,
attachment extraction, OCR and cloud processing remain outside scope. Existing
limits remain until measured corpus evidence justifies a bounded adjustment.


COSC 310 compatibility follow-up, 2026-10-11
------------------------------------------

Status: implemented and verified in isolated candidate 1.3.2. This follow-up uses `pdf-dark-7` and does not change installed
1.2.9 or the reserved 1.3.1 payload described above.

The eight-PDF lecture corpus contains 361 pages. Five original documents now
convert and repeat without another rewrite: Software Engineering Process (50),
User Stories (33), Software Modeling (50), Python/Git (36), and FastAPI (43).
All 25 COSC 304 documents / 746 pages pass native regression and repeat checks.
These are bounded engineering results, not universal PDF support.

The implementation adds conservative convex rounded/pill interiors, nested
opaque backgrounds, measured glyph crossing and globally checked thin border
colors. Earlier text partially hidden by later cards needs a proved original
panel or paper backdrop, final fill/stroke validation, unchanged paint order,
and contrast on every exposed background. Near-hidden source text remains
refused, including on already-dark palette panels. The tests include independent
hidden-text cases that caught and corrected intermediate unsafe admissions.

Older direct link dictionaries retain destination and placement semantics under
version-specific admission. Null normalization avoids false structure mismatches;
non-null tags, links, passive attachments and supported layers remain checked.
Image handling adds exact black-matte normalization, bounded local photo overlays,
validated white/colored image backings and conservative masked-image placement.
Opaque screenshot recoloring falls back to original encoded imagery when local
edge tests detect lost labels, white button text or amplified JPEG noise. Small
blue links are distinct from broad colored button surfaces. These tests are
bounded ambiguity detectors, not OCR or a proof of every raster label's legibility.
Retained screenshot backgrounds can therefore remain light.

Failures now carry bounded static categories and canonical page numbers through
the worker and application. Unsupported content is distinct from damaged input;
resource failures do not invent a page number. Diagnostics never echo document
text or arbitrary native stderr. Conversion still uses the single PDF (Dark mode)
action and mandatory validated copies, with no cloud service, AI reconstruction,
page rasterization, original modification or new user settings.

Remaining whole-document refusals:

| Original | First refused page | Evidence and next action |
| --- | --- | --- |
| Intro | 37 | JPEG dimensions and mask length disagree. Re-export from the authoring source. A disposable dimension correction can convert, but intended source appearance is not established; no automatic repair. |
| Requirements | 2 | Malformed path coordinates also occur on pages 3?4. A disposable normalization matches two renderer interpretations but does not prove author intent; re-export. |
| Existing Software Modeling Dark Mode reference | 19 | Text touches low-contrast ellipse outlines. One other collision needs more precise curve/glyph geometry, which does not resolve the real conflicts. Remains unsupported, not an already-dark no-op. The original light Software Modeling lecture converts. |

The source-repair assessment preserves the original PDFs and clearly separates
scratch repairs from original-document acceptance. Screenshots, private lecture
files and reference assets are not shipped as automated test fixtures.

Native evidence and galleries currently live in the ignored local directories
`cosc310-suites42`, `cosc310-dark-build42`, `cosc304-cosc310-regression42`,
`cosc310-fastapi-build40-review`, and `cosc310-build39-changed-review` under
`.codex-temp`. The final review gallery (`.codex-temp/cosc310-final-review/index.html`, archived) links
all successful COSC 310 page comparisons and actual-worker copies. Its
verification record (`.codex-temp/cosc310-final-review/verification.json`, archived) binds
the native engine, exact staged worker, original hashes and independent audit.

Candidate 1.3.2 is `artifacts/production-staging/a3c5ae406f6447a28fabcb9ebbb106b7`.
All 4,271 payload hashes match inventory SHA256
`29F0F1C75367E2F3A1576B463CE435DEA864D4C0FB78667887EB71E0220C0527`.
The reserved 1.3.1 inventory and all 4,271 files also still match. Native host
SHA256 is `EEAA2A718B27B3F86693E3D820E10A910345261E096794157A542977C4B83C6C`.

All 30 successful originals / 958 pages pass actual-worker publication and
repeat checks. The three original refusals pass typed category/page/message,
unchanged-source and no-partial-output checks. Native tests pass 36 suites;
managed verification passes 4,893 foundation, 147 private diagnostic, 51 public,
14 workflow and 29 failure/cancellation checks, plus 180 supplied-success and
9 supplied-refusal checks. Fourteen packaging/tamper guards pass.

The [independent worker audit](../.codex-temp/preservation-audit-worker132/summary.json)
checks all 958 page renders against native output, root/page semantics, links,
tags, passive attachments, image dimensions/masks and 1,264 alternate-layer-state
page checks. Original vector geometry remains; five pages add only the validated
white image backings. All 212 current COSC 310 renders match reviewed output.
Only one prior COSC 304 gallery page changes: Relational Model page 6 now retains
the source screenshot, avoiding unreadable pale column-label text. That changed
page was reviewed, and the other 745 page renders remain identical.

Installed 1.2.9 remains unchanged. This is an unsigned engineering candidate,
not installation, signing, publication, owner approval or a universal PDF fidelity
claim. No automatic repair, AI reconstruction or page rasterization was added.


Waterfall Text Readability Follow-up
-----------------------------------

The source implementation now smooths weak JPEG chroma at neutral raster-letter
edges after reconstructing genuine colored antialiasing. This removes the
full-brightness speckling in the Software Engineering Process lecture's page 13
Waterfall diagram. Image resolution, labels, placement and alpha are preserved;
the source bitmap still limits the sharpness of the result.

A bounded post-map check retains the original image when repeated nearly equal
paper/ink samples acquire large RGB jumps. Proven colored-panel samples are
excluded because their ink/fill role transitions are legitimate. The check
also respects exact masks; it is not OCR or a general legibility guarantee.

The new independently authored neutral-edge regression fails on the prior host
and passes on the updated host. All 37 native suites and 147 private diagnostic
checks pass. All 30 previously supported originals / 958 pages convert and
repeat without another output; the three original refusals remain unchanged.
The preservation audit confirms original metadata, text, glyph positions, image
dimensions and masks, with 1,264 alternate-layer-state checks. Vector geometry
matches the prior accepted output on all 958 pages, including the same five
previously validated image backings. Twenty changed page renders were reviewed;
the other 938 match the prior host's renders.

The source pin and managed adapter select native host SHA256
`E76FDBFAFE6EB542F4B5AC17978D979E6CD10E2C9875B3F7F58A72ADC421FD04`.
Local evidence is under `.codex-temp/waterfall-suites6`, `waterfall310-6`,
`waterfall304-6`, and `waterfall-audit6`. The
[Waterfall comparison](../.codex-temp/waterfall-readability-assessment/index.html)
links the updated 50-page reading copy and full-page review.

This is a verified source follow-up, not a new combined application candidate.
Reserved candidates 1.3.1/1.3.2 and installed 1.2.9 were not replaced. The next
packaged candidate requires a new version and actual-worker release checks.


Installed 1.3.4 Rejection Investigation (2026-10-15)
--------------------------------------------------

The installed engine reproduces four COSC 310 lecture refusals on disposable
copies. Inspection and conversion agree on the first affected page, return no
PDF output, and leave every original hash unchanged. Evidence is retained in
`.codex-temp/pdf-dark-134-rejections/results.json`.

| Original | First page | Reason and support path |
| --- | --- | --- |
| Lecture 1 - INTRO.pdf | 37 | Image and soft-mask declarations disagree with the JPEG frame and exact decoded mask length. A bounded intrinsic-dimension recovery now has independent evidence; it is not implemented yet. |
| Lecture 5 - Requirements.pdf | 2 | Invalid path coordinates, also observed on pages 3-4. Prefer re-export; the prior normalized copy subsequently failed a page-13 contrast check. |
| Lecture 8 - Software Modeling-2.pdf | 9 | Orange vector text over a recolored diagram fails local contrast validation. Support requires a more precise glyph/composite contrast proof or a validated retained light diagram region. |
| Lecture 9 - Introduction to software Architecture.pdf | 3 | A wide masked image contains separate visible regions whose combined bounding box overlaps another card. Support requires separate alpha-region placement and backing checks. |

For Architecture, the actual painted regions are disjoint. Tracking multiple
bounded alpha-support regions, including interpolation margins, could permit
local source-color backings while preserving original JPEG and mask data. A
single backing across the whole image box would alter the transparent gaps.
For Modeling-2, weakening the contrast threshold or ignoring the overlay could
publish unreadable text; validation must cover the actual composited result.

Prior independent PDFium/MuPDF repair evaluation is retained under
`.codex-temp/cosc310-source-repair-evaluation`. The newer 1.3.4 recheck supersedes
the earlier Intro follow-up refusal: correcting its four image/mask dimensions
to the JPEG's 270 by 260 frame produces a full 38-page export and repeat no-op.
JPEG and mask bytes remain unchanged. The decoded mask has exactly 70,200
samples, agreeing with that frame. Both readers visibly repair the original
striped image, so this is damaged-source recovery, not pixel-identical source
preservation; any implementation must disclose it and verify only the recorded
normalizations. Evidence: `.codex-temp/pdf-dark-134-rejections/intro-intrinsic-proof.json`
and `repaired-probe.json` in the same directory.

Treating Requirements' malformed all-zero coordinate tokens as zero produces
identical page 2-4 pixels in both PDFium and MuPDF. Their broader malformed-number
interpretations differ, so a recovery must remain narrowly tokenized and scoped
to numeric drawing operands. The normalized full document still needs its
page-13 contrast issue resolved. Evidence: `requirements-render-equivalence.json`
and `numeric-reader-probe.json` in the rejection investigation directory.

Those were the baseline support paths. Native implementation progress is
recorded below; every extension still needs complete-document conversion,
repeat no-op, structural preservation, independent page rendering and
existing-corpus regression checks. No source PDF, installed engine or reserved
release was modified.


Native Consistent-Export Checkpoint (2026-10-15)
----------------------------------------------

The current isolated host exports both original Modeling-2 (39 pages) and
Architecture (40 pages). Both repeat without output or changed pages. This is
native-host evidence, not actual-worker publication or an installed release.
The original Intro and Requirements inputs remain unsupported pending recovery.

The source now partitions noninterpolated masks across entirely transparent
rows/columns, retaining sample margins and bounded region counts. It preserves
contained text with its original image/backing. Earlier paint may be ignored
only where every padded mask sample is exactly opaque; later overlaps and
near-opaque samples do not satisfy that proof. Alpha-aware backdrop checks also
admit a repeat conversion of already-dark masked diagrams with unrelated marks
in transparent areas.

Image/text contrast uses the union of padded glyph footprints. If the ordinary
palette cannot be proved safe, a separately validated connected image/annotation
composite can retain its original colors on local white paper. The proof bounds
the area, accounts for connected images, clips and reused resources, and checks
other paint. The normal dark conversion remains preferred. Overlay panels and
their labels must recolor together. Existing white backings are recognized on
repeat, and inserted artifact blocks are removed only from the semantic
comparison after exact generated-content verification.

Aggregate native work limits are now 256 Mi pixels and 1 GiB decoded bytes,
including both normal and fallback attempts. The 16 Mi-pixel per-image,
128 MiB per-stream, source/output, object and operation limits remain bounded.
Combined-worker deadline/memory and limit-negative checks are still required.

Current host SHA256:
`2DF6227C8503E0B9854762D161D01F8898F856CC0BB68F2C1FB91E4FF9DAC5F1`.
Private implementation SHA256:
`3C339076E5E39B558E77C6229AC33ABC701689E9B6943675D530E738358AB2E1`.
Build directory ends in `pdf-dark-build-76556519806c4f6bb9324de8ab4591b9`.
The reviewed production pin, policy identity and installed 1.3.4 are unchanged;
do not stage this host before the remaining combined review.

Evidence is under `.codex-temp/consistent-modeling13` and
`.codex-temp/consistent-architecture13`: native results, repeat facts, preserved
original hashes, glyph/font/position equality, page geometry, metadata,
attachments and link checks, and independent PDFium page renders. Every page has
a before/after contact sheet; targeted visual review and existing-corpus checks
remain part of final acceptance. The scratch renderer decodes the native BGRA
buffer correctly; earlier build-12 contact sheets had red/blue swapped and are
diagnostic history only.

New authored suites cover alpha regions (19 cases), glyph contrast (4 cases)
and local composites (12 cases); all pass the current host.
Selected existing image/backing, retained-image, near-dark, diagram, transparency,
raster-readability and convergence suites pass their executed checkpoints.
Three legacy suites now replace newly supported refusal cases with preservation
checks: colored backings (11 cases), image overlay panels (7 cases), and layout.
The main-agent combined rerun passes all six suites; logs and scratch reports
use `.codex-temp/consistent-final-*13`. Full regression is not yet complete. The
pin-gated Native and Features suites currently reject the unreviewed host before
testing; this is not a passing result or a reason to refresh the release pin early.


Bounded Source Recovery Checkpoint (2026-10-15)
---------------------------------------------

The original Intro now exports all 38 pages through isolated host build 17 and
repeats without output. Its one inconsistent image/mask pair is normalized to
the JPEG's intrinsic dimensions only after exact decoded alpha length and RGB
sample validation. The original encoded JPEG and decoded mask bytes remain
unchanged. Mask dictionaries are copied before correction, preserving aliases.
Conflicting mask geometry, ambiguous equal-area reshaping, predictors, matte
normalization and missing evidence remain refusals. Recovery does not modify
the source file.

Output verification independently replays recorded normalizations against a
fresh source parse, then applies the existing resource, semantic and byte
checks. The structural baseline is captured before normalization, including
passive attachments; a content stream aliased as attachment data cannot be
silently repaired through that alias.

The native coordinate recovery recognizes the bounded grammar
`0{1,3}\.0{1,3}-[0-9]{1,4}` only in complete, numeric `m`, `l`, `c`, `v`, `y`,
`cm` and `re` operations. Tokenization excludes names, strings and comments;
arrays, dictionaries, inline images, other operators, mixed malformed operands
and incomplete operations are not recovery contexts. Records bind stream
identity, decoded offset, original/replacement bytes and operator/operand
position. The same records must reproduce on the independent source parse.
An independent 100-case PDFium/MuPDF probe passes all 42 eligible combinations;
several broader malformed-number forms produce different reader behavior.
Reader agreement establishes the selected rendering interpretation, not author
intent. Operations split across separate content streams are still a recovery
boundary and need a bounded joined-stream implementation and regression.

Native replies now report `recoveredImages` and `recoveredCoordinates`. Typed
admission preserves both counts, refuses impossible no-op claims and binds them
to conversion results. Older replies default these fields to zero. Application
source adds an explicit completed-with-warning message for either recovery.
Actual-worker publication and warning presentation still need verification.
The release policy identity and reviewed host pin have not been updated.

Build-17 evidence:

- Host SHA256: `66F469F00A534E5543FC366340E17D70A0B978D1704E615FEB7814EE5E16F7AC`.
  Build directory ends in `pdf-dark-build-33e46e5c87f44d73b9550e8e3b377e05`.
- `consistent-intro17/recovery-audit.json` proves full-document export, repeat
  no-op, unchanged original, exact JPEG/mask preservation and all 38 rendered
  pages matching the manually corrected reference in both PDFium and MuPDF.
  `structural-review.json` checks glyph/font/position equality, page geometry,
  metadata, attachments and links. All four before/after contact sheets were
  reviewed; page 37's previously striped image now renders coherently.
- Authored suites pass 15 image-recovery and 88 coordinate-recovery cases.
  Managed model/protocol contracts pass 49 checks, including duplicate fields,
  count bounds and admission/result mismatch refusals.
- ImageRegions (19), GlyphContrast (4), ImageComposites (12) and RetainedImages
  (18) pass. Full original Modeling-2 (39 pages) and Architecture (40 pages)
  still export and repeat without output. Evidence is under the corresponding
  `consistent-recovery-*17` and `consistent-lecture-regression17` directories.

Requirements now passes its malformed coordinates and reaches the subsequent
contrast checks. Page 13's opaque teal cards use five cubic segments: one
rounded corner is split in two. Their white labels are inside the cards, and
the existing convex control-polygon proof passes. The old per-cubic orthogonal
tangent and exactly-four-curve checks prevented containment proof.

Build 18 generalizes bounded convex panels to 4-16 monotone cubics and separately
checks join turns before using narrow stroke bounds. Requirements then reaches
page 26, which still refuses contrast. Existing RoundedPanels (24),
CrossingGlyphs (9) and PanelBorders (11) pass. The initial SubdividedPanels
fixture used a label wider than the font's conservative advance bound; after
correcting the fixture, all eight cases pass without relaxing font containment.
Evidence: `consistent-requirements18`, `consistent-recovery-SubdividedPanels18b`
and `requirements-page13-analysis` under `.codex-temp`.


Full Original Export Checkpoint (2026-10-15)
------------------------------------------

Isolated build 22 exports all four original problem documents and repeats with
zero changed pages and no output: Intro (38 pages), Requirements (61),
Modeling-2 (39) and Architecture (40). Source hashes are unchanged. Requirements
reports 15 recovered drawing coordinates; Intro reports one recovered image.
All four preserve searchable text, glyph/font geometry, page geometry,
metadata, attachments and links in independent checks. Every one of the 1,949
original vector paths remains present in order; additional paths are the
engine's validated backgrounds/backings.

Requirements page 26 contains two split rounded-card tangents whose serialized
control points differ from collinearity by about 0.000007 page points. The
engine now proves a single-lap chord polygon, subdivides curves exactly with
De Casteljau, and clips its safe interior against each subcurve's control-hull
inset. This accounts for rounding geometrically without changing path tokens
or raising the old convexity tolerance. Ink containment uses a distance-scaled
tolerance smaller than the inset. Zero-length pill edges are handled explicitly.
Stroke joins use the first exactly nonzero tangent; a short tangent falls back
to generic conservative stroke bounds instead of substituting a farther chord.

Requirements' repeat failure came from a page number partly covered by a later
image and band. The image extends about 0.00595 page points beyond the band,
so the band alone cannot prove coverage. The contrast proof now accepts a
later unmasked, opaque, axis-aligned image with a known rectangular clip, or a
later opaque rectangle covering an intervening paint/glyph intersection
exactly. Remaining ink still needs a verified paper backdrop. Masked images,
unknown clips, unproved rotation and exposed earlier-image backdrops retain
their refusals; no overlap tolerance was expanded to hide the strip.

Build-22 evidence:

- Host SHA256: `2419DFF7BA4C0C3C6D7BA389599C49FFB351D0E7945FCAC9FF0737B411227828`.
  Build directory ends in `pdf-dark-build-f0dd59b560454acb9514643c8c793ff9`.
  Native source SHA256: `E3A79914F2EA63E2918FDB61A6AFFBCA14C219314AD8ADBE68B271ACFC53465B`.
- `.codex-temp/consistent-{intro,requirements,modeling,architecture}22` contains
  full original/output PDFs, conversion/repeat facts, structural reports and
  independent PDFium renders. `consistent-vector-preservation22.json` records
  all original vector paths; `consistent-render-regression22.json` proves all
  178 output page images match the reviewed earlier exports. Requirements'
  six source/output contact sheets were reviewed in full.
- `consistent-topic-corpus22b/results.json` passes the existing 25-document,
  746-page topic corpus through full export, repeat no-op and unchanged source
  checks. Its input list comes from the prior recorded corpus, not a new sample.
- RoundedPanels (24), SubdividedPanels (13), PanelBorders (11), FuturePanels
  (20) and LaterImageOcclusion (10) pass. The new tangent-kink fixture fails
  the former build-18 proof and passes build 22 with unchanged path geometry.
- Thirty-five additional native suites pass under `consistent-suites22`.
  CurveBounds initially retained obsolete refusal expectations, reproduced on
  build 17 as well. Its updated three-case suite now verifies exact rendered
  preservation for an overlapping original-color composite and safe recoloring
  for a bounded non-smooth join, plus geometry and repeat checks. It passes in
  `consistent-curve-bounds22-verified`. Native, Features and Accent remain
  gated on the reviewed release pin and are not claimed as passing this host.

This is source/native evidence, not a verified combined release candidate.

Build 23 adds a final positive-area check for the clipped panel interior so
repeated vertices at a collapsed point or line cannot establish coverage.
The complete rerun passes all 42 ungated native suites, including the new
short-tangent miter regression, under `consistent-suites23`. The 25-document /
746-page corpus passes again in `consistent-topic-corpus23`. All four originals
export and repeat without output in `consistent-{name}23`; independent MuPDF
comparison proves all 178 pages retain build 22's rendered pixels, text,
drawings, page geometry and metadata (`consistent-build23-comparison.json`).
Different serialized PDF bytes are not treated as a geometry or pixel change.
The current isolated build directory ends in
`pdf-dark-build-d915867da5934335a98e84a777a33106`; host SHA256 is
`4EFE2BB128AD0D93F3ABE3F7550B0F74E574D2D50F91FD75531CEBFE9E8A041D`, and
native source SHA256 is
`B75D8332B3E9CBBFF10F5AF9520E11179008590892CF454332770E4504CD380B`.
The short-tangent fixture establishes an ideal PDF miter reaching glyph ink
and a pre/post native admission difference. MuPDF's flattening does not render
that tiny apex miter, so it is conservative geometry evidence rather than a
claim of a visible MuPDF defect. The current documentation/source-boundary
check passes 226 documentation files; selected Git whitespace checks pass.

This checkpoint preceded joined-stream recovery and combined-candidate work.


Joined Content and Candidate Preparation (2026-10-15)
---------------------------------------------------

The reviewed `pdf-dark-8` host recovers drawing operands in their complete page
context. It copies the joined stream before repair, so shared page objects and
passive attachments keep their original bytes. Independent verification replays
the exact recorded recovery against a fresh source parse. The bounded number
grammar remains unchanged; ambiguous numeric fragments are refused.

An independent font-encoding fixture exposed a QPDF joining difference: a
literal string crossing a content-stream boundary can change a source space
into character code 10, which some fonts map to a visible glyph. Every
multi-stream page now checks literal-token boundary crossings before rewriting,
even without a damaged coordinate. Such strings receive an actionable refusal.
Complete strings, arrays spanning streams, hex-string whitespace and drawing
operand boundaries remain supported. Empty streams and existing LF endings were
checked separately. This prevents a demonstrated fidelity error rather than
silently accepting QPDF's different interpretation.

Build 28 is the reviewed source/native checkpoint for candidate 1.3.5:

- Build directory ends in `pdf-dark-build-fe968568c2ce4414985604a6b1973923`.
  Host SHA256 is
  `DEF3416B9529BFA8C1EAD0F8891962BA15FC78913D23AFA958D18E81901E3D0F`;
  native source SHA256 is
  `375E03DA4EFB08817FFC9F9364618ABAE1286822D76E4C3E235CBC592DA3F604`.
  The public source/binary pin and private adapter select this exact build.
- All nine original COSC 310 lectures export and repeat without another output
  in `.codex-temp/consistent-lectures28`. All 178 pages of the four original
  problem PDFs match the reviewed baseline's MuPDF pixels, text traces, drawing
  geometry and metadata. All source hashes remain unchanged.
- The 25-document / 746-page corpus passes full export and repeat no-op in
  `consistent-topic-corpus28`. All 42 ungated native suites pass in
  `consistent-suites28`; the pinned Native, Features and Accent suites pass in
  `consistent-{Native,Features,Accent}28`. JoinedCoordinates passes 59 cases
  with independent PDFium/MuPDF rendering, glyph checks and refusal witnesses.
  CoordinateRecovery passes 88 cases, including byte-exact attachment aliases.
- The automatic, height-filling details view and cause-specific PDF warning
  summaries pass 167 hidden view contracts (`consistent-ui27.log`). These are
  layout/model checks, not desktop-input or screen-reader acceptance.
- The real-worker document contract now accepts expected image/coordinate
  recovery counts and checks actual publication warnings, unchanged originals
  and recovery-free repeat no-ops. Combined candidate execution remains pending.

This native checkpoint was followed by the combined verification below.


Verified Candidate 1.3.5 (2026-10-15)
------------------------------------

The complete production candidate is
`artifacts/production-staging/6443e8fbcdc145d48bdd769fed78135e`. Its verified
version receipt binds inventory SHA256
`431D31A57FB0F4CAA3A41AEADE8C5FE12579C995C91649182C68B294A8722B70`.
The unsigned archive is
`artifacts/release-candidates/9a010b162353490a933e250166a78714/ContextSuite-win-x64-unsigned.zip`,
SHA256 `B8722F2EC9D41EA6198F8F13CB086D609C31247BFA4D37EA8223E8A65FEFC5B8`.
Archive extraction, exact engine/source pins, membership, dependency notices
and release-manifest checks pass. It retains all installed 1.3.4 engine families.

Combined evidence under `.codex-temp`:

- `consistent-worker135` runs the actual staged worker with an exact staged-Core
  test binding. All nine original COSC 310 documents (390 pages) publish as
  collision-safe copies, retain original hashes and repeat without another
  output. Intro reports one recovered image; Requirements reports 15 recovered
  coordinates. Publication warnings match the reported causes. The managed
  adapter independently validates text and glyph placement with PdfPig.
- `consistent-worker135-comparison.json` verifies all 390 published pages against
  reviewed native outputs: MuPDF pixels, text traces, drawing geometry, links,
  attachments, page geometry, metadata and independently decoded image data.
  The four original problem documents account for 178 of these pages; their
  earlier source/output PDFium review and vector-preservation evidence still
  applies because the reviewed native output comparison is exact.
- `consistent-worker-topics135/results.json` passes all 25 original corpus
  documents (746 pages) through actual-worker publication, explicit warning
  checks, unchanged-original checks and repeat no-ops.
- `consistent-worker135/pdf-dark-failures.log` passes 33 checks, including
  observation of a live native child before cancellation, cleanup, selective
  retry and a 513-page resource-limit refusal before conversion admission.
  The longest lecture probe/export/repeat contract took 23.51 seconds in the
  observed run. Observed worker/native peak working sets were about 110.3/83.6
  MiB, within the existing runtime bounds; this is corpus evidence, not a claim
  about every possible PDF.
- The first split-text refusal test incorrectly expected a page number for the
  generic unsupported category. Its failed assertion is retained in
  `consistent-worker135/results.json`. The worker returned a normal typed
  refusal, not a crash. The corrected staged-Core/worker test passes all three
  checks in `consistent-refusal135-bound.log`, with the established page-free
  static-export advice, unchanged original and no partial output.
- `consistent-foundation135.log` passes 5,177 contracts against the staged worker.
  `consistent-ui135.log` passes 167 hidden view contracts, including automatic
  details expansion, height allocation, scrolling and cause-specific warnings.
  `consistent-payload135.log` passes 14 tamper/membership/package checks.
- `consistent-completion135.json` consolidates the final audit and rechecks
  every staged payload file, archive identity and test input identity after
  execution. `consistent-preserved135.json` verifies all 2,787 installed 1.3.4
  files and prior receipt identities. Settings remain absent as before, with no
  save or migration. A license blob differs from the earlier installation
  receipt but predates this goal turn; it was not modified by this work.

The goal is complete within the verified policy bounds. Ambiguous source data
still receives a refusal rather than a damaged export. No installation, shell
registration, signing, publication, commit or push was performed.
