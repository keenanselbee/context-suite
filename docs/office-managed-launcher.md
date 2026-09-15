Office Managed Launcher
========================

The private `OfficeSandboxProcess` now launches the fixed native Office host from
managed code. It creates a suspended, hidden child with an explicit AppContainer
SID and zero capabilities, assigns its job, verifies the actual token, then
resumes execution. This advances the worker integration boundary; it does not
yet expose customer Office conversion.


Ownership and bounds
--------------------

The worker owns the native process/job. The Windows profile must be owned
separately by the application so a worker crash cannot bypass profile cleanup.
The current isolated tests use the existing evaluation supervisor for that role.
The managed launcher itself creates no profile, changes no ACLs and publishes
no files. Callers must retain verified host/runtime/path leases and explicit
filesystem grants. Production construction of those inputs remains pending.

The launcher supplies exactly twenty named environment entries; it copies no
ambient environment and accepts no command arguments for the native host. Only
NUL input and separate stdout/stderr write handles are inherited. It verifies
the actual AppContainer state, expected SID and empty capability list while the
child is suspended. Job limits are read back after being set: eight processes,
512 MiB per process, 1 GiB aggregate and termination when the last job handle
closes. This is process/resource containment, not complete content/network
acceptance for Office.

The default deadline is two minutes; internal callers can shorten it but cannot
extend it. Stdout is bounded to the 4,096-byte completion protocol and stderr to
65,536 bytes. Excess data, cancellation and timeout stop the job. All exits stop
remaining descendants, check zero active job members within five seconds and
wait for the retained host handle before returning. Pipe consumers are drained
before their cancellation resources and handles are disposed. A failed process
never returns an accepted completion, and a successful process still needs the
strict completion protocol and independent PDF validation.

The native host's 128 MiB output check remains a post-export limit. Live staging
disk bounds, resource-exhaustion acceptance and application-owned publication
are not supplied by this launcher.


Clean environment correction
----------------------------

The first managed attempt (`cs51`) reached native initialization but timed out
after 120 seconds. The earlier native harness had supplied `SAL_LOK_OPTIONS`
before startup. The host changed only the Windows environment; this did not
update the C runtime's startup environment used by the engine. The host now uses
`_wputenv_s` before loading DLLs and verifies the Windows values too. Microsoft
documents the separate [CRT environment table](https://learn.microsoft.com/en-us/cpp/c-runtime-library/environ-wenviron?view=msvc-170)
and its [update API](https://learn.microsoft.com/en-us/cpp/c-runtime-library/reference/putenv-s-wputenv-s?view=msvc-170).
The clean managed environment deliberately omits these engine variables; the
passing exports therefore exercise the host's own initialization.

The failed supervisor then encountered a reused diagnostic filename while
preparing the next case. It unwound profile ownership, but did not write its
normal cleanup receipt. A separate read-only check verified the profile folder
and registry mapping absent, and process inspection found no remaining test
processes. Its root-token flag was inherited from the older harness and is
incorrect for a managed, full-trust root; it is not sandbox evidence. Later
supervisors use unique diagnostic filenames and correctly label their root as
full trust. The actual native host token is checked by both the managed launcher
and the host itself.


Verification
------------

The private Release build passes with zero warnings/errors. The native host and
authored process-fault probe build with `/W4 /WX`. Public foundation code is
unchanged from the preceding 2,986-contract checkpoint; those contracts are not
counted as a new run here.

The evaluation supervisor supplies held path-chain/file identities and a pinned
runtime. The private test harness holds the exact native executable and source,
runs modern Office source preflight, verifies original bytes/write times and
checks successful replies against actual candidate hashes. It uses fresh owned
copies and profiles for each format.

Retained evidence is beneath
`.codex-temp/office-isolation/1a880d93be994b9a9a3467bb77aa1ba3`:

- `cs61`: three actual managed-to-native exports. Completion identity and
  independent PDF structure, text, page geometry and rendered-pixel comparisons
  against the retained `cs41` ordinary controls all pass separately.
- `cs62`: three cancellations after observing the native host's new profile
  settings. This establishes cancellation during startup, not active rendering.
- `cs63`: three deliberately shortened deadlines reject work without an output.
- `cs64`: three already-cancelled requests reject before launch.
- `cs65`: three unexpected-environment requests reject before launch.
- `cs67`: the independently authored `ProcessProbe` floods stdout, floods stderr
  and fails with a sleeping descendant, respectively. It loads no Office engine.
  Each case fails for the expected specific reason without an accepted
  reply/output; the descendant's recorded PID and creation time is checked after
  cleanup. The prior `cs66` run checked failure generally; the final assertions
  distinguish pipe-budget failure from an unrelated launch failure.

`cs56` through `cs60` retain the preceding passing clean-environment matrix before
the final cancellation-resource lifetime and job-limit readback refinements.
Neither those earlier builds nor the failed `cs51` are substituted for the final
source-bound cases.

The final native host build is
`.codex-temp/office-host/7aa240c3f940486b9937b14f92c58729`.
The process-fault probe build is
`.codex-temp/office-process-probe/4d6d1a1cc8e847918f4f4e97a609b505`.
The final export/stop supervisor is
`managed-final-af2ebd389a9443728fa6e2bb8098a68a`; the final process-fault supervisor
is `managed-reason-bb47852261a444c5a5e8e88db747d9b0`. Their source/build receipts
include hashes of the managed payload and four changed private launcher/test
sources. The later harness only strengthens fault-reason assertions; its previous
test source/assembly/symbols are retained and hash-checked separately for the
export/stop evidence. The launcher implementation is identical in both runs.

Independent export inspection is
`inspection-43896ecc802a411aa29ca9a2c8eb1478/results.json`, and completion checks
are `.codex-temp/office-managed-completions.json`. Final reconciliation is
`.codex-temp/office-managed-launcher-verification.json`: three exports, twelve
stop/admission cases, three specific process faults, 28 exclusive file opens,
removed profile folder/mapping, all 19,332 source/copy runtime hashes and exact
copied membership. Test-supervisor result files correctly describe the outer
managed process as full trust; they are not standalone native-host token reports.


Remaining integration
----------------------

Connect production app-owned profile/grant leases to worker requests and the
managed launcher, with pinned runtime composition and complete admission. Then
connect completion to independent validation and transactional PDF-copy
publication. Test active-render cancellation and worker/application loss through
those actual boundaries. Resolve calculation/font policies, broader fidelity,
content/network enforcement and runtime adoption. No installed or visible UI,
screen-reader, theme/DPI, live commerce or release acceptance is claimed here.

The subsequent [profile/grant ownership checkpoint](office-profile-ownership.md)
adds the application component and a native worker-crash/access matrix. It also
corrects the earlier attributes-only directory-lease assumption. Production
context construction and worker/publication routing remain pending.
