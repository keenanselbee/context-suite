Free Access And Repository Transition
=====================================

Status: local migration and candidate verification complete; live retirement
prepared but not executed. Decision accepted 2026-10-30 under
[0021](decisions/0021-free-product-and-public-implementation.md).

The owner authorized one free application, retirement of the private build
boundary and commercial selling, reviewed milestone commits, and a verified
release candidate. The unfinished PDF dark-mode changes in both repositories
are explicitly included and must be verified with the migration.


Milestones
----------

1. Preserve the starting working changes and identify source, installed,
   candidate, and engine versions separately.
2. Import only reviewed required noncommercial implementations, native bridges,
   and tests. Keep exact pinned source bytes and retain private history.
3. Remove commercial runtime and UI while preserving operation validation and
   media/publication safeguards. Old access files must remain untouched.
4. Make build, test, CI, packaging, and current receipts independent of the
   private checkout, retaining historical receipts and required engine inputs.
5. Verify contracts, real media/PDF workflows, free admission with legacy state,
   candidate payloads, and isolated clean-source builds. Record exact outcomes;
   do not infer installed or hosted acceptance from local checks.
6. Review diffs and commit independent milestones. Prepare an immutable free
   candidate with its source/engine receipts; preserve existing installations.
7. Update product/website copy. Prepare exact storefront and remote-archive
   retirement actions after verifying the migration; deployment and live account
   changes are separate from the local candidate.


Starting Evidence
-----------------

Public baseline: 275a6f9b56132d1e4d90808f763e6ee41cab360c.
Private baseline: 33ba1255e2ffcfd386dd29d6b181c6a016fc3763.
Both had authorized unfinished PDF changes. Local-only snapshots, file inventory,
and subsequent evidence belong under .codex-temp/free-transition-20261030.
The initial import copies 237 selected tracked noncommercial private files,
including current PDF modifications; it does not import commercial configuration.

At task start, working version was 1.5.5 and HEAD metadata was 1.5.3. The README
records 1.5.5 as installed and verified; this task has not re-established that
installation claim. Choose a new release version only after checking reservations.


Completion Evidence
-------------------

The local implementation, source migration and candidate verification are
complete. No remote archival, storefront retirement,
website deployment, or replacement of the installed application has occurred.

- Main decisions/terms: 3c18d7f; included PDF milestone: 838de52; free runtime,
  source, build and packaging migration: a64321c.
- Private PDF milestone: 3b5ded0; local retirement documentation and preserved
  inactive workflow: 2409fb3. Private source history/configuration remain private.
- Website free-product wording: 3f33d23, verified in isolation with the site build
  and focused smoke, projects, routes, layout and mobile suites. Other dirty
  website work was preserved; full-site publication checks remain separate.
- Fresh-source production compilation without a private checkout passed.
  Integration passed 5,368 contracts, DDS 121 and image engine/adapter 942.
  A generated Office fixture initially exceeded the existing 240-character path
  bound; shortening only the fixture layout resolved it. The original failure
  receipt and the passing continuation are retained separately.
- Main foundation passed 4,978 contracts; hidden views passed 165. Release
  packaging passed 35 checks, installer orchestration 20, selection/recovery 236,
  components 77, and fixture cleanup 26. These mocked installer checks do not
  establish native installation or upgrade acceptance.
- Five current native PDF suites passed 119 checks against the unchanged pinned
  engine. All ten native source hashes match.

Unsigned candidate **1.6.2** was built from clean source
bc35753c5eee0d7f9d73f59019a1ae343d38bd59. Its schema-two manifest records one source
revision and 2,913 candidate files. All selected engine families, product terms,
283 notice links, payload inventory, and the independently extracted archive
passed verification. Local-only artifact:
artifacts/release-candidates/446a7286eba943618b42ba330b0d517a/ContextSuite-win-x64-unsigned.zip
(217,007,733 bytes). SHA-256:
EDED10709B3F86955E3A7DA4372B3F2BB9092EB845A48B35E7884AAD73B37CFE.

The exact candidate passed 811 PDF/Optimize checks: six workflow modes (352),
62 exact historical documents (372), six changed current documents (36),
16 historical refusals (48), and one changed current document's resource-limit
refusal (3). Fifteen separate checks against the retained 1.5.5 worker confirmed
the existing limit and warning classification. All original corpus and preset
inputs, candidate files, worker, native engine and test host remained unchanged.

Seven prior corpus originals had changed since their historical hashes were
recorded. Six current versions converted; the seventh exceeds the existing
64-detached-artifact ceiling and is refused by both versions without output.
An early replay script also omitted the final historical correction for four
warning-bearing refusals: these deliberately use generic UnsupportedInput rather
than exposing parser text. Exact historical identities, retained final results,
native diagnostics and the old worker establish those expectations. Both failed
exploratory assertions and the passing independent continuations remain recorded;
no production bound or test-source expectation was weakened.

The installed 1.5.5 payload was separately compared with its retained inventory:
all 2,915 files still match. No normal-profile upgrade, registration, signing,
remote CI, deployment or publication acceptance is inferred from these checks.

Local-only evidence is under .codex-temp/free-transition-20261030. Reproduction
commands, source/input hashes and retained failure details are in its receipts
and logs. Version 1.6.0 was reserved but failed HEIF staging because the archived
build recipe still named the old private source location. Its reservation and
partial stage are retained; later candidates must use a new version. The next
1.6.1 attempt passed HEIF and stopped at the analogous AVIF recipe guard. Both
attempts are failed staging evidence, not accepted candidates. A clean-source
recipe test also exposed automatic line-ending conversion; explicit attributes
now preserve the reviewed HEIF recipe bytes in Windows checkouts.

The HEIF compatibility fix (d9ec4fe), line-ending rules (31e22ef), and AVIF/SVG
recipe retention (efed14f) preserve independently pinned historical identities.
All optional input families passed read-only preflight. Eight HEIF and seven
AVIF/SVG recipe checks also passed from a fresh committed Windows source archive.
Version preparation is recorded in bc35753; later closeout documentation does not
change the preserved candidate's source identity or bytes.


Retained Output And Cleanup
---------------------------

Compact fresh-source evidence was copied and hash-verified before cleanup review.
Both failed staging directories contain only files byte-identical to the verified
final stage: 491,044,576 and 502,389,595 bytes. Their permanent version reservations
remain required even though these partial copies are redundant.

Automatic approval review rejected both broad cleanup-script preparation and a
later bounded deletion of those two classified staging directories, with only
the reason "blocked by policy." No cleanup script was executed and no selected
directory was deleted. Keep the reviewed manifests and failure classifications;
do not treat the unused script as approved. Any later cleanup must recheck exact
paths, identities, dependencies and reparse points.

Significant local-only retained output includes about 947 MiB of failed staging
copies, 547 MiB of verified production staging, a 753 MiB candidate directory
including its archive, 182 MiB of PDF replay evidence, and a 590 MiB isolated
website verification worktree. Roughly 331 MiB of fresh-source test snapshots
and a 36 MiB committed-source check remain too. The website worktree retains
browser evidence and unfinished publication obligations; its dependency junction
must never be traversed during cleanup. Main/private Git history, engine inputs,
notices, receipts, existing installed/rollback payloads, and unrelated website
work remain protected. Purposes, reproduction commands and exact selections are
recorded in the local-only receipts and cleanup-manifest.json.


Prepared Live Retirement
------------------------

These are reviewable next actions, not completed account changes:

1. Push the reviewed main and private milestones only when publication is
   authorized; inspect the matching hosted checks. Provision the selected pinned
   engine assets before claiming manual production CI acceptance.
2. Archive only the existing production Context Suite product in Polar. Keep
   its benefits, older customer keys, downloads and order history intact. Polar
   documents that `is_archived` prevents new purchases while retaining existing
   customer benefits; removing benefits is unnecessary. Verify the current
   product/organization identity and checkout result at execution time. See
   [Polar product updates](https://polar.sh/docs/api-reference/2026-10/products/update-product).
3. Push the reviewed private retirement commits before archiving
   `keenanselbee/context-suite-private`. Keep its visibility private and retain
   the local checkout/history. Review outstanding issues/PRs before making the
   remote read-only; see [GitHub archival](https://docs.github.com/en/repositories/archiving-a-github-repository/archiving-repositories).
4. Deploy the scoped website wording through the normal verified publication
   workflow. Release binaries only after the separate installer, signing and
   customer acceptance decisions have been addressed.

The local-only live-retirement-plan.json records the exact privately configured
Polar target and intended update. Its account state has not been inspected in
this task. No keys, benefits, provider history, remote repositories or existing
installations are deleted by this plan.
