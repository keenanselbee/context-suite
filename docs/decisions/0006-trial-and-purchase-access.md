Trial And Purchase Access
=========================

Status: accepted for product direction; detailed policies remain open
Date: 2026-09-06


Context
-------

The intended commercial product offers a three-day trial, then requires website
login and purchase validation. Development should prioritize useful media
workflows and ordinary paying customers over extensive piracy prevention.


Decision
--------

Adopt a three-day trial and website-based purchase activation for the planned
commercial release. Login identifies an account; a separate entitlement check
determines whether that account owns access to the product.

Keep all media processing local. Network access is permitted for account and
purchase services; file contents and selected file paths are not activation
data. The application owns access decisions before starting a paid operation.
Explorer menu enumeration and media parsers never contact licensing services.

Trial expiry must not interrupt an already admitted batch or prevent viewing
its results. Display expiry and activation status clearly. Trial and paid access
use the same production application; there is no separate review access mode.

Accept that local trial records and client-side checks can be bypassed. Do not
add invasive hardware fingerprinting, scattered hidden trial records, or
anti-debugging machinery as part of the initial release.


Proposed Policy And Open Decisions
---------------------------------

- Trial start: proposed at the first actual media operation, not installation.
  Confirm the triggering event, elapsed-time definition, and clock-change rule.
- Activation: proposed browser login followed by a signed license stored
  locally, with only the verification public key distributed in the app.
- Offline access: recommended after successful activation; license lifetime,
  refresh requirements, and outage behavior are not yet selected.
- Confirm which features remain available after expiry, account recovery,
  device transfers, refunds/revocation, and supported access states.
- Pricing, purchase model, identity/payment providers, public-source licensing,
  and customer-facing terms remain undecided.

Resolve these policies and their failure cases before implementing the trial
and before a paid release. This record does not select an authentication
protocol, cryptographic format, or billing provider.


Consequences
------------

Milestone 1 defines a replaceable access-policy boundary and test states. The
website, payment integration, and real activation flow can follow the first
useful media slices, but must be verified before the paid image release.

Validation must cover trial expiry, unpaid and paid accounts, invalid licenses,
unavailable services, and completion of batches admitted before expiry. Exact
offline and recovery expectations follow the policy decisions above.


Alternatives Considered
-----------------------

- Seven-day trial: replaced by the chosen three-day product direction.
- Continuous online enforcement and extensive anti-tamper work: disproportionate
  to the intended maintenance budget.
