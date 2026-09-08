Release Redistribution Checklist
================================

Reviewed: 2026-09-07. Engineering review, not a legal opinion or release approval.

The curated development payload materially reduces the dependency inventory.
The DDS development candidate additionally links pinned CPU DirectXTex (MIT)
through the private bridge. Its license and hash/provenance identity now travel
with the payload. Its direct DLL imports are Windows and Microsoft C++/UCRT
components; this is not evidence that every statically linked component has
completed release review. Review the DDS build map and archive its exact source
and notices alongside the image-engine evidence before distribution. See the
[DDS scope and evidence](dds-conversion-goal.md).
Do not use the stock NuGet native bundle as a release fallback. Runtime coder
restrictions are safety controls, not removal of compiled third-party code.

Completed engineering work
--------------------------

- Pinned native/ImageMagick sources and dependency artifact, recorded static
  linker/configuration evidence and actual output hash. Required libxml2 remains
  for XMP handling. No excluded HEVC/H.264, RAW, text-rendering or liquid-rescale
  dependency appears in the accepted native link map.
- Selected exact curated native/notice hashes; disabled NuGet native copying
  and reject stock, extra engines, unreviewed dependencies and missing notices.
- Retained curated ImageMagick, Apache, JPEG, PNG, WebP, Little CMS, XML and zlib
  notices, plus the separate pinned WebP patent grant. JPEG 12/16 are build
  configurations over the same pinned JPEG source.
- Added explicit product-level IJG acknowledgment in
  [THIRD-PARTY-NOTICES.txt](../assets/THIRD-PARTY-NOTICES.txt), rather than relying
  on an instruction embedded in upstream license prose.
- Copy the installed .NET toolchain's license and third-party notices alongside
  generated app hosts; this is still a framework-dependent application. Runtime
  installation/bundling must be reviewed with the eventual installer.
- Record every local production file's relative path, length and SHA256.
  Keep private sources and test-only hosts out of the production file allowlist.

Component obligations
---------------------

The retained inventory uses attribution/permissive-style licenses; no GPL/LGPL
library appears in the accepted map. That observation is not a blanket legal
clearance. Retain copyright/license/disclaimer text, mark custom build changes,
and avoid implying upstream endorsement. This native inventory does not by
itself require publishing the proprietary application or building an LGPL
relinking kit. Reassess whenever dependencies change.

[ImageMagick's license](https://imagemagick.org/license/) permits commercial
integration subject to its conditions.
[IJG's executable distribution condition](https://github.com/ImageMagick/jpeg-turbo/blob/b91492930ceb23c0b5282e8b9fc21de54182d92e/README.ijg)
requires acknowledgment in accompanying documentation; that acknowledgment is
now included. If distributing its source, retain the required README and
changed-source disclosures too. Archive the actual source notices and input
artifacts before any source/binary release; the recipe alone is not a source
archive. The WebP grant covers its stated patent claims, not arbitrary third-party
claims or every future codec.

Remaining release gates and owners
----------------------------------

1. **Owner: confirm Microsoft tooling license eligibility.** The
   [VS 2026 Build Tools terms](https://visualstudio.microsoft.com/license-terms/vs2026-ga-diagnostic-buildtools/)
   distinguish use with a licensed Visual Studio product from the narrower
   open-source-dependency exception. Installing Build Tools alone does not prove
   all uses are licensed. Confirm a valid eligible Visual Studio license for the
   authored native shell and any applicable runtime distribution. The
   [redistribution list](https://learn.microsoft.com/en-us/visualstudio/releases/2026/redistribution)
   is conditional on the relevant license and excludes preview/debug-only code.
   No conclusion about the user's license entitlement is inferred here.
2. **Owner: settle product/source terms.** Choose the public source license,
   proprietary EULA, warranty/support/refund terms and any needed third-party
   protections. Do not retroactively label the whole repository MIT or assume
   that public visibility grants a redistribution license.
3. **Release review: review the exact archived components and intended markets.**
   Confirm retained file-specific notices, source-change disclosures and any
   applicable patent questions. Escalate unresolved legal questions to qualified
   counsel; account/payment-provider approval is not codec clearance.
4. **Implementation: finish the customer package.** Signing, installer/runtime
   prerequisites, update policy, release-only file inventory, omission of
   development PDBs/test material and a notice viewer/link remain release work.
   Re-run final package media/UI checks and scan dependencies for advisories.
   Paid Polar activation is also still unimplemented.

The in-progress [packaging goal](release-packaging-goal.md) now creates an
unsigned internal candidate without PDBs/test material, records source and engine
identities, checks runtime prerequisites and builds unsigned identity packages.
The approved Inno 7.1.0 offline installer now compiles as an internal first-install
candidate, with pinned .NET/VC redistributables and mocked lifecycle contracts.
Its bundled license and upstream FAQ were reviewed; commercial-license purchase
is requested rather than strictly required. This does not establish Microsoft
runtime redistribution entitlement or close signing, upgrade/repair, hosted CI
or clean-machine release gates. See [decision 0012](decisions/0012-inno-offline-installer.md).

No payment secrets or signing keys are stored here. No license purchase,
installation, customer distribution, account change or publishing was performed
as part of this review. The remaining gates do not prevent local BMP/TGA work.
