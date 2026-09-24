Candidate Source And Notice Review
==================================

Status: internal review packet prepared on 2026-09-23; source delivery,
component adoption and commercial redistribution remain incomplete.
This advances the [owner-review goal](broad-file-support-goal.md), without
reserving a product version or changing any installed or retained payload.

The local index is
`.codex-temp/distribution-review/691b5b7fd58f4017a6e152fae1f18962/index.html`.
It collects 54 unchanged notice files and four selected source archives, with
independent input/copy hashes and 59 verified local links. It has no scripts,
remote assets or private application source. The source kits keep their documented
build inputs; in particular, the HEIF kit retains its selected AOM static artifact.
This is an engineering review collection, not a complete customer source offer.

Later [PNG](png-dependency-notices.md), [DDS/palette](dds-palette-source-review.md),
[Office Python](office-python-notices.md), [Office component](office-component-notices.md)
and [PDF](pdf-dependency-notices.md) checkpoints retain additional source/notice
evidence. The original packet above has not yet been recomposed with those
supplements; its counts remain historical.


Selected companion archives
----------------------------

| Engine | Archive | Bytes | Binding |
| --- | --- | ---: | --- |
| Office | `office-source-kit-04.zip` | 1,148,584,167 | [Selected source/runtime record](../tools/office-engine/source-runtime-candidate.json), exact archive/manifest, 169 members and 140 dependency archives |
| Expanded audio | `audio-source-review.zip` | 39,462,654 | [Expanded bundle](expanded-audio-distribution.md), current source/runtime/notice verifier |
| HEIC/AVIF | `heif-source-review.zip` | 21,667,656 | [Color-corrected bundle](heif-color-fallback.md#source-delivery-and-retained-failures), current source/runtime/notice verifier |
| Windows Voice | `voice-source-review.zip` | 8,876 | [Provider bundle](windows-voice-distribution.md), current source/runtime/recipe verifier |

The archives total **1,209,723,353 bytes**. The proposed delivery is a separate
source companion alongside the eventual application download, with an exact
version/checksum mapping. No hosting, upload or public source URL is configured.
The archive total is not an application-size measurement. Measure the fresh
combined application before asking the owner to accept its footprint.

The packet includes selected audio/Voice/HEIF/PDF license, notice and patch texts,
the current Office runtime's LICENSE/NOTICE/credits/readme, and the retained
image/DDS/PNG/palette/.NET notices. Audio/HEIF/Voice/PDF files are checked against
their committed selection records. Office notices match its pinned runtime
inventory; base notices match the retained combined payload inventory.
No private source, new runtime build, engine adoption or release approval is
inferred from those matches.

`review-manifest.json` records the copied files and bundle identities;
`inputs.json` binds the original paths and hashes. The three existing bundle
verifiers pass. Office's current archive and manifest match the selected record,
with exact ZIP membership/sizes; its previously completed source-tree comparison
is reused, not described as another rebuild. All originals and copies remain
unchanged after verification. `index-check.json` records link containment and
index/manifest identities.

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
| LGPL modified-library use | Select and implement a supported replacement or relinking route; the current fixed identity checks reject changed libraries | Engineering and owner |
| Source delivery and product terms | Approve actual source-download location, public-source terms and proprietary product terms; bind delivered archives to each released binary version | Owner, then packaging |
| File-specific notices | [PNG dependency source/notices](png-dependency-notices.md), [DDS/palette mapping](dds-palette-source-review.md), [Office Python notices](office-python-notices.md), [Office component coverage](office-component-notices.md) and [PDF dependency review](pdf-dependency-notices.md) prepared; consolidate the review packet and complete release checks | Engineering/release review |
| Customer notice access | [License entry and offline index implemented](bundled-notices.md); compose into the fresh candidate, review actual opening and finish source instructions | Engineering |
| Adoption and intended markets | Resolve component/codec patent questions and explicitly select the release engines | Owner/release review |
| Final composition | Build a fresh version, measure the actual footprint and replay A01-A08/performance against its exact bytes | Engineering |

Source availability is distinct from archive integrity. For example, MPL 2.0
section 3.2 requires covered source availability and instructions for recipients;
section 3.4 protects license notices. A local ZIP alone does not complete the
recipient-facing delivery step. See the [Mozilla license text](https://www.mozilla.org/en-US/MPL/2.0/).

The bundled HEIF LGPLv3 section 4 and audio LGPLv2.1 section 6 describe
modified-library/relinking conditions. Their exact texts are in the review
packet and its original bundles. The current source kits and successful rebuilds
do not, by themselves, establish a usable application path for modified libraries.
Do not weaken identity verification silently or claim this gate closed.

The existing [redistribution checklist](release-redistribution.md) still governs
release decisions. Network enforcement, PowerPoint tolerance, real media,
human acceptance and the exact candidate replay remain separate under the
[finite acceptance contract](local-candidate-acceptance.md).
