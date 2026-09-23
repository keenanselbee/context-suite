Office Renderer Source Build
===========================

Status: an isolated source kit is assembled; standalone source preparation,
fresh configuration, reconstructed compilation and all 22 native cases pass.
All 148,225 fresh kit files now match the compiled source tree exactly.
Reconstructed restricted-host chart, preview, external-link and macro controls
also pass; DDE repeats its known fidelity failure and remains refused. Durable runtime identity,
redistribution review and production adoption remain open. This work supports the
[passive-chart repair](office-chart-renderer-repair.md), not a new Office scope.


Verified inputs and preparation
-------------------------------

The pinned upstream source is LibreOffice commit
`8221e31b3ac356a1623c672912a3d2b492f7e3d1`. Its original archive SHA-256 is
`7844FC4BD214147AB219012F57F2458A38D544DC8C0A154AF956882D724AF890`.
The complete comparison accounts for 148,166 original regular files, eight
modified files, three additions and 56 materialized internal links, with no
missing files. Exact evaluated source deltas apply and reverse against retained
archive originals; runtime repairs, build adjustments and native tests are
classified separately.

The evaluated `configure.ac` had an incidental Windows-1252 encoding change.
An isolated canonical copy restores UTF-8 and leaves exactly one intended diff
against the original: omit the build's EICAR/antivirus-exclusion preflight.
Antivirus remains enabled. Both evaluated and canonical copies generate
configure scripts that pass shell syntax and `--help`; generated script/help
text is identical after decoding in the corresponding encoding.
`G:\ContextSuite-OfficeBuild\evidence\configure-utf8-01\results.json` records the
comparison. The original evaluated source tree and runtime remain unchanged.

The actual upstream `Makefile.fetch`, expanded with the successful Windows build
configuration, selects exactly the 140 retained dependency inputs. Every selected
filename and SHA-256 matches the cache. The twelve other declarations in
`download.lst` are not selected by this configuration. This comparison uses an
instrumented print-only download function and Make's dry-run option; it executes
no downloader or recipe. The receipt is
`evidence\dependency-fetch-closure-02\results.json`. The first attempt lacked
`gb_Side` before including configuration and failed before enumeration; its log
is retained. This verifies the upstream fetch set, not every external tool's
license or a complete successful rebuild.


Local source-kit artifact
-------------------------

`G:\ContextSuite-OfficeBuild\tools\office-source-kit-01` contains 165 files and
1,148,517,451 bytes. It includes the original source archive, all selected
dependency inputs, the three evaluated patch groups, native-test additions,
canonical UTF-8 configuration, upstream source notices, a standalone preparation
script and historical build/configuration records. It contains no Context Suite
private source or compiler installation.

The ZIP at the same path with `.zip` appended is 1,148,543,529 bytes, SHA-256
`6E750E2E9A946552CF9BCAA24BA2EF7BE4E0684DBFD63CFF74E272587732650F`.
Every archive entry is read back and matched to its recorded hash. Stored ZIP
compression avoids recompressing upstream archives. The source-kit manifest is
`F7D1DDAE0E81B22DAD4120F540B5A23E2C1F227C277CC9A6E7F4D5C196C3B545`.
Receipts are `evidence\office-source-kit-01.json` and
`evidence\office-source-kit-01-archive.json`.

The kit's own `PrepareSources.py` runs from that directory, without repository
or private-checkout access. It verifies kit inputs before writing to a fresh
destination, validates source members, materializes internal links without
elevation, applies the evaluated patches and verifies modified/added identities.
Its default uses the canonical UTF-8 configuration; `--recipe evaluated` retains
the exact earlier encoding for historical reproduction.

The actual standalone replay into `source\kit-replay-01` passes: 148,166 regular
files, 56 materialized links, eight verified modifications and three additions.
`source\kit-replay-01-preparation.json` records the result. The canonical
`configure.ac` SHA-256 is
`383EFE9C5ED474115EAFA8F30FFDCD0E5EE1ED0D0F941890EF58DCFDC3597FBD`.
This preparation performs no configure, compilation, installation or network
operation. It is distinct from the subsequent fresh configuration/build checks.

Fresh configuration against the reconstructed tree subsequently passes in
120.28 seconds, with no `config.warn` entries. Its `config_host.mk` is byte-identical
to the evaluated configuration after mapping only the source, build and dependency
cache paths. `evidence\source-kit-configure-01\result.json` and
`evidence\source-kit-build-01-preparation\preparation.json` retain the result,
exact inputs, planned targets and path comparison.

The clean build started under `build\source-kit-01`, with two outer Make jobs,
one nested Python compiler/MSBuild worker, sequential packaging targets and the
existing 100-GiB workspace monitor. The measured starting workspace is
45,417,300,012 bytes. Its command, log, footprint and eventual exit receipts use
`evidence\source-kit-build-01-*` and `evidence\source-kit-build-01.log`.
The launch itself is not a pass. The native test target is compiled with execution
suppressed; required native and restricted-host execution follow separately.

Attempt 01 stopped with exit 2 after 1,886.48 seconds. Its OpenSSL compiler command
returned `0xc000026b`, identified by the installed Windows SDK as
`STATUS_DLL_INIT_FAILED_LOGOFF`; the parallel Graphite target also stopped.
The monitor did not exceed its budget (last measurement 47,813,653,641 bytes).
The terminal receipt and absent original process tree were verified before an
incremental resume on 2026-09-23. No source or compiler repair was inferred from
this environment interruption, and the failed receipt remains intact.


Reusable configure entry point
------------------------------

[Configure-Source.py](../tools/office-engine/Configure-Source.py) accepts a JSON
settings file followed by `--plan` or `--execute`. The settings contain exactly
ten absolute local paths: `Workspace`, `Source`, `Build`, `Kit`, `Bash`,
`Environment`, `Jdk`, `Ant`, `Clang` and `PkgConfig`. `Source` is the prepared
UTF-8 tree, `Kit` is its complete source-kit directory, and `Environment` is the
reviewed child-environment JSON described below. JDK and Ant paths are their
installation roots; Clang and pkg-config paths identify their executables.
The command uses existing tools and does not install dependencies.

The plan verifies source-kit artifact identities, the preparation receipt and
the modified/added source identities. It constructs the fixed reviewed options
with explicit tool, dependency-cache and build paths; it accepts no arbitrary
extra configure flags. `--plan` prints that plan without creating a build or
starting configuration. `--execute` requires a fresh build directory, retains
the plan and log, and checks the recorded inputs again afterward. Configuration
has a ten-minute deadline and the same 100-GiB workspace monitor; interruption
stops its owned process tree and preserves a receipt. This step runs compiler
capability probes but does not run any native build target or renderer.

Keep this command beside `Run-SourceBuild.py`, whose path, hashing and workspace
helpers it uses. Workspace, source, build, dependency cache and configuration
remain explicit; no global PATH or machine setting is changed. The original
historical scripts remain useful provenance, not the portable entry point.

[Test-SourceConfigure.py](../tools/office-engine/Test-SourceConfigure.py) takes the
same settings path and `--baseline-options <existing-autogen.input>`. It compares
the actual options, verifies the no-write CLI plan and Bash syntax, and refuses
out-of-workspace/nested builds, multiline tool paths, unknown settings and an
existing build directory. These checks do not run configure or compilation.

The eight focused checks pass at
`evidence\configure-checks-c849ca10fe534ac4ab8a228d71b18257`. Actual configuration
through this entry point then completes in 120.49 seconds at
`build\portable-configure-01`, with no `config.warn` entries and all recorded
inputs unchanged. Its `config_host.mk` and `config_host_lang.mk` match the clean
build baseline byte-for-byte after mapping only the build-directory path.
`evidence\source-configure-6beaed1fd6404262a9cc96b3d021c53e` retains the plan,
launch, log, result and comparison. This uses the same source and installed
toolchain; it does not establish source relocation or a different toolchain.


Reusable configured-build driver
--------------------------------

[Run-SourceBuild.py](../tools/office-engine/Run-SourceBuild.py) provides a reusable
execution step for an already configured source tree. Workspace, build, Bash,
Make and reviewed child-environment JSON paths are explicit. `--plan` validates
them and prints the fixed seventeen-target command without creating files or
launching a build. `--execute` records input/tool hashes, command, process, log,
workspace measurements and terminal result in a fresh evidence directory.

The driver requires source/build/cache/temp paths inside the owned workspace,
ordinary paths, two configured jobs, dependency hashing and the bounded Python
recipe. Each package target must match its source recipe's declared package name;
those recipes are included in the input hashes. It clears inherited Make flags,
supplies `PARALLELISM=2` explicitly and
runs targets sequentially. An OS-held byte lock prevents two instances of this
driver from using the same configured build; it does not coordinate independent
manual Make commands or the earlier one-off launcher. Do not start another build
against the active tree. Lock ownership is released when its process exits.

The existing 100-GiB monitor checks before launch and periodically during work.
Exceeding that budget or interrupting the monitor stops its owned process tree;
the monitor is not a filesystem quota or a general process sandbox. Compilation
still suppresses native test execution, which remains a separate acceptance step.
The environment JSON uses the fixed reviewed export names from the historical
build script, including child-local tool PATH, temporary/cache locations and the
existing telemetry/parallelism settings. No machine-wide environment is changed.

[Test-SourceBuild.py](../tools/office-engine/Test-SourceBuild.py) passes nineteen
checks at `.codex-temp/office-source-build-driver/62ba8fb1356645f180b43401bf7c3955`.
Actual Bash/Make run harmless echo/failure targets in a path containing spaces.
The tests verify no-write planning, invalid limits/cache/temp refusal, inherited
Make-flag removal, concurrent-driver refusal, exact target order and stopping at
the first failure with its receipt retained. Missing or mismatched package
definitions are rejected during planning. They run no compiler or Office
engine and do not establish runtime resource-limit or interruption acceptance.

The earlier seventeen-target run under
`evidence\source-build-4e24fc1032364d8d81456e7e81273ace` ended with exit 2 after
11,498 seconds on 2026-09-23. It compiled the native test and three executable
targets, then stopped because `Package_setup` is not a registered Make target.
The two invalid filename-derived aliases were removed; the declared targets
`Package_instsetoo_native_setup` and `Package_instsetoo_native_setup_ure` were
already present. Inputs remained unchanged and the workspace stayed below its
100-GiB limit. This is a failed build, not native acceptance.

The corrected fifteen-target plan passes against the existing configuration.
An incremental continuation uses the same compiled tree, toolchain, environment
and resource limits, with receipts under
`evidence\source-build-c08e65578faa4f78a191784ffad6dde4`. No source extraction,
reconfiguration or clean rebuild was requested. This continuation completed with
exit zero after 591.34 seconds, unchanged inputs and no budget violation. Native
tests were not executed by the build.

Replay preparation found six hardcoded path sites in the authored native test,
still referring to the earlier `chart-native` tree. A test-only relocation change
derives fixture/profile paths from the reviewed gbuild target and validates the
profile relationship. Its first incremental compilation failed on two C++ API
type mismatches (`source-build-a53c2025fc6d4ea784fcafa99914ef3c`). The corrected
test-only build completed in 92.43 seconds with exit zero and unchanged inputs
at `source-build-2c327426897e48ed98ff49c0a85a6065`.
The shipping renderer sources are unchanged. Preserve both build receipts and
the old source kits; the corrected driver and relocated test must be included in
a new source-kit revision before final delivery.

The actual reconstructed native replay passes all **22 cases** at
`evidence\reconstructed-native-run-01`. The runner retains the same bounded job,
separate desktop, exact case-count/transcript, command and inventory requirements;
its allowed build root points to `source-kit-01`. Ten command/inventory controls
and ten synthetic transcript checks pass before execution. The tests cover
passive-chart creation and activation boundaries, native macro/DDE positive and
blocked controls, inert binary OLE previews in Word/Excel/PowerPoint, and package
namespace boundaries. All 3,823 shipping runtime files remain unchanged by the
test-only compilation. Postverification at
`evidence\reconstructed-native-run-01-postverification.json` confirms all 5,230
runtime/test/fixture files retain their membership and bytes after execution,
with zero owned processes left. The local native profile is retained as evidence.

This native replay alone does not establish restricted-host rendering or
independent PDF fidelity. The reconstructed host checks below supply scoped
rendering evidence; final source/runtime correspondence and adoption remain open.


Standalone kit with build commands
----------------------------------

Evaluation 02 at `G:\ContextSuite-OfficeBuild\tools\office-source-kit-02` retains
the original pinned source/dependency/patch inputs and adds the four public
configure/build commands and their focused test scripts from public commit
`a039dd3ad085533d202ebeea6c9445db1df0375e`. Its README gives standalone source
preparation, explicit settings/environment fields, configure/build commands,
toolchain requirements and remaining acceptance boundaries. Evaluation 01 and
its ZIP remain intact; no private implementation is included.

The new kit contains 169 files and 1,148,553,285 bytes. Its manifest SHA-256 is
`099024ABFF64DD452F5FC7A959E29EFEE17FB786BF58D989A52B4FF40EAE417A`.
The stored ZIP is 1,148,579,825 bytes, SHA-256
`5A84AC24450726340F2658848FD66DF4B9CF0233278B69DA71B053121ABB826E`.
Every archive member was read back and matched to its size and hash.
`evidence\office-source-kit-02.json` and
`evidence\office-source-kit-02-archive.json` retain these receipts.

Both planners run from the kit directory with no repository input dependency.
Configure options match the successful baseline after changing only the kit
dependency-cache path, and the build plan retains the same seventeen targets.
This historical plan includes the two invalid aliases described above; its
planning result does not establish that all targets can execute. Evaluation 02
is retained unchanged and must be superseded with the corrected build driver
before final source delivery.
No new build directory is created and all kit members remain unchanged.
`evidence\source-kit-02-standalone\results.json` records this scoped result.
It uses the existing prepared source, configured build and installed tools;
it does not claim another source extraction, configuration or completed rebuild.

Final source/runtime correspondence, helper distribution terms, dependency and
toolchain redistribution review, and the native/restricted-host replay remain
required. This local archive is not approval to redistribute or adopt the engine.


Corrected source kit and reconstructed host replay
--------------------------------------------------

Evaluation 03 at `G:\ContextSuite-OfficeBuild\tools\office-source-kit-03` contains
the corrected fifteen-target driver and the relocated native test that passes
all 22 cases. It retains the pinned source archive, 140 dependencies, runtime
patches and build settings. It contains 169 files and 1,148,556,097 bytes; manifest
SHA-256 is `934F38439E68893D487D7033EE9895ECE7935F621E3B8B137F7A245332BADAFE`.
Its 1,148,582,637-byte stored ZIP has SHA-256
`EE90743371CEA5ADE53CC88144D53958D1C60E18ABA0A81905CC2D54039BE3FE`.
Every ZIP entry was read back and verified. Kits 01/02 remain unchanged.

Actual standalone preparation into `source\kit-replay-03` passes: 148,166 regular
archive files, 56 materialized links, eight modifications and three additions.
Both planners run from the kit without a repository input dependency; configure
options match the successful baseline after mapping the kit path, and all fifteen
build targets match the successful continuation. The planners create no build
directory and launch no compiler. Receipts are `evidence\office-source-kit-03.json`,
`evidence\office-source-kit-03-archive.json`, `source\kit-replay-03-preparation.json`
and `evidence\source-kit-03-standalone\results.json`. The completed comparison at
`evidence\source-kit-03-correspondence\results.json` verifies all **148,225 files**
in both the fresh kit03 tree and the compiled kit01 source tree. There are no
missing files, differing files or generated extras. This binds the delivered
source bytes to the already completed compilation without another full build;
it does not by itself establish final runtime packaging or redistribution.

The reconstructed renderer is separately retained at `tools\office-review-runtime-03`
with the current source-matched application host and the same five app-local CRT
files. Its inventory SHA-256 is
`F7918C2946A49E858556E7577E9B5459FE04677108F6E0E598D9513FD0F6090D`:
3,828 runtime files totaling 545,216,509 bytes. The complete engine archive has
3,833 files, expands to 546,687,520 bytes and compresses to 222,964,245 bytes;
SHA-256 is `D45C2A23CEB0158D8A7AD42606FF2338CA517210605AFD8ECAAB92F3F5BB038A`.
This is a separate evaluation payload; application pins and reserved candidates
are unchanged. Of the runtime files, 509 differ from the earlier compiled tree.

`evidence\office-review-runtime-03-acceptance.json` records twelve actual
restricted-host Word/Excel/PowerPoint preview exports, exact earlier page rasters,
profile/mapping removal and unchanged input identities. Static inspection of
325 native files finds no missing dependency in the reviewed locations or
unbundled MSVC import. It is not clean-machine or dynamic-loader provenance proof.
`evidence\chart-host-export-04-postverification.json` adds four live-chart exports
under both calculation modes: unchanged independent text/geometry/chart controls
pass and all four page rasters exactly match the earlier baseline.
`evidence\chart-host-export-05-postverification.json` adds the remaining two live
Word/PowerPoint chart exports: unchanged text, geometry, bar-value and category
checks pass, with exact earlier page rasters. Complete payload membership, runner
sources and inputs remain unchanged. All eighteen native profiles/mappings are
absent after these chart and preview runs.

`evidence\reconstructed-security-host-replay-01.json` separately records five
external-workbook controls and one macro control passing, with ten profile
removals across those six exports and four failed DDE probes. The DDE cases repeat
the prior missing-link/recalculation failure; they are not a security pass.
The current worker still refuses the known DDE inputs before native access:
28 checks pass at `.codex-temp/office-dde-refusal/5c56ec0c12d846b994f4b251c9ece64d`.
No DDE support, affirmative network-denial result or general fidelity is claimed.


OOXML shape data packaging repair
---------------------------------

The missing PowerPoint fill traced to omitted runtime data, not changed renderer
code. The CLI diagnostic at `.codex-temp/powerpoint-fill-diagnostic-01` reproduces
the loss with Evaluation 03. Its ODP export retains the authored `#ddeeff` fill
but has no preset geometry path. The pinned importer reads
`share/filter/oox-drawingml-cs-presets`; that directory was absent from the partial
build payload. Upstream `oox/Package_customshapes.mk` and `oox/Package_generated.mk`
declare the three required files below.

| Runtime data | Bytes | Source |
| --- | --- | --- |
| `oox-drawingml-cs-presets` | 1,696,554 | Verified source tree, `oox/source/drawingml/customshapes/` |
| `oox-drawingml-adj-names` | 6,288 | Existing `oox/generated/misc` custom-target output |
| `vml-shape-types` | 126,322 | Existing `oox/generated/misc` custom-target output |

`evidence/shape-resource-regeneration-01/results.json` independently regenerates
both generated files from the fresh, verified source kit using the existing Perl
tool. Both match the original build outputs byte-for-byte. The presets file and
all generator inputs match the completed source-tree comparison. No compiler runs.

The configured-build driver now includes `Package_oox_customshapes` and
`Package_oox_generated`, with their source recipe declarations in its input hashes.
Its nineteen boundary/harmless-Make checks pass at
`.codex-temp/office-source-build-driver/c5246bcfe3ca41bb8f1cbf6d99b760a3`.
A reviewed dry run selects only those two data packages. Actual execution at
`evidence/source-build-7d3fc5f31dc143d2b9688e011bbde74b` exits zero in 57.42 seconds;
`evidence/shape-resource-packages-01-preparation/postverification.json` confirms
exactly three added data files and all 3,823 earlier build-runtime files unchanged.
This is a package-copy continuation, not another native compilation. The two-job
and 100-GiB workspace limits remain; no clean or reconfiguration is performed.

Fresh `tools/office-review-runtime-04` retains every Evaluation 03 compiled binary
and adds these data files plus its revised inventory. Its 3,831 runtime files total
547,045,673 bytes; inventory SHA-256 is
`738D1AD2B320A3E1FF6D6E3173CD6B25D5E70C8B707E3B1EBD79220FD0BD738A`.
The complete engine ZIP contains 3,836 files, expands to 548,517,003 bytes and is
223,036,511 bytes compressed, with SHA-256
`469821C5CAADA154DBC1E7C7FE277C6C66AE0B004E5E66F0D655BAC9BEDDB8FA`.
Every archive entry was read back and verified. Earlier payloads remain unchanged.

`.codex-temp/powerpoint-fill-diagnostic-02` restores both original control-page
rasters exactly through the CLI. `.codex-temp/powerpoint-fill-host-02` repeats the
same exact-pixel, text and two-page result through the actual restricted host,
retaining macro/active-content policy and source hashes. The native profile and
mapping are removed, and all captured source/runtime/tool inputs are unchanged.
The first host runner failed on an incorrectly encoded fixture pathname before
profile creation; its receipt remains. No fidelity tolerance was changed.

Source kit04 at `tools/office-source-kit-04` now contains the corrected build
helper from public revision `7a02d1297fa024641362b00b6bfb63c7cae62795`. Its 169 files
total 1,148,557,627 bytes; manifest SHA-256 is
`9D0EF709159DC2CCCAD6BE315DB7833032C540507DA55CE0EA656EFC3012AF73`.
The verified 1,148,584,167-byte ZIP has SHA-256
`F911DC854D29266182664B0212D6891BD1ABDBAFDD66150F3D02F55653EA2AFA`.
Only README and the two build-driver files differ from kit03. Every source archive,
dependency, patch, native addition and preparation helper is unchanged; its
148,225-file source comparison remains applicable. Standalone configure/build
plans pass with the seventeen targets and no repository input dependency. No new
source extraction, configuration or native compilation was needed. Receipts are
`evidence/office-source-kit-04.json`, `evidence/office-source-kit-04-archive.json`
and `evidence/source-kit-04-standalone/results.json`.

This proves the data repair in the isolated reconstructed payload and updates its
source-delivery draft. The later [application selection](office-runtime-verification.md#reconstructed-selection-with-shape-resources-2026-09-23)
binds runtime04 in packaging and the private lease; ordinary application exports
now pass exact Word/Excel/PowerPoint control pixels. The earlier failed paired
outputs remain retained. Replay the remaining fidelity/recovery requirements
before final candidate acceptance. The [packaged source identity](office-production-payload.md#packaged-source-identity-2026-09-23)
now supplies durable runtime provenance; distribution terms and adoption remain
separate gates.


Remaining build and adoption work
--------------------------------

The preparation, configure and configured-build commands now accept explicit
locations and are included in Evaluation 03. Final source delivery remains tied
to the eventual adopted runtime; changing source/toolchain locations beyond the
evaluated configuration requires corresponding verification. The reconstructed
compilation, complete source-tree comparison and scoped native/host replays
above pass. Final packaged-byte acceptance remains required before adoption.
The earlier paired application recovery replay exposed
[missing PowerPoint text-box fill](office-worker-lifetime.md#paired-source-built-interruption-replay-2026-09-23),
now repaired in the selected application payload above; remaining recovery and
fidelity requirements still need replay.

Keep the existing runtime and reserved 1.1.1 candidate immutable. The upstream
archive-derived `version.ini` has an empty build ID. The public
[source-runtime record](../tools/office-engine/source-runtime-candidate.json)
now binds source/configuration/patch/toolchain identity and identifies this as a
modified evaluation build. Packaging requires its exact bytes beside the engine
as `ContextSuite.OfficeSource.json`. The record includes source-kit04 identities,
the three evaluated patch groups, portable configure options, actual configure
and build-receipt hashes, compiler/linker versions, and the data-package repair.
It adds no native code and does not require another native compilation.
Selected tool versions are not a complete hermetic toolchain inventory or proof
of bit-for-bit reproducibility. Notices, external tool/runtime licensing, final
source delivery and adoption remain required.

The [local candidate contract](local-candidate-acceptance.md) still requires the
application D07/regression checks, isolation evidence, exact final A01-A08 and
performance replay, plus the consolidated human-review artifacts. This source
checkpoint does not close those gates or authorize installation or publication.
