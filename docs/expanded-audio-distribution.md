Expanded Audio Source And Runtime Review
=======================================

Status: source/runtime archives, extracted-kit rebuild and isolated payload
verified. Updated: 2026-09-19. No formal version, installation,
Explorer registration or distribution approval is implied.

The [surround repair](wma-pro-surround-repair.md) selects the seven-file expanded
runtime. This checkpoint adds its corresponding-source and notice inventory to
the existing packaging workflow. It preserves the earlier curated profile and
reserved combined 1.1.1 payload. The separately authored Windows Voice provider
is not in these archives; provider source/build/deployment remains a distinct
requirement before the expanded combined candidate is complete.


Explicit profile and source relationship
---------------------------------------

`Prepare-AudioDistribution.py --expanded` requires the exact identities in
[expanded-candidate.json](../tools/audio-engine/expanded-candidate.json), all six
selected build switches and the five recorded patches. The default still selects
the original curated profile. Native review verifies every intermediate source
identity when patches share a file. Preparation verifies all original archive
members against the original source inventory and every effective source against
the reviewed patch result. It never edits historical builds or source archives.

The source kit includes eight original archives, twenty authored recipe/tool
files, five patches and build instructions. Run its `Rebuild-AudioSource.ps1
-ExpandedInputs` command to rebuild the selected profile. The patches repair
Pro overlap, Pro channel-matrix precision, Standard terminal reservoir/overlap,
Voice packet caching and timestamp-free raw PCM transport. FFmpeg Voice is still
not used by the application; the separate Windows provider remains necessary.

The notice inventory identifies 1,055 compiler inputs and collects 1,040 source
comment notices. For patched FFmpeg files, the collected notice is bound to the
original archive bytes; the kit also contains the exact modification patches.
The fifteen files without collected comment notices are the same wrapper/table
inputs listed in the [earlier review](audio-distribution.md). This is engineering
provenance, not a complete file-specific licensing or patent opinion.


Archive evidence
----------------

Bundle `audio-distribution-bddd3e073e354a5eb54cc8e93a81bb55` contains:

| Archive | Bytes | Entries | SHA-256 |
| --- | ---: | ---: | --- |
| Source review | 39,462,654 | 34 | `9F819C910D2CF502FF23882BFEC80AFCABEBDFFDAFFDB1E0173F3C8BF785C160` |
| Runtime review | 2,461,347 | 18 | `FE1514AF3AC7E9CB43E89E6D84944AF1C84F218F05EF82662F3C8AF5E3C3F63B` |

The seven native runtime files total 5,058,560 uncompressed bytes. Runtime notices
explicitly identify the modified FFmpeg build and unchanged IJG attribution.
Original licenses, patches, recipes and runtime identities remain together.
Private application source, supplier binaries and Windows codec DLLs are excluded.

Fifteen distribution checks pass in
`audio-distribution-tests-214a67d4e329480db63794e3d73ebfcf` after the final
strict profile/Boolean checks. The earlier fifteen-check run is retained at
`audio-distribution-tests-a54e51c267dc4029978e6553a5a6c449`. In addition to the
earlier runtime/source/notice/duplicate-entry refusals, these reject missing or
altered patches, a changed profile, disabled repair selection and a changed
effective-source inventory. Altered evidence/archive checksums are recomputed
where needed to test the selected identities rather than only outer hashes.


Isolated deployment
-------------------

[expanded-payload-candidate.json](../tools/audio-engine/expanded-payload-candidate.json)
pins the runtime archive and all eighteen deployed files. `Stage-AudioPayload.py`
selects between the two explicit profiles using the independently pinned probe
identity and then checks every binary/notice. There is no arbitrary runtime
fallback. Staging reads the verified bundle profile, extracts only named entries
and refuses an existing or partial destination. The default release allowlist
still excludes audio; isolated verification requires `-AllowAudioCandidate`.

The original 1.1.1 audio payload passes the updated verifier without modification.
The new expanded deployment checks run on disposable copies of its image/managed
foundation, not a new formal production version. These packaging checks do not
repeat media conversion or certify compatibility of that historical managed copy.

Fifteen payload checks pass in
`expanded-audio-payload-ea95f81defcc4909904556de06f108bc`: exact deployed bytes,
binary/notice/license mutations, mixed runtime profiles, missing probe, extra
library, development/existing/partial destination refusals and injected copy
failure. The real production allowlist accepts the explicit candidate and
rejects it without the candidate switch. Both the original combined inventory
and the disposable candidate inventory pass again after the checks.


Extracted-kit rebuild
---------------------

The verified source archive was extracted to `rb-expanded-32ac9943`, with no
previously compiled dependencies copied in. Its own expanded rebuild command
passes all sixteen dependency tests across the six groups, then builds FFmpeg
at `audio-ffmpeg-a7241fef54574eb4ad45a453bfe829b2` inside that kit. The native
review `build-review-5cdcd79ae24647b8a40aecf0372b4954` accepts eleven reviewed
diagnostics and checks all seven files' hardening/import metadata. The retained
command log is `rb-expanded-32ac9943-rebuild.log`. All 34 extracted kit inputs
remain byte-identical afterward.

The rebuilt identity passes all 121 frozen surround/regression cases in
`candidate-5d4d6240927f42ce929cafa56d27363f` under the existing surround corpus.
Its six-target independent smoke passes at
`smoke-f5eed71e9ba74d3a8037181900a43372`, including embedded PNG decoding.
These tests do not repin the application to the rebuilt identity or claim
byte-reproducible binaries; paths and PE build metadata can differ. The packaged
runtime remains the identity with the preceding full worker/direct-command
evidence. No managed behavior changed in this packaging checkpoint; the prior
4,384 foundation result was not rerun.


Remaining release work
----------------------

Package the Windows Voice provider with its source/build and notices, then compose the fresh
combined candidate and replay its actual workers. Source delivery, product/source
terms, modified-library use with runtime identity checks, Microsoft runtime
eligibility and intended-market review remain separate adoption decisions. Keep
human fidelity, visible accessibility, performance and installed lifecycle gates
explicit; archive integrity is not release clearance.
