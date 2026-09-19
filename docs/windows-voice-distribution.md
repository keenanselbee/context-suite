Windows Voice Source And Runtime Review
======================================

Status: source-kit rebuild and isolated deployment verified, 2026-09-19.
Final combined candidate, broader fidelity and release adoption remain open.

The [expanded audio runtime](expanded-audio-distribution.md) uses a separate
owned host for WMA Voice. This checkpoint packages that host with its notice and
a rebuildable source kit. It keeps the previously tested executable identity;
it does not adopt a new decoder or change the private runtime pin.


Selected files and source kit
-----------------------------

[voice-provider-candidate.json](../tools/audio-engine/voice-provider-candidate.json)
pins the 178,176-byte `voice/WmaVoiceMftProbe.exe` to SHA-256
`B03029719B338363193C6111A204B144A23EF52947301D6CA5493267C3D0DBA5`.
The second runtime file, `voice/NOTICE.txt`, is 942 bytes. No Microsoft codec
DLLs or private application source enter either archive.

Bundle `voice-distribution-ddfd5050914b4600bebcdd45faa571f8` contains:

| Archive | Bytes | Entries | SHA-256 |
| --- | ---: | ---: | --- |
| Source review | 8,876 | 4 | `6A88DC24BF27BB3B4E5FB42340188A226048988BA2B908CD9B99D3E6250DA414` |
| Runtime review | 93,415 | 2 | `EF7A7F15C1C1EB050C2D8E6FBEA7A6756955D156CF950844D1426944F34F671A` |

The source archive includes the owned C++ host, standalone Python build recipe,
instructions and notice. Source SHA-256 is
`785D6CEA510316D86ADDC6C5CB20BD920F4586D43C03D21F477683A218C78447`.
The build uses the existing Windows x64 C++ toolchain, treats warnings as errors,
and checks PE hardening and the four expected system imports. It creates only a
fresh local scratch directory; no dependency download or installation is needed.
The host statically links the Microsoft C/C++ runtime with `/MT`. Applicable
tool/runtime eligibility and source/product terms remain release decisions.

`WindowsVoiceDistribution.py --prepare '<selected helper directory>'` retains
seven local build/test evidence files alongside the two archives. Verification
checks source and executable identity, all 48 retained native results, and exact
archive membership and contents against reviewed repository inputs. Evidence
records contain local machine paths and are not included in either archive.


Rebuild and deployment evidence
------------------------------

The standalone recipe first rebuilt at
`windows-voice-build-feb5688b630b4fdda70755377fcfbf0b`; its replay at
`wma-voice-mft-04617e1848d64ef68d6d308610f0bba9` passed 48 checks.
The source ZIP was then extracted to `voice-source-kit-c9d89dcb0c364893acd8b2c869231a78`.
Its own recipe built `windows-voice-build-401d390c3da74462ab3485900e12b0bd` inside
that kit, passing native hardening/import review. All four extracted inputs
remained byte-identical. Replay `wma-voice-mft-0ce04fefc477409e964bd32fd65c1374`
passed 36 exact Windows-reference comparisons, eight malformed-input refusals
and four ownership/input guards. Rebuilding does not promise binary identity or
automatically change the application's selected host.

`Stage-AudioPayload.py --distribution '<expanded bundle>' --voice-distribution
'<Voice bundle>' --payload '<fresh scratch stage>'` validates both bundles before
copying. It deploys twenty pinned files: eighteen expanded audio files plus the
Voice host and notice. Voice requires the expanded profile. Existing destinations,
partial copies, unexpected files and altered or missing identities are refused.
Omitting the complete optional Voice directory still permits the eighteen-file
audio profile; application tests separately verify missing-provider refusal.

`Build-Production.ps1 -VoiceDistributionDirectory` forwards the same explicit
selection and requires an audio distribution and a fresh `StagingId`. No formal
version was reserved in this checkpoint. The real production allowlist continues
to reject audio without `-AllowAudioCandidate` and verifies the Voice directory
before admitting its two names.

Twenty-eight archive/deployment checks pass at
`voice-distribution-tests-37f82be797ce419eab6ef9c897b7e912`. They include altered
host/source/recipe/notice, missing host, extra synthetic private/system entries,
duplicate ZIP membership, changed evidence, failed native acceptance and changed
identity. Recomputed outer checksums cannot bypass trusted content checks.
Payload checks cover missing/altered files, mixed curated/Voice composition,
injected copy failure, refusal of partial retries, invalid input before copying,
actual production allowlist behavior and build prerequisites before version
reservation. The original curated audio and copied baseline inventories remain
unchanged. The earlier 25-check run is retained at
`voice-distribution-tests-4102e20eb1b846fdbc36b3e228616fa9`.

Actual Convert-command replay at
`expanded-audio-direct-7d510728d7bf4e179247d4039ad3575f` builds the current managed
worker/test host and stages both reviewed bundles into its disposable payload.
All 211 checks pass, including seven input variants, six targets, necessary
quality prompts, access refresh, mixed selection, retry and absent Voice provider.
Both the composed worker and its separately composed no-Voice counterpart retain
their recorded file identities after execution. Managed builds report zero
warnings/errors. This repeats the direct-command corpus against packaged native
files; it does not repeat the full 1,008-check worker corpus or human listening.
Repository boundary/theme/link checks pass for 186 documentation files. The prior
4,384 foundation result was not rerun for this packaging-only change.


Remaining acceptance
--------------------

This completes the isolated provider source/deployment path, not the expanded
commercial release. The fresh combined versioned candidate, representative
listening, real missing-component Windows behavior, broader fidelity and resource
budgets remain required. Source delivery, Microsoft runtime eligibility and
product/source terms remain separate adoption decisions. Windows-reference
agreement uses the same system codec; it is not independent codec fidelity proof.
