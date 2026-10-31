Documentation
=============

Use [project status](../README.md#project-status) for the current source, verified
candidate, installation, and remaining acceptance. Other guides link there
instead of maintaining competing version summaries.


Current Guides
--------------

| Need | Owning document |
| --- | --- |
| Product boundaries and UX | [Product design](product-design.md) |
| Supported formats and limits | [Capability matrix](launch-capability-matrix.md) |
| Tool behavior | [Analyze](analyzer-design.md), [Convert](converter-design.md), [Optimize](optimizer-design.md), and [documents](document-design.md) |
| Builds, tests, isolation, and CI | [Development](development.md) |
| Source/output maintenance | [Repository maintenance](repository-maintenance.md) |
| Writing and presentation | [Style guide](style-guide.md) and [commit style](commit-style.md) |
| Accepted choices and supersession | [Decision index](decisions/README.md) |


Remaining Work And Acceptance
-----------------------------

- [Roadmap](roadmap.md): current priorities, separate from earlier milestones.
- [Free transition](free-access-goal.md): migration evidence and external retirement.
- [Release packaging](release-packaging-goal.md): release and installer gates.
- [Broad file support](broad-file-support-goal.md): exact engine/capability evidence
  and remaining variant/acceptance limits.
- [Local candidate acceptance](local-candidate-acceptance.md),
  [manual workflow review](manual-workflow-review.md), and
  [desktop smoke tests](desktop-smoke-tests.md): human and desktop verification.

A source test, archive verification, installed check, hosted CI result, and human
review establish different things. Keep their source/candidate identities and
limitations explicit.


Historical Records
------------------

[Project history](project-history.md), [product implementation history](product-implementation-history.md),
[foundation development history](foundation-development-history.md),
[historical roadmap](roadmap-history.md), and [CHANGELOG](../CHANGELOG.md) preserve
earlier milestones. Dated goal/evaluation records retain their original test
results and limitations; their old installed versions or trial behavior do not
describe current policy.

[Decision 0021](decisions/0021-free-product-and-public-implementation.md) governs
the free product. Earlier commercial decisions retain their rationale with
supersession notices. Historical private configuration and Git history remain
excluded from public archives.

Ignored evidence paths are local-only records, not public downloads. Preserve
source identities and exact acceptance receipts when condensing narrative.


Preserved Packaging Documents
-----------------------------

Some engine READMEs, kit descriptions, and distribution build instructions are
hash-pinned source-kit material. Their original bytes may describe the historical
private layout. Do not globally replace those references or repin an engine just
to modernize wording. Follow [development](development.md) and the current
unpinned engine guides for the main-repository build, while preserving the
selected source/notice correspondence.
