Product Versioning
==================

Status: local candidate version and staging reservation implemented, 2026-09-14.
Installation, signing and publishing remain separate authorized actions.

`Version.props` owns the product's `MAJOR.MINOR.PATCH` value. Minor and patch are
single digits; increment patch for an ordinary candidate, carrying 9 to the next
minor/major. Managed assemblies and file metadata derive `MAJOR.MINOR.PATCH.0`.
The three package templates use that same Windows form. Update their values and
[CHANGELOG.md](../CHANGELOG.md) together before staging changed packaged contents.
Private projects import the parent build properties; they do not own a separate
product version.

Run `tools/Test-ProductVersion.ps1` for source/template checks. Add `-Payload` to
check the staged application, worker, core, private and commercial assemblies.
`Build-Production.ps1` runs both checks and reserves candidate versions in
`artifacts/production-version-receipts/<version>.json` before producing files.
An exclusive create prevents concurrent or later reuse, even with another staging
GUID. A failed attempt retains its reservation; advance the version for another
attempt. New candidates must also be newer than all reserved versions. Successful
staging adds the inventory's SHA-256 and verified state.
Never remove a receipt to reuse the version, overwrite an existing payload, or
rebuild an already accepted version for publishing. Publish the preserved verified
artifact when separately authorized. This guard is deliberately stricter than
permitting a rebuilt identical retry.

The receipt covers the current local production-candidate command. It does not
yet enforce every downstream installer/archive command or verify release approval.
Ordinary compile/scratch test runs do not reserve versions. Existing development
payloads and historical receipts remain unchanged. See the changelog for the
earlier implicit managed 1.0.0 and shell-prototype 0.1.0.0 history.

Candidate 1.0.1 is
`artifacts/production-staging/a932bd2a928c48d5b708d407d892693a` with inventory
SHA-256 `2C0B0E1EAE3928A2783A7843E72E3F99442BC3D14433353A1CD953D8F6199855`.
The full production build and version checks pass. Reusing 1.0.1 with a fresh
staging GUID is refused before creating a payload; the prior receipt and inventory
hashes remain unchanged. Evidence: `.codex-temp/mp3-version-verification.json`.
Both absolute and relative payload paths pass the verifier after correcting the
initial relative-path resolution error. Host-only shell checks pass for three
manifests, three batches, six availability cases and unknown-schema refusal;
COM invocation and Explorer routing were not tested. Logs:
`.codex-temp/mp3-output-production.log` and `.codex-temp/mp3-version-shell.log`.

Three isolated fixture workspaces also verify rejection of a lower version,
a two-digit minor component and a mismatched package template, each before any
payload is created. Their scripts are repository-owned copies and never reach
engine/build execution. Evidence:
`.codex-temp/version-policy-7b4585256abd4419a16efcbbcebf7be1/version-policy.json`.
