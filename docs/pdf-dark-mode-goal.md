PDF Dark Mode Conversion
========================

The owner approved a new [lecture PDF support goal](pdf-dark-lecture-support-goal.md)
on 2026-10-11. It extends the reading palette to already-dark slides and adds
validated preservation for tagged structure, links, passive attachments, static
layers and wider font metrics. Its corpus contains 25 PDFs and 746 pages.
That extension is implemented as `pdf-dark-6`; its goal document records current
corpus verification and candidate status. Historical evidence below is unchanged.

Text-edge follow-up, 2026-10-11 (`pdf-dark-5`)
--------------------------------------------

Colored annotation edges previously became almost as bright as their stroke
cores, producing pale outlines. A bounded 7-by-7 source neighborhood now provides
stronger matching ink samples. The engine estimates coverage against white paper
or a locally supported panel color, accepting only small color-fit errors, then
composites that coverage against the corresponding dark background. This pass
targets the bright colored-ink branch; it does not expand strokes or replace
text. Neutral text and the established panel-ink mapping remain intact. The
local fit is an appearance heuristic, not semantic text recognition or a promise
to restore details absent from source images.

The full lecture passes independent text/glyph/vector/mask checks. Five pages
(8, 10, 11, 37, 44) changed from `pdf-dark-4` and received rendered review; the
other 45 are pixel-identical. Page 44's enlarged annotation and method-label
crops were also inspected. The actual worker matches the reviewed glyphs,
paths and 2x pixels on all 50 pages. Sixteen authored diagram cases include
monotonic colored edge coverage on white and blue backgrounds, retained hue,
unchanged adjacent background, fine strokes and open/filled symbols. Native,
features, accent, layout (19), transparency (8), focused managed (114) and
payload (14) checks pass. The production solution builds without warnings/errors.
The broader foundation suite was not repeated for this bounded native update.

The [updated comparison](../.codex-temp/pdf-dark-edges-review/index.html) starts
at page 44 and adds four-times-resolution PDF renders and a single-page view.
Normal reading-size zoom remains calibrated to PDF points. Browser checks cover
resolution, view selection, paired zoom/pan and narrow-screen layout. See the
[close-up comparison](../.codex-temp/pdf-dark-edges02-lecture/text-crops.png),
[all-page review](../.codex-temp/pdf-dark-edges02-lecture/index.html), and
[verification evidence](../.codex-temp/pdf-dark-edges02-evidence.json).
This update is source and isolated-runtime only; originals and reserved releases
remain unchanged. No installation was performed.


Readability follow-up, 2026-10-10 (`pdf-dark-4`)
----------------------------------------------

The owner requested clearer small diagram labels and a larger comparison view.
The native policy now estimates panel colors from bounded local samples instead
of coarse palette-bin centers, reducing compression fringes around labels. A
smooth coverage adjustment brightens existing ink within colored panels while
retaining intermediate antialiasing samples. It does not dilate strokes, resize
images, reconstruct text or recover source JPEG detail. Neutral image mapping
and the existing local text-over-image contrast guards remain unchanged: a trial
neutral-ink boost reduced contrast beneath a separate PDF label and was excluded.

The complete lecture passes independent text/glyph/vector/mask checks. Pages 37
and 44 changed and received rendered review; the other 48 pages are pixel-identical
to the reviewed `pdf-dark-3` output. The actual worker produces matching glyphs,
paths and 2x rendered pixels on all 50 pages. The production solution builds with
zero warnings/errors. Verification includes 15 authored diagram cases (including
fine-stroke contrast, unexpanded strokes, open/filled diamonds and panel seams),
native/features/accent suites, 19 layout and 8 transparency checks, 114 focused
managed checks, and 14 payload guards. The broader foundation suite was not
repeated for this bounded mapping change.

The [two-pane comparison](../.codex-temp/pdf-dark-readability-final/index.html)
offers Original, Previous, Improved and Reference, with shared zoom and paired
panning. Browser checks cover image loading, version selection, synchronized
scrolling and the narrow-screen layout. See the
[complete lecture](../.codex-temp/pdf-dark-readable02-lecture/index.html) and
[verification evidence](../.codex-temp/pdf-dark-readable02-evidence.json).
This is source and isolated-runtime work only; no installation or release
reservation was performed, and earlier review artifacts remain intact.


Diagram-image follow-up, 2026-10-10 (`pdf-dark-3`)
------------------------------------------------

The owner requested improved embedded diagrams on lecture pages 14, 19, 37 and
44. A bounded image policy now recognizes compression-tolerant monochrome line
art and some colored panels using spatial marks, local/global palette
concentration and edge checks. It maps paper to near-black, panel fills to dark
tints, and labels to light ink, interpolating compression/antialiasing mixtures
between paper, fills and ink. It changes image pixels without changing dimensions,
mask samples, page geometry or searchable PDF text; it cannot restore detail
already lost in the source JPEG.

A post-map check preserves resources with substantial unlabeled colored regions
whose edges would lose contrast against dark paper. This prevents the regression
found on the green diagram on page 22; it is not a general perceptual proof.

New masked-diagram recoloring requires the prior preserved-panel admission and
an exact binary cutout confined to the outer pixel boundary. Substantial partial
alpha, internal holes and inherited form placements remain outside this extension.
Local text-over-image verification checks transformed pixel footprints and known
rectangular clip intersections; unknown clips, intervening paint and insufficient
contrast refuse conversion. A bounded sparse-contact case retains existing
near-black text/diagram strokes that both become light ink. This is an explicit
heuristic for conservative font bounds, not glyph-outline or semantic recognition.
Gray panels beneath text and white labels over recolored dark raster ink have
negative regressions.

This update remains source-only and uses the existing single PDF (Dark mode)
command. No installation, release reservation or changes to older artifacts are
included. It does not guarantee universal image classification or fully dark
results for every document; unsupported/ambiguous images keep their prior policy.

Validation is complete in an isolated runtime: 13 authored diagram checks, native
feature/accent/layout/transparency regressions, 114 focused managed PDF checks,
14 payload guards, and all-page text/glyph/vector/mask verification pass. The
production solution builds with zero warnings/errors. The broader 5,122-contract
foundation run passed before the final native color-edge guard; all affected PDF
checks were rerun with the final pinned host. The worker-produced lecture matches
the reviewed output at 2x on every page. Seven pages changed visually from the
previous source result (8, 10, 11, 14, 19, 37, 44); each was reviewed. The other
43 are pixel-identical to that earlier reviewed output.

Open the [four-page comparison](../.codex-temp/pdf-dark-diagrams-review/index.html)
or [all-page review](../.codex-temp/pdf-dark-diagrams-lecture09/index.html).
[Verification evidence](../.codex-temp/pdf-dark-diagrams09-evidence.json) records
the final identities and checks. No older review artifact was overwritten.


Owner-approved follow-up, 2026-10-10: layout-aware recoloring is implemented and
verified in source and isolated runtime composition (`pdf-dark-2`).
The complete 50-page lecture is the acceptance target for this follow-up;
candidate 1.2.7 and its recorded support limits remain unchanged. Identify local
text/background relationships and handle transparent diagram placements without
hiding underlying marks. Preserve searchable text, vector geometry, masks,
photographic content and originals, using the existing single direct command.
Do not silently retain light pages, flatten pages or substitute AI reconstruction.
Require positive and negative overlap/image regressions, independent rendered
review of every lecture page, and actual-worker validation before declaring the
expanded policy implemented. This follow-up authorizes source implementation and
isolated builds/review, not installation, registration, publication or changes to
reserved release payloads.

Owner-approved goal, 2026-10-10. Status: engineering implementation and review
handoff complete in unsigned [candidate 1.2.7](pdf-dark-mode-review.md), within
the admission policy below. Not installed. One **PDF (Dark mode)** Convert
destination performs a document appearance conversion, not size optimization.
Native UI, representative customer fidelity and commercial acceptance remain
separate; the complete lecture reference is unsupported in reserved candidate
1.2.7, but supported by the source follow-up below.

The goal is a deterministic local transformation preserving searchable text and
drawing geometry. No AI reconstruction, OCR, cloud processing or generated content
is included. Always publish a collision-safe `Filename - Dark Mode.pdf` copy,
even when overwrite is enabled.


Layout and transparency follow-up
---------------------------------

The source-only `pdf-dark-2` host replaces the whole-page contrast veto with
bounded text/fill placement analysis. An earlier opaque axis-aligned rectangular
panel can retain dark fill beneath its contained label when no intervening paint
invalidates that relationship. Gray labels are included, as are image/stroke
occlusion checks. Compound paths, uncertain font metrics, partial overlaps,
clipped/rotated ambiguous relationships and cross-resource conflicts remain
conservative refusals. Joined thin white rules on colored paper are recolored as
foreground only when the whole connected group is isolated from other paint.

Transparent-image handling has two additional bounded cases. Mostly opaque,
light diagram panels can keep their original pixels and exact masks when internal
edge checks pass. Sparse colored annotation strokes can be recolored with their
exact masks only when placement checks establish that earlier underlying paint
falls in fully transparent pixels. No white backing is inserted. Transparent
photographs, substantial translucent regions, newly exposed internal holes,
overlapping ink and unverified form placements remain unsupported. Classifiers
are heuristics with positive and negative fixtures, not semantic recognition or
a universal guarantee. Retained panels/photos/scans can stay light; an image-only
page can therefore remain visually light even though its page paper is converted.
The existing result reports preserved image colors rather than promising uniform
darkness.

The 50-page lecture now passes the native host and actual managed worker:
50 changed pages, 15 recolored and 10 preserved image resources as reported by
the engine. All pages pass independent text, glyph position, vector geometry and
mask checks. All rendered pages were reviewed against the originals/reference;
the final four changed section-title pages and complex diagram pages were also
inspected at full render size. Source and reference hashes remain unchanged.
The output intentionally differs from the supplied reference in preserved panels
and accent colors; no AI/OCR/flattening or additional production dependency was
introduced.

Evidence: [50-page review](../.codex-temp/pdf-dark-layout-lecture-07/index.html),
[structural report](../.codex-temp/pdf-dark-layout-lecture-07/report.json), and
[follow-up verification](../.codex-temp/pdf-dark-layout07-evidence.json).
Native, feature, accent, layout and transparency regressions pass. The managed
host passes 36 model, 51 public, 14 worker, 9 cancellation/failure and 4 complete
document checks, 14 payload guards and 5,122 foundation integration contracts.
The solution builds without warnings or errors. The executable
and all three native source files, including the image-policy header, are pinned.
Reserved candidate 1.2.7 remains unchanged; this update requires a separately
authorized new release composition before installation or distribution.


Appearance and preservation
---------------------------

Use fixed near-black paper, warm light text and readable accent colors. Preserve
page order/dimensions, font/glyph placement, vector geometry and supported document
structure. Preserve photographic imagery. Recolor simple embedded diagrams only
under a validated deterministic policy; preserve ambiguous images with an honest
result or necessary preview. Do not silently rasterize pages, discard meaningful
features or imply universal PDF support.

Already-dark, scanned/image-only and mixed pages require explicit policies.
Signed/protected documents must retain their protections. Review interactive
features, links/bookmarks, tagged structure, annotations, color spaces, patterns,
gradients, clipping and transparency before declaring them supported. Unsupported
constructs need actionable refusals.


Prototype findings
------------------

The owner supplied a 50-page lecture and its dark counterpart as read-only local
references. Extracted text and vector drawing geometry match on all pages.
Embedded image data changes while dimensions remain the same. This establishes
a useful comparison target, not its tool provenance or general fidelity.

An independently authored scratch prototype recolors text/path paint operations
without changing coordinates, adds backing for implicit paper, and recolors
classified diagram pixels without changing dimensions. Source hashes, extracted
text and vector geometry are verified on all 50 pages. A research-only PDF library
is not a production dependency choice. Production needs stricter inventory,
resource limits, feature preservation and an independently validated engine path.

Visual review caught a concrete failure: preserving a transparent dark-text
diagram unchanged made its letters unreadable on dark paper. The next prototype
retained its original soft mask while recoloring admitted diagram RGB, correcting
the reviewed appearance. Unchanged image bytes do not prove unchanged appearance
when the backdrop changes. Ambiguous transparent imagery and shared resources
across mixed light/dark pages remain required implementation cases.

The provisional diagram classifier uses neutral/light pixel proportions and a
coarse palette bound. This is a hypothesis, not proof that an image is a diagram.
Validate photographic false positives, scans, grayscale gradients, antialiasing,
colored diagrams, masks and nested/shared resources before production adoption.
Do not use this one lecture pair as the whole test corpus.

Read-only inspection of the installed Dark Reader extension informed the
appearance research. Its browser PDF backdrop filter does not supply a saved-PDF
conversion engine. No extension/reference code or assets are copied into this
implementation.


Integration and verification
----------------------------

Use a distinct `pdf-dark` action and typed planning/work/result contracts. Reuse
the sequential worker, one admission, bounded child execution, immutable source
identity, transactional publication and selective retry. Add explicit dark-copy
naming intent; do not reuse combined-image PDF grouping or optimization's
smaller-output requirement. Recoloring may increase file size.

Evaluate existing independently sourced qpdf/PDFium engines first. Retain
untouched document objects and image masks. Structural optimization's unchanged
semantic hash is not a recoloring oracle: paint data intentionally changes.
Validate source identity, page/text/drawing structure, supported extras, intended
color transformations and rendered results before publication.

Bound bytes, pages, objects, stream expansion, decoded pixels, nested resources
and execution time. Test malformed/oversized sources, missing/altered engines,
source changes, collisions, cancellation/worker failure, mixed selections and
retries that preserve completed copies.

Completion requires production integration; targeted and applicable existing
PDF/Office/image/audio/WEM/shell/icon regressions; accurate capability docs;
before/after and necessary UI review artifacts; and a fresh compliant unsigned
combined candidate with verified extraction. Preserve installed/reserved releases.
Native UI/accessibility, representative customer fidelity and commercial acceptance
remain separate. Installation, registration, signing, commits/pushes, publication
and unrelated cleanup are not authorized by this goal.


Implementation checkpoint
-------------------------

This checkpoint records reserved candidate 1.2.7 (`pdf-dark-1`). The source-only
follow-up above supersedes its lecture and local contrast limitations, without
changing that release's files or historical evidence.

The public typed plan, native framing contract, managed adapter, sequential worker,
direct app action and PDF-only shell target are implemented in source. Dark-mode
copies have an explicit naming intent that refuses replacement. Supported work
uses one batch admission; already-dark files remain unchanged; preserved image
colors produce a visible warning. The app bounds PDF selections before probing.

Current scratch verification passes 50 public model/application/publication checks,
14 real-worker checks, nine selective-retry/active-cancellation checks, 14 engine
packaging guards, 702 shell availability checks, shell batch activation and
336 icon-frame checks. The full production solution builds to isolated scratch
with zero warnings or errors. The independently rebuilt native host matches its reviewed
scratch executable byte for byte. Optional engine staging and release verification
hooks are implemented. Candidate 1.2.7 now preserves that reviewed native pin in
an isolated staged payload and verified archive; exact evidence is in the review.

These checks do not close representative customer fidelity acceptance. Visual review found that a white
label on a compound dark filled region could become invisible even while text and
geometry checks passed. Native admission now refuses that contrast conflict, with
rendered regression checks for the refused callout and an eligible arrow. Bounded
engineering verification passes; broader fidelity and release acceptance remain
open. The engine pin is not a commercial release decision.

The native engine deliberately refuses the owner's complete lecture because of
contrast conflicts and ambiguous partially transparent embedded images. The earlier 50-page research
prototype is not evidence that the production policy admits this reference. Its
outputs and early native lecture renders remain research-only artifacts. Do not
ship those renders or weaken admission just to match one reference document.

The transparent-image investigation checked all ten masked placements with the
source transform, clipping path, image and mask. Six matched a white-only backing
at both tested resolutions; four did not. Some masks reveal other page marks,
and one comparison changes with rendering scale. An unconditional white backing
would hide content. Finite raster equality alone is not an all-zoom preservation
proof, so this investigation does not relax production admission.


Current admission policy
------------------------

The list below records candidate 1.2.7. Current source retains these general
bounds with the explicitly described layout/transparency extensions above.

- Accept bounded static pages using supported fonts, ordinary grayscale/RGB/CMYK
  paint operators and supported image/form resources. Recolor paint operations
  without changing text or path coordinates. Add opaque near-black page paper.
- A full-page dark background with predominantly light foreground paint is kept
  unchanged. A wholly already-dark document produces no copy. On mixed documents,
  clone changed-page resources so unchanged pages retain their original content.
- Preserve opaque images that do not meet the diagram classifier. Report that
  some images retain their original colors; this includes photographic and
  unclassified scanned content. There is no OCR or promise of fully dark scans.
  The classifier admits neutral white-paper line art or neutral dark masked ink;
  it remains a tested heuristic, not a semantic identification of diagrams.
- Preserve image dimensions and supported soft masks. Refuse ambiguous partial
  transparency, unsupported image color/mask variants and contrast conflicts
  between light text and dark filled regions. Do not flatten these cases.
- Refuse encrypted/signed PDFs, interactive forms, annotations, tagged structure,
  active/layered catalogs, nonempty named entries, output intents, Type 3 fonts,
  unsupported color spaces, patterns, shading, inline images and fractional
  graphics-state transparency. Untested constructs remain unsupported.
- Limit each source to 64 MiB and 512 pages; each selection to 128 distinct PDFs
  and 2 GiB; each output to 128 MiB. Native inspection/conversion has a 120-second
  deadline and bounded process memory, objects, nesting, decoded bytes, operations
  and pixels. Timeout, malformed data or failed independent validation publishes
  no candidate output.

These bounds describe the verified candidate behavior. The before/after handoff
includes independently rendered diagnostic fixtures and the admitted lecture title
page; it does not expand the support boundary to the entire lecture.

The broader feature suite passes protected/interactive/annotated/tagged refusals,
supported soft masks, preserved photographic/scan pixels, shared resource isolation,
nested forms and source/page limits. A rendered accent fixture exposed inadequate
contrast for saturated blue and colored fills. The corrected mapping uses relative
luminance; all tested fills exceed 3:1 and colored strokes exceed 4.5:1 against
the new paper, while caption text remains readable. Candidate 1.2.7 passes the
47-suite combined coverage with the documented unchanged isolated SVG interruption
rerun, complete payload guards and fresh archive extraction. See the
[review evidence](pdf-dark-mode-review.md) for retained failures and exact identities.
