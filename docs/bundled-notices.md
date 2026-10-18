Bundled Third-Party Notices
==========================

Status: implemented and checked in source on 2026-09-23; fresh candidate
composition and human acceptance remain pending.

The later [PNG dependency notice checkpoint](png-dependency-notices.md) adds two
required base notices. [Office Python notices](office-python-notices.md) add one
optional Office file, and [PDF dependency notices](pdf-dependency-notices.md) add
three qpdf supplements. [Office component notices](office-component-notices.md)
add one further file: current indexes have ten base or 63 expanded links and
pass 71 index checks. The initial 8/56-link evidence below remains historical.

The 2026-10-13 Office packaging update passes 81 index checks. It binds the
packaged source record to the current selection before choosing Python notices.
Bundled mode preserves the existing link order; Python-free mode omits that
one link and rejects a stale Python notice. The selected Python-free runtime
now passes 58 complete Office packaging checks; candidate 1.3.4 verifies all
282 local links. Office itself remains bundled.

License now includes **Third-party notices**, available without activation or
online validation. It opens `ThirdPartyNotices.html` beside the application in
the Windows file association for HTML. Missing files and association failures
produce a separate accessible status message without replacing license status.
The existing Close/Escape action and compact content sizing are retained.

The production build generates an offline index after assembling the selected
components. Links point to bundled files, grouped by tool family. It has no
scripts or remote assets and follows the browser's light/dark appearance.
It does not fetch source archives or promise an unconfigured download location.
The [redistribution work](candidate-distribution-review.md) still includes source
delivery, modified-library support, complete transitive notice review and owner
adoption decisions.

`tools/release/Write-NoticeIndex.ps1` derives optional links from the existing
component selection records and fixed Office notice paths. Both supported audio
profiles currently share the same paths; the script rejects a future mismatch.
Their existing engine verifiers remain responsible for exact notice hashes.
Generation requires every selected notice to exist and be nonempty. The payload
allowlist now admits the index, verifies its generated contents and includes it
in the normal file inventory. Historical reserved payloads remain untouched;
they predate this new packaging requirement.


Verification
------------

- Release test-host build: zero warnings/errors; 126 hidden view contracts pass.
  These include notice-button accessibility, missing files, successful local
  launch routing, association failure, independent license status and polite
  status automation. Browser launches were intercepted in the tests; no real
  licensing storage or network was used.
- `tools/release/Test-NoticeIndex.ps1`: 64 checks pass for base and expanded
  membership, all local links, stale/edited indexes, missing/empty notices and
  preservation of the preceding index after failed generation. The final receipt
  is `.codex-temp/notice-index/2e5e2ac79ecd4d4f9ca1f864002eaa2a/results.json`.
- Actual selected notice copies also pass generation and verification at
  `.codex-temp/notice-index-real/0292694827f840f88176d0a38aa5c32f`.
  Its input receipt binds original/copy hashes to the selected expanded audio,
  Voice, HEIF and PDF records, current Office inventory and base payload inventory.
  All originals remain unchanged. No engine binaries were copied or rebuilt.

The base index has eight links; the expanded index has **56**. The latter adds
the PDF renderer's extensionless `LICENSE` and `libjpeg_turbo.ijg` to the 54 files
in the earlier review collection, and exposes the retained HEIF patch records.
This improves notice access without asserting that the full transitive review
or source offer is complete.

The first synthetic run assumed the earlier collection's count and failed;
comparison with the selection records identified the two omitted PDF files and
retained HEIF patch links. The first actual-copy attempt used curated audio
hashes for expanded audio and stopped before generation. Its failure record is
retained at `.codex-temp/notice-index-real/7a5cb2da53e6420ca2bbbc94ef1e59c9`;
the corrected attempt uses the expanded record. No expected engine or notice
hash was changed.

Actual browser launch, visual layout, screen-reader delivery, other themes/DPI,
and replay against the fresh complete production payload remain unverified.
For manual review, open Settings, then License, choose **Third-party notices**,
and check a notice from each included tool family. Confirm that license status
is unchanged and closing the browser leaves Context Suite usable.
