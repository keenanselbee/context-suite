Polar Integration Plan
======================

Status: provider selected; implementation deferred
Date: 2026-09-06

The user reports that Polar has approved the account. No dashboard configuration,
purchase, license activation, download delivery, or refund flow has been verified
from this repository. No live credentials are needed for this documentation.
This plan implements the direction in
[decision 0006](decisions/0006-trial-and-purchase-access.md).


Product Setup To Confirm
-----------------------

- Product: Context Suite, one-time purchase; price and upgrade rights still open.
- Fulfillment: Polar delivers license keys and the installer when release-ready.
- License benefit: visible, prefix CONTEXT, one activation, customer deactivation
  enabled, no usage quota, and no paid-key expiry. These are intended settings,
  not verified dashboard state. The three-day app trial is separate.
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
| Organization ID | `<organization-id>` | Pending |
| Product ID | `<product-id>` | Pending |
| License benefit ID | `<license-benefit-id>` | Pending |
| Checkout URL | `<checkout-url>` | Pending |
| Customer portal URL | `<customer-portal-url>` | Pending |

The organization, product, and license benefit IDs and checkout URL were supplied
by the user during production onboarding. Confirm their environment and product
association before runtime use; checkout and benefit delivery remain untested.
The organization slug and support email are recorded with the private values.
The portal URL follows the documented slug-based format; verify it opens the
correct organization before shipping. See
[Polar customer portal](https://polar.sh/docs/features/customer-portal/introduction).
The displayed CAD default is a product/checkout setting, not verification of
the payout currency. Screenshot observations are not live integration tests.

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
project under proprietary/, after reading its own instructions. Use ordinary
asynchronous HTTP behind the existing boundary; do not introduce a service,
custom login system, or multi-provider framework merely for future flexibility.

The application will collect the customer key, activate it against the configured
environment, and retain the returned activation ID. Subsequent validation must
check the expected organization, license benefit, activation, granted status,
and expiry rather than treating any successful HTTP response as paid access.
Use the customer-facing license endpoints; never ship a merchant bearer token.
See [Polar license documentation](https://polar.sh/docs/features/benefits/license-keys).

Store customer activation state in protected per-user application storage, never
source control, logs, shell request files, or worker messages. Proposed protection
is Windows user-scoped encryption; confirm recovery semantics before coding.
Use minimal installation data rather than hardware serials, IP binding, or media
paths. A one-instance limit is modest enforcement, not proof of unique hardware.

Check access before admitting work in application orchestration, never during
Explorer menu enumeration or media parsing. Already admitted batches finish and
their results remain accessible after expiry. Network calls need cancellation,
bounded timeouts, and distinct invalid-license versus service-unavailable states.

Offline grace and revalidation timing require a decision. A locally cached JSON
response is not a provider-signed offline entitlement; local encryption does not
make it one. Do not add a custom signing backend or promise indefinite offline
access. Recovery after a lost activation response must not blindly create more
instances; provide a retry/reconciliation or portal-deactivation path.


Implementation And Release Checklist
------------------------------------

- [ ] Capture the non-secret configuration above and verify the benefit settings.
- [ ] Decide trial start/clock rules, post-trial feature access, offline grace,
  refresh frequency, device/reinstall recovery, refunds, and upgrade rights.
- [ ] Exercise checkout and benefit delivery in Polar's separate
  [sandbox environment](https://polar.sh/docs/integrate/sandbox).
- [ ] Implement private adapter, public activation UI, protected storage, and
  admission policy without changing media or Explorer responsibilities.
- [ ] Add deterministic contract tests with synthetic keys and simulated HTTP;
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
