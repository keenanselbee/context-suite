Expanded Audio Worker Verification
==================================

Status: disposable worker evaluation, 2026-09-17. Customer menu admission,
formal engine packaging and combined release acceptance remain pending.
The installed application and reserved combined 1.1.1 payload are unchanged.

The later [recovery checkpoint](expanded-audio-recovery.md) replaces the repaired
runtime pin after finding a long Lossless raw-transport diagnostic overflow. Its
fresh worker repeats all 1,008 checks and 796 publications. Runtime hashes below
identify this original checkpoint, not the newer loader selection.
The subsequent [surround Pro repair](wma-pro-surround-repair.md) repeats all
1,008 checks and 796 publications with per-channel WAV error limits enforced
for surround as well as stereo. It supersedes the earlier unaccepted surround
comparison below; broader fidelity and final candidate adoption remain open.

This checkpoint connects the [AIFF adapter](aiff-conversion-adapter.md),
[repaired WMA runtime](wma-system-codec-evaluation.md) and optional
[Windows Voice provider](wma-voice-conversion-adapter.md) to the real worker,
application access checks and transactional copy publisher. It does not enable
FFmpeg's failing Voice decoder or turn recognition into customer admission.


Composition and policies
------------------------

The private loader admits the exact repaired seven-file runtime from
`audio-ffmpeg-9ec011dc8135484f98c2c10d0aedff84`. Its encoder SHA-256 is
`466873A38C3846C5D96A839D6A0D4DCF6621290B96F72A4F196A56DD6555E004`.
Every probe/library identity and DLL membership is checked and held through
existing file leases. Earlier runtime identities retain their previous limits;
Pro and Lossless conversion requires this repaired identity.

The worker composes a Voice provider only from its adjacent `audio-engine/voice`
directory. The helper SHA-256 is
`B03029719B338363193C6111A204B144A23EF52947301D6CA5493267C3D0DBA5`.
The existing loader verifies its identity before execution. Without that provider,
Voice remains refused; there is no FFmpeg fallback. Successful Voice results
identify the Windows decoder as well as the FFmpeg encoder. Windows codec DLLs
are not copied into the payload.

Public planning treats Pro as a lossy source. Lossless input retains inventoried
16/24-bit precision and needs no lossy-source acknowledgement. When native probing
omits Lossless precision, the complete ASF inventory supplies it; conflicting
codec, container, rate, channel count or precision refuses conversion. Existing
resampling, precision and lossy-transcoding consent still applies.

The signal-error limits remain unchanged. An excessive error now travels through
the existing typed validation-failure protocol and produces an actionable message:
"The converted audio did not preserve the sound closely enough. Try WAV. The
original was kept." A failed reservation retains its failure record; it is not a
published output. Cleanup and source preservation are checked separately.


Finite worker matrix
--------------------

`Test-ExpandedAudioWorker.py` builds the current managed worker/test host and
copies a disposable payload under `.codex-temp`. It records every payload hash,
checks fixture/reference identities and verifies the payload again after the run.
It uses generated inputs, an isolated fresh local trial, copy output and a recycler
that throws if called. It does not install, register Explorer commands, contact
Polar or use customer files.

The matrix has 144 inputs and six target decisions per input:

- 95 independently authored Windows WMA cases: Standard, Pro, Lossless and Voice.
- 27 earlier expanded inputs, including precision-boundary AIFF/AIFF-C vectors,
  three compressed AIFF-C variants and WMA v1/v2.
- 22 moderate one-second mono/stereo tones covering AIFF and AIFF-C NONE
  PCM8/16/24/32, float32/64 and little-endian PCM16.

Each positive case probes through IPC, plans with explicit consent, passes local
access admission, converts in the real worker, publishes a named copy and
re-probes the published target. WAV output is also parsed independently and
compared with the recorded reference samples and extent. Both the authored source
and working copy must remain byte-identical. Ten unsupported MP3 rate/layout
decisions are refused before execution. Fifty-eight extreme precision-vector
lossy encodings must return the specific validation refusal without publishing
or leaving an output-directory entry. They remain safety tests, not successful
conversion claims.

Standard WMA expected output length comes from independent Windows decoding,
including encoded padding, rather than the pre-encoding authored sample count.
For example, a one-frame 44.1 kHz mono source decodes to 2,048 frames, and a
48,000-frame 48 kHz stereo source decodes to 49,664 frames. No trimming or padding
is added to make the candidate fit a mistaken expected length.

Standard and stereo Pro WAV samples use the previously established peak
`1e-6` / RMS `3e-7` agreement bounds. Voice and Lossless comparisons are exact;
AIFF comparisons retain their recorded decoded references or authored PCM.
Three surround Pro cases record numeric differences and verify extent, but do
**not** assert independent sample agreement. Surround Pro fidelity remains an
open acceptance gate.


Evidence and retained failures
------------------------------

The final run, `expanded-audio-worker-7d8102d616014ea0a3a867475767bee3`, passes
**1,008 checks with 796 validated copy publications**: 864 target decisions
(796 publications, ten unsupported MP3 decisions, 58 signal-error refusals)
and 144 unchanged-original checks. There are zero failed cells. Payload hashes
remain unchanged after execution. Build logs, fixture/reference identities,
published output hashes, comparison metrics and per-file results are retained
inside that evidence directory.

The first run, `expanded-audio-worker-3d0b6a6688b144ae91f9882c557b8ea7`,
retains 646 verified publications and 76 failed cells. Eighteen WAV assertions
used the authored count instead of Windows-decoded extent. The remaining 58 were
the unchanged signal-error guard rejecting extreme lossy conversions. A separate
100-cell adapter diagnosis in
`expanded-audio-refusals-e347b15de9e9497baad60755ddfba4ec` identified that exact
cause in all 58 cases; no quality threshold was relaxed.

The second run, `expanded-audio-worker-3cd6014af0de45a789d8bd048c4eb13c`,
corrected extents and added representative tones, but the refusal assertion
incorrectly required a null publication record. Failed reservations intentionally
retain a `Failed` record. The corrected assertion requires that outcome, the
specific validation message and an unchanged output directory.

The focused runtime/precision run `wma-runtime-f61152daedbc482c8e5cdaaa006dbbe2`
passes 23 checks, including old-runtime refusal, missing Voice provider,
conflicting precision and tampered/missing/mixed runtime membership.
Foundation replay in `expanded-audio-foundation-replay.log` passes 4,380
contracts. It preceded the final user-message branch; the actual worker matrix
exercises that branch. An earlier PowerShell redirection wrapper stopped on
expected native stderr; the corrected capture retained stdout/stderr without
turning diagnostics into a test failure.

The repaired runtime passes **368 existing audio probe/encoding checks** in
`repaired-audio-regression-20260917-typed`, with the independent supplier decoder.
The first invocation supplied relative paths and stopped before media work. The
absolute-path replay stopped at an existing Ogg refusal test that still expected
the previous exception/message. The final run checks the specific typed
validation failure and retains the same two signal-error refusal fixtures.
Release builds of the worker and public/private hosts have zero warnings/errors.


Reproduction and remaining acceptance
------------------------------------

Run from the public repository with existing repository-owned evaluation inputs:

```powershell
python -B tools/audio-engine/Test-ExpandedAudioWorker.py `
  '<repaired build directory>' '<streaming Voice helper directory>' `
  '<95-case system corpus>' '<27-case expanded input fixtures>'
```

The runner creates new evidence and refuses altered runtime or fixture identities.
The public host also exposes `--expanded-audio-worker <new evidence> <worker>
<fixture manifest>` for the prepared payload. The private host's `--wma-runtime`
mode checks runtime selection/precision; `--expanded-audio-refusals` is a diagnostic
mode that records outcomes, not a passing launch-acceptance suite.

Remaining work includes expanded-input worker interruption/recovery, direct-command
and mixed-selection acceptance, metadata/artwork through this worker composition,
surround Pro and broader real speech/music fidelity, hostile/discontinuous
timelines, missing Windows component environments, resource/performance budgets,
listening and visible usability, redistribution review and combined 1.1.2
packaging. Earlier adapter/process evidence remains useful but does not replace
those integration and human checks. Signing and commercial release gates remain
separate, and the current goal is incomplete.
