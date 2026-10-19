Optimize Expansion Goal
=======================

Status: approved and started on 2026-10-19. This is an implementation goal,
not a claim that the additional formats are available or release-ready.
The owner authorized incremental commits in the public and private repositories
and task-owned cleanup. Installation, pushing and public release are separate.

Expand useful same-format optimization with modest dependency growth. Preserve
originals, meaningful content and metadata, bounded fixed recipes, validated
smaller-only publication, and the existing direct-command experience.
Follow the [optimizer design](optimizer-design.md),
[fixed recipes](decisions/0016-fixed-optimization-recipes.md), and
[maintenance rules](repository-maintenance.md).


First Milestone
---------------

| Family | Required outcome | Initial boundary |
| --- | --- | --- |
| JPEG | Lossless compressed-domain recompression | Bounded common representations; preserve coefficients, interpretation and admitted metadata; no decode-and-save quality loss |
| WebP | Lossless still-image recompression | Exact decoded samples, alpha, hidden RGB and admitted metadata; animation and another lossy generation excluded |
| PDF | Demonstrated structural compatibility improvements | Preserve document behavior and content; no flattening, downsampling, signature invalidation or encryption removal |
| DOCX/XLSX/PPTX | Lossless package recompression | Exact uncompressed entry bytes and required package/ZIP properties; no Office dependency, recalculation or external access |
| Existing PNG/FLAC/PDF | Preserve verified workflows | Existing recipes, admission, publication and resource protections remain regression requirements |

New families initially expose Auto and Lossless only, both using the same
conservative lossless recipe. Existing PNG Auto retains its documented bounded
loss policy. Filename recognition alone never admits a file. Define exact
variant and metadata bounds before enabling each handler; unknown meaningful
features must remain intact or cause an explained refusal.

Document outputs remain mandatory copies. Use copies for initial new image
handlers until their recoverable replacement path has equivalent evidence.
Preserve one batch/settings snapshot and sequential worker. Mixed selections
complete supported items and explain unsupported ones individually.


Baseline And Size Policy
------------------------

Starting committed revisions are public `e9a8594` and private `82e6479`.
Both working trees were clean. Source metadata and the latest recorded local
installation are 1.5.2. Prior verification is historical baseline evidence,
not verification of this goal's future changes.

The installed 1.5.2 payload inventory contains 2,915 entries. Applying the
existing component classifier yields 260 shared entries / 45,095,586 bytes
(about 43.0 MiB) and 2,655 Convert-only entries / 527,359,500 bytes. Their
combined 572,455,086 bytes are about 545.9 MiB. This is an inventory projection,
not a newly packaged Optimize-only installation; it excludes the inventory
file itself, installer metadata/cache and shared Microsoft prerequisites.
Local-only baseline receipt: `.codex-temp/opt-71d1898e/baseline.json`.

Reuse verified JPEG/WebP, PNG, audio and PDF dependencies where their actual
interfaces support the preservation contract. Package recompression must not
pull the Office engine into Optimize. Measure installed and compressed payload
growth when changing dependencies; do not set an arbitrary size cap. Record
why a significant increase is worthwhile before adopting it. Keep notices and
required source delivery with pinned engine changes.

No plugin framework, general scripting runtime, background service, automatic
engine downloads or full video stack is needed for this milestone. Update
the existing [component policy](optional-tool-installation.md) when an actual
dependency changes, and validate every affected selection.


Execution And Acceptance
------------------------

- [x] Establish clean source heads, accepted scope and payload-size baseline.
- [ ] Define exact initial image representations, metadata and engine paths.
- [ ] Implement and verify JPEG through the real worker and application flow.
- [ ] Implement and verify still WebP through the same flow.
- [ ] Reproduce a useful PDF structural refusal and broaden it safely with regressions.
- [ ] Implement DOCX/XLSX/PPTX admission, package preservation and recompression.
- [ ] Integrate all new handlers with shell hints, batch admission, retry and settings.
- [ ] Verify combined workflows and all affected installer component selections.
- [ ] Measure final payload growth and representative processing resource use.
- [ ] Update current support documentation, review diffs, commit completed groups,
      and close out task-owned transient output with retained evidence identified.

The first PDF source checkpoint replaces recursive indirect-reference hashing
with a bounded deterministic object table. Linked 48/128/512-object chains now
retain aliases/cycles without consuming nesting depth; direct nesting, object,
node and byte bounds and protected/history refusals remain. Focused verification
passes 129 PDF rewrite/Office inventory checks; the isolated public harness passes
5,037 foundation contracts. Actual-worker and representative-render checks remain
open, so the PDF milestone above is not complete. Local-only evidence:
`.codex-temp/opt-71d1898e/pdf-rewrite.log` and `foundation-third.log` in the same
directory. Earlier harness attempts used a relative fixture root and then the
DLL rather than apphost; their failures are retained separately and corrected
in the successful absolute-root/apphost invocation.

Extend existing planning, worker, executor and publication boundaries. Add
shared code only where implemented handlers need it. Each recipe gets one
primary attempt and at most one predetermined fallback under a shared deadline;
every attempt starts from the original. Do not search multiple successful
encodings for marginal savings or hide engine/validation failures as fallback.

For each family, require useful successful fixtures plus malformed, truncated,
unsupported, no-improvement and resource-boundary cases. Verify source stability,
metadata/content preservation, output reopen, smaller-only publication and
repeat operation. Cover cancellation, worker failure, source changes, collision,
output failure and subsequent recovery through the shared workflow. Test the
actual packaged worker, not only isolated engine calls. Keep licensing admission
and trial-expiry behavior consistent with existing admitted batches.

JPEG validation must protect quantization/coefficient interpretation as well as
metadata. WebP must protect exact decoded samples including alpha/hidden RGB.
PDF structural equivalence and feature inventories need independent rendering
checks for representative affected documents; appearance alone cannot establish
interactive preservation. Package validation must compare complete entry content
and required structural properties, not only selected XML or an unzip success.
Reject encrypted, signed, macro-enabled and unsupported ZIP/package variants
initially. Representative Office reopen checks must not execute active content.

Measure whole-operation latency, peak memory, temporary disk use, savings and
refusals on representative and held-out inputs. Preserve fixed quality contracts;
never weaken them to make a fixture pass. Keep local automated, human and native
installer acceptance separate. Document unavailable checks rather than claiming
success. A verified development candidate does not close the existing signing,
installer lifecycle or commerce release gates.


Commits And Task Storage
------------------------

Commit coherent, verified families with their contracts, tests and owning
documentation. Review the exact diff and required checks before each commit;
keep public/private changes and compatible revision records separate. Preserve
concurrent user work. The owner's instruction to commit along the way supplies
authorization for this goal's reviewed groups; it does not authorize pushes.

Use the task's single scratch root for compact receipts and owned experiments.
Reuse pinned engines and useful build inputs instead of copying complete
payloads per test. Prefer a reusable isolated managed build and generated small
fixtures; create a full combined candidate only when integration needs it.
Existing tools may create their own guarded run directories; record ownership
and follow their cleanup behavior rather than inventing a second cleanup system.

After successful verification and process exit, delete only this task's
disposable intermediates after checking resolved containment and reparse points.
Retain concise logs, source/engine identities, checks and necessary failure
evidence. Keep active caches, selected engine inputs, source kits, installed and
rollback payloads, reserved versions and historical evidence intact. There is
no broad scratch deletion, age-based sweeper or automatic Git clean. Report
significant retained output and cleanup failures at each milestone.


Later Reviewed Milestones
--------------------------

ICO embedded-PNG optimization, lossless GIF, bounded TIFF, ODF/EPUB packages,
additional image/audio lossy presets, SVG and AVIF/HEIF evaluation remain later
work. PDF/Office embedded-image reduction also needs separate lossy acceptance.
Video requires a separate product/dependency decision. Do not silently expand
this goal or mark a required first-milestone family complete by moving it here.
