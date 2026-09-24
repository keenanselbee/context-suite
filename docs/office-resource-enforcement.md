Office Launcher Resource Enforcement
====================================

Status: 61 checks passed on 2026-09-23 through the production managed launcher.
This is a bounded component test using an authored native helper, not an Office
engine rebuild or a claim of complete hostile-document acceptance.

[Test-OfficeResources.py](../tools/office-engine/Test-OfficeResources.py) builds
the private contract harness and a small static-CRT x64 helper using existing
tools. It creates one disposable profile, grants only its scratch binary/output
trees, and calls the unchanged Office launcher with its fixed environment and
zero-capability AppContainer. The helper reads the actual job limits: eight
processes, 1 GiB per process and aggregate, and kill-on-close.

Run only with disposable-profile authorization:

~~~powershell
python -B tools/office-engine/Test-OfficeResources.py --create-disposable-profile
~~~

The eleven sequential requests cover:

- A successful 600 MiB private-commit control before testing and after every
  failure. Actual private-commit deltas are checked. Allocations are freed without
  touching the pages; this does not deliberately fill physical RAM.
- A refused 1,040 MiB allocation. Since process and aggregate limits are equal,
  this proves their effective ceiling, not which limit independently refused it.
- A refused 600 MiB allocation while a live child retains another 600 MiB.
  Each allocation alone fits the process limit; their sum exceeds the job limit.
- Seven suspended children retained alongside the root. The ninth job process
  is refused with ERROR_NOT_ENOUGH_QUOTA; all seven child identities are checked.
- Standard-output and diagnostic floods exactly one byte above the current
  36 KiB and 64 KiB limits. Both produce the specific bounded-channel refusal.

Memory attempts require at least 3 GiB of available commit beforehand. The
resource helper intentionally exits unsuccessfully after recording an allocation
or process refusal; the launcher must reject that completion. Following controls
verify successful launcher work, not Office export, PDF validation or publication.
Each request has a shortened fifteen-second deadline. Production timeout and
resource values are unchanged.

Every recorded root/child identity is absent or exited when the launcher returns.
Its production cleanup also requires zero active job members before return.
The final audit checks profile-directory and SID-mapping removal, all scratch
ACLs for the test SID, and unchanged helper/original bytes.


Retained evidence
-----------------

The passing source/binary-bound receipt is
`.codex-temp/office-resources/700866a38e9b44bbb55763dc79595de8`.
Its corresponding `office-isolation` directory contains the eleven case reports
and `results.json`: 61 checks, nineteen recorded process identities and 18 scratch
ACL entries checked. Managed compilation reports zero warnings/errors. The
native helper builds with warnings treated as errors. Source and binary hashes
remain unchanged across execution.

The first attempt, `7f3899c1232948d7bada28df8e7535e2`, passed the memory/process
cases but timed out on a 4,097-byte output fixture: the current reply protocol
permits 36 KiB. Its failed receipt is retained, with profile/mapping/ACL cleanup
passed and the timed-out helper separately confirmed absent. The corrected
fixture exceeds the current protocol constant by exactly one byte, checked by
the managed harness. The older ProcessProbe fixture receives the same correction.
No production bound was relaxed to obtain the pass.

This closes the concrete production-launcher pressure gap left by the earlier
[native isolation experiment](office-isolation-evaluation.md). The subsequent
[output I/O checks](office-io-failures.md) add actual renderer write refusal and
following worker exports. They do not fill a volume. The separate network-denial
evidence, actual Office content acceptance and exact final-candidate replay remain
separate requirements.
The 128 MiB PDF and 64 MiB calculated-workbook acceptance limits remain
post-export validation bounds; this test establishes no hard live disk quota.


Packaged library replay, 2026-09-24
---------------------------------

All 61 checks now pass using the exact `ContextSuite.Private.dll` and
`ContextSuite.Core.dll` shipped in unsigned 1.1.2. The disposable contract host
retains source-linked application ownership code whose sources match the
candidate build. The hash-checked resource helper is reused without compilation.
Evidence is retained at
`.codex-temp/candidate-112-office-safety-e3c31cf536a047b5bd8744ba6ff059be`,
with case/profile results under
`.codex-temp/office-isolation/fc3cc9cedefb4c38abe6dd0c800c36e9`.

This remains a launcher component test: it invokes the shipped private launcher
with the authored pressure helper, not an Office document. All eleven requests,
following controls and profile/process/ACL cleanup pass under unchanged limits.
The captured binaries, sources and candidate membership are unchanged afterward.
The accompanying [output-denial replay](office-io-failures.md#exact-candidate-replay-2026-09-24)
uses the exact release worker and native renderer. Neither result establishes
affirmative network denial or a live disk quota.
