PDF Dark-Mode Review
====================

Historical evidence relocation (2026-10-13): paths marked archived below now
reside under `Z:\Backup\Context Suite\workspace-history\20261013\payload`,
using the same repository-relative paths. Follow the
[workspace recovery instructions](repository-size-reduction-goal.md#uncompressed-workspace-relocation-2026-10-13)
before rerunning historical checks.

Installation follow-up, 2026-10-11: local **1.2.8** now includes `pdf-dark-5`,
the independent Analyze window and menu availability fixes. The complete installed
payload matches staging `8a37ff3f29dc4f52a83a914a4c6d0361`; all three shell
registrations and packaged COM activations pass. Settings, trial and license
files are unchanged. The earlier source-only and 1.2.7 candidate statements
below describe their original verification milestones. Reserved candidate files
remain unchanged; the previous installed 1.2.5 payload is backed up.

Latest source follow-up: `pdf-dark-5` removes pale colored-lettering outlines by
preserving edge coverage against dark paper and supported panel backgrounds.
Open the [updated comparison](../.codex-temp/pdf-dark-edges-review/index.html),
now with higher-resolution renders and a single-page view, or the
[label close-ups](../.codex-temp/pdf-dark-edges02-lecture/text-crops.png).
All 50 pages pass structural and actual-worker checks; five changed pages were
visually reviewed and the other 45 match the previous output. See the
[text-edge policy and validation](pdf-dark-mode-goal.md#text-edge-follow-up-2026-10-11-pdf-dark-5).
Source-only; not packaged or installed.

Previous source follow-up: `pdf-dark-4` improves small labels in colored panels
and reduces compression fringes. Open the
[two-pane comparison](../.codex-temp/pdf-dark-readability-final/index.html) with
shared zoom/panning or the [full lecture](../.codex-temp/pdf-dark-readable02-lecture/index.html).
Pages 37 and 44 were visually reviewed; the other 48 match the previous output.
All 50 pages pass structural and actual-worker checks. See the
[readability policy and validation](pdf-dark-mode-goal.md#readability-follow-up-2026-10-10-pdf-dark-4).
Source-only; not packaged or installed.

Previous source follow-up: `pdf-dark-3` improves embedded monochrome and colored
diagrams on the four owner-selected pages. See the
[focused comparison](../.codex-temp/pdf-dark-diagrams-review/index.html),
[complete lecture](../.codex-temp/pdf-dark-diagrams-lecture09/index.html), and
[policy and verification](pdf-dark-mode-goal.md#diagram-image-follow-up-2026-10-10-pdf-dark-3).
All 50 pages pass structural checks and actual-worker conversion. Seven changed
renders were reviewed; the other 43 match the earlier reviewed output. JPEG
detail is retained, not reconstructed; some image panels remain light when
recoloring would lose contrast. Source-only, not packaged or installed.

Source follow-up, 2026-10-10: `pdf-dark-2` now converts all 50 pages of the owner
lecture through the actual worker. See the
[current review](../.codex-temp/pdf-dark-layout-lecture-07/index.html) and
[implementation details](pdf-dark-mode-goal.md#layout-and-transparency-follow-up).
Text/glyph placement, vector geometry and masks pass independent checks on every
page. All pages received rendered review; images deliberately preserved in their
original colors may retain light backgrounds. This follow-up is not packaged or
installed. The candidate 1.2.7 evidence and limitations below are historical and
its reserved files have not changed.

Status: isolated unsigned candidate **1.2.7**, engineering verification and review
handoff complete, 2026-10-10. Production staging, fresh archive extraction and
exact-candidate regression coverage passed within the documented feature bounds.
Installed 1.2.5 and reserved 1.2.6 remain unchanged.

Open the [local before/after review](../.codex-temp/pdf-dark-review-127/index.html).
It contains authored vector, line-art, soft-mask, photographic/scan, shared-resource
and colored-accent fixtures, plus an admitted isolated lecture title page. PDF
links retain searchable text and vector content. The menu illustration uses the
existing product icons and implemented labels; it is not an Explorer screenshot.
The gallery layout was checked in a browser at desktop and narrow widths; native
UI, keyboard, accessibility and owner acceptance remain separate.

The complete owner lecture is **unsupported**. Its contrast conflicts and
ambiguous transparent images cannot be safely transformed by this policy.
Several masks reveal underlying page marks; white backing would cover content.
The earlier 50-page research prototype is not a production result. No AI, OCR,
cloud processing or flattened-page fallback is included.

The [admission policy](pdf-dark-mode-goal.md#current-admission-policy) defines
supported variants and explicit refusals. A preserved photograph or unclassified
scan can remain bright; the result discloses unchanged image colors. All outputs
are collision-safe dark-mode copies, even with overwrite enabled. Wholly
already-dark documents remain unchanged without another copy.


Candidate identity
------------------

- Version: `1.2.7`.
- Staging: `8992a4a157104762b4e04e54b37f06db`.
- Inventory SHA-256:
  `D75367D5D7C4DDB8F66446314E592F3B4A381D6780159A5099B51CD196F59949`.
- Native PDF-dark host SHA-256:
  `9B74ABBE6B567DB064C3A05AC82A88F16A6978FC4E09E568683C43D19168521A`.
- Unsigned archive (`artifacts/release-candidates/fbd7529a16a9433493360f9defe7590a/ContextSuite-win-x64-unsigned.zip`, archived).
- Archive SHA-256:
  `F94F29E48F91DAB4057FC7001439F2E4F0A541A029F819362849959BD2AEBBED`.
- The release manifest (`artifacts/release-candidates/fbd7529a16a9433493360f9defe7590a/ContextSuite/release-manifest.json`, archived)
  records public/private revisions, dirty-source status, selected engines and
  every packaged file. The archive was extracted to a fresh directory and
  verified by the same complete candidate guards.

The native host is independently compiled against the existing pinned qpdf 12.4.1
distribution. Its source and binary pins, runtime DLLs and notices are checked
by the owning staging tools. No research Python PDF/image library ships with it.
The candidate retains all existing image, audio, WEM, PDF and Office engines.
Dirty public/private source provenance is recorded; this is unsigned internal
verification, not a published release.


Engineering evidence
--------------------

The combined replay (`.codex-temp/pdfdark127-final-checks/results.json`, archived) covers
47 suites using the staged worker/application and test hosts explicitly bound to
the staged Core/Private assemblies. Its
inputs (`.codex-temp/pdfdark127-final-checks/inputs.json`, archived) record those bindings,
commands and runtime identities. Every staged file remained unchanged.

The first run passed 46 suites. The existing SVG interruption test missed its
live-child/parent-CPU observation before cancellation. Its unchanged
[isolated rerun](../.codex-temp/pdfdark127-svg-isolated.log) passes all 16 checks,
including live child observation, cancellation, worker termination and timeout.
The initial failure is retained, not relabeled as a pass. The
[consolidated evidence](../.codex-temp/pdfdark127-evidence.json) records both runs.

| Check group | Passing evidence |
| --- | --- |
| Foundation | 5,122 contracts, including batch/publication, image/DDS/PNG and actual WPF startup |
| PDF dark mode | 50 public model/application checks, 14 actual-worker checks and nine selective-retry/active-cancellation checks |
| Native fidelity | Independently authored paint, image, shared-resource, nested-form, protected/interactive refusal and resource-limit fixtures; outputs rendered and inspected |
| Colored accents | Rendered RGB fills at least 3.27:1, strokes at least 4.84:1 against paper; distinct hues and readable captions |
| Existing workflows | Image compatibility, GIF, ICO/AVIF/TIFF/SVG, video audio, common audio, WEM/Wwise, PDF analysis/optimization/pages/Word, images-to-PDF, Office and HEIF |
| Shell and icons | 702 filename-only availability checks, isolated COM/batch activation and 336 Windows-decoded icon frames |
| Packaging | 14 PDF-dark payload guards, 23 release-package guards, complete engine/notice allowlists and 283 local notice links |

The full production solution builds with zero warnings or errors. Public/private
build-boundary checks pass. The
[preserved inventories](../.codex-temp/pdfdark127-preserved-identities.json)
verify all 4,257 installed 1.2.5 files, all 4,257 reserved 1.2.6 files and all
4,271 new staged files. Both reference PDF hashes remain unchanged.

The [review manifest](../.codex-temp/pdf-dark-review-127/review-manifest.json)
identifies every artifact, the exact native host, reference source hash and page
admission results. Reference files were read-only and unchanged. Authored images
are diagnostic fixtures, not representative customer-photography acceptance.
Native menu/COM availability and compiled icon checks cover the new PDF-only
destination; the existing PDF glyph is reused with the matching theme variant.

Installation, shell registration, signing, publication, commits and unrelated
cleanup were not performed. Native UI/accessibility, representative customer
fidelity and commercial release acceptance remain separate decisions.
