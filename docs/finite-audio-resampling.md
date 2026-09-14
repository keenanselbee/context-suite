Finite Audio Resampling
=======================

The 2026-09-14 implementation repairs the original 28 short-input failures in
the [duration investigation](short-resampling-verification.md). It retains the
source-duration guard and the existing native filter, encoding presets, signal
comparison, metadata validation and application-owned publication boundary.
The adapter and newly staged worker have the bounded evidence recorded below;
broader audio and release acceptance remain separate.


Boundary policy and resource limits
-----------------------------------

For rate-changing input with at most 256 decoded source frames, the adapter
retains the original float64 samples in a bounded prefix. It reflects samples
about the endpoint positions, repeating that pattern for a 1,024-frame working
extension. A one-sample input has a constant extension. The native resampler
therefore receives enough working data to initialize and drain its unchanged
filter. The extension is an interpolation boundary, not added recording time.

Only the original interval, with `ceil(sourceFrames * outputRate / sourceRate)`
frames, becomes the reference and encoder input. A temporary float64 WAVE carrier
preserves sample values and explicit speaker masks. The encoder receives this
already resampled interval, so it cannot independently repeat the short-input
failure. Original tags and pictures continue through their inventoried output
handlers; intermediate carriers do not decide metadata policy.

Longer recordings retain their existing resampling path. Their reference extent
must fall between the floor and ceiling of the rate ratio, with at least one
frame. An integral ratio still requires an exact count. Fractional duration can
differ by less than one output sample; the original 19-frame result for an ideal
23.5-frame interval remains invalid. The boundary test's 257 frames at 192 kHz
have an ideal 64.25-frame output. Both independently invoked libswresample and
SoX resamplers produce 64, establishing why the initial ceiling-only guard was
too strict. The finite short-input recipe consistently chooses the ceiling.

The retained prefix is at most 16 KiB for eight channels. Working files are in
the owned operation directory and included in normal cleanup. Native temporary
output is capped at 1 MiB and checked for finite, complete samples and the
expected extended duration before the original interval is selected. Existing
whole-source, decoded-byte, cancellation and process limits remain in force.
No third-party source, binary, enabled filter or installed component changes.


Verification
------------

The private host exposes reproducible modes with pinned candidate and independent
runtime paths and fresh repository scratch directories:

```powershell
dotnet '<private contract DLL>' --short-resampling '<candidate bin>' '<new evidence>' '<independent bin>'
dotnet '<private contract DLL>' --finite-resampling '<candidate bin>' '<new evidence>'
dotnet '<private contract DLL>' --resampling-sources '<candidate bin>' '<new evidence>' '<independent bin>'
dotnet '<private contract DLL>' --rate-layout-matrix '<candidate bin>' '<new evidence>'
```

The short matrix now includes 255/256/257-frame boundary cases alongside the
original 1/16/47/127-frame cases, at seven rates and mono/stereo. Outputs are
independently decoded and compared with the source-derived duration interval.
Every operation checks original bytes/write time and empty adapter scratch.
All **98 cases pass**, including the 28 previously refused conversions. The
Release private test host builds with zero warnings/errors. The 222-case
rate/layout regression retains 188 successes and 34 intended policy refusals.

Analytic checks cover DC, passband, transition and stopband signals at seven
rates with mono, stereo, quad, 5.0, 5.1, 6.1 and 7.1 speaker masks. Distinct signed
channel amplitudes expose reordering. DC and passband outputs are compared with
mathematical signals; above-band tones must be suppressed. Transition-band cells
check finite data but do not assert passband gain. Prefix capture is checked
across writes larger than its memory bound.
All **294 analytic cases** and the bounded-prefix check pass. Maximum passband
error is approximately `7.11e-6`; maximum stopband RMS is `2.31e-7`. These are
generated-signal measurements, not human listening or all-waveform certification.

Sixteen source-container cases cover tagged FLAC, MP3, AAC-LC M4A and Vorbis,
mono/stereo, at 47 and 44,107 frames. Titles, independent source/output extents,
finite samples, original preservation and cleanup pass. An earlier attempt to
create a short AAC seed with the generic high-amplitude fixture failed the
unchanged encoder signal-error bound before resampling; that failure is retained.
The final authored fixtures use the same moderate cosine signals as the original
short matrix. This does not certify arbitrary transient or extreme-value AAC.

`.codex-temp/finite-resampling-session.json` locates the original experiment,
analytic checks, adapter and boundary matrices, metadata cases and rate/layout
regression. `.codex-temp/resampling-fractional-boundary.json` retains the two
independent resampler observations. Failed intermediate attempts are preserved.
The reconciled counts and evidence hashes are in
`.codex-temp/finite-resampling-verification.json`.

Candidate 1.0.9 remains unchanged and predates both the guard and this repair.
Broader input/metadata variants, short surround encoding, representative
listening/player acceptance, interrupted-worker recovery, visible UI, required
Office conversion and commercial release gates remain open.


Candidate 1.1.0 and publication
-------------------------------

Candidate **1.1.0** is staged at
`artifacts/production-staging/169f27e246ce43c4808afa9f99e044a3`, with 116 verified
files and inventory SHA-256
`160B80B502C99E225408D114450E524BE68C817A37719E3B8BD8E75737E652D2`.
Its reservation is `artifacts/production-version-receipts/1.1.0.json`. Version
1.0.9's inventory and every payload file retain their previous hashes.

The new direct-command harness passes **40 isolated checks**. Declining the
resampling prompt publishes nothing and consumes no admission. Accepting a mixed
eleven-file batch requests resampling and lossy-transcoding consent as needed,
uses one admission and the same real worker, and creates validated named copies.
The inputs include previously failing tiny WAV files, the 256/257-frame boundary,
and tagged FLAC, MP3, M4A and Vorbis. Source bytes/write times and titles remain
unchanged. Repeating a conversion creates a new name and preserves the first copy.
A refusing recycler ensures copy-only scope.

All twelve published files independently decode to finite samples within the
source-derived duration bounds; finite-path outputs use the exact ceiling count.
All **2,822 foundation contracts** pass. Release production and public test-host
builds report zero warnings/errors. No app installation, Explorer registration,
native recycling, live commerce or visible UI acceptance was performed. The test
access service is isolated from customer licensing; the harness closes its worker.

```powershell
dotnet '<public contract DLL>' --resampling-direct '<new evidence>' '<staged worker>' '<short matrix fixtures>' '<tagged source fixtures>'
```

`.codex-temp/resampling-production-session.json` records staging and final test
locations. The final direct report is `resampling-direct.json` in that recorded
directory. `.codex-temp/resampling-direct-final-independent.json` records all
twelve decoded outputs; `.codex-temp/resampling-payload-verification.json`
reconciles both candidates. Build and test logs use `resampling-production`,
`resampling-direct-final` and `resampling-foundation` prefixes.
