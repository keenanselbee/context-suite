Signed Installer Acceptance Checklist
====================================

Status: prepared, not executed. All native rows below are pending.
The internal installer still rejects existing installation directories. Passing
mocked contracts or compiling an EXE does not authorize opening that gate.

Decisions before a native run
----------------------------

- Publisher/display name is owner-selected: **Keenan Selbee**. Complete trusted
  signing onboarding and confirm the exact certificate subject.
  Recommendation: use one stable publisher for the EXE and all three sparse
  identities; settle its exact certificate subject before freezing manifests.
  Do not infer the certificate subject from a website name or test publisher.
- Owner: approve permanent package names ending in Analyze, Convert and Optimize,
  one shared prefix, stable installer AppId and a four-part version policy.
  `ContextSuite.PackagingTest`, `CN=UnsignedPackagingTest` and the internal
  installer AppId are evidence identities, not approved production identities.
- Owner: choose an explicitly authorized disposable Windows test machine or
  isolated Windows runner. Windows Home is sufficient for development; no host
  certificate trust changes, feature enabling or VM setup are implied here.
- Release engineering: define signed-output provenance. Signing changes hashes;
  retain unsigned engine/source evidence, sign the intended payload, then
  inventory and verify the final signed bytes. Do not change curated source pins
  just to make a signed DLL pass an unsigned-payload checker.
- Owner/release review: complete the separate redistribution and product-terms
  checks in [release redistribution](release-redistribution.md).

Microsoft requires the package publisher to match its signing certificate;
see [package signing](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)
and [external-location package identity](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps).
If the owner selects Microsoft's hosted signing service, check current eligibility
and onboarding in [Artifact Signing setup](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart).
No provider account, certificate or spending decision has been made by this task.

Evidence packet for each run
---------------------------

Record Windows edition/build/architecture, original user and elevation, installed
runtime versions, source commits, dirty-source flags, installer/package SHA256,
certificate subject/thumbprint and signature verification results. Capture setup
exit codes/logs, active pointer/journal contents and current-user package names,
versions, publishers and actual external locations before and after each case.
Use disposable image/DDS fixtures and isolated settings/trial/license sentinels;
record hashes before/after. Never use customer media or real payment credentials.

Native matrix
-------------

| ID | Case and required result | Status |
| --- | --- | --- |
| N01 | Trusted signed fresh install as non-admin: one uninstall entry, one working Start menu shortcut, three peer Explorer roots pointing at the committed versioned payload. | Pending |
| N02 | Each runtime missing separately and both missing: only missing prerequisites run offline, consent/UAC belongs to prerequisites, app registration remains the original non-elevated user. | Pending |
| N03 | Prerequisite declined/fails/needs restart: clear non-success result; no app activation, forced reboot or Explorer restart. Re-run after restart behaves correctly. | Pending |
| N04 | Explorer Analyze/Convert and Start menu work from installed files; image/DDS conversion preserves inputs and validates output. Check spaces/Unicode user paths. | Pending |
| N05 | Unsigned/wrong-publisher/tampered inputs, prototype registration, legacy flat root and foreign installation are rejected without registration or user-data changes. | Pending |
| N06 | Higher-version upgrade: old payload retained until verified switch; exactly three roots at new external locations, no duplicate commands, active launch and settings/trial/license continuity. | Pending |
| N07 | Same-version exact-release repair: damaged app and missing registrations repaired into a separate directory. Changed same-version payload and downgrade rejected. | Pending |
| N08 | Fail/terminate before and after each registration, pointer and journal boundary: recover according to durable state; never remove the only usable registered payload. Verify actual process termination, not just thrown exceptions. | Pending |
| N09 | Terminate/cancel during extraction and before uninstaller creation: report incomplete state honestly; document supported recovery or owner-assisted cleanup. No arbitrary recursive deletion. | Pending |
| N10 | Uninstall after success, failed upgrade, partial first install and failed uninstall: target actual active version, retain files on unregistration failure, retry without resurrecting removed registrations. | Pending |
| N11 | Actual Inno file ledger: remove owned versions and exact metadata only, leave unknown files and user data intact. Record any leftovers and reboot-required behavior. | Pending |
| N12 | Running app/worker/Explorer surrogate and concurrent setup/uninstall: no forced termination, coherent failure/retry, mutex serialization; verify named-mutex ABI and cancellation callbacks. | Pending |
| N13 | Network unavailable and signing trust/revocation failures: predictable errors, no unexpected dependency download, media stays local. Verify launch-helper appearance and pending-state messages. | Pending |

N06-N08 require a separately reviewed native-test admission change and explicit
test authorization. Today's compiled installer cannot exercise them through
normal setup. Do not add a customer-visible bypass or mark these rows passed
from the lower-level coordinator tests. Keep this matrix separate from automated
contract counts. Power-loss guarantees require separate evidence beyond killing
the setup process.

Exit criteria
-------------

Attach evidence per row, triage failures, rerun the affected cases on the final
signed artifact, and review before enabling customer upgrades. Hosted CI,
commercial clearance and paid Polar activation remain separate gates. Approval
of this checklist is not installation, signing or publishing authorization.
