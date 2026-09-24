PNG Dependency Notices
======================

Status: source and notice preparation implemented on 2026-09-23; the optimizer
executable is unchanged. Commercial adoption, advisory review and the fresh
expanded candidate remain separate gates.

The oxipng 10.1.0 Windows executable was previously accompanied only by its root
MIT license. The selected source at revision
`d54e7c04ea58170167d95d9b2b2ec07571602fe2` declares additional dependencies.
The [notice record](../tools/png-engine/notice-review.json) now binds the source
archive, Cargo lock, normal/build dependency closure, individual notice hashes
and the unchanged production executable hash.

Locked Cargo metadata for `x86_64-pc-windows-msvc` identifies oxipng plus 50
registry crates. The collection conservatively includes build-time packages;
it is not a claim that every listed crate contributes instructions to the final
binary. Root development-only dependencies are excluded. All 50 crate archives
match their lockfile checksums, and the inspected extracted files match those
archives. No build scripts were executed and no native compilation was run.

The collection retains 92 license/notice files and copyright lines from 76
source files. This includes the Apache-licensed libdeflater wrapper, the separate
MIT libdeflate COPYING file, and crossbeam-channel's third-party notice.
`assets/third-party/oxipng/Oxipng.ThirdPartyNotices.txt` combines those notices
without changing their license text.

The existing executable contains Rust source identity
`f134bbc78dac04a17324341a8d4a15a6930d81a7`. The upstream January 25, 2026 nightly
manifest identifies the same revision. Its checksum-verified compiler archive
supplies the unchanged `COPYRIGHT-library.html`, retained as
`Oxipng.Rust.Library.Notices.html`. The compiler and standard-library archives
were downloaded only for inspection; neither was installed or executed. This
does not establish Microsoft runtime/tool entitlement or reproduce the upstream
optimizer build. The pinned upstream executable remains the selected artifact.

The two additional notice files total **973,396 bytes**. The private project
copies them into worker build output, the PNG stager includes them, and final
payload verification requires their exact hashes. The component-staging check
can still validate the unchanged original engine inputs independently. The
[License notice index](bundled-notices.md) now exposes both files: ten links in
the base package and 58 with all selected optional components.


Retained source and verification
--------------------------------

Evidence is under
`.codex-temp/png-notice-review/2acc0f981e3b4558a9666a89d895af6c`.
Its `oxipng-source-notices.zip` contains the upstream source ZIP, 50 crate
archives, both generated/retained notice files and their review record. It is
24,679,341 bytes, SHA256
`FCDAB5EADB7D12F00BCDC32626FF16BF1B1C65C86326062CD9C98D47223F6293`.
It contains no proprietary application source. The Rust compiler binaries are
not included in this source companion.

- `python -B tools/png-engine/Test-NoticeBundle.py <archive>` verifies 51
  component records, all 92 retained notice identities, source archive and asset
  hashes, and exact bundle membership without extracting or executing anything.
- `tools/png-engine/Test-DependencyNotices.ps1` passes nine checks for valid,
  missing and altered notices, with restored controls. Receipt:
  `.codex-temp/png-notice-tests/b697d023c7384869b69e3d021b322f34/results.json`.
- `tools/release/Test-NoticeIndex.ps1` passes 66 checks with the new links.
  Receipt: `.codex-temp/notice-index/3aa80e424d69417d94b3082e06b8131f/results.json`.
- Fresh isolated Release application/worker builds pass with zero warnings/errors.
  The base-image scratch composition at
  `.codex-temp/png-notice-payload/000a4e3d19c14b77b8ceda2be3ced45d` passes the full
  production payload allowlist, engine hashes and notice index check. This is
  not the final versioned expanded candidate and has no installed registration.

Initial scratch composition attempts detected stale shared assemblies in normal
incremental output. Fresh isolated output removed that ambiguity. Its library
build products also included three library-only `.deps.json` files; these were
excluded from scratch application composition, preserving the production
allowlist. Earlier attempts remain retained. No binary pin or guard was relaxed.

Both new notice assets use fixed LF checkout rules. The Rust HTML retains its
original whitespace and the generated collection retains its bound end-of-file
spacing; exact-file whitespace attributes preserve their verified hashes.
The original executable, reserved packages and installed application are unchanged.
