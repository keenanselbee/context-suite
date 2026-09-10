Trial And Purchase Access
=========================

Status: accepted; trial settled in decision 0009, paid policy settled in decision 0017
Date: 2026-09-06


Context
-------

The intended commercial product offers a three-day trial, then requires paid
license activation. Development should prioritize useful media
workflows and ordinary paying customers over extensive piracy prevention.


Decision
--------

Adopt a three-day trial and Polar-hosted checkout with in-app license-key
activation for the planned commercial release. This updates the original
website-login direction: no custom website accounts or authentication backend
are required. A checkout redirect alone does not establish paid access.
See the [Polar integration plan](../polar-integration.md) for provider setup,
configuration boundaries, and the implementation checklist.

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


Policy And Remaining Release Decisions
--------------------------------------

- Local trial: [decision 0009](0009-first-image-engine-and-trial.md) settles
  72 elapsed hours from the first confirmed valid conversion (extended to
  optimization by decision 0013), atomic start,
  clock-change handling, safe storage failures, and in-flight completion.
- Activation: Polar license key with one active installation and customer
  deactivation for transfers. Target a non-expiring one-time purchase; confirm
  the actual dashboard settings and customer-facing terms before release.
- Offline access: [decision 0017](0017-paid-access-and-release-candidate.md)
  accepts 30 days from successful validation, daily refresh and cached access
  during outages without extending the deadline.
- Settings and existing results remain available after trial expiry; new
  conversion/optimization is blocked. Analyzer stays available. Portal-assisted
  transfer is accepted; actual refund/revocation behavior still needs live testing.
- Polar is selected; the user reports account approval. All future updates are
  included in one purchase. Final pricing,
  public-source licensing, and customer-facing terms remain undecided.

Implement the settled access policy with its failure tests; verify the
remaining live-provider and release gates before sale. Polar's online JSON validation is not a signed
offline license. A custom signing service is not part of the initial plan.


Consequences
------------

Milestone 1 defines a replaceable access-policy boundary and test states. The
website, payment integration, and real activation flow can follow the first
useful media slices, but must be verified before the paid image release.

Validation must cover trial expiry, unactivated and paid access, invalid licenses,
unavailable services, and completion of batches admitted before expiry. Exact
offline and recovery expectations follow the policy decisions above.


Alternatives Considered
-----------------------

- Seven-day trial: replaced by the chosen three-day product direction.
- Continuous online enforcement and extensive anti-tamper work: disproportionate
  to the intended maintenance budget.
