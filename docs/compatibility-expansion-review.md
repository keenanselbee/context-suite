Compatibility Expansion Review
==============================

Historical evidence relocation (2026-10-13): paths marked archived below now
reside under `Z:\Backup\Context Suite\workspace-history\20261013\payload`,
using the same repository-relative paths. Follow the
[workspace recovery instructions](repository-size-reduction-goal.md#uncompressed-workspace-relocation-2026-10-13)
before rerunning historical checks.

Status: unsigned **1.2.6**, engineering verification and review handoff complete,
2026-10-10. Exact-candidate checks and fresh extraction passed. Installed 1.2.5
is unchanged; native UI, accessibility and representative-media acceptance remain
separate.

Open the [local candidate review](../.codex-temp/compatibility-review/index.html).
It contains eight renders of the actual WPF track-picker and SVG-to-JPEG window
sources, unchanged since packaging, in light/dark and minimum-width layouts. Display facts are synthetic;
the renders show content rather than OS title bars. The harness changes only its
own application theme, not Windows settings. Native interaction, keyboard,
screen-reader, contrast-theme and owner acceptance remain separate.

The review also includes copied, validated JFIF/ICO-to-PNG, SVG-to-JPEG,
PDF-to-JPEG, four GIF frame PNGs and selected-track MKV-to-WAV outputs from the
exact final candidate. The
[manifest](../.codex-temp/compatibility-review/review-manifest.json) records
source paths, sizes and SHA-256 identities. These are authored diagnostic
fixtures; they do not establish representative customer-media fidelity.

Review the [converter design](converter-design.md#compatibility-expansion-approved-2026-10-09)
for exact behavior, limits and exclusions. Existing destination labels, icons
and command order are retained. The previous [icon review](image-expansion-review.md)
remains relevant to the unchanged glyphs; filename availability is tested again
for the added inputs. MKV was explicitly approved on 2026-10-09; M4V is excluded.

Exact candidate
---------------

- Staging: `c17478c8bb244aeeabf6402e08482458`.
- Inventory SHA-256:
  `E674E834836325BDE484FE90FEE55B9CF8E23F6D063B67AC276E89B6D466BC85`.
- Unsigned archive (`artifacts/release-candidates/450d26a6e6134120971e562a2099a7c1/ContextSuite-win-x64-unsigned.zip`, archived).
- Archive SHA-256:
  `4ED646678DFA726554AE492A81E7B3AE888AE7450B48B83E0590EC55AAAACF49`.
- The release manifest (`artifacts/release-candidates/450d26a6e6134120971e562a2099a7c1/ContextSuite/release-manifest.json`, archived)
  records both repository revisions, dirty-source status, tools and engine
  identities. Final documentation was updated after packaging without changing
  reserved payloads or source binaries.

Candidate construction verified fresh archive extraction. Hash comparison after
conversion tests verified all 4,257 staged files and all 4,257 unchanged installed
1.2.5 files; see [preserved inventories](../.codex-temp/compat126-preserved-identities.json).

Engineering evidence
--------------------

The main replay (`.codex-temp/compat126-final-checks/results.json`, archived) passes all
40 suites with the complete candidate inventory unchanged before/after. Its
inputs (`.codex-temp/compat126-final-checks/inputs.json`, archived) record commands,
host identities and explicit binding to staged Core/Private assemblies.
The staged worker and application are used directly.

| Check group | Passing final-candidate evidence |
| --- | --- |
| Foundation | 5,122 contracts including image/DDS/PNG, batch/publication, interruption/recovery and actual WPF startup |
| New image inputs | JFIF 28; ICO input 43; mixed JFIF/ICO workflow 5 |
| GIF | Parser/compositor 31; adapter 38; actual-worker workflow 31, including canvases, duplicates, cancellation, worker termination, selective retry and source/folder identity |
| SVG | PNG/JPEG adapter 115; workflow 18; interruption 16; native renderer 50; two maximum-canvas cases |
| PDF JPEG | Page workflow 33; direct commands including partial JPEG retry 21; private raster 41 |
| Video audio | Models/picker/IPC 154; adapter 71; actual-worker workflow 30 across MP4/MOV/WebM/MKV |
| Existing image expansion | ICO adapter 119, workflow 65, interruption 13; AVIF adapter 108, workflow 33, interruption 14; TIFF adapter 653, workflow 19, interruption 13 |
| Existing worker features | WEM decode 53; Wwise encode including MP3/float WAV 56; audio conversion 20, interruption 96, FLAC 14; PDF analysis 11, optimization 17, page failure 20, images-to-PDF 20, PDF-to-Word 14; Office 15; HEIF 137 |
| Audio adapters | Combined probe/encoding 370; AIFF 365 with 68 outputs; Standard WMA 283 with 36 outputs; Windows Voice 339 with 222 outputs |
| Shell and assets | 663 filename-only availability assertions, isolated COM/activation contracts and 336 Windows-decoded icon frames |
| Packaging | 23 release-package guards; 27 audio payload guards; 16 audio-distribution guards; all engine/notice allowlists and 281 local notice links |
| Repository | Public/private build boundary, system-theme/source boundary, 222 documentation files and both whitespace checks |

Focused evidence: extra results (`.codex-temp/compat126-extra/results.json`, archived),
[PDF page workflow](../.codex-temp/compat126-pdf-page-worker.log),
[Voice](../.codex-temp/compat126-voice.log) and
[packaging](../.codex-temp/compat126-packaging-final.log). Counts overlap where
focused modes repeat foundation assertions; they are not an additive total.
PDF reference pixels are reused independent evidence with source hashes rechecked.
Prompt renders reuse the unchanged window sources; final outputs were recopied
and hashed in the 24-artifact review manifest.

Video checks include independent decoded samples, ten-minute Vorbis/WebM and
PCM16/MKV, surround preservation/downmix, chapter/artwork omission consent,
malformed input, missing engines, resource limits and immutable selective retry.
MKV remains bounded to the documented codecs, 512 MiB input, 65,536 selected
packets/frames and 1 GiB decoded reference audio. AC-3, E-AC-3, DTS and TrueHD
remain unsupported; this is not unrestricted full-length movie support.

The combined audio engine retains all five earlier repairs, seven pinned runtime
files, 13 reviewed diagnostics and PE hardening checks. Its source kit rebuilt
six dependencies and FFmpeg from eight retained upstream archives. These are
reused build/evaluation artifacts whose runtime identities match the final stage;
distribution guards were rerun on the final bundle. Native expanded-input checks
also regenerated 27 fixtures with exact authored PCM/float samples.

The native WMA diagnostic deliberately exercises direct FFmpeg Voice decoding:
all 95 cases preserve extent; 20 Standard, 26 Pro and 13 Lossless cases pass their
existing sample criteria. Full acceptance remains false because 32 of 36 FFmpeg
Voice cases retain the known fidelity failure. This is not counted as a passing
full native matrix. Production uses the pinned Windows Voice provider; all 36
Voice fixtures pass its final adapter's six-target checks above. Failed diagnostic
and preliminary packaging invocations are retained and distinguished from passes.

Human review and release boundary
--------------------------------

Review native Explorer menus, themes, DPI, keyboard and screen-reader behavior;
audition representative soundtracks and inspect representative image/document
outputs. GIF PNGs retain order, not playback timing. No first-frame command or
extra conversion submenu was added.

No new installation, registration, signing, commits, uploads, publication or
unrelated cleanup was performed. These engineering results do not replace
commercial release, game/customer fidelity or owner acceptance.
