Free Access And Repository Transition
=====================================

Status: in progress. Decision accepted 2026-10-30 under
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

The local implementation and source migration are committed. Candidate media
verification is still in progress. No remote archival, storefront retirement,
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
  engine. All ten native source hashes match. Packaged-worker corpus replay is
  recorded separately when complete.

Local-only evidence is under .codex-temp/free-transition-20261030. Reproduction
commands, source/input hashes and retained failure details are in its receipts
and logs. Version 1.6.0 was reserved but failed HEIF staging because the archived
build recipe still named the old private source location. Its reservation and
partial stage are retained; later candidates must use a new version. The next
1.6.1 attempt passed HEIF and stopped at the analogous AVIF recipe guard. Both
attempts are failed staging evidence, not accepted candidates. A clean-source
recipe test also exposed automatic line-ending conversion; explicit attributes
now preserve the reviewed HEIF recipe bytes in Windows checkouts.


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
