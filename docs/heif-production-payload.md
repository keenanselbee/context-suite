HEIF Production Candidate Payload
=================================

Status: explicit isolated packaging implemented; 53 packaging checks, five
additional immutable-stage guard checks and 659 combined-worker checks pass.
Formal release staging, commercial redistribution and human acceptance remain
separate. The reserved combined 1.1.1 payload is unchanged.


Selected runtime and staging
----------------------------

The [deployment selection](../tools/heif-engine/payload-candidate.json) retains
all fourteen files from the verified
[color/alpha runtime bundle](heif-color-fallback.md): three decoder DLLs, three
source patches, the component notice, and seven license/source-notice files.
They occupy **5,062,827 bytes** under the worker's `heif/` directory. The source
review ZIP remains separate local evidence; this staging path does not establish
public source delivery or modified-library-use compliance.

The [stager](../tools/heif-engine/Stage-HeifPayload.py) verifies both source and
runtime review bundles, matches the fixed runtime ZIP identity, checks all
fourteen member identities, and only then creates the destination. The private
worker's three binary pins must agree with the deployment selection. No private
source, fixture encoder or upstream test image is copied into the payload.

Verification requires exact file and directory membership. Missing or changed
notices, unexpected files/empty directories, reparse points and hard-linked
files are refused. Each written file is checked before continuing. Copy failure
retains the partial directory, and a retry cannot overwrite it. This is static
packaging verification; the worker's live file/directory leases still apply.
An existing `payload-inventory.json` also prevents staging, before distribution
access or output creation; adding an engine cannot mutate an inventoried stage.


Build and verify
----------------

Add `-HeifDistributionDirectory '<verified local HEIF review bundle>'` to
[Build-Production.ps1](../tools/Build-Production.ps1), together with a fresh
`-StagingId`. It composes with the existing audio, PDF and Office options.
The default build omits HEIF. The complete production verifier must explicitly
receive `-AllowHeifCandidate`; without it, HEIF files remain outside its allowlist.

No formal package was produced in this checkpoint. Before that step, finalize
the complete payload and advance the reserved product version. **1.1.2** is the
next available version after the retained 1.1.1 stage; a changed packaged payload
must not reuse either prior version or overwrite its evidence.

The scratch-only packaging test accepts the existing combined candidate as a
read-only base:

```powershell
python -B tools/heif-engine/Test-HeifPayload.py `
  --distribution '<verified local HEIF review bundle>' `
  --base-payload '<retained combined production stage>'
```

It creates a disposable copy, removes only that copy's inherited inventory,
stages HEIF and refreshes the application and worker from current Release builds.
It verifies the combined allowlist and writes/verifies a new scratch inventory.
This is not another formal release of the assemblies' current version. The
original stage, version reservation and input distribution are hash-checked and
left unchanged. No Office process, Windows profile or installed app is started.

The interoperability runner can execute this exact combined scratch worker:

```powershell
python -B tools/heif-engine/Test-HeifInterop.py `
  --payload '<verified combined scratch payload>' `
  --inputs '<pinned local upstream corpus>' `
  --color-evidence '<passing authored color evidence>'
```

This mode validates all five engine families and the complete file inventory,
builds only the test host, and invokes the worker in place. It neither rebuilds
nor copies the supplied worker. Hash receipts cover that exact payload before
and after execution. It proves the recorded HEIF worker/publication cases in
the combined layout; it does not rerun every other engine's media acceptance.


Recorded packaging evidence
---------------------------

The 53-check run is
`.codex-temp/heif-payload-tests/a35b0137522a40ce8e4da4da7a763e29`.
It verifies all fourteen missing-file and fourteen corrupt-file cases, unexpected
DLL/text/empty-directory members, hard links, junction roots and children,
overlapping/out-of-scope destinations, fresh-stage opt-in and immutable inputs.
Two injected copy failures retain the partial first DLL, stop later copying,
fail verification and refuse retry without changing that retained file.

The subsequent five-check guard run is
`.codex-temp/heif-payload-guard/efa51c5555924eb4ad0954c5accf2e33`.
It adds the inventoried-stage refusal after the full packaging run, proves that
both the actual reserved base and an authored inventory marker block staging
before the distribution is accessed, and confirms that a fresh stage still
accepts all fourteen files. The large combined-copy test was not repeated for
this early refusal guard.

The combined scratch copy contains **19,464 inventoried files**, totaling
**1,578,992,612 bytes** before its inventory JSON. Inventory SHA-256:
`041AFBEB0ED40D90D98939A7C7547E933E1615DA75E90BBE08239F2F6B2926C5`.
This includes all existing optional engines and the current application/worker;
the managed Release builds have zero warnings/errors. Its `combined/` directory is a test artifact,
not a reserved or installable release. The complete runtime adds 5,062,827 bytes;
the combined size also reflects refreshed managed assemblies.

The read-only base remains
`artifacts/production-staging/3efa4342aed9435995d8f00b52fead96`, whose reserved
inventory SHA-256 is
`5C931EBBF0BBCB4DF1D576D4714628D420B9EB539956A7ED39589A9975AA497E`.
The packaging run verifies every original file's final hash. The review bundle
remains `heif-distribution/bd207dd987e948e1b080faf261b288f9` beneath scratch.

An initial invocation misspelled the base directory and failed before combined
copying at `heif-payload-tests/2a69c35aa415410b943103371a9dc356`. That failed
attempt is retained. The harness now rejects a missing base/inventory before
starting its fixtures; the corrected run above is the passing evidence.


Combined worker execution and remaining gates
--------------------------------------------

The exact combined worker passes the upstream/authored color corpus at
`.codex-temp/heif-interop/6dc45b340a51486fb2a5a022ada49170`: **659 checks and 104
published JPEG/PNG copies**. The receipt records `PackagedExecution: true` and
identifies the worker inside the combined scratch copy above. Every source,
fixture and payload hash remains unchanged. This mode builds only the contract
host, with zero warnings/errors; it does not replace the supplied worker.

The run covers ten upstream and 42 authored SDR inputs to both targets, plus
typed unsupported/HDR refusals and same-worker recovery. The independently
rotated source PNG retains exact visible RGB and alpha placement. Existing
matte, mandatory-copy, precision/profile, output validation and source-preservation
checks apply. The separate [native interruption evidence](heif-color-fallback.md)
remains unchanged; this packaging checkpoint does not claim another interruption
run or a new visual review.

All sixteen existing non-HEIF production packaging regressions also pass at
`.codex-temp/production-packaging-6f7e323b87be479283dd216120a13368`.
Public source-boundary/theme checks and 173 documentation checks pass. General
foundation/media suites were not rerun for this packaging-only change.

The later [combined expanded-input checkpoint](combined-expanded-inputs.md)
retains this decoder alongside expanded AIFF/WMA and Windows Voice. The exact
combined worker repeats all 659 interoperability checks with 104 copies, and
its full inventory and compressed archive identities pass. Human image review
and final versioned-candidate acceptance remain open.

The packaging path closes the former missing-HEIF-stager gap. Fresh formal
versioned staging and complete cross-engine acceptance remain under the active
[goal](broad-file-support-goal.md). AIFF/WMA, broader real-file fidelity, declared
timing/package budgets, visible UI and accessibility checks remain unfinished.
Source delivery, modified-library use and other
[redistribution decisions](heif-distribution.md#remaining-release-decisions) are
not cleared by local packaging. No installation, Explorer change, live commerce,
signing or publishing occurred.
