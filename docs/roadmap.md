Roadmap
========

This is the current plan after the free migration on 2026-10-30. Use
[project status](../README.md#project-status) for source, candidate, and installed
versions. The [historical roadmap](roadmap-history.md) preserves earlier milestone
numbers and acceptance records; it is not a second active plan.


Implemented Foundation
----------------------

Context Suite has one free production application built from the main repository,
with selected local engines and retained source/notices. The private checkout,
trial, paid activation, and License UI are no longer application requirements.
See [decision 0021](decisions/0021-free-product-and-public-implementation.md).

The context-menu-first UI, broad read-only Analyze, bounded image/audio conversion
and optimization, and selected PDF/Office workflows are implemented. This is not
a claim that every variant or release environment is accepted. The
[capability matrix](launch-capability-matrix.md) and owning designs define supported
operations, fidelity limits, and refusals.


Remaining Priorities
--------------------

1. Complete release acceptance in the [packaging goal](release-packaging-goal.md):
   matching hosted checks, prerequisites, signing, native installer/upgrade
   lifecycle, and clean-machine evidence. Local archive verification does not
   establish these gates.
2. Complete visible and accessibility review in
   [local candidate acceptance](local-candidate-acceptance.md) and
   [manual workflow review](manual-workflow-review.md). Use the free UI and retain
   explicit gaps where the former visual launcher is unavailable.
3. Carry out separately reviewed external retirement from the
   [free transition goal](free-access-goal.md): commerce changes, private-repository
   archival, and website deployment, preserving existing customer records and
   benefits. Prepared changes are not completed live actions.
4. Resolve remaining engine, variant, and acceptance limits recorded in
   [broad file support](broad-file-support-goal.md) and the relevant tool designs.
   Preserve exact historical evidence; do not treat completed implementation as
   new work or broaden support from recognition alone.
5. Follow [repository maintenance](repository-maintenance.md) for correctness,
   task output, and separately reviewed historical cleanup. Preserve source,
   engine pins, version receipts, installed/rollback payloads, and required evidence.


Product Boundary
----------------

Keep the completed direct-action UX, copies by default, explicit Settings consent
for eligible replacement, and bounded validation/publication. New scope requires
an explicit product decision. Video conversion, cloud processing, AI editing,
CD ripping, arbitrary engine commands, and silent destructive processing remain
outside the accepted scope; see [product design](product-design.md).
