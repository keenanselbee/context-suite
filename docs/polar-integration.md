Polar Integration Plan
======================

Status: paid policy accepted; implementation in progress
Date: 2026-09-06

The user reports Polar approval and supplied sandbox checkout, confirmation email,
license grant and benefit-setting screenshots. Desktop activation, download
delivery and refund flow remain unverified. No live credentials are needed here.
The [accepted paid policy](decisions/0017-paid-access-and-release-candidate.md)
settles lifetime updates, daily refresh, 30-day offline grace, one active
installation with transfer and free Analyzer access after trial expiry.
This plan implements the direction in
[decision 0006](decisions/0006-trial-and-purchase-access.md).


Product Setup To Confirm
-----------------------

- Product: Context Suite, one-time purchase including all future updates; price is $5 CAD under [decision 0020](decisions/0020-seven-day-trial-and-pricing.md).
- Fulfillment: Polar delivers license keys and the installer when release-ready.
- License benefit: the owner confirmed one activation, customer deactivation
  enabled, no usage quota and no paid-key expiry on 2026-09-13. Visible delivery
  and prefix CONTEXT remain intended presentation settings. The seven-day app
  trial is separate; owner confirmation is not live API acceptance.
- Hosted checkout with Polar's default confirmation page; no custom account site.
- Keep live checkout unpublished until delivery and commercial access pass tests.


Configuration Reference
-----------------------

Record each environment separately. Do not invent IDs or copy production values
into sandbox configuration. The following values are not secrets, but actual
deployment configuration belongs with private commercial composition. The
user-supplied production values are recorded in
`proprietary/docs/polar-configuration.md` in the separate private repository.
That file is not required for reading or validating these public docs.

| Value | Production | Sandbox |
| --- | --- | --- |
| Organization ID | Recorded privately | Recorded privately |
| Product ID | Recorded privately | Recorded privately |
| License benefit ID | Recorded privately | Recorded privately |
| Checkout URL | Recorded privately | Recorded privately |
| Customer portal URL | Recorded privately; authenticated recovery unverified | Recorded privately; authenticated recovery unverified |

The organization, product, and license benefit IDs and checkout URL were supplied
by the user during production onboarding. Confirm their environment and product
association before release use. Sandbox checkout and benefit delivery have
owner-provided screenshot evidence. The owner also exercised sandbox desktop
activation, validation, second-install refusal and transfer; see the dated
[release evidence](commercial-release-candidate-goal.md). Final test-activation
cleanup and installed production acceptance remain unverified.
The organization slug and support email are recorded with the private values.
The portal URL follows the documented slug-based format; verify it opens the
correct organization before shipping. See
[Polar customer portal](https://polar.sh/docs/features/customer-portal/introduction).
The displayed CAD default is a product/checkout setting, not verification of
the payout currency. Screenshot observations are not live integration tests.

The owner confirmed on 2026-09-13 that Context Suite's existing live product is
CAD 5.00 as a one-time purchase, matching decision 0020. This is owner confirmation,
not a completed production purchase. Context Suite's four access-related benefit
settings also have separate owner confirmation; Navigator's screenshots alone
would not establish them. Both products use the same Polar organization.
Trials remain local and retain their original start
time; no Polar subscription trial is needed.

Keep actual environment values in the private configuration record, not this
public table. This is release organization, not a security boundary: identifiers
are not credentials, and published checkout/portal links are intentionally public.
Do not paste merchant access tokens, customer keys, bank details, identity
documents, or webhook secrets into chat or either repository. No administrative
token is required in the distributed desktop application. If release automation
later needs a token, use a scoped CI secret or secret manager, not a tracked file.


Implementation Boundary
-----------------------

Keep the public access-policy contract and UI in the public repository. Implement
Polar-specific transport and commercial composition in the existing private
`ContextSuite.Commercial` project under proprietary/. Use ordinary
asynchronous HTTP behind the existing boundary; do not introduce a service,
custom login system, or multi-provider framework merely for future flexibility.

The application collects the customer key, activates it against the configured
environment, and retain the returned activation ID. Subsequent validation must
check the expected organization, license benefit, activation, granted status,
and expiry rather than treating any successful HTTP response as paid access.
Use the customer-facing license endpoints; never ship a merchant bearer token.
See [Polar license documentation](https://polar.sh/docs/features/benefits/license-keys).

Store customer activation state in protected per-user application storage, never
source control, logs, shell request files, or worker messages. Implemented protection
is Windows DPAPI CurrentUser with bounded, atomic storage and pending-operation recovery.
Use minimal installation data rather than hardware serials, IP binding, or media
paths. A one-instance limit is modest enforcement, not proof of unique hardware.

Check access before admitting work in application orchestration, never during
Explorer menu enumeration or media parsing. Already admitted batches finish and
their results remain accessible after expiry. Network calls need cancellation,
bounded timeouts, and distinct invalid-license versus service-unavailable states.

Daily refresh and 30-day offline grace are accepted and implemented. A locally cached JSON
response is not a provider-signed offline entitlement; local encryption does not
make it one. Do not add a custom signing backend or promise indefinite offline
access. Recovery after a lost activation response must not blindly create more
instances; provide a retry/reconciliation or portal-deactivation path.


Implementation And Release Checklist
------------------------------------

- [x] Capture non-secret configuration and owner-provided sandbox benefit settings.
- [x] Decide trial start/clock rules, post-trial feature access, offline grace,
  refresh frequency, transfer recovery and lifetime upgrade rights in decision 0017.
- [x] Owner exercised checkout and benefit delivery in Polar's separate
  [sandbox environment](https://polar.sh/docs/integrate/sandbox).
- [x] Implement private adapter, public activation UI, protected storage, and
  admission policy without changing media or Explorer responsibilities.
- [x] Add deterministic contract tests with synthetic keys and simulated HTTP;
  do not make ordinary tests contact Polar or use customer credentials.
- [ ] Verify first activation, repeated validation without consuming another
  slot, second-installation rejection, deactivation, and successful transfer.
- [ ] Verify wrong organization/benefit, revoked or expired keys, key rotation,
  malformed responses, timeouts, rate limits, offline recovery, and lost replies.
- [ ] Verify actual refund-to-revocation behavior; do not assume a refund
  automatically removes every entitlement. Document any required operator step.
- [ ] Verify trial expiry during a batch, restart/upgrade persistence, and no
  credential leakage in logs, public CI, source archives, or packaged binaries.
- [ ] Attach the tested installer and verify customer download/portal recovery
  before publishing live checkout. Sandbox success is not production evidence.

Do not delay useful media slices for commerce implementation. Account approval
settles onboarding, not release readiness. This documentation does not authorize
live purchases, refunds, uploads, or dashboard changes.

See [licensing implementation and verification](licensing-verification.md) for
local test evidence and customer recovery instructions. The remaining checklist
items require live-provider or installed/interactive evidence; synthetic tests
do not satisfy those release gates.
