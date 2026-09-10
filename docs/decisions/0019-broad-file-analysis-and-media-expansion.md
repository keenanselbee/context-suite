Broad File Analysis And Media Expansion
======================================

Status: accepted; document launch actions selected, implementation in progress
Date: 2026-09-09

Context
-------

The owner wants launch usefulness beyond images: common audio, PDF/documents,
and Analyze that can explain at least something about any file. A large catalog
should describe what common file types are generally used for. Signing is
explicitly deferred while product development continues.

Decision
--------

1. Make Analyze useful for every readable regular file through a generic fallback,
   qualified identification and deeper bounded parsers where supported. Keep it
   read-only, local and available independently of paid transformation access.
2. Package a reviewed, versioned offline format catalog with typical-use prose
   and provenance. Separate recognition, actual file facts and executable
   capabilities. Extension hints are not proof of content identity or safety.
3. Add common audio analysis/conversion and initially bounded lossless FLAC
   optimization through independently evaluated engines and fixed presets.
4. Include PDF and Office/OpenDocument identification and analysis. The owner
   selected images-to-PDF, PDF pages-to-images, PDF optimization and
   Word/Excel/PowerPoint-to-PDF for launch on 2026-09-09. Evaluate exact input
   variants, fidelity and dependencies before enabling these actions.
5. Retain decision 0018's direct context-menu UX, copies by default, explicit
   saved overwrite consent, necessary prompts and quiet success. New format
   families must not recreate the removed general-purpose workspace.

The [broad file support goal](../broad-file-support-goal.md) defines milestones,
deliverables, verification and remaining decisions. The owner approved broader
direction; the catalog count target and engine candidates in that plan are
implementation proposals, not claimed requirements already agreed in detail.

Supersession and retained boundaries
-----------------------------------

This supersedes the image-only launch ordering in
[decision 0010](0010-first-release-formats-and-curated-engine.md) and the product
roadmap; audio need not wait for signing or an image-only public release.
Decision 0010's curated image-engine and bounded capability guarantees remain.
[Decision 0018](0018-context-menu-utility-and-output-preference.md)'s image-only description broadens to files/media while its UX and
safety contracts remain. Video identification is permitted; video conversion,
archive extraction, macro execution, arbitrary commands and cloud processing
are not added by recognizing those file types.

Document analysis does not require a rendering engine. Document transformation
dependencies, multi-output publication and fidelity are separate decisions.
The existing commercial release goal remains historical image evidence and an
open release-gate checklist, not proof the expansion is ready.

Consequences
------------

Breadth in the catalog can grow independently from parser and conversion breadth.
Unknown, ambiguous, encrypted and malformed files remain useful report outcomes.
Each detector and advertised operation needs evidence; a broad engine capability
list is not a release manifest. No signing, installation, registration, provider
request, purchase, new commit or publication is authorized by this decision.
