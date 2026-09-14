Short Audio Resampling Verification
==================================

Historical checkpoint: duration validation repaired while short resampling
remained incomplete. The later [finite-input repair](finite-audio-resampling.md)
resolves the original 28 failures and corrects the ceiling-only rounding policy.
Updated 2026-09-14. Neither checkpoint is complete audio/release acceptance.

The generated matrix exposed a shared-resampler failure: both the reference and
the encoded output could omit the same final samples and pass their comparison.
For 47 PCM16 source frames at 96 kHz, mono and stereo conversion to 48 kHz Opus
returned 19 frames, although the intended extent is 24. Independent decoding
confirmed the shortened results. These were adapter-admitted candidates, not
published customer files. No worker/publication path was invoked in this matrix.

The adapter now counts decoded source frames at their original rate before any
resampling. It cross-checks available WAV/FLAC inventories and computes the output
extent as `ceil(sourceFrames * outputRate / sourceRate)`. This retains the source
time interval to within one output sample. The planned-rate reference must have
that extent before encoding starts. The later exact-length, finite-sample,
signal-error, metadata and publication checks remain required.

The extra decode occurs only when the rate changes. It uses the same bounded
owned snapshot, cancellation deadline and decoded-byte limit; its samples are
counted and discarded rather than stored in another complete reference file.
It adds processing time to resampled conversions. It does not certify waveform
fidelity or repair native resampler flushing.


Measured results
----------------

The private `ShortResamplingMatrix` generates seven rates
(8, 11.025, 16, 44.1, 48, 96 and 192 kHz), mono/stereo, and
1, 16, 47 and 127 source frames. Every cell records errors, expected extent,
accepted output hashes and independent decoding where output is admitted.

| Evidence | Before | After |
| --- | --- | --- |
| Total cases | 56 | 56 |
| Correct accepted outputs | 28 | 28 |
| Incorrect accepted outputs | 2 | 0 |
| Refused conversions | 26 | 28 |
| Full acceptance matrix | Failed | Failed |

The two newly refused cases fail the independent duration check before encoding;
no output artifact is returned. Other failing cases produce no complete resampled
reference. Refusal preserves the source, but does not satisfy the intended short
conversion support. Both matrix invocations deliberately exit nonzero.

All source bytes and write times remain unchanged, and adapter scratch is empty
after every operation. The existing 222-case rate/layout matrix matches every
expected success/refusal. Eight additional FLAC, MP3, AAC-LC M4A and Vorbis source
cases, each mono/stereo, pass: independently decoded 44,107 frames at 44.1 kHz
become exactly 48,008 frames at 48 kHz with finite samples and verified metadata.
The Release private host builds with zero warnings/errors.

Reproduce with absolute paths to the Release private contract host and pinned
candidate/independent engine directories:

```powershell
dotnet '<private contract DLL>' --short-resampling '<candidate bin>' '<new evidence>' '<independent bin>'
dotnet '<private contract DLL>' --resampling-sources '<candidate bin>' '<new evidence>' '<independent bin>'
dotnet '<private contract DLL>' --rate-layout-matrix '<candidate bin>' '<new evidence>'
```

The local receipt `.codex-temp/short-resampling-session.json` locates the complete
before/after matrices, rate regression and source-container evidence. The
reconciliation is `.codex-temp/short-resampling-verification.json`. Logs use
`short-resampling-*` and `resampling-sources-*` prefixes. A separate direct native
probe is `.codex-temp/short-resample-probe-414d5aa8b5d744c49d8fdea437b7152f`.


Remaining implementation
------------------------

Fix and review finite-signal resampler flushing and boundary behavior, then
require all 56 cases to succeed with independently checked duration and samples.
Do not reduce filter quality, pad published audio, truncate validation or classify
these refusals as successful conversions merely to make the matrix pass. Broader
rates, precisions, layouts, metadata and listening remain separate obligations.

Static inspection of the pinned [resampler source](https://raw.githubusercontent.com/FFmpeg/FFmpeg/9b0578816c6f94514d330d4f2ae7e44a9fb42692/libswresample/resample.c)
provides a concrete next lead: initial buffering waits for the filter extent plus
one sample, while flushing reflects only half the available input, capped by the
filter extent. A very short input can remain below the startup requirement.
That is an implementation hypothesis to verify, not an accepted boundary policy
or a reason to weaken duration validation. The source was retrieved for static
review only; no third-party file or native binary was changed.

Candidate 1.0.9 is unchanged and predates this guard. Its short resampling must
not be presented as verified; the two shortened-output observations apply to its
adapter behavior. Updated production staging and direct-worker verification wait
for the next candidate, which must use version 1.1.0 or later. Required Office
conversion, visible acceptance and commercial release gates remain open.
