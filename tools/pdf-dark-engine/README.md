PDF Dark-Mode Engine Candidate
==============================

These source tools support an isolated, optional PDF dark-mode candidate. They
do not stage a release, install Explorer packages, reserve a product version,
or authorize publication. The normal production payload does not include the
dark-mode engine.

`Build-PdfDark.ps1 -PreparedDirectory '<new repository PDF scratch directory>'`
verifies the pinned qpdf 12.4.1 archive and extracted inventory, rejects linked
entries, and builds the private native host in a new `pdf-dark-build-*` directory
under that prepared scratch directory. It uses Visual Studio 18 x64 and Windows
SDK 10.0.26100.0, with inherited Directory.Build props and targets disabled.
The script prints the host path and records source, build and qpdf DLL hashes in
the new build directory. It never refreshes an existing SDK build.

The reviewed `payload-candidate.json` pin selects one host executable and its
native source hashes. `Stage-PdfDarkPayload.py --payload '<new candidate>'
--build-directory '<printed build directory>'` requires the complete pinned PDF
candidate already staged in that candidate directory. It verifies that PDF
payload through `pdf-engine/Stage-PdfPayload.py`, then checks the pinned host
and native source identities before creating `pdf-dark/`. That directory gets
the host plus the exact qpdf runtime DLLs and notices already selected under
`pdf-engine/`, excluding `qpdf.exe`. Existing `pdf-dark/` directories are never
overwritten. With only `--payload`, the script verifies exact membership and
hashes; extra, missing, changed or linked entries fail.

`Build-Production.ps1` can opt in through `-PdfDarkBuildDirectory` only with a
new `-StagingId` and all three PDF candidate inputs. Candidate staging reserves
the product version as described in `docs/product-versioning.md`; this source
workflow does not run that command. `Test-ProductionPayload.ps1` accepts
`-AllowPdfDarkCandidate` only alongside `-AllowPdfCandidate` and verifies the
same exact files. Until the host and pin are reviewed together, do not pass
this option to candidate staging. Candidate 1.2.7 opts in with the reviewed
native pin; subsequent source or binary changes require a new review and version.
The current pin selects the reviewed `pdf-dark-8` consistent-export extension. The build
records the implementation, CMake definition and all seven policy headers.
The parent `docs/pdf-dark-lecture-support-goal.md` records supported bounds,
corpus evidence, independent preservation review and current candidate status.
The pin does not describe historical candidate 1.2.7 or installed 1.2.9.

The source pin includes bounded source recovery, joined page-content checks,
transparent image regions and the prior diagram readability fixes. Existing
reserved candidates retain their recorded hosts. Candidate 1.3.5 passes the
combined-worker verification recorded in the owning goal document; installed
1.3.4 remains unchanged.



Verification
------------

The active consistent-export follow-up also adds `Test-PdfDarkImageRegions.py`,
`Test-PdfDarkGlyphContrast.py` and `Test-PdfDarkImageComposites.py`. They use the
same `--engine` and fresh `--scratch` arguments. These authored fixtures check
transparent gaps, exact-opacity occlusion, actual glyph footprints and the
original-color composite fallback, including rendered preservation and repeat
no-ops. They do not update the reviewed payload pin or authorize staging.

`Test-PdfDarkImageRecovery.py` and `Test-PdfDarkCoordinateRecovery.py` exercise
bounded damaged-source recovery, explicit native recovery counts, preserved
image/mask bytes, lexical exclusions, ambiguous refusals and repeat no-ops.
`Test-PdfDarkNumericInterpretation.py --renderer '<PDFium host>' --scratch '<fresh
scratch>'` supplies independent PDFium/MuPDF interpretation evidence for the
coordinate grammar; agreement does not establish author intent.
`Test-PdfDarkJoinedCoordinates.py --engine '<host>' --renderer '<PDFium host>'
--scratch '<fresh scratch>'` checks drawing operands across streams, shared-page
contexts, independent PDFium/MuPDF pixels, glyphs and ambiguous split text. The
coordinate suite also checks byte-exact passive attachments sharing page content.
`Test-PdfDarkSubdividedPanels.py` verifies multi-cubic corners and serialized
tangent kinks with exact path preservation. `Test-PdfDarkLaterImageOcclusion.py`
checks partially exposed text behind later opaque images or covering panels,
including repeat no-ops and masked, clipped and prior-image refusal controls.
`Test-PdfDarkShortTangents.py` checks conservative miter bounds when subdivision
makes a true endpoint tangent very short. Its continuous-geometry collision
witness is separate from renderer flattening behavior.

The test-only Python tools use independently authored fixtures, not reference
project assets. `Test-PdfDarkNative.py`, `Test-PdfDarkFeatures.py` and
`Test-PdfDarkAccent.py` each accept `--engine '<pinned host>' --scratch '<fresh
repository .codex-temp directory>'`. They require PyMuPDF, pypdf, ReportLab and
Pillow for independent parsing and rendered inspection; these are not shipped
dependencies. The suites cover paint/geometry preservation, admitted images,
mixed pages/shared resources, protected/interactive refusals, resource limits
and measured colored-accent contrast. Inspect the emitted page PNGs as well as
the programmatic results.

`Test-PdfDarkPayload.py --payload '<complete PDF/dark payload>'
--build-directory '<pinned native build>' --scratch '<fresh repository scratch>'`
checks staging and tamper/membership guards on its own disposable copies.
It verifies that the supplied baseline is unchanged after the tests.

The public contract host provides `--pdf-dark-public`, `--pdf-dark-workflow` and
`--pdf-dark-failures`, each followed by a fresh scratch directory and the actual
worker executable. The failure suite observes a live owned native child before
cancelling, and exercises selective retry after a publication failure. Use the
final candidate worker for release evidence; scratch-host passes alone do not
verify the combined release.

`Test-PdfDarkLayout.py` and `Test-PdfDarkTransparency.py` exercise local label
contrast, intervening paint, joined rules, preserved panel masks, sparse colored
annotations and negative ambiguity controls. They accept `--engine` and a fresh
`--scratch`; layout optionally accepts `--lecture` for selected local pages.
`Review-PdfDarkDocument.py --engine '<host>' --source '<local PDF>' --reference
'<optional reference PDF>' --scratch '<fresh scratch>'` checks every page's text,
glyph positions, drawing geometry and masks, then creates a three-way rendered
gallery and converted copy. Visual acceptance remains a separate review step.
The managed host's `--pdf-dark-document <fresh scratch> <worker> <source PDF>`
verifies the real worker/admission/publication path on a disposable source copy.
Append `<expected recovered images> <expected recovered coordinates>` to verify
explicit recovery counts and publication warnings as well as repeat no-ops.

`Test-PdfDarkDiagrams.py --engine '<host>' --scratch '<fresh scratch>'` checks
authored compressed monochrome/colored diagrams, edge masks, text over images,
clipped placement refusals and photographic/gradient negative controls. Keep
rendered review separate from its structural and pixel assertions.

`Review-PdfDarkReadability.py --current '<review directory>' --previous '<older
review directory>' --scratch '<fresh scratch>'` creates an offline two-pane
gallery from existing document reviews. `--pages` defaults to `14,19,37,44`.
Both panes share zoom and paired scrolling; version selectors allow Original,
Previous, Improved and Reference. This tool does not alter PDFs or page images.
The optional single-page view gives one version the full available width.
`--render-scale` accepts 2, 3 or 4 (default 4); 2 reuses existing review PNGs,
while 3/4 render fresh previews from the PDFs using test-only PyMuPDF. Original
and reference PDFs must remain available at their recorded local paths for these
higher-resolution previews. Rendering does not restore missing source detail.


The COSC 310 follow-up adds independent rounded-panel, legacy-link, matte,
near-dark image, image-backing, local-photo, screenshot-label, raster-readability,
thin-border and future-panel suites. `Test-PdfDarkFuturePanels.py` includes both
proved panel/paper backdrops and negative hidden-text, opacity, stroke and clip
cases; `Test-PdfDarkRasterReadability.py` includes original-image fallbacks and
positive diagram/blue-link controls. Run these with a fresh `--scratch` and the
reviewed `--engine`; they do not alter customer PDFs.

`Test-PdfDarkNeutralEdges.py` checks independently authored JPEG text beside
colored borders, including exact binary masks, smooth edge ramps, colored
lettering and panel controls. Source pixel measurements use Pillow's JPEG
decode; PyMuPDF verifies PDF placement, text and masks. The raster-readability
suite also measures authored badge and button glyph contrast and hue, allowing
either an unchanged original image or a readable conversion.

The contract host also accepts `--pdf-dark-document-refusal <fresh absolute scratch>
<actual worker> <absolute source PDF> <canonical page 1..512 or none> <ImageFailure name>`.
It probes a disposable source copy and verifies the exact static category/page
message, unchanged original and copy hashes, and absence of a partial/output PDF.
This complements successful whole-document checks and cannot replace them.
