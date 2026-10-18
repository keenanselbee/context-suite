Convert Icons And WEM Conversion Goal
====================================

Status: engineering complete on 2026-10-08; owner/native-menu/in-game acceptance
remains open. Icons, loose WEM decoding, installed-Wwise encoding and setup have
native/worker verification and the concrete review handoff below.
The installed application is unchanged. This is a selected
extension to the existing launch matrix, not completion of broader release review.


Scope and customer flow
-----------------------

Integrate the [reviewed format icons](convert-format-icons-plan.md), preserving
the Compact monochrome direction, root commands, target order and filename-based
filtering. Every existing output and Settings gets its own icon. WEM gets a new
hexagonal sound glyph when its conversion command is enabled.

For loose WEM input, Convert > WAV decodes locally without requiring Wwise.
For WAV or MP3 input, Convert > WEM remains visible even without Wwise. A focused
prompt detects a compatible installed Wwise Authoring tool or offers installation guidance
and Locate Wwise. Validate and remember the selected installation, then continue
the pending batch. Do not bundle Wwise or expose arbitrary console arguments.

Use a tested game profile or Match original WEM, with codec settings secondary.
Matching means supported observable properties, not recovering the original
encoder build or quality setting. Prioritize PCM and Vorbis for BG3 and Elden Ring;
mark game profiles unverified until evidence supports their exact variants.
Installed Authoring version and target-game compatibility are separate facts.

The supplied BG3 voice files use mono Vorbis at 44.1/48 kHz. The two inspected
Sovereign banks contain 169 mono PCM16 entries at 48 kHz and six mono Vorbis
entries at 44.1 kHz. These observations describe those mod files only. They do
not justify one universal Elden Ring encoding preset. Do not distribute mod
audio as repository fixtures.

BNK extraction/repacking and an independently authored encoder are outside this
goal. Retain batch operation, access admission, cancellation, source preservation,
validated temporary outputs and application-owned publication. Inspect loops,
sample precision and meaningful metadata rather than silently discarding them.


Completion requirements
-----------------------

- Unique light/dark assets for 14 existing targets, WEM and Settings; native
  small-frame validation, shell mapping/fallback tests and complete packaging.
- Pinned, independently sourced decoder with dependency/license/notice review;
  bounded execution and meaningful PCM/Vorbis decoding evidence.
- Installed-Wwise discovery, manual selection, persisted validated choice,
  missing-tool recovery and actual WAV-to-WEM execution/validation.
- Simple profile/reference choices with honest compatibility and preservation
  limits. Do not enable a target merely because its filename is recognized.
- Worker and application integration, mixed batches, malformed inputs, output
  validation, cancellation, collision-safe copies and source-change handling.
- Updated capability/design and dependency records, reproducible integration
  evidence and a concrete human/native-menu/game review handoff.

Engineering completion is distinct from owner acceptance and commercial release.
Do not infer human or in-game passes from automation. Installation, Explorer
registration, commits, pushes and publication remain separately authorized.


Current evidence
----------------

The native shell scratch build passes `Test-ShellPrototype.ps1`, including all
15 destination icon paths, Settings, separators, missing-target fallback, 210
availability checks and complete-selection activation. WAV/WAVE offers WEM;
WEM offers only WAV, including mixed-case and mixed-selection filtering. Wwise
is not queried or launched while Explorer enumerates commands. Windows decoding checks
pass all 304 frames (19 glyphs, two appearances, eight sizes). A decoded ICO
contact sheet was visually inspected at 16/20/24/32/48 pixels in light and dark.
Installed Explorer, contrast-theme and full DPI acceptance remain open.

Retained scratch evidence: `.codex-temp/convert-icons/native-sheet.png` and
`.codex-temp/wem-shell-encode`. No installed payload was changed.

Installed Wwise 2023.1.10.8659 is available for isolated validation. Its local
official command/schema files confirm `convert-external-source` requires a
project with a Windows platform and a conversion ShareSet. Discovery alone does
not prove conversion or game compatibility.

Isolated `create-new-project` and `convert-external-source` now pass eight
synthetic PCM16 source cases: PCM As Input and Vorbis Quality High at 44.1/48 kHz,
mono/stereo. Rates/channels and declared lengths agree; PCM media bytes match
the source samples. This is authoring evidence, not game acceptance. Projects,
fixture scripts and results remain under `.codex-temp/wem-wwise-probe`.

The [bounded WEM inventory](../src/ContextSuite.Core/Audio/WemHeader.cs) passes
37 authored contracts and read-only checks of those eight outputs, 12 BG3 samples
and all 175 embedded Sovereign entries. Five Sovereign entries contain forward
loops. A real-file failure exposed terminal odd-sized data without RIFF padding;
the parser now accepts only that bounded terminal exception, with regression
fixtures. Raw hashes are not interpreted as proof of encoder version. The
scratch harness is `.codex-temp/wem-header-real`; no game audio was copied into
distributable fixtures.

The full foundation run passes 4,611 contracts, including the 37 WEM header
contracts, 22 Wwise installation contracts and remembered-location settings
checks. Discovery finds and hashes the installed Wwise 2023 executable without
launching it. Settings preserve the selected path across ordinary preference
edits, reject relative paths and retain an unavailable installation's location
for later recovery. Its log is `.codex-temp/convert-icons/foundation-wwise-policy.log`.
After the same-path installation-refresh regression fix, the targeted setup and
planning suite passes 33 checks. The final worker run below includes the later
missing-decoder preflight and WEM-specific validation guidance.

The [decoder build recipe](../tools/wem-engine/README.md) now selects pinned
vgmstream r2117 with a Wwise-only initializer and Vorbis as its sole optional
codec dependency. The audit found restrictions in unrelated RELIC/CompressWave
sources; they are removed, and the linker map also rejects MIO objects. The
prospective corresponding-source archive excludes those sources rather than
redistributing the full upstream archive. Its standalone rebuild and the
resulting decoder pass the synthetic PCM/Vorbis corpus. The exact payload is
admitted by explicit optional-engine composition; it has not been installed or
included in a new full production/release candidate.

The current scratch candidate is
`.codex-temp/wem-engine-build/clean-binary-rebuild-01/install`. It rebuilds from
its cleaned corresponding-source ZIP alone, passes all eight looped Wwise
fixtures, and has 16 independently checked receipt entries. The archive contains
1,111 files; its only retained prebuilt binary is the required x64 Vorbis DLL.
The receipt records source SHA-256
`17C310068B491D10B35838CE7E1A69404AAD69D34A5465EA4CE109394FB00C7E`.
This supersedes the earlier scratch candidates containing unused upstream DLLs.

The reproducible [installed-Wwise check](../tools/wem-engine/Test-WwiseConversion.py)
passes eight plain and eight looped WAV-to-WEM cases. The curated decoder then
passes all eight looped round trips, preserving rate, channels, frame count and
forward-loop boundaries. PCM samples are byte-exact; Vorbis signal correlation
exceeds 0.9996 in these authored tones. This is not a listening or game-runtime
acceptance claim. Preserve decoded PCM16 as PCM16; the decoder's float scaling
is not an appropriate exact-PCM oracle. Vorbis uses float32 WAV with one
playthrough and explicit loop export.

Loose WEM decoding is now connected through the existing audio batch/admission,
worker and publication route. The private adapter pins/leases the decoder,
uses a read-only source snapshot and a job-owned child with bounded stdout,
and checks WAV framing, sample extent, rate/channels, precision, loop boundaries,
finite float samples and exact PCM samples before filling a reserved output.
Initial admission is little-endian PCM16/Vorbis, mono/stereo, 44.1/48 kHz.
Unknown metadata and unsupported variants are refused. The wider public planning
bounds do not override this worker admission.

The [reproducible workflow check](../tools/wem-engine/Test-WemWorkflow.ps1) passes
53 actual worker/application checks with eight looped authored fixtures, without
FFmpeg or Wwise in the payload. It covers one admission per batch, collision-safe
copies, source changes, malformed mixed selections, occupied reservations,
cancellation between items and worker recovery. The private adapter harness
also passes 16 looped/non-looped fixtures and engine-integrity/missing-engine
checks. The final combined run also passes 28 installed-Wwise checks; staged
evidence is `.codex-temp/wem-workflow-1bcde02760f9402fa6620b2f28364f89`.

The scratch-only BG3 acceptance harness scans all 5,982 loose WEMs (337 mono
Vorbis at 44.1 kHz and 5,645 at 48 kHz). A short, median and long example from
each group decodes through the actual adapter with validated frames and output
hashes. The longest has 2,636,802 frames. The harness observes the native decoder
running, cancels it, verifies an empty reservation, no surviving decoder child
or job directory, and then successfully decodes again. Evidence is retained in
`.codex-temp/wem-bg3-acceptance`; game audio is not in distributable fixtures.
This is six representative decodes, not decoding every scanned file or a
listening/in-game acceptance pass. Sovereign coverage is embedded-header
inspection plus independently authored PCM/Vorbis fixtures; product BNK editing
remains excluded.

Decoder staging now verifies all 17 runtime/provenance files against a pinned
receipt. Missing/tampered notices are refused before staging. Production/release
composition accepts an explicit WEM distribution, records its optional-engine
selection and verifies exact membership. The notice index links five notices,
build instructions and corresponding source; its seven WEM links and missing
LGPL-notice refusal pass an isolated fixture check. No new version was reserved
and no full production/release candidate was built during this checkpoint.

WAV-to-WEM now uses the inspected user-installed WwiseConsole, with fresh bounded
scratch projects and explicit factory PCM As Input or Vorbis Quality High
ShareSets. Initial WAV admission is PCM16, little-endian RIFF, mono/stereo at
44.1/48 kHz, up to 64 MiB, with an optional canonical forward loop. Unknown
metadata is refused. The adapter holds source/tool/reference leases, reinspects
their identities, owns child jobs, bounds deadlines and scratch files, and
validates output properties and loops. PCM samples must match exactly; decoded
Vorbis must pass per-channel RMS error limits (0.15 full scale, and half of
non-silent source RMS). Silent/inverted-output negative fixtures are rejected.
The private harness passes 16 looped/non-looped real Wwise conversions plus
changed source/reference/tool, malformed input, active cancellation and recovery.

The focused setup window offers detection, Locate WwiseConsole, Get Wwise,
Match original WEM and secondary explicit codec choices. It persists the
validated path, reinspects before Continue, replaces stale same-path identities,
and keeps the pending batch intact. Setup cancellation starts no worker or
access admission. The 28 actual worker checks include both codecs on four
rate/layout cases, reference matching, changed reference/tool refusal, one
admission per batch, deferred setup, original-batch continuation and source copies.
An unavailable decoder is detected before asking the user to configure Wwise.

The final application Debug build succeeds with zero warnings/errors. Offscreen
renders of the actual WPF setup content fit at 680 DIP, the reference state at
520 DIP, and expanded advanced content at 150 percent bitmap DPI. Initial dark
text before HWND creation was a rendering artifact: creating a hidden HWND
without showing a window initializes white inherited text, matching the existing
audio dialog. Detached control rendering does not establish live theme contrast
or keyboard accessibility. Screenshots are in `.codex-temp/wem-ui-render/renders`.
The repository check passes all 220 documentation files, public-source boundaries
and system-theme policy; both repositories pass tracked whitespace checks.


MP3 and float32 follow-up
------------------------

The owner-approved follow-up on 2026-10-08 adds MP3 to the same WEM menu command
and admits float32 WAV, including the current Vorbis-WEM decoder's output.
The private adapter lazily loads the existing pinned audio runtime only for MP3.
It verifies MP3 framing and metadata, decodes through the existing validated audio
adapter, and prepares a bounded PCM16 WAV in owned scratch. Float input takes the
same preparation path without FFmpeg. PCM16 originals retain the prior exact path.

One batch quality prompt covers PCM16 precision reduction and, for MP3-to-Vorbis,
another lossy encode. No conversion/admission starts before consent. Preparation
retains frame count, sample rate, channels and supported forward loops, revalidates
the written WAV and removes it on completion or failure. In-range samples use
round-to-even PCM16 quantization with representable endpoints; nonfinite and
out-of-range samples are refused rather than silently clipped. Wwise output still
passes exact PCM or decoded Vorbis sample verification before publication.

Input remains mono/stereo at 44.1/48 kHz and at most 64 MiB; decoded float WAV and
prepared PCM16 also have bounded size. Float WAV supports IEEE fmt16, fmt18 with
zero extension size, and fmt40 extensible IEEE-float with validBits32 and the
ordinary mono/stereo mask. An optional four-byte fact count must equal the sample
extent. One canonical forward smpl loop is supported. User WAV extra chunks and
MP3 descriptive tags/artwork remain refused because WEM cannot preserve them.
The internal decoder's verified technical encoder marker has a narrow allowance;
it does not authorize dropping arbitrary user metadata.

The final application build has zero warnings/errors; the full foundation suite
passes 4,676 contracts, targeted WEM setup/planning passes 97, and the native shell
passes 240 availability checks. The canonical isolated pipeline passes 53 WEM
decode checks, 42 Wwise checks without FFmpeg and 56 after the verified audio
payload is staged. It exercises exact float/MP3 frame counts and loops, both WEM
codecs, source hashes, mixed batches, one quality prompt, cancelled consent,
changed sources, invalid floats, tool/reference changes and reservation cleanup.

Evidence: `.codex-temp/wem-mp3-foundation.log`,
`.codex-temp/wem-mp3-canonical-workflow.log` and
`.codex-temp/wem-workflow-09d2133da7f841f7a354114edc572aa0`.
The private scratch harness additionally passes fmt18/fmt40, NaN/infinity/peak,
missing-engine/consent, and quantization endpoint/fraction checks. No new decoder
dependency or Wwise distribution is introduced. Installed/menu/game acceptance
remains subject to the handoff below; no installation or publication was changed.


Dependency review
-----------------

The pinned receipt inventories 188 linked object names, including Microsoft CRT
objects. The linked-source notice scan agrees with the supplied Getopt, miniz,
libacm, NWA, Sun G.721 and UTK notices; upstream COPYING covers the remaining
vgmstream source. RELIC, CompressWave and MIO are absent from the link map and
sanitized source. Only the required x64 Vorbis DLL remains as a prebuilt binary
inside that source archive. Runtime imports are Windows/CRT plus that DLL.

GNU Getopt is statically linked into the separate CLI, not the application.
The bundle supplies its LGPL text, full CLI/Getopt source and build recipe; a
standalone rebuild from that source archive has passed. This is the technical
source/relink evidence. Final commercial distribution terms and source delivery
remain part of release review, not a claim of legal clearance. Future decoder
changes must repeat the object/notice review and update the independently
verified receipt, runtime pins and staging pins together.


Review handoff and known limits
------------------------------

| Review | Concrete procedure and expected result | State |
| --- | --- | --- |
| Installed menu | After separately authorized candidate build/install, inspect Convert on image, audio, PDF, WAV and WEM selections. All 15 outputs and Settings have distinct silhouettes; Word is a clear W; existing order is retained and WEM is appended. Missing destination assets fall back to the Convert root. | Native automation passed; installed visual acceptance open |
| Theme and scaling | Review native menus and setup at 100/125/150/200 percent, light/dark and Windows contrast themes. Check 16/20/24 px silhouettes, focus, labels, scrolling and keyboard navigation. | ICO frames and light offscreen layout checked; live desktop acceptance open |
| Missing Wwise | With no selected installation, invoke WAV > WEM. Get Wwise opens the official page only on click; Locate accepts the actual Authoring/x64/Release/bin/WwiseConsole.exe. Continue remains unavailable without a valid tool and reference or codec. Cancelling leaves originals untouched. | Model and batch checks passed; picker/browser interaction open |
| Resume and recovery | Choose the installed Wwise 2023.1.10.8659 and a matching original WEM, Continue a two-file batch, repeat with an updated/missing tool and a changed reference. Valid setup resumes once; stale facts refuse publication and locating the updated tool recovers. | Automated state and actual-worker checks passed |
| BG3 | Use disposable copies of a known working mono Vorbis asset at each rate. Decode/listen, edit the float32 WAV, Match original, confirm the PCM16 preparation, encode, then test via the existing mod workflow. Compare duration, voice, playback and transitions. | Six representative decodes passed; listening/game replacement open |
| Elden Ring | Using an external established bank workflow, test PCM16 mono 48 kHz and Vorbis mono 44.1 kHz replacements against the two observed Sovereign banks; verify five looped assets at transitions. Preserve original banks. | Headers and authored loop fixtures passed; game/bank reinsertion open |
| Distribution | Reserve a fresh version/stage, pass the reviewed WEM distribution explicitly, verify payload/notices/source index and complete release acceptance before installation/publication. Keep the existing 1.2.3 payload intact. | Isolated composition/receipt/index checks passed; full release candidate outside this goal |

The subsequent owner-approved input extension accepts float32 WAV and MP3 by
preparing PCM16 internally after explicit precision consent. MP3-to-Vorbis also
requires lossy-transcoding consent, combined into one batch prompt. The original
PCM16 path remains exact and does not require that precision prompt. Other rates,
channel layouts, codecs, big-endian WEM,
unknown metadata and unsupported loop variants are refused, not silently changed.
Match original does not reproduce original encoder version, Vorbis quality,
codebooks, bank identity or game integration. No BG3/Elden Ring game preset is
advertised as verified. The installed Wwise version is authoring evidence only.

No installation, Explorer registration, commits, pushes or publication were
performed. Existing installed/reserved payloads and unrelated working edits
remain intact.
