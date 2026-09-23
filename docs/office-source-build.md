Office Renderer Source Build
===========================

Status: an isolated source kit is assembled; standalone source preparation and
fresh configuration pass. Complete clean compilation, native/host replay,
durable runtime identity,
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
recipe. It clears inherited Make flags, supplies `PARALLELISM=2` explicitly and
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

[Test-SourceBuild.py](../tools/office-engine/Test-SourceBuild.py) passes sixteen
checks at `.codex-temp/office-source-build-driver/6272a86d409645199905cff07b4a3ca9`.
Actual Bash/Make run harmless echo/failure targets in a path containing spaces.
The tests verify no-write planning, invalid limits/cache/temp refusal, inherited
Make-flag removal, concurrent-driver refusal, exact target order and stopping at
the first failure with its receipt retained. They run no compiler or Office
engine and do not establish runtime resource-limit or interruption acceptance.

The read-only plan against the reconstructed configuration also matches all
seventeen targets and the exact reviewed child environment. After attempt 01
terminated, this driver resumed the existing tree with that environment. Its
receipts are under `evidence\source-build-4e24fc1032364d8d81456e7e81273ace`.
Completion, native execution and inclusion in the final source kit remain pending.


Remaining build and adoption work
--------------------------------

Historical configuration scripts preserve actual commands, but contain the
original workspace paths. Portable preparation/configuration orchestration still
needs to map source, tool and dependency paths; the configured-build execution
step above now accepts explicit locations and retains the limits. Complete clean
compilation and native/restricted-host replay must bind the rebuilt bytes to
this source kit. A successful extraction or configuration is not that evidence.

Keep the existing runtime and reserved 1.1.1 candidate immutable. The upstream
archive-derived `version.ini` has an empty build ID; distribution needs a durable
source/configuration/patch/toolchain identity without pretending the modified
renderer is an unmodified upstream binary. Notices, external tool/runtime
licensing, final source delivery and adoption remain required.

The [local candidate contract](local-candidate-acceptance.md) still requires the
application D07/regression checks, isolation evidence, exact final A01-A08 and
performance replay, plus the consolidated human-review artifacts. This source
checkpoint does not close those gates or authorize installation or publication.
