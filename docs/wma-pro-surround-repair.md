WMA Pro Surround Matrix Evaluation
=================================

Status: finite native and actual-worker acceptance passed; broader release gates remain.
Updated: 2026-09-19. Formal staging and installed payloads remain unchanged.

The retained 95-input Windows-codec corpus includes three 48 kHz, six-channel,
24-bit Pro sources. The one-sample case already agrees closely. The 2,049-frame
and one-second cases differ from Windows by approximately 0.001665 and 0.001635
at their largest sample errors. Equal decoded lengths did not clear fidelity.

Channel inspection localizes the differences to intervals on the five full-range
channels; the LFE channel remains within the existing numerical limits. A
diagnostic copy of the pinned native source logs transmitted mixing matrices.
Its output remains byte-identical to the selected runtime. Replacing the fixed
matrices with exact DCT coefficients changes none of these three outputs: the
problem intervals use transmitted matrices instead.

The [upstream decoder](https://www.ffmpeg.org/doxygen/trunk/wmaprodec_8c.html)
reconstructs transmitted rotations using floating-point matrix arithmetic. Its
pinned source's fixed matrices already have coefficients at approximately 1/256
steps. Rounding the completed transmitted matrix to that same precision brings
all three diagnostic cases within the previously selected peak `1e-6` and RMS
`3e-7` limits against Windows. This is an inference from source inspection and
independent decoder observations, not a claim of an available normative codec
specification. Audio output is not rounded, padded, trimmed or normalized.

Diagnostic workspace `pro-matrix-diagnostic-ac16405c77dd4d25ac175e6eb861afbe`
retains control/exact-default results and the final-matrix Q8 experiment.
The latter observes peak errors at most `5.07e-7` and RMS at most `1.06e-7`.
Its script then fails while attempting to write the source for a different,
unused sine-table experiment. That experiment did not build or run. The
completed final-matrix results remain diagnostic, not a passed release build.
No historical source or selected runtime was changed.


Frozen acceptance before the fresh build
---------------------------------------

- Replay all 95 retained inputs with verified hashes. All non-surround-Pro
  decoded bytes must stay identical; this does not accept the previously known
  FFmpeg Voice fidelity failures. The application still uses Windows Voice.
- For every retained surround Pro case, preserve all frames/channels and require
  peak error at most `1e-6` and RMS at most `3e-7` against Windows, for each channel
  as well as the complete output. Do not relax the existing stereo limits.
- Generate 26 fresh 48 kHz, six-channel PCM24 sources with the owned Windows
  fixture encoder. Twenty-one tone lengths cover 1, 127, 128, 129, 255, 256, 257,
  511, 512, 513, 1,023, 1,024, 1,025, 2,047, 2,048, 2,049, 4,095, 4,096, 4,097,
  48,000 and 48,001 frames. Five additional one-second signals cover silence,
  staggered impulses, deterministic independent noise, correlated channels and
  sparse channel activity. Require exact Windows/source frame counts and the
  same per-channel numerical limits; retain encoder failures as failures.
- Build from pinned source with an explicit opt-in matrix patch. Review native
  diagnostics, hardening/imports, source/patch identities and existing six-target
  smoke. Verify sequential patch identities when both Pro repairs touch one file.
- Only after that evidence passes, review application runtime adoption and rerun
  affected worker/publication checks. Human listening, other rates/layouts,
  hostile timelines and full launch acceptance remain separate.

The patch applies final-coefficient rounding only to WMA Pro; the shared XMA
path is unchanged. The default native recipe remains unchanged unless the new
`--wma-pro-matrix-repair` option is selected with expanded inputs.


Fresh native evidence
---------------------

Build `audio-ffmpeg-a938e7d84aae48e3b206b11432c7925d` completes with the
explicit matrix option and all previously selected repairs. Source identities
and patch order are recorded in its build receipt. The matrix patch SHA-256 is
`F74D74519C93BE997C2CD53EC7010748C1049DD2085A0242F389CB637D19744F`.
The native review accepts eleven individually reviewed diagnostics and verifies
hardening/import metadata for all seven runtime files. Its final evidence is
`build-review-16af63bcf892435299e984b212378c08`.

The first source review fails because Git reconstructs pristine source using
Windows CRLF while the archive uses LF. The checker now restores LF only when
the resulting exact SHA-256 matches the recorded pre-patch identity. It verifies
every intermediate state in a disposable copy, including both repairs to the
same Pro decoder. Eight control/mutation checks pass in
`audio-source-review-6673fe39c32f444c8a02fef16021af38`, refusing unselected or
missing patches, changed patch identity/target, changed intermediate identity,
changed inventory and altered source bytes. Historical sources are untouched.

`Test-WmaProSurround.py` prepares the predefined sources in
`wma-pro-surround-8daf5367c2194f22a153abc4ecdf961d`. Its fresh
`candidate-f81d2e95e9e04e30a1fc5fde1626f813` run passes all 121 cases:
29 surround comparisons retain exact extents and pass each channel's existing
peak/RMS limits, and 92 other outputs remain byte-identical to the retained
decoder outputs. Worst channel peak is `5.0664e-7`; worst channel RMS is
`1.8609e-7`. This does not accept the known FFmpeg Voice waveform failures;
the application continues to require its separate Windows Voice provider.

The six-target candidate/supplier smoke passes in
`smoke-26f9dc03480e42dfa488cd922e0f8135`, including embedded PNG decoding and
unchanged originals. Broader rates/layouts, real-content listening, hostile
timelines and final combined candidate acceptance remain open.

The private adapter now selects the new exact seven-file identity for isolated
worker testing. The worker oracle requires surround Pro to meet the stereo
limits and checks every channel separately; it no longer records an unbounded
surround comparison as acceptable integration evidence.


Isolated application adoption
-----------------------------

Worker replay `expanded-audio-worker-a2af4af1a88f405d921d02d013804640`
passes **1,008 checks with 796 publications** across the same 144-input corpus.
Every WAV reference comparison now checks each channel independently, including
all three retained surround Pro sources. Original hashes and the exact worker
payload remain unchanged. Both managed Release builds have zero warnings/errors.

The selected runtime SHA-256 inventory is:

| File | SHA-256 |
| --- | --- |
| `ffmpeg.exe` | `A5C61F77EE6D541A1B75F55A02A9C0A33F66C7E972D9932D4D40342EC2D3715D` |
| `ffprobe.exe` | `5EE15EEF2ED11A4F5C0110F8324FC13A1D4E39B94B0D456261753F03ED8D63C6` |
| `avcodec-63.dll` | `74D3276AFEAE95B354159B8B1266FCA0185A115D0913DDD92C9CAB7A8F7C7BD4` |
| `avfilter-12.dll` | `2732CA46D82DCDE2870CD31E3079BFBADB8249A933C8260D59FEAFEA13F87F20` |
| `avformat-63.dll` | `5B96093AB4D5C74354D12E96AF6AE355538634413F5610B49E0EA0A9E00E8E2D` |
| `avutil-61.dll` | `7FE2FD137248F65ECDBEFCAC5A4DF34029DB76457426A5FE8B99A6EF2CAC4383` |
| `swresample-7.dll` | `62BC091A40810E5358F39761D259DF9C5D639C1DFA40BAC82FF52A692BD06A68` |

The existing audio adapter suite passes **370 checks** in
`pro-matrix-audio-regression-a90d22b4aec845b7830465b7b8570fbc`, including
independent decoding, precision, metadata and cancellation/cleanup controls.
Runtime replay `pro-matrix-runtime-e36cb1f3e61949a28dcaf5bac7011a66` passes
**23 identity/precision checks**, including altered/missing/mixed files and
missing Voice-provider refusal. An initial fixture lookup found multiple
historical matrices and stopped before execution; the replay uses the documented
corrected matrix. An initial direct private-host command named the wrong target
framework directory and did not launch; the corrected `net10.0` path passes.

Direct-command replay `expanded-audio-direct-5805d08483f14264affd7144c66296ed`
passes **211 checks**, including six targets, mixed batches, exact quality
decisions, expired access and refresh/retry, missing providers and unchanged
originals/earlier copies. Both fresh managed builds have zero warnings/errors.
`pro-matrix-foundation.log` records **4,384 passed foundation contracts**.
Repository validation passes 184 documentation files and public boundary/theme
checks; six changed Python scripts pass syntax checks. These automated results
do not substitute for listening, visible accessibility or final packaging.
