Repository And Application Size Reduction
========================================

Status: completed for the authorized internal candidate scope, 2026-10-13.
The Python-free reduced runtime, exact source/notice correspondence, safe cleanup,
combined 1.3.4 worker, performance budgets and unsigned installer are verified.
Office stays bundled. At the 2026-10-13 checkpoint, installed 1.2.9, reserved
candidates and unrelated work were preserved. See the [current project
status](../README.md#project-status) for subsequent source and installation versions.
Installation, registration, commits and publication remain separate actions.

Follow-up workspace relocation completed on 2026-10-13: the repository measured
28.31 GB at that checkpoint after moving 45.52 GB of historical outputs to Z: without
new compression. The final section records the backup location and recovery
procedure; earlier local paths describe their historical locations.


Scope And Acceptance
--------------------

The first experiment derives a runtime from the current source-built Office
selection, retaining Colibre and omitting only 19 alternative icon themes and
12 SDK tools/import libraries. The explicit
[reduction policy](../tools/office-engine/runtime-reduction-policy.json) preserves
the complete base selection and source record. The
[derivation tool](../tools/office-engine/Derive-ReducedRuntime.py) checks full
source membership and hashes, copies retained bytes into a new scratch tree,
then verifies both trees. It does not change production pins or execute Office.
This replaces neither the source kit nor the original full runtime.

The measured exclusion is 71,997,680 bytes (68.66 MiB). The derived runtime has
3,800 files totaling 475,047,993 bytes, compared with 3,831 / 547,045,673 bytes.
This reduction is selected in the verified isolated 1.3.3 candidate. Preserve drawing
resources, fonts/locales, Python, notices, source availability, shared security
guards and every currently supported conversion.

Acceptance requires paired full/reduced exports from the same inputs, fonts and
settings: Word international text, styles, revisions and images; Excel charts,
calculations and print layout; PowerPoint slide order, shapes and images.
Compare page geometry, extracted text and rendered pixels. Exercise damaged-input,
isolation, cancellation, resource and recovery checks and measure runtime cost.
Only after these pass may inventory, private runtime pins, source derivation and
notice records advance. A new unused product version must integrate the latest
PDF readability source and all selected engines, with real-worker and installer
artifact verification. Reserved versions must not be overwritten.


Protection And Cleanup
----------------------

`artifacts/size-reduction-baseline-20261012` retains two independently read and
SHA-256-verified source ZIPs, including tracked/untracked public/private files and
Git metadata, plus source identities and a filesystem size inventory. This local
rollback evidence contains private source and must never enter public archives.
The filesystem scan is a non-atomic working-directory measurement; source ZIPs
are separately checked against their captured hashes after copying.

Before deletion, retain exact protection/cleanup manifests and verified installed
and rollback payloads. Protect all version receipts, live registrations/payload,
reserved stages, archives with their metadata, pinned source/build inputs and
current test evidence. In particular, preserve the E76FDBFA PDF-dark host and its
Waterfall review. The explicitly reserved aborted historical archive stays.

Cleanup must archive needed history, verify archive integrity and every extracted
file in a fresh restore directory, then recheck exact original identities before
removing only manifested paths. Reject out-of-root paths, reparse points, protected
descendants and changed files. A blanket deletion of scratch or artifacts is not
permitted. Preserve failed test/build evidence until classified.

The release workflow now removes its temporary verification extraction only
after verification and the archive hash receipt succeed. Failure evidence stays.
Workspace watcher exclusions omit generated scratch, artifacts and read-only
reference trees without hiding source or the separate private repository.


Conditional And Deferred Work
----------------------------

Python removal requires a distinct source configuration and rebuilt service
registry, not deleting Python from this runtime. The pinned upstream configure
supports `--enable-python=no`, while still requiring build-time Python. Current
services register four Python components even though the host disables execution.
Evaluate disk/build prerequisites and the complete Office matrix before adopting
such a candidate. The roughly 42.7 MiB Python subset is an upper-bound inventory,
not a verified saving. Keep the current runtime if this experiment cannot meet
the acceptance gates.

PDF DLL consolidation is deferred: current adapters hash and lease engine-local
dependencies. Sharing them would require a separate loading/integrity design.
Offline installer prerequisites and required source/notices remain included.


Evidence And Remaining Work
---------------------------

- Protected payload identities and classified cleanup manifest with restore proof.
- Full/reduced Office fidelity, failure, isolation and timing comparisons.
- Reviewed current inventory, source derivation, notices and worker pins.
- Conditional Python evaluation outcome and any remaining limits.
- Combined new-version worker/installer artifact verification.
- Before/after checkout, staged app and distribution sizes with recovery steps.

The selected reduction, exact cleanup, source/provenance integration, new-version
worker and unsigned installer evidence are complete below. A separate no-Python
native build experiment is now configured and compiling; Python remains in the
selected runtime. The repaired interruption matrix passes all nine scenarios,
with Excel measured at an incomplete workbook write. The pre-existing equation
limitation remains explicitly excluded from any fidelity claim. Do not
mark the whole goal complete from the successful size measurements alone.


Initial Experiment Evidence
---------------------------

The full runtime is retained at
`.codex-temp/office-repaired-application/8825206a1d11484ca725f28992a67c91/application-engine`.
The derived experiment is `.codex-temp/office-reduction/initial/office-engine`,
with inventory SHA-256
`90D2BC40C36870523331CF9A9C97A3B19C5BA963B6EFCEA5DF160C14E619954D`.
Its recipe excludes exactly 31 pinned files and checks every retained byte.
The private lease, public staging selection and supplemental notice records now
select the same derived inventory. The packaged source record carries the exact
exclusions and base archive/inventory, policy and derivation-tool hashes. Staging
rejects a missing/changed recipe or altered original source/build identity.
The complete original source kit and notices remain unchanged.

Full and reduced real-worker exports pass six authored matrices: multilingual
Word layout (1 document), PowerPoint layout (1), slide order/hidden slides/notes
(3), Excel charts and print layout in both calculation modes (4), Word font
styles (15), and Word revisions (13). The
[paired comparison](../tools/office-engine/Compare-ReducedRuntime.py) verifies
identical document parts, PDF page geometry, text with bounds and all rendered
pixels at 144 DPI across these 37 documents. Full baseline and reduced output
evidence is under `.codex-temp/office-reduction`. Original documents and native
profile cleanup pass; independent authored oracles are recorded separately.
Representative Word and PowerPoint renders were visually inspected.

The existing full-runtime revision oracle accepts a move-final spacing
difference from its clean reference within 0.071 points; this is not an exact
match to that separate reference. The reduced output is pixel-identical to the
full-runtime output and does not introduce that difference. These fixtures do
not prove arbitrary Office fidelity.

The live payload and rollback copies for 1.2.8 and 1.2.5 match their full retained
inventories (4,271 / 4,271 / 4,257 files). The protected manifest binds all 32
version receipts and the current E76FDBFA PDF-dark host. The completed cleanup
selection contains twelve older scratch roots, approximately 5.706 GiB logical.
Its 2,922,893,362-byte archive was fully extracted and hash-checked before
31,959 files and 3,052 directories were removed. This saves 3,204,226,687 bytes
(2.984 GiB) before the small receipts; it is not the entire checkout delta. Use
[the archive helper](../tools/maintenance/Archive-WorkspaceHistory.py) with that
explicit selection; never infer deletion permission from folder age alone.

The derivation safety fixtures pass eight checks. Packaging cleanup fixtures
pass success, verifier/hash/receipt failure retention, path and junction cases.
The workspace history helper passes sixteen bounded fixture checks, including
complete restore hashes, protected/path refusals, changed inputs and retained
failed extractions and archive changes during restore. The actual cleanup ran
the preceding helper revision; the post-cleanup check independently confirms the
retained archive hash. Repository boundary, system-theme and 226-document checks
pass. This is an implementation checkpoint, not completion of the goal.


Additional Validation And Limits
-------------------------------

The reduced runtime passes 69 native runtime membership/read-lease checks and
18 source-record binding checks. Resource enforcement passes 61 checks using an
authored probe; that is separate from real-engine memory measurements. Generic
Word, Excel and PowerPoint execution passes 43 checks for both full and reduced
workers, including original preservation, publication cleanup and recovery.

The repaired interruption matrix passes cancellation, worker loss and application
deadline checks with following exports for all three Office families: 102 checks,
18/18 native profiles removed. Word and PowerPoint retain observed PDF growth;
Excel uses the existing exact-file debugger pause at an incomplete workbook
write after PDF export. This is not proof of Excel interruption during a partial
PDF. A focused worker-loss rerun after tightening the following Excel workbook
hash/byte checks passes 34 checks, with 6/6 profiles removed.

Evidence is retained under `.codex-temp/office-worker` in runs
`828c6b0b3315463db11f11c0f2db4640` and
`3997a6c7f3794b9ab6aa17a1f0d63a3c`. Main-agent review verifies result counts,
stop phases, host termination, all profile removals and the focused run's current
harness source hashes. Changes are test-only. Earlier growing-PDF failures and two
unsuccessful PDF-breakpoint attempts remain evidence: attaching after host
discovery could miss fast writes, so that proposed helper extension was discarded.

The larger interruption run observes an OfficeHost peak working set of
270,467,072 bytes and sampled private memory up to 337,825,792 bytes. Worker-to-host
creation intervals are 3.125-3.430 seconds, including preparation and integrity
verification. These are workload observations, not memory ceilings, cold-start
benchmarks or proof of a speed improvement. A separate cached runtime read-lease
measurement takes 2.64 seconds to acquire and 2.30 seconds to reopen.

Extra schema-valid Word equation fixtures expose a pre-existing limitation:
both full and reduced runtimes omit inline and display OMML equations. The
prepared importer requests a StarMath embedded object, while the existing
active-content policy permits newly created internal charts only. Matching
omissions are not equation fidelity. No formula-support claim is added and no
security guard is weakened as part of the packaging reduction. A separate fix
needs explicit safe formula support or admission refusal, with regression tests.

The derived runtime ZIP is 166,291,005 bytes versus the retained full ZIP's
223,036,511 bytes: 56,745,506 bytes (54.12 MiB) smaller. Every ZIP member has been
read and hash-checked, with exact source membership retained in the receipt.
The combined 1.3.3 staging inventory is 610,918,241 bytes / 4,240 files versus
1.3.2's 682,908,839 bytes / 4,271 files, excluding the inventory itself. The
71,990,598-byte net application reduction also includes the latest PDF host and
version/provenance metadata differences. The distribution ZIP is 227,254,041 bytes.

Python remains bundled. Exact prepared-source inspection confirms
`--enable-python=no` and conditional pyuno registration, but there is no rebuilt
and validated no-Python candidate. No Python files or service entries are removed.
The 42.7 MiB figure remains an upper bound, not an achieved reduction. The actual
Cygwin/Perl path probe expands this repository's Windows short alias back to its
space-containing path, which upstream autogen rejects. The earlier
`python-evaluation.json` remains historical source-inspection evidence.

The user authorized the fresh external workspace
`G:\ContextSuite-OfficeBuild\python-free-20261012` for this experiment. The reviewed
proposal, settings, input hashes and path probe are retained under
`.codex-temp/python-free-proposal-20261012`. At admission G: had approximately
1.14 TiB free. The experiment retains the two-job and 100-GiB workspace limits;
the latter is a cap, not an assertion that the build requires that much space.
Existing external tools, source trees and builds remain inputs, not write targets.

`Configure-Source.py --python-runtime none` adds `--enable-python=no` to
the reviewed options. Its default remains bundled Python. The first actual
configure attempt refused to proceed because native Windows configuration still
requires explicit build-time Python. The failed build directory and receipt are
preserved; inputs were unchanged and no compilation occurred. A separate retry
uses `--build-python` with the existing Python executable, hash-bound in the plan,
to set upstream `PYTHON_FOR_BUILD` while disabling runtime Python.

The explicit build-Python configuration passes in 120.15 seconds, with all
captured inputs unchanged. Its receipt is
`G:\ContextSuite-OfficeBuild\python-free-20261012\evidence\source-configure-e6e5d54325f746d2b1ead10a33cf13d5`.
`config_host.mk` confirms `DISABLE_PYTHON=TRUE`, no PYUNO/PYTHON build type, and
the explicit build-time Python executable. The fixed-target build plan passes
and native compilation has started in `build-explicit-python`; this is not yet a
compiled or accepted runtime. Failed first-attempt evidence remains separate.

The first native attempt stopped with exit 2 after 5,058 seconds: HarfBuzz's
Meson compiler check could not locate `MSVCRT.lib`. The configured library exists;
an authored compiler control verifies that the missing native search-path
environment causes failure and the configured paths allow compilation/execution.
A first environment fix exposed gbuild's separate target-specific `INCLUDE`
variable and stopped after 60 seconds. Both terminal receipts report unchanged
inputs and no budget violation. Their logs, failed Meson directories and original
driver versions remain preserved under `evidence/resume-msvc-environment-01` and
`evidence/resume-msvc-environment-02`.

The corrected driver supplies configured paths through `gb_COMPILER_SETUP`,
clears inherited `LIB`/`INCLUDE`, and passes 27 driver checks including the
target-specific variable conflict. Incremental attempt
`evidence/source-build-bf2c646576964bbca9e6e4cfd51c9402` uses the same source,
configuration and compiled tree. It completed with exit 0 after 8,428 seconds,
unchanged inputs and no disk-budget violation. Its wrapper receipt is under
`evidence/resume-msvc-environment-02`. This is a build-environment repair, not
proof of an accepted runtime. The future source kit must include this revised
build helper as well as the revised configure helper.

`evidence/configure-helper-reconciliation-01.json` preserves the original helper
bytes matching the historical configure record and verifies that the revised
helper produces an identical configure plan apart from its own input hash.
The configure-used functions/constants also match structurally. Acceptance
checks the original configure helper at its retained path and the current build
helper against the resumed build; no historical receipt or configuration is
rewritten. HarfBuzz has subsequently compiled and linked successfully.

The native acceptance runner's five-file source folder was restored from the
verified September scratch archive. All four recorded source hashes and both
linked public helper hashes match its original build receipt. A fresh runner
under the authorized workspace changes only workspace/build paths and linked
helper locations; main-agent review verifies this exact transformation and its
binary/test hashes. Ten synthetic transcript and seventeen process-lifetime
checks pass. The complete 22-case native suite subsequently passed against the
new renderer. Its 23 authored fixture files are copied into the fresh build
and verified against the earlier accepted fixture hashes, with unchanged original
files and a matching native test source. Their document relationships contain no
external targets or old build paths. The fresh command/runtime manifest and
runtime assembly remain required. The detailed sequence is retained in
`.codex-temp/python-free-proposal-20261012/acceptance-plan.json`; historical
runtime counts, Python notice passes and old service registries cannot be reused.

The staged driver `.codex-temp/validate-python-free-native.py` now provides
separate preparation, command-guard, native-execution and postverification steps.
Each requires the successful final build receipt and unchanged configure/build
inputs before writing acceptance files. All four incomplete-build refusals pass,
and its expected 22 case names match the pinned native source. This verifies the
preparation guards only. Actual native execution is recorded separately below.
The current receipt is `evidence/native-acceptance-driver-review-03.json` under the
authorized workspace.

`.codex-temp/assemble-python-free-runtime.py` is prepared for the subsequent
native-accepted build. It requires current inventories, all three delivered OOXML
shape resources, source-matched host and CRT identities, and fresh registries
with resolved component files and no Python providers. The full experimental
runtime retains its theme/SDK membership to separate source-build effects from
the accepted trimming experiment. It correctly refuses while compilation is
incomplete and has not yet assembled or accepted a runtime. The historical host
build folder is archived; its source/hash receipt remains embedded in
`G:\ContextSuite-OfficeBuild\evidence\office-review-runtime-03.json`, and the
same pinned executable remains in the accepted reduced runtime.

Nine authored registry/preparation checks pass: complete local components,
Python provider/module refusals, missing or external component refusals, path
traversal and unknown roots, empty registries, and no runtime writes before build
completion. These use inert fixtures rather than a compiled renderer. The
receipt is `temp/runtime-assembly-contracts-01/results.json` under the authorized
workspace. A continuation process holds the exact live build process handle,
verifies its creation time and image, and waits for successful final receipts
before running the hash-bound native stages. It never restarts the build and
stops on any failed stage. The first sequence stopped on the failed build without
running native tests. Its replacement holds the resumed build's exact process
handle, with evidence under `evidence/native-acceptance-sequence-03`. Sequence 02
was replaced before native acceptance to bind the configure-helper reconciliation;
only its watcher was stopped, and the active compilation remained untouched.

Sequence 03 completed all ten command guards and the 22 native cases, with
unchanged native inputs and no remaining owned process. Assembly then correctly
refused three generated Python wizard registrations. The upstream wizard module
declared its four Pyuno targets unconditionally even though the package list
honored `DISABLE_PYTHON`. The isolated source patch now guards those four targets;
it does not edit registry contents. The original source matches the pinned source
archive, and four real Make evaluations prove unchanged Python-enabled targets
and removal of only those four targets when Python is disabled
(`evidence/python-free-wizard-condition-02/results.json`).

The outer Make wrapper dropped a proposed `--what-if` flag; its dry-run evidence
is retained. A direct gbuild attempt first required explicit source/build paths.
The corrected invocation regenerated the registries in 43 seconds, exit 0, with
unchanged build inputs and no disk-budget violation. Receipt
`evidence/python-free-wizard-registration-03/results.json` binds the source patch,
original registries and incremental build. Exactly two of 3,755 inventoried files
changed: the generated and installed service registries. Each lost exactly the
fax, letter and agenda Python services; every non-Python component and every
other inventoried file remained identical. This source patch must enter any
future accepted source kit.

Sequence 04 preserves the first native profile under its evidence directory and
uses fresh preparation, guard, launch and postverification receipts. All ten
guards and all 22 native cases passed again against the repaired inventory.
Assembly then refused eight registered Java components whose JARs were not
delivered by the selected build targets: reportbuilder, ScriptFramework,
ScriptProviderForJava, smoketest, form, query, report and table. The registry
closure gate remains intact; payload completion and another inventory-bound
acceptance pass are still required. No experimental runtime has been selected
for shipping, and no Python-free size saving is claimed.

The configured Java/report-builder deliverables were built through nine
upstream `Jar_*` targets and `Package_reportbuilder_templates`. The explicit
reportbuilderwizard target closes a manifest-only dependency from report.jar;
declared JAR dependencies are built by gbuild. The monitored incremental build is
`evidence/source-build-a6bb05ff2b5f4fe1ab02224788fa387f`, with preparation under
`evidence/python-free-java-deliverables-01`. It passed in 97 seconds with unchanged
inputs and no budget violation. Its 31 additions comprise 25 JARs, the report
template, javamaker and four javamaker build files; all prior native files remain
identical. `verification.json` verifies JAR integrity, local manifest dependencies
and all four service registries against that exact expanded inventory. This adds no JRE
and does not assert Java-dependent application functionality or adoption.

Sequence 05 preserves the second native profile and records another successful
ten-guard/22-case native replay. Full experimental runtime assembly then passed:
2,380 files, 507,715,792 runtime bytes, inventory SHA-256
`68372A0C9507E2D8388E988186D24EC885B935A7779A891A957AD77BEBEFF29B`.
This is an untrimmed experimental payload, not an adopted or distributed saving.
The twelve restricted-host Word/drawing exports subsequently passed every
authored oracle, exact historical raster comparisons and profile cleanup
(`evidence/python-free-direct-host-01/results.json`).

Worker preparation changes only four inventory constants in the isolated
application snapshot and copies the verified payload. The source build omitted
three empty package-cache directories required by the application lease; these
are created only in the worker copy and explicitly recorded in
`evidence/python-free-worker-preparation-01/results.json`. The accepted direct-host
payload and original checkout remain unchanged. The 37-export worker/fidelity
sequence stopped on its first Word layout export before PDF publication
(`evidence/python-free-worker-fidelity-01`). The worker and contracts compiled
without warnings or errors, and run inputs remained unchanged. Its retained
ownership journal records stopped processes, revocation of all five grants and
profile deletion. A separate diagnostic using the unchanged compiled adapter
identified the existing 240-character runtime-path limit: two files in the test
worker's long snapshot path reach 242 characters. Shortening only the context
path still fails; using the shorter runtime path succeeds. All three diagnostic
profiles and their mappings were removed, with original input hashes unchanged
(`evidence/python-free-adapter-diagnostic-01/results.json`).

A fresh snapshot at the authorized workspace's `a` directory copies the same
1,117 source inputs, experimental runtime and 49 comparison inputs. All copies
match their recorded hashes, and three locked managed restores pass. Its longest
worker runtime path is 220 characters. The production path policy and unchanged
test implementations are preserved; the original snapshot and failed run remain
available. `evidence/python-free-short-snapshot-01/results.json` records this
preparation. The fresh sequence in `evidence/python-free-worker-fidelity-02`
passes all 37 worker exports across six modes, their complete profile/context
cleanup, and exact paired document-part, page, text/bounds and 144-DPI pixel
comparisons. This establishes the recorded fixture coverage, not arbitrary
document fidelity; the prior equation and revision-reference limitations remain.

`evidence/python-free-worker-safety-01/results.json` records passing checks for
the actual experimental worker: 69 runtime-integrity/lease checks, 61 launcher
resource checks, typed worker refusals, 65 I/O-denial/recovery checks and 102
active-interruption checks. All nine I/O profiles and eighteen interruption
profiles were removed. Cancellation, worker loss and application deadline each
cover Word, Excel and PowerPoint and a subsequent successful export. Excel still
uses the explicitly observed unfinished-workbook-copy phase, not partial PDF
generation. Source, fixtures and worker runtime identities remain unchanged.
Source correspondence, current component notices, final trimmed-runtime and
combined-candidate verification remain required before Python-free adoption.

The initial `application-snapshot-01` captured 1,117 verified application,
private, test and build-input files (17,950,054 bytes). It is private test
material, not a public source artifact. Initial managed dependency restore and
compilation of the worker, core contracts and private PDF/Office contracts passed
with isolated output/cache paths before experimental pin selection. The later
four-pin change and short-snapshot worker acceptance are recorded above. The
original source, restore and build
receipts are `evidence/application-snapshot-01.json`,
`evidence/application-snapshot-restore-01/results.json` and
`evidence/application-snapshot-build-preparation-01/results.json` under the
authorized workspace. Shipping selection remains unchanged.

Two historical restricted-host export harnesses are restored from the verified
September archive (six source files each), checked against their recorded source
hashes, and copied into the isolated snapshot. Only their path constants/guards
and Core assembly reference change; the current production sandbox, process and
ownership sources remain unchanged. Both harnesses compile with zero warnings
or errors and each passes 25 compiled-profile checks. A separately verified copy
of their original PDFium probe preserves the historical rendering oracle.
`evidence/direct-host-preparation-01/results.json` records all current inputs and
outputs. This preparation did not establish runtime compatibility or security
acceptance; the subsequent twelve-export results are recorded above.
The prepared `.codex-temp/run-python-free-direct-host.py` requires the completed
native sequence and assembled runtime before creating evidence or profiles. Its
incomplete-build refusal is verified, and all twelve retained baseline PDFs and
paired original rasters are rechecked. Actual execution must pass each authored
text/geometry/preview oracle, exact baseline pixels, unchanged inputs and all
twelve profile/mapping removals before worker integration proceeds.

The prepared `.codex-temp/prepare-python-free-worker-runtime.py` then binds those
direct-host results to the exact assembly receipt and rechecks their PDFs and
rasters. It verifies all 1,117 original snapshot inputs and rejects additional
source files, copies the verified runtime into snapshot scratch, preserves the
required empty package-cache directories, and changes only four inventory
constants in the snapshot's private runtime lease. The host, CRT pins and
security checks stay unchanged. Syntax, source membership and refusal before
build completion were verified at preparation time. Its retained revision 03
completed the worker copy and pin selection described above. The main checkout's
runtime selection is unchanged.

`evidence/comparison-inputs-01.json` records 49 verified files copied into the
snapshot: the existing PDF engine/renderer, their notices/dependencies, and six
authored ordinary/large interruption fixtures. Original identities are unchanged.
`evidence/fidelity-baseline-01.json` revalidates both retained sets of 37 authored
source/PDF outputs across six modes, their reports, applicable fixture-source
hashes and recorded fonts. It preserves the existing equation limitation rather
than treating equal omissions as fidelity. The prepared
`.codex-temp/compare-python-free-fidelity.py` uses the unchanged paired-comparison
implementation with the exact original baseline report and a snapshot-local
candidate report. Revision 03 routes the accepted shorter snapshot; all 37
new-runtime comparisons now pass with the freshly assembled inventory and
successful unchanged-input execution.

While the native continuation sequence is live, do not invoke its same one-use
preparation/execution/assembly stages manually. Inspect that sequence's terminal
receipt before resuming after compilation, and preserve any failed-stage evidence.

The actual no-Python configuration selects 134 upstream dependency archives.
Print-only expansion of `Makefile.fetch` with `make -n` verifies every selected
archive against the retained kit without executing downloads or changing inputs.
The six retained but unselected archives are CPython 3.12.14, bzip2 1.0.8,
libffi 3.5.2, Meson 1.8.3, SQLite 3.53.4 and xz 5.8.3. This is a source dependency
selection result, not a measured runtime saving. The receipt is
`evidence/dependency-fetch-python-free-01/results.json` under the authorized
workspace; kit04 and the build's copied kit remain intact.

If the experiment passes, its fresh source kit must include the revised configure
helper, explicit build-time interpreter identity, current configuration/build
receipts and this dependency selection. Preserve every earlier kit and record.
The read-only `.codex-temp/assemble-python-free-source-kit.py` preflight verifies
the retained kit's 168 manifest members, all 140 dependency archives, the four
current public build/configure helpers, configure/build input identities and the
134-selected/six-unselected archive split. Its plan is retained in
`.codex-temp/python-free-source-kit-plan-01.json`. Actual assembly is deliberately
unavailable in that original planning script; its `--execute` refusal and absence
of output were checked. Subsequent implementation and current status follow.
Final component notices must bind the completed new kit and therefore follow
source-kit assembly, while remaining mandatory before runtime adoption.
Component notices must be regenerated from the new native-file inventory and
new linker dependency records. The existing Python notice checker specifically
verifies 675 packaged pip/setuptools files, and its preamble says those packages
are in the runtime. Neither that pass receipt nor its runtime claim applies to
the experiment. Any replacement notice policy must explicitly verify Python's
absence and accurately distinguish retained source/build notices from distributed
runtime components; merely changing inventory hashes or suppressing the old
checker is insufficient. A subsequent theme/SDK derivation also needs a policy
bound to the new full runtime and its source record, not the old base hashes.

The reviewed `.codex-temp/python-free-kit-tools-01` assembler has now created
`tools/office-source-kit-python-free-01` inside the authorized workspace. Its
171 files include the manifest and all 140 retained dependency archives. Manifest
SHA-256 is `0B09F156345C3574D6E4EB88F37491545F618289822CE90F56BFF37B347199E6`.
It retains the original three patch groups and adds the verified wizard patch;
the four public configure/build helpers are updated, and a separate Java-closure
helper reproduces the ten additional targets. The actual new kit's read-only
Java plan matches the historical shell command exactly, with receipt
`evidence/python-free-java-reproduction-plan-01/results.json`. No additional
native compilation is claimed. Prior kits and source-correspondence evidence
remain intact; the new kit explicitly marks correspondence and notices pending.

Fresh extraction to the workspace's `s` directory passed the kit-contained
preparation helper: 148,166 regular files, 56 materialized links, nine modified
sources and three additions. Full prepared-to-compiled byte comparison passes
under `evidence/python-free-source-correspondence-02`: all 148,225 files match,
with no missing, changed or generated extra files. The original
comparison process stopped before writing its result; its successful extraction
and inputs were preserved. The resumed comparison revalidates those identities
and writes fresh evidence without extracting again. Notice work accounts for
the newly delivered Java components and attributes `sqlite3.dll` to SQLite
embedded in NSS, rather than the unselected standalone SQLite archive. The
historical notice packets are preserved.

The isolated notice draft in `evidence/python-free-component-notices-01` has
been generated and independently recomputed successfully: 83 components, 304
native/JAR runtime files, 244 dependency-target records and 364 notice blocks.
Its verifier checks source archives, native output/CRT correspondence, Java
module/project/package recipes, actual service-registry XML and Python runtime
absence. Six authored refusal tests pass. The separate
`evidence/python-free-component-notices-01-verification.json` binds the packet
and tooling. This draft does not replace production notice checks or establish
source-correspondence acceptance; those remain integration work.

The Python-free theme/SDK experiment is now derived in the authorized workspace
at `a/.codex-temp/python-free-reduction-01/office-engine`. Its 32 exact exclusions
remove 72,107,990 bytes, leaving 2,348 runtime files and 435,607,802 bytes.
Every retained file is unchanged. This is 39,440,191 bytes (about 37.61 MiB)
smaller than the already accepted 475,047,993-byte reduced runtime; it is an
runtime measurement, not an app or installer saving. The complete
Python-free base ZIP is 211,678,035 bytes, SHA-256
`86C79506F4BB834798358C0BF8383E211FF0DBB0EE44C617D68C2A9A2BDC8E51`.
Every archive member was compared against its pinned original. The derivation
and archive receipt is `evidence/python-free-reduction-01/results.json`.

A new short-path snapshot `b` contains the same accepted application sources
with only four Office lease constants changed for the reduced inventory.
Source-copy verification and three locked restores pass. All 37 exports in the
unchanged worker fidelity matrix pass exact authored source, page geometry,
text/bounds and 144-DPI pixel comparisons against the original full-runtime
baseline. The receipt is
`evidence/python-free-trimmed-worker-fidelity-01/results.json`. The original
full-runtime snapshot `a` and all its evidence remain intact. Trimmed-runtime
safety also passes: 61 resource checks, 69 runtime integrity checks, safe worker
refusals, 65 I/O failure/recovery checks with nine removed profiles, and all 102
interruption checks with 18 removed profiles. The acceptance receipt is
`evidence/python-free-trimmed-worker-safety-01/results.json`; its captured
sources, fixtures and worker bytes remain unchanged. The Excel interruption
observation remains the bounded unfinished-workbook-copy phase. Source/notices
integration subsequently passed and the runtime is selected in candidate 1.3.4.

Production packaging now recognizes an explicit `pythonRuntime` selection.
Missing mode preserves bundled behavior; `none` refuses Python inventory files,
Python service registrations and stale supplemental Python notices. Mandatory
component notices remain bound to the source kit and runtime. The v2 portable
notice verifier keeps v1 behavior separate and requires independent selection,
source correspondence and exact archive-derived notice text. Six Python-mode,
eleven companion and 81 notice-index checks pass. Historical bundled
notice ordering is preserved. The selected engine is now Python-free; immutable
earlier candidates and the installed application remain unchanged.

The Office execution and performance harnesses now request verification of the
WEM, PDF-dark and AVIF/SVG engines when those families exist in the combined
candidate. Their owning verifiers still reject incomplete or changed engines.
Both harnesses produce the same verification flags for immutable candidate
1.3.3; its complete production verification passes with 283 notice links.
This check verifies packaging compatibility, not new performance measurements.
The current source-record bindings also pass all 18 checks after the packaging
changes. The benchmark still needs the final Python-free combined candidate.

The accepted experimental runtime and immutable source kit now have verified
archives, recorded in `evidence/python-free-deliverable-archives-01/results.json`.
`tools/python-free-runtime-reduced-01.zip` is 155,088,620 bytes, SHA-256
`BF31D4DE9A3046CE945D07706ED55955513E89625EF3038308799E12159903AA`.
This is 11,202,385 bytes smaller than the previous reduced Office ZIP; the final
application ZIP and installer have not yet been rebuilt. The source kit ZIP is
1,134,557,071 bytes, SHA-256
`EC0235C6D97B4EB28F1488C2258A91E974A35702DC203DEDA78112CF6602D0A5`.
Every archived member matches its original, and both source trees are unchanged
by archiving. The source-kit manifest retains its pending correspondence status;
the full comparison and separately bound acceptance receipt must complete before
shipping selection changes. These retained G: archives add evidence storage;
they are not part of the installed application or a new cleanup saving.

Twelve configure checks pass, including real pinned-kit validation, comparison
against the retained successful baseline with only its dependency directory
relocated, isolated experimental options, explicit build-Python hashing,
no-write CLI planning and path/override refusals. The fresh workspace contains a
complete hash-verified copy of source kit 04, freshly prepared sources and
independent caches. Configuration must report `DISABLE_PYTHON=TRUE` before the fixed native
build targets run. The one-worker Python build recipe stays pinned because
build-time Python can still be required. No runtime adoption follows compilation
alone: rebuilt service registrations, source provenance, security and paired
conversion/fidelity checks are mandatory. Failure retains the selected Python
runtime and the experiment's evidence.


Combined Candidate And Recovery
------------------------------

New reserved version 1.3.3 uses stage
`artifacts/production-staging/872d5798df0743a8811dac60e90e84c6`, inventory SHA-256
`A0978659BD4314F7F485BDF11F954D6D01D96C04F9C6D537C6273791776288AC`.
The reduced Office inventory and E76FDBFA PDF-dark host are verified in that exact
payload. The production build, 336 icon-frame checks, 283 notice-index links,
engine allowlists, dependency/version checks and release archive round trip pass.
The successful `archive-verification` extraction is removed by the new workflow.

The combined worker passes the 5,177-check foundation suite, 43 real Office
execution checks, 14 PDF dark-mode workflow checks and 29 PDF failure/cancellation
checks. Office output-denial and following-export tests pass 65 checks on the
reduced engine. Native resource, integrity and fidelity evidence is described
above; Excel's interruption phase and the native equation limitation remain
explicitly bounded. The first combined Office invocation used an invalid
test-evidence path and was refused before engine execution; the corrected fresh
Office evidence run passed. It did not require an application change.

Distribution candidate:
`artifacts/release-candidates/7fd1fd9c7f904a1bb422875c50db09fa/ContextSuite`.
Its archive SHA-256 is
`9BF97871037ABDDC459A19D889E42E359630495378921238DF31B65713388D3F`.
The unsigned internal installer is under
`artifacts/installer-candidates/480447d69ad6407aab6e2d8d53cae07e`.
Compilation and source-payload/hash verification pass; 20 installer contracts
pass with mocked Appx operations. No install, registration, native upgrade,
signing, commit, push or publication is performed. Unsigned identity packages
remain non-installable through the guarded installer.

Post-cleanup verification finds no missing baseline source files, no unrelated
source changes, unchanged public/private Git heads, all 32 prior version receipts
unchanged, and unchanged live/rollback payload inventories and contents. The two
source rollback ZIPs and full original Office runtime remain retained. The live
1.2.9 payload needs no rollback because it was not modified.

Reproduction and restoration entry points:

- Derive from the retained full runtime with `Derive-ReducedRuntime.py` into a
  fresh scratch output; compare using `Compare-ReducedRuntime.py` and rerun the
  native worker checks before selecting a different runtime.
- Use `Test-OfficeSourceRecord.py`, `Test-OfficeRuntime.py` and
  `Stage-OfficePayload.py` to check selection, leases and exact staged membership.
- The precise 1.3.3 engine/build arguments are retained in
  `.codex-temp/size-reduction-133-build-inputs.json`. A changed production build
  must use a newer unused version and fresh stage, never overwrite 1.3.3.
- Verify compacted history with `Archive-WorkspaceHistory.py verify --run
  scratch-20261012-size-reduction`, then extract to a new empty scratch location
  and copy back only selected absent paths. Preserve all archive receipts.
- `artifacts/size-reduction-baseline-20261012/postcheck.json` and
  `final-sizes.json` record preservation and measured logical sizes. Baseline
  source ZIPs contain private source/Git metadata and are not public deliverables.

Final measured distribution sizes are 227,254,041 bytes for the ZIP and
231,225,882 bytes for the unsigned installer. All 4,235 application source files
fed to the installer match the release candidate; its other compiler inputs and
final executable also match the retained receipt.

The whole workspace grows from 65,528,056,401 to 68,542,093,490 logical bytes
(+3,014,037,089 bytes, about 2.81 GiB) despite the 2.984 GiB cleanup saving,
because the new tests, candidate, archive and installer build evidence are
retained. Both non-atomic scans exclude reparse entries and the baseline evidence
directory, which separately holds about 181 MB. Do not describe the cleanup
saving as an overall checkout reduction. Future retention work must classify
those copies explicitly and preserve immutable release identities and recovery
evidence; it must not blanket-delete the new outputs to improve this number.


Python-Free Selection And Candidate 1.3.4
----------------------------------------

The conditional experiment is accepted for internal candidate integration.
Office remains bundled. The selected runtime contains 2,348 files / 435,607,802
bytes, inventory SHA-256
`085E2D0DDE0B7F496FBE1E2562E410C569AD22CB44A6C4EA1C77DC0712690ED8`.
It saves 111,437,871 bytes (106.28 MiB, 20.37%) against the original full Office
runtime, including the preceding theme/SDK reduction. This does not imply a
broader conversion capability or change the documented native-equation limit.

The immutable new kit's pending correspondence entry is superseded by the
separately pinned `evidence/python-free-source-correspondence-02/comparison.json`;
the kit itself was not rewritten. Both 148,225-file inventories have SHA-256
`24FAA1659A3C4A976455D4BC8E8357B5619D7E5B07B4E1CC8891B5B4EDB0222B`.
There are no generated extras to waive. The actual build selected 134 dependency
archives; the source kit conservatively retains all 140 archives.

Full and trimmed portable notice companions pass at
`evidence/python-free-component-companion-full-02` and
`evidence/python-free-component-companion-trimmed-01` in the authorized workspace.
They reproduce 364 notice blocks from 83 components, with the full build's 304
native/JAR entries retained as conservative dependency evidence. The trimmed
companion verifies all 301 retained native/JAR files and exactly 32 SDK/theme
exclusions with unchanged retained bytes. The first full-companion attempt is
preserved: verification passed, but its wrapper requested an obsolete result
field before saving the receipt. The corrected wrapper uses the verifier's
retained-file comparison field; no acceptance condition was removed.

The selected source, payload and reduction records were assembled from actual
configure/build, registry, Java, archive, source and worker receipts under
`evidence/python-free-production-records-01`. The corresponding prior source
records and private lease bytes remain in `.codex-temp/python-free-selection-01`.
Build-only Python remains a required build tool; no Python runtime or Python
service registration is packaged. Historical Python notices remain available for
historical runtime verification.

All 58 full staging/allowlist/refusal checks pass at
`.codex-temp/office-payload-tests-50910f2168a6449da9e71bf09188df5d`.
The source record passes 18 checks and the explicit runtime modes pass six tests.
The selected local engine is `.codex-temp/office-python-free-reduced-01/office-engine`.

Candidate 1.3.4 is reserved at
`artifacts/production-staging/f8a18435b7754108a111a29b7326aecc`, inventory SHA-256
`C33121B2D4638DCC336F3EBC1ED30FEB39AA32F6B474A632104447589F3494C7`.
Its 571,163,417 bytes exclude the inventory itself, compared with 610,918,241
bytes in 1.3.3 and 682,908,839 bytes in 1.3.2. The E76FDBFA PDF-dark executable
and all other selected engines are retained. All 282 offline notice links pass.
The actual combined worker passes 5,177 foundation, 43 Office execution,
14 PDF-dark workflow and 29 PDF-dark failure/cancellation checks, recorded in
`.codex-temp/size-reduction-134-worker`. Captured test and worker identities
remain unchanged.

The release candidate is
`artifacts/release-candidates/66a7cca46dd44e1196a52afc67ac0fc0/ContextSuite`.
Its ZIP passes a complete fresh-extraction check; the successful temporary
extraction was removed. ZIP SHA-256 is
`79802B86DB22D755F62DB5F411B38C18012EECDD0A30D459057FE64CBA630869`.
Build arguments are retained in `.codex-temp/size-reduction-134-build-inputs.json`.
Never overwrite this version with changed files.

The unsigned internal installer is
`artifacts/installer-candidates/75c02ed0b5ea45fc84fc97ff9205558b/ContextSuite-1.3.4.0-win-x64-internal.exe`.
Compilation passes and 25 installer contracts pass with mocked Appx operations.
Its 225,435,486 bytes have SHA-256
`F93F518C9CF7F510F91B77850C5EE082E5A207977B3DFF51003A691F78E17AAB`.
Identity packages remain unsigned and the installer retains its native-upgrade
admission guard. This is not installation, native upgrade or signing acceptance.

Fifteen fresh-worker exports pass the declared 45-second median / 75-second
maximum budgets. Word, Excel and PowerPoint medians are 4.73, 4.92 and 4.66
seconds; the slowest sample is 6.25 seconds. Originals and captured inputs are
unchanged and every disposable profile is removed. Evidence is
`.codex-temp/office-performance/0d469ac651ad42aba817eae0e0f2cda1`.
Worker peak working sets range from 53,575,680 to 62,394,368 bytes; this is the
managed worker measurement, not aggregate memory of its native child processes.
The separate 61-check native resource acceptance remains applicable. These
measurements establish budget compliance, not a speedup over the old runtime.

The new `postcheck-python-free.json` in the baseline evidence directory confirms
all original source files remain present, Git heads and all 32 original version
receipts are unchanged, the live 1.2.9 and both rollback payloads match their full
inventories, and the cleanup archive remains intact. Both source rollback ZIPs
also retain their original hashes. Additional source differences are confined to
the Office build, interruption/packaging tests, provenance/notices and their
documentation. No additional scratch cleanup is inferred from successful tests.

Final measurement is retained in
`artifacts/size-reduction-baseline-20261012/final-sizes-python-free.json`.
All 2,782 application files supplied to the installer match the release candidate,
as do the retained installer inputs and final executable. The final ZIP is
216,206,065 bytes, 11,047,976 bytes smaller than 1.3.3. The installer is
5,790,396 bytes smaller. The original full Office runtime, exact 1.3.3 payload
and both source rollback archives were rehashed successfully before measurement.

The repository scan records 73,494,810,591 logical bytes versus the initial
65,528,056,401: an increase of 7,966,754,190 bytes (about 7.42 GiB), despite the
2.984 GiB net cleanup. Retained tests, runtime copies, source/notice evidence,
candidate and installer outputs account for the additional storage. This is not
a claim that the overall checkout became smaller. The authorized external
workspace separately contains 42,338,767,482 logical bytes (39.43 GiB), within
the 100 GiB experiment budget, recorded in `python-free-external-size.json`.
Scans are non-atomic, skip reparse entries and distinguish logical sizes from
allocated disk space. The repository scan excludes its baseline evidence
directory consistently with the original scan.

Final public/private whitespace checks and repository validation pass, including
226 documentation files, public-source boundaries and theme policy. No live
installation, registration, commit, push, signing or publication was performed.
Native installer upgrade, visible UI and commercial/source-delivery acceptance
remain separate release work. PDF DLL consolidation and further retention cleanup
remain deferred rather than being introduced into this reduction.


Uncompressed Workspace Relocation, 2026-10-13
-------------------------------------------

The separately authorized cleanup moved 149 selected historical roots containing
270,555 files and 45,517,860,080 bytes to
`Z:\Backup\Context Suite\workspace-history\20261013`. The `payload` directory
mirrors repository-relative paths. Files and folders were copied directly;
pre-existing ZIP/7z archives were relocated unchanged, with no new compression.
Only manifested source entries were removed, after source/backup SHA-256 and
membership checks and a final source-identity check. Roots containing junctions
were excluded. A read-only directory in an old test fixture required clearing
its directory attribute after verification; the interruption and resume receipts
are retained.

The final non-atomic scan measures 28,309,355,708 logical bytes (28.31 GB,
26.36 GiB), within the requested 20-30 GB range. Unlike the preceding baseline
scan, this includes local baseline evidence and relocation manifests. Reparse
targets are not traversed. Most remaining storage is current build material and
retained evidence under `.codex-temp` (19.79 GB) and `artifacts` (8.27 GB).
The separate G: Office build workspace was not changed.

Receipts and recovery instructions are in
`artifacts/workspace-archive-20261013`; the backup retains matching manifests,
per-file hashes and completion receipts under `metadata`, with `README.txt` at
its root. To recover historical evidence, copy the needed mirrored tree from
`payload` to its absent original location and verify against its receipt. Do not
overwrite an existing tree. Historical scripts and immutable version receipts
still name their original paths; restore required inputs before rerunning them.
The older archive helper and ledgers remain local and are also copied under
backup `support/history-archives`.

Post-relocation verification confirms all 1,528 captured public/private source
files are unchanged, all original version receipts retain their hashes, and
the live 1.2.9 payload, two rollback payloads and retained 1.3.3/1.3.4 stages
match their complete inventories. Both recent release candidates, the latest
installer, current build inputs and source rollback archives remain local.
The PDF-dark host is unchanged. The 1.3.4 release candidate passes its engine,
package and 282 offline notice-link checks after relocation. This checkpoint
documentation was added after the source-preservation comparison.

The cleanup does not change application code, versions, installation or
registration, and does not commit or publish either repository.
