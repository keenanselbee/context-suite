Paid Access And Image Release Candidate
======================================

Status: accepted; implementation in progress
Date: 2026-09-09

This decision settles the paid-access questions in decision 0006. It does not
claim release approval or authorize live commerce, publishing or installation.

- Publisher: Keenan Selbee. Final package publisher must match the validated
  signing identity. Signing purchase and identity verification are deferred.
- One purchase unlocks Context Suite and all future updates, including major
  versions. One active installation, transferable using customer deactivation.
- Keep the existing 72-hour local trial from first confirmed valid work.
- Paid access permits 30 days offline after successful online validation.
  Attempt background refresh once daily while running; unavailable servers do
  not revoke a still-valid cache. Explicit invalid/revoked/expired entitlement
  blocks new paid work once learned. Cached state is not a signed entitlement.
- No new Convert/Optimize work after access expires. Analyze, settings, license
  recovery and existing results remain available. Admitted batches finish.
- Store minimal activation state using Windows per-user protection. Preserve
  it across ordinary upgrades/uninstall. No serial-number fingerprinting,
  merchant tokens in the application, custom accounts or signing backend.
- Lost activation/deactivation replies require reconciliation or portal recovery,
  never automatic repeated activation requests that consume another slot.
- Website/Inno remains primary, retaining three peer Explorer commands and the
  classic/modern menu selection. Store EXE listing is a later possibility; free
  Store/MSIX signing does not justify changing the established menu contract.

The owner supplied sandbox identifiers, a successful test checkout/email and
license-grant screenshots, and confirmed duplicate-benefit cleanup. The benefit
settings show one activation, user deactivation, no expiry and no usage limit.
Actual desktop activation, transfer and refund/revocation remain unverified.

See the [release-candidate goal](../commercial-release-candidate-goal.md) and
[Polar integration plan](../polar-integration.md). Deployment identifiers stay
in the separate private configuration record; customer keys never enter Git.
