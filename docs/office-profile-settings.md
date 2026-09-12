Office Evaluation Profile Settings
===================================

The [Excel calculation experiment](excel-calculation-evaluation.md) showed that
seeding a fresh profile does not establish that the engine retains the requested
settings. Recalculation overrides disappeared during first use; an update-check
setting also returned as true. These findings apply to the evaluation harness,
which is not a production Office converter or an isolation boundary.

The harness now initializes normal conversion profiles without a document,
reapplies the requested settings, and verifies their saved declarations after
the rendering process exits. Each run uses a new short repository-local profile.
The `--version` probe and deliberate profile-length/environment experiments do
not receive the new initialization/post-render acceptance claim.

[OfficeProfileSettings](../tools/office-engine/Probe/OfficeProfileSettings.cs)
sets the macro, active-content and Python disable flags to true, macro security
level to 3, and automatic update checks/downloads and OpenCL to false. It uses
explicit XML Schema types, including the update-job argument properties. The
calculation experiment additionally sets its requested mode. Other initialized
settings are preserved; conflicting declarations for owned properties are removed
before one replacement is written. XML reads prohibit DTDs/external resolution
and have a 1 MiB character limit.

Verification requires exactly one matching property declaration and the expected
value. Missing, duplicated or changed declarations stop the evaluation rather
than allowing a successfully produced PDF to imply the settings held. The
requested settings and a successful post-process verification record remain in
the disposable profile for inspection.

This proves declaration persistence only. It does not prove that every engine
code path enforces these flags, that startup or document execution made no network
requests, or that hostile active content cannot execute. The pending
[OS isolation work](office-isolation-evaluation.md) and hostile-document tests
remain required. Only independently authored passive documents may run through
this harness.

Evidence (2026-09-11)
----------------------

All 17 profile declaration contracts passed: correct creation, seven changed-value
refusals, duplicate refusal/repair, both calculation modes and mismatched-mode
refusals, DTD/wrong-root refusal, and preservation of unrelated settings.

The default three-family run produced the expected two Word pages, one Excel
page and two PowerPoint pages. All PDFs passed qpdf and independent PDFium
page/text/render/geometry checks, including Word headers/page fields, Excel print
area/hidden sheet and PowerPoint hidden-slide policy. Original hashes remained
unchanged. The requested seven profile declarations passed post-process checks
for each family; update arguments retained explicit Boolean types.

Evidence is
`.codex-temp/office-engine/a56167ab9fb54686971491918ccd26f8/evaluation-01391ee0e6774c2bafaae5282c1a01e7`.
It includes `office-evaluation.json`, PDF/text/render output and
`independent-profile-verification.json`. The independent inspection checks source
and PDF hashes and all seven saved declarations in the recorded profiles.
The command log is `.codex-temp/office-profile-settings.log`.

The evaluation host builds in Release with zero warnings/errors, and repository
boundary/theme/78-document/whitespace checks pass. No Office processes remained
after the run. Foundation, production packaging, legacy exports and the full
calculation/path matrices were not rerun for this evaluation-only correction.
