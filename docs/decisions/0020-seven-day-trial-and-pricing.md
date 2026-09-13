Seven-Day Trial And Pricing
==========================

Status: accepted; local trial verified, live commerce pending
Date: 2026-09-13

This decision supersedes the trial duration and undecided pricing in decisions
0006, 0009 and 0017. It does not authorize live commerce or release publication.

- Context Suite costs $5 CAD as a separate one-time purchase including all future
  updates. One active installation remains transferable through Polar.
- The local trial lasts seven days (168 elapsed hours) from the first confirmed
  valid conversion or optimization. Opening the application does not start it.
- Existing schema-1 records retain StartedUtc. Calculate the new expiry as that
  original time plus seven days. An older trial between days three and seven
  becomes usable for the remaining time; it does not restart.
- Preserve clock checks, failure handling, admitted-batch completion and free
  Analyze access. Rejected paid access must not fall back to a new trial.
- Preserve daily paid refresh and 30-day offline grace. Purchase entitlements
  have no scheduled expiry or major-version restriction.
- Update the existing Polar product and benefit rather than creating duplicate
  Context Suite grants. Verify CAD checkout, tax treatment, key delivery and
  refunds separately in sandbox, then production. No recurring billing trial.

Codex Navigator adopts the same price, trial length and paid validation policy
as a separate product. Its trial begins explicitly and expiry replaces its view
with licence recovery; those implementation decisions belong to its own repo.

Verification must cover original-start migration, the exact seven-day boundary,
restart persistence, concurrency, clock rollback and admitted work. Existing
three-day fixture expiry dates must be moved past seven days.
