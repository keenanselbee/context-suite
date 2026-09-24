Candidate Source And Notice Review
==================================

Status: consolidated internal review packet prepared on 2026-09-23; source delivery,
component adoption and commercial redistribution remain incomplete.
This advances the [owner-review goal](broad-file-support-goal.md), without
reserving a product version or changing any installed or retained payload.

The local index is
`.codex-temp/distribution-review/202fb33d40bd4318a7d58ba25775d369/index.html`.
It collects nine selected source/evidence archives, all 63 files linked by the
current expanded notice index, the index itself, ownership records and current
[local-library instructions/helper](local-library-replacement.md). There are
89 copied files and 76 verified local index links. The HTML loads no scripts or
remote assets. The separately downloadable PowerShell helper is explicit opt-in;
opening the review does not execute it. No private application source is included.

The source kits retain their documented build inputs, including HEIF's selected
AOM static artifact. The [PNG](png-dependency-notices.md),
[DDS/palette](dds-palette-source-review.md), [Office Python](office-python-notices.md),
[Office component](office-component-notices.md) and [PDF](pdf-dependency-notices.md)
supplements are now consolidated. This is an unversioned engineering review
collection, not a published source offer. The earlier four-archive packet at
`691b5b7fd58f4017a6e152fae1f18962` remains retained as historical evidence.


Selected companion archives
----------------------------

| Engine | Archive | Bytes | Binding |
| --- | --- | ---: | --- |
| Office | `office-source-kit-04.zip` | 1,148,584,167 | [Selected source/runtime record](../tools/office-engine/source-runtime-candidate.json), exact archive/manifest, 169 members and 140 dependency archives |
| Expanded audio | `audio-source-review.zip` | 39,462,654 | [Expanded bundle](expanded-audio-distribution.md), current source/runtime/notice verifier |
| HEIC/AVIF | `heif-source-review.zip` | 21,667,656 | [Color-corrected bundle](heif-color-fallback.md#source-delivery-and-retained-failures), current source/runtime/notice verifier |
| Windows Voice | `voice-source-review.zip` | 8,876 | [Provider bundle](windows-voice-distribution.md), current source/runtime/recipe verifier |
| PNG | `oxipng-source-notices.zip` | 24,679,341 | [Source and 50 locked crates](png-dependency-notices.md), source/notice verifier |
| DDS/palette | `dds-palette-source-notices.zip` | 1,038,151 | [Component source records](dds-palette-source-review.md), source/header/notice verifier |
| PDF | `pdf-source-notice-review.zip` | 81,974,139 | [Qpdf sources and PDFium supplier evidence](pdf-dependency-notices.md); not a complete PDFium source tree |
| Office Python | `office-python-source-notices.zip` | 22,809,931 | [Python/pip source and notices](office-python-notices.md), including a duplicate Office-kit dependency |
| Office components | `office-component-notice-evidence.zip` | 3,548,570 | [367 source notice blocks](office-component-notices.md); dependency archives remain in the Office kit |

The archives total **1,343,773,485 bytes**, including duplicated dependency
content; this is not a unique-source measurement. The proposed delivery is a separate
source companion alongside the eventual application download, with an exact
version/checksum mapping. No hosting, upload or public source URL is configured.
The archive total is not an application-size measurement. The later
[archive rehearsal](expanded-release-composition.md) measured approximately
612 MB unpacked / 241 MB compressed. The fresh version still needs its own
measurement before owner footprint acceptance.

The packet preserves every notice linked by the current expanded application
index in the same relative directory layout. Full production verification binds
that index and the engine/base notices before collection. The engine and five
supplement verifiers all pass; Office component verification compares 321 native
files, and Python verification compares 675 runtime files. The Office source ZIP
and internal manifest match the selected hashes, exact membership and sizes.
No private source, new runtime build, engine adoption or release approval is
inferred from those matches.

`review-manifest.json` records the copied files and bundle identities;
`inputs.json` binds original paths and hashes. Previously completed source-tree
comparison and native rebuild evidence are reused. All originals and copies
remain unchanged after verification. `verification.json` records collection and
link checks. No new native build, version reservation or installation ran.

`README.txt` gives self-contained source-kit and library-replacement instructions;
`tools/New-LocalLibraryManifest.ps1` matches the committed helper. The retained
engineering Markdown files explicitly keep their repository-relative links;
they are supplementary records, not the packet's standalone instructions.

An initial scratch attempt is retained at
`3f35cfd28e444454a3a7bd27ab278559`. It stopped on a mistyped, 66-character HEIF
hash in the one-off collection script. The original archive matched the owning
record and passed the existing verifier; the corrected attempt above succeeds.
No archive, engine pin or verification condition was relaxed.


Decisions and unfinished work
-----------------------------

| Item | Concrete next action | Owner |
| --- | --- | --- |
| Tool/runtime eligibility | Confirm the project's ownership/use and applicable Microsoft Community/runtime terms; the installed edition alone is not entitlement evidence | Owner |
| LGPL modified-library use | [HEIF/audio local replacement route implemented and tested](local-library-replacement.md); instructions/generator now collected, fresh candidate inclusion and terms/distribution review remain | Engineering and owner |
| Source delivery and product terms | Approve actual source-download location, public-source terms and proprietary product terms; bind delivered archives to each released binary version | Owner, then packaging |
| File-specific notices | PNG, DDS/palette, Office Python/components and PDF supplements consolidated and verified; complete exact-candidate release checks | Engineering/release review |
| Customer notice access | [License entry and offline index implemented](bundled-notices.md); compose into the fresh candidate, review actual opening and finish source instructions | Engineering |
| Adoption and intended markets | Resolve component/codec patent questions and explicitly select the release engines | Owner/release review |
| Final composition | Build a fresh version, measure the actual footprint and replay A01-A08/performance against its exact bytes | Engineering |

Source availability is distinct from archive integrity. For example, MPL 2.0
section 3.2 requires covered source availability and instructions for recipients;
section 3.4 protects license notices. A local ZIP alone does not complete the
recipient-facing delivery step. See the [Mozilla license text](https://www.mozilla.org/en-US/MPL/2.0/).

The bundled HEIF LGPLv3 section 4 and audio LGPLv2.1 section 6 describe
modified-library/relinking conditions. Their exact texts are in the review
packet and its original bundles. Source kits and successful rebuilds alone do
not establish a usable application path for modified libraries. The
[explicit HEIF/audio route](local-library-replacement.md) now passes modified and
rebuilt library checks through the actual worker while retaining default pins.
Final candidate inclusion, recipient instructions and terms remain open; this
technical result does not close distribution review.

The existing [redistribution checklist](release-redistribution.md) still governs
release decisions. Network enforcement, PowerPoint tolerance, real media,
human acceptance and the exact candidate replay remain separate under the
[finite acceptance contract](local-candidate-acceptance.md).
