Optimize Preset Expansion Goal
==============================

Status: active, owner authorized on 2026-10-19. This follows the completed
[first expansion](optimization-expansion-goal.md); it does not change the
installed 1.5.2 application or the reserved 1.5.3 candidate.

Deliver useful Auto, Lossless, Balanced, and Smallest choices wherever the format
has a defensible quality tradeoff. Do not expose duplicate settings just to fill
four slots. Keep the existing direct-action workflow, bounded recipes, smaller
validated outputs, and compact dependency footprint.


Quality And Safety
------------------

Follow [decision 0016](decisions/0016-fixed-optimization-recipes.md).
Lossless preserves decoded content and admitted metadata. Balanced and Smallest
allow different, explicitly bounded quality reductions. Auto uses conservative
preflight, chooses Lossless when uncertain, and never chooses Smallest.
Each recipe has at most one predetermined fallback, starts from the original,
and shares one deadline. A larger, invalid, or unacceptable candidate is never
published. No smaller result is a valid outcome.

Protect dimensions, orientation, color interpretation, transparency, hidden RGB,
animation timing, page structure, formulas, and other meaningful content where
applicable. Do not silently resize, remove metadata, flatten documents, change
formats, or add a generation of lossy processing under the Lossless label.
Expanded image and document paths retain mandatory copies. Warn about lossy
processing in the selected action's description and preserve that policy in
result details. Auto stays lossless for already-lossy images until a conservative
automatic policy has its own evidence.


Coverage And Sequence
----------------------

| Family | Work and acceptance boundary |
| --- | --- |
| PNG | Retain all four existing recipes and their regression evidence. |
| JPEG | Add fixed Balanced/Smallest recipes with local error, edge, and gradient checks; keep exact coefficient optimization for Lossless and conservative Auto. |
| WebP | Admit ordinary still lossy inputs, preserve exact alpha and metadata, and add meaningful quality presets. Animation requires separate timing/frame evidence. |
| PDF | Assess and implement selective embedded-image optimization with explicit image validation and unchanged non-image structure; never enable generic lossy engine switches without a preservation oracle. |
| DOCX/XLSX/PPTX | Extend exact package recompression with individually validated embedded-image substitutions, unchanged other members, and document reopen evidence. Preserve formulas and cached results. |
| FLAC | Retain Auto/Lossless; compression effort is not a lossy quality tier. |
| GIF/TIFF/ICO | Evaluate existing engines, metadata/frame/page preservation, and useful savings before enabling a bounded subset. |
| SVG/ODF/EPUB | Evaluate safe structural/package compression first; only add quality tiers if a real validated media transformation is available. |
| AVIF/HEIF, other audio, video | Assess separately against Optimize-only dependency cost and quality guarantees; no implicit large engine addition or new video scope. |

Compatibility work must be driven by reproducible ordinary files. Revisit size
and representation limits with measured memory/time bounds, not blanket increases.
Every assessed family ends with implemented evidence or a concrete exclusion and
reason. An unimplemented stage remains open unless its safety/dependency review
establishes that it is unsuitable for this scope.


Execution And Verification
---------------------------

- [ ] Freeze recipe identities, preservation rules, and supported representations.
- [ ] Implement JPEG/WebP contracts, private recipes, worker validation, and direct actions.
- [ ] Verify real savings and refusal/fallback behavior on a representative image corpus.
- [ ] Implement viable document image policies and verify non-image preservation.
- [ ] Assess and implement useful low-dependency additional format paths.
- [ ] Verify mixed batches, retries, cancellation, timeouts, source changes, and publication.
- [ ] Verify Optimize-only dependencies and measure payload growth.
- [ ] Run combined checks, review scoped diffs, and commit coherent public/private milestones.
- [ ] Update current support claims and clean task-owned disposable output.

Use small authored fixtures for deterministic contracts and independent,
appropriately licensed examples for representative quality evaluation. Include
text, diagrams, photographs, gradients, transparency, metadata, and already
compressed inputs. Report savings and acceptance rates, including no-benefit
cases. Automated metrics do not establish universal visual quality.

Reuse the existing engines and test/build tooling. Keep task builds under one
unique `.codex-temp` root, preserve compact receipts, and remove successful
disposable output after processes stop. Preserve reserved candidates, installed
payloads, reference inputs, and earlier evidence. No installation or push is
authorized by this goal.
