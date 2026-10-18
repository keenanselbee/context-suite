Office Component Dependency Notices
===================================

Status: supplemental source notices and packaging verified on 2026-09-23.
The native runtime is unchanged. Final candidate replay, recipient source
delivery, modified-library support and release adoption remain open.

A separate [v2 companion verifier](../tools/office-engine/Test-ComponentNoticeSourcesV2.py)
supports the Python-free experiment without changing the historical v1 verifier.
It requires an independently selected record, exact portable companion members,
source-kit and runtime identities, completed source correspondence, native and
Java dependency evidence, and byte-exact regenerated notices. Source archives
remain in the companion source kit; verification extracts or executes no code.
Eleven focused tests pass, including actual-archive and reduced-runtime evidence
checks. The 83 archives reproduce all 364 selected notice blocks, including NSS's
embedded SQLite disclaimer. Full and reduced companion verification passes;
the selected reduced runtime compares all 301 retained native/JAR files against
the 304-file full build. Source correspondence verifies 148,225 exact files with
no extras. Combined candidate integration remains pending in the
[size-reduction goal](repository-size-reduction-goal.md).

The Python-free runtime and v2 supplement are now selected for packaging. The
historical v1 checkpoint below describes the earlier bundled-Python runtime.


Source selection and correspondence
----------------------------------

The [supplement](../assets/third-party/office/Office.Dependency.Notices.html)
contains **367** source notice files and selected complete comment blocks from
**76** dependency archives already retained in Office source kit 04. It adds
libexttextcat's missing component notice, nested PDFium/Skia notices, the JPEG
IJG text, OpenSSL acknowledgments, SQLite's source disclaimer, and lp_solve's
COLAMD authors/copyright, permission acknowledgment and original availability.
The existing runtime licenses and [Python supplement](office-python-notices.md)
remain required.

The selection is intentionally conservative: named license, copyright, author
and patent files include nested tests and build tools. Their presence does not
assert that every nested source component is shipped. Header dependency records
are evidence of a build dependency, not proof that every referenced library is
linked. The [review record](../tools/office-engine/component-notice-review.json)
preserves these limits and the exact selected archive identities.

The current runtime comes from `build/source-kit-01`, with reconstructed source
`source/kit-replay-01`. All **321** native runtime files match that build's
installed output or its five separately recorded app-local Microsoft runtime
additions. **244** matching target dependency records identify source components;
eight explicit Python/crypto/GPG dependencies supplement that set. The unpacking
recipes resolve to the source kit's pinned archives. No source archive was
downloaded, installed or executed for this work.

An initial scratch inventory used the older `build/chart-native` dependency
records. A native-file comparison refused that mismatch. The retained
`current-build-review.json` and packaged evidence instead bind the current
source-kit build. The component/archive set agrees, but the old target records
are not used to claim correspondence with the current binaries.


Text and retained evidence
--------------------------

The HTML supplement is **2,601,702 bytes**, SHA-256
`5FAAF519C449FA33B5E8CEA1CE0DE2659C6C8972913EA056CFBDFD9695E25C9F`.
It displays escaped source text in UTF-8, with local component links, wrapping
and browser light/dark styling. LittleCMS AUTHORS and the Office PDFium FreeType
license use ISO-8859-1 in their archives; their text is decoded explicitly.
All **367** HTML text blocks encode back to their recorded original text bytes.
Original complete source files remain in the source kit. SQLite and lp_solve
comment selections record their full source-file hashes and exact byte ranges.
The COLAMD source in the current unpacked build matches its retained archive.

The review companion is retained at
`.codex-temp/office-component-review/fb3854d03e7c4368a57f3e919c6a0f0b/office-component-notice-evidence.zip`.
It is **3,548,570 bytes**, SHA-256
`90C4A20733CF5D10EE1834B0144DB29EBDCDC8ADCA672E71EB27E4928DE53EF8`.
It contains the supplement, review records, unpacking recipes, dependency
records and runtime inventory. It references the existing source kit rather
than duplicating its dependency archives. It contains no proprietary application
source and is not a published source offer.

Verify with:

```powershell
python -B tools/office-engine/Test-ComponentNoticeSources.py <evidence.zip> --source-kit <office-source-kit-04> --engine-directory <office-engine>
```

The verifier checks the current selection, archive hashes, exact companion
membership, build dependency mapping and complete selected native-file set.
It recreates the supplement directly from source archives without extracting
or executing their code. The engine argument adds actual native-file comparison.
`verification-03.json` records successful verification plus four refusals for
changed source archives, changed build records, extra companion members and
changed notice text. It also records all text-block comparisons and 76 local
component links. Actual browser appearance and screen-reader delivery remain
unverified.


Packaging checks
----------------

Office staging now requires `Office.Dependency.Notices.html` beside the
application, outside the immutable engine. It verifies the notice's exact
identity and current runtime/source-kit binding before copying. Existing
supplement files are never overwritten. The production allowlist and offline
index include the additional file; the expanded index has **63** links.

**60** Office payload checks pass at
`.codex-temp/office-payload-tests-7aa14fb8aebe4abaa4546bd8391e07d9`.
They include actual full staging/production verification, missing/corrupt
supplements, mismatched selection, existing-file preservation and earlier
inventory/copy-failure guards. **71** index checks pass at
`.codex-temp/notice-index/3aff510a6ec34ba699abec53e18f3adc`.
All captured source/base/engine identities remain unchanged. No native rebuild,
Office process, Windows profile, installation or product-version reservation
was needed.

The first payload-test attempt stopped because the newer scratch base had no
formal `payload-inventory.json`. The harness now removes an inherited receipt
only when present; scratch composition does not create a formal version receipt.
The full engine and production allowlist checks remain required and passed.
The failed attempt is retained at
`.codex-temp/office-payload-tests-a99babbeaac94a2d8aa00fb950c8c53d`.

This checkpoint closes the identified component-notice omissions. Consolidating
the source/review packet and completing the remaining
[distribution decisions](candidate-distribution-review.md) still precede final
candidate composition. Source inventory integrity is not release clearance.
