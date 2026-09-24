PDF Dependency Notice Review
============================

Status: the identified qpdf dependency notice omission is repaired in packaging
on 2026-09-23. PDFium's selected notice texts have retained upstream comparisons
and supplier recipes. This is source/notice engineering evidence, not a new
native build, supplier-build attestation or commercial redistribution approval.


qpdf dependencies
-----------------

The selected qpdf 12.4.1 Windows archive contains qpdf30.dll with statically linked
dependency libraries.
Its source release's Windows build recipe installs libjpeg-turbo, zlib and
OpenSSL through vcpkg. The actual pinned DLL retains libjpeg-turbo 3.2.0 with
build date 20260827, zlib 1.3.2 and OpenSSL version-provider strings including
3.6.3. The upstream source archives for these versions are retained with hashes.
This identifies notice versions; it does not reconstruct historical compiler
flags or every vcpkg patch.

The previously selected qpdf LICENSE.txt and NOTICE.md did not contain those
dependency notices. [Qpdf.Dependency.Notices.txt](../assets/third-party/pdf/Qpdf.Dependency.Notices.txt)
now preserves seven full source texts: zlib's license, libjpeg-turbo's license
and IJG README, and OpenSSL's license, README, authors and acknowledgments. Its
40,570 bytes have SHA-256
`0B2BE0C1A780C1E9B90D64019EC06BAFC8FEE3BECAD322F3FD0A3A93DB5133AD`.
Original text and whitespace remain unchanged within the collated sections.

Each of `pdf-engine`, `pdf-renderer` and `pdf-validator` receives the same
supplement beside its qpdf runtime. Existing qpdf and PDFium notices remain.
The authored product notice now acknowledges the FreeType Project and IJG
explicitly and points to the qpdf dependency supplement. The
[payload selection](../tools/pdf-engine/payload-candidate.json) pins all three
copies; only notice files and notice hashes changed. All **31** selected native
binary paths retain their previous identities.

The [supplier release metadata](https://github.com/qpdf/qpdf/releases/tag/v12.4.1)
matches the retained Windows and source archive hashes. The archived qpdf build
scripts use a rolling vcpkg cache whose release retains three recent assets.
The original August cache is no longer among them. The current cache was not
substituted for historical build evidence. The source/notice record explicitly
retains that limitation; no exact supplier rebuild is claimed.


PDFium supplier and notice correspondence
----------------------------------------

The [immutable supplier release](https://github.com/bblanchon/pdfium-binaries/releases/tag/chromium%2F8044)
identifies supplier recipe commit
`5453f3afc4785cbad82c05f6ceb4dabea0cb81a0` and PDFium branch `chromium/8044`.
Its observed branch head is `f91ca5a72358bb0b00b4da9481b21fe668157614`, dated
before the supplier release. Retained DEPS, build configuration and original
notice files use that exact commit and its dependency revisions. This is an
observed source correspondence, not an embedded binary attestation.

The supplier collects notices from third-party names in generated build.ninja.
Its 14 shipped component texts cover PDFium, Abseil, AGG, fast_float, FreeType,
ICU, Little CMS, libjpeg-turbo (two files), OpenJPEG, libpng, LLVM libc, simdutf
and zlib. **Six** match the supplier's documented source transformations
byte-for-byte; **eight** match after normalizing line endings for comparison.
Packaged files remain their original bytes. The review record's `matches`
field describes raw byte equality; `matchPolicy` records the accepted comparison.

The shipped GN arguments explicitly disable V8, XFA and PartitionAlloc. The
reviewed standalone defaults disable Skia, experimental Brotli, Fontations and
Rust BMP/JPEG/PNG decoders. The supplier checks out the minimal configuration
for this non-V8 build. These recipes explain the selected feature scope without
asserting an independent complete linker-map audit. No missing notice text was
found among the supplier's selected 14 files.


Retained review companion
-------------------------

The [source record](../tools/pdf-engine/notice-source-review.json) binds all
inputs, source members, transformations, shipped notices, version markers and
limitations. The local companion is
`.codex-temp/pdf-source-review/c1e3f1e2c3db44c184926c2ab60fd4d6/pdf-source-notice-review.zip`:
**81,974,139 bytes**, SHA-256
`225280C1E7CACD2E57A5C7B624A1472D53CD23CB4B2841DAA3FACAFE9E78AE41`.

It contains five source archives (qpdf, its three dependency versions and the
PDFium supplier recipes), a PDFium notice/configuration evidence archive, the
review record and qpdf supplement. It contains no authored private host source.
It is not the complete PDFium source/dependency tree, a reproduced build kit,
or a hosted source offer. Original qpdf and Microsoft runtime copies remain
bound by the existing payload selection; redistribution eligibility is separate.

Run `python -B tools/pdf-engine/Test-NoticeSources.py <bundle>` to verify the
exact companion membership, archive pins and all source notice text. Add
`--payload <isolated payload>` to check the three actual qpdf libraries and
notices plus all packaged PDFium notices. No archive is extracted or executed.


Packaging verification
----------------------

Fresh image/audio/PDF scratch composition at the same review directory's
`payload-03` passes staging, full production verification, notice-index checks,
and source/notice verification. The earlier `payload` attempt stopped before
PDF destination writes because the original prepared renderer folder held a
20,992-byte host rather than the selected 27,648-byte host. Its failed log remains.
The corrected `pdfium-prepared` input reuses the pinned host from a previously
verified package and the unchanged upstream archive. `inputs.json` records both
origins. No expected host identity was changed and no host was rebuilt. The
intermediate `payload-02` also passed; the final copy uses canonical LF endings
for Context Suite's authored acknowledgment so its pin survives Git checkouts.
Upstream notice text is not normalized.

`Test-PdfPayload.py` passes **25** checks at
`.codex-temp/pdf-payload-tests-c09df98f9ea54c94b051f481a7092295`.
It includes adjacent native import closure, individual missing dependency
notices, same-length notice corruption, original payload identity preservation,
and existing archive/path/extra-file and default-adoption refusals.
`verification.json` beside the review companion records positive source checks
and refusals for changed notices, source bytes, PDFium evidence and ZIP membership.

The [offline index](bundled-notices.md) now has ten base or **62** expanded links.
Its **70** synthetic checks pass at
`.codex-temp/notice-index/ca48077372ca4bb8a2f9b7aaf0d0574a`.
The retained stages and reserved version 1.1.1 remain untouched. No media engine
was executed, installed or registered. This checkpoint does not replace the
fresh complete candidate's A01-A08/performance replay, visual review or
[remaining release decisions](candidate-distribution-review.md).
