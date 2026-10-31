Free Product And Public Implementation
======================================

Status: accepted; local migration and candidate verification complete.
Live retirement and release acceptance remain separate; see the transition goal.
Date: 2026-10-30


Decision
--------

Context Suite becomes free to use with all supported Analyze, Convert, and
Optimize features and future official updates. Remove trials, purchases,
activation, licensing network requests, and commercial admission checks from
the production application. Keep one production build and the existing media
capabilities, engine pins, output safety, and installation boundaries.

Import a reviewed snapshot of the required authored media implementations,
native bridges, and tests into the main repository. The owner explicitly
included the unfinished public/private PDF dark-mode work in this migration
and its verification. Preserve the independent private repository and history;
it becomes an archive, not a build or CI dependency. Do not import its Git
history, commercial configuration, ignored inputs, or licensing implementation.
Existing ContextSuite.Private namespaces may remain to avoid unrelated identity
changes; they no longer denote a separate checkout requirement.

Keep the published source available for inspection under the repository's
source-available terms. Free official use does not imply an open-source license.
Preserve third-party notices, source-delivery obligations, and native engine
identities. Complete application source does not make engine inputs optional
or imply byte-identical native rebuilds.

Remove only commercial checks. Retain confirmed-plan validation, executable-item
checks, batch identity, explicit loss/matte/replacement choices, supported-tool
checks, cancellation, worker isolation, validation, and transactional output
publication. Analyze remains read-only. Old trial and license records are inert:
never read, migrate, refresh, or delete them as part of the free runtime.
Ordinary settings, recovery state, installed payloads, and rollback history remain.

Prepare a verified unsigned free candidate with exact source and engine evidence.
Signing, native installer acceptance, installation, pushes, website deployment,
live storefront changes, and remote archival remain distinct actions. Stop new
sales only as a reviewed release step; preserve purchase history and consider
older paid clients before disabling any service they still require.


Superseded Decisions
--------------------

This decision supersedes the private-build requirement in
[0005](0005-public-and-proprietary-builds.md), commercial access in
[0006](0006-trial-and-purchase-access.md), the trial portions of
[0009](0009-first-image-engine-and-trial.md), paid access in
[0017](0017-paid-access-and-release-candidate.md), and pricing/trials in
[0020](0020-seven-day-trial-and-pricing.md). It removes License from the customer
surfaces in [0018](0018-context-menu-utility-and-output-preference.md). Their
media, safety, process, and historical acceptance decisions otherwise remain.

Track actual implementation and verification in
[the free transition goal](../free-access-goal.md).
