Windows Voice Streaming Verification
====================================

Status: isolated provider verification, 2026-09-17. Customer/worker admission,
packaging and broader fidelity acceptance remain open. This extends the
[optional Voice adapter](wma-voice-conversion-adapter.md); it does not clear the
[release goal](broad-file-support-goal.md).


Streaming and limits
--------------------

The earlier provider buffered all decoded samples and stopped at 262,144 frames,
roughly 12–33 seconds at the admitted rates. It now streams the native float32
sample transport into an owned float64 WAV using fixed 64 KiB input and 128 KiB
conversion buffers. Every sample must remain finite, within PCM16 range and on
the exact PCM16 lattice. Reads may split samples at arbitrary bytes. The header
is finalized only after successful child exit and complete nonempty samples.
Failures remove the newly reserved transport; existing files are never replaced.
Empty stdout defers startup-error classification to the process exit status.

Voice now shares the file adapter's 512 MiB input and 1 GiB float64 decoded budget.
The native limit is the corresponding 134,217,728 mono frames, with at most one
million compressed-reader iterations. Per-pull allocation and output-iteration
limits remain. Inherited mode suppresses per-sample diagnostic lines so normal
long recordings do not exhaust the existing 64 KiB stderr budget. The enclosing
conversion's 120-second deadline and cancellation/job ownership remain in force.
These resource bounds are not a duration or completion guarantee. Full-limit
memory/disk stress, peak process memory and supported-machine budgets remain open.

The refreshed helper SHA-256 is
`B03029719B338363193C6111A204B144A23EF52947301D6CA5493267C3D0DBA5`;
source SHA-256 is
`785D6CEA510316D86ADDC6C5CB20BD920F4586D43C03D21F477683A218C78447`.
Native evidence `wma-voice-mft-91f1e1f74e9d43039e1fecff9d65295f` passes
all 48 existing path-mode checks, including refusal above the new input bound.


Five-minute speech evidence
---------------------------

[Test-WmaVoiceDuration.py](../tools/audio-engine/Test-WmaVoiceDuration.py) retains
an explicitly adapted copy of the hash-checked, owned short-fixture generator.
The original generator and existing short corpus remain unchanged. Optional
`--speech` mode uses an already-installed Windows English speech voice to author
local spoken text into a repository scratch WAV, without playback or networking.
The existing isolated encoder resamples it to each tested mono PCM16 rate. The
PCM repeats to exactly five minutes plus one sample, then the Windows encoder
authors Voice and the Windows decoder produces the reference.

```powershell
python -B tools/audio-engine/Test-WmaVoiceDuration.py --speech '<expanded runtime bin>'
dotnet run --project proprietary/tests/ContextSuite.Audio.ContractTests/ContextSuite.Audio.ContractTests.csproj -c Release -- `
  --wma-voice-duration '<expanded runtime bin>' '<helper directory>' '<duration corpus>' '<new evidence directory>'
```

Use absolute runtime/corpus/evidence paths. Corpus
`wma-voice-duration-de5ad03d632541c5a5c5dea85689d7b0` records Microsoft David
Desktop and hashes the source WAV, adapted writer, runtime and all fixtures.
All four Windows references preserve the authored sample counts at 8, 11.025,
16 and 22.05 kHz. This uses the same codec as the provider and does not replace
independent codec, real-recording or human listening acceptance.

Private evidence `wma-voice-duration-convert-00529198da724c4089498bf0145d092d`
passes **55 checks with 24 conversions**: all four rates to all six existing
targets. Streamed samples match the Windows reference exactly; WAV/FLAC preserve
them, Opus obeys the existing resampling consent/count policy, and metadata,
caller position, cleanup and original hashes pass. Transport checks cover tiny
fragments, 64 KiB crossings, profile fields, truncated/nonfinite/out-of-range
samples, a one-sample budget excess and native-failure cleanup followed by reuse.

Observed native decode times are 0.451–0.808 seconds; per-conversion observations
including validation and retained output copy are 1.081–7.692 seconds. The short
matrix ran concurrently for part of this test. These are warm local observations,
not controlled benchmarks or release thresholds. The 24 outputs total 138,986,012
bytes. The existing short/metadata matrix repeats **339 checks and 222 outputs**
in `wma-voice-convert-263d9141c7604617b4fbbd0d8d253ded`.
Availability replay `wma-voice-availability-c43187446c1644b6b88a87403f5e9a49`
passes **26 checks**, including startup-error classification with empty streamed
output. Process replay `wma-voice-process-b4c647643edb4ddea49e786a1484378f`
passes all **52 ownership/cancellation/recovery checks**. Managed Release builds
have zero warnings/errors. Public foundation and unrelated audio matrices were
not rerun for this provider-only streaming change.


Retained failures and next acceptance
------------------------------------

The first duration test (`59248117390e4887a94fd831f57ea69d`) stopped in a test
exception catch before conversion: `InvalidDataException` needed an explicit
catch alongside `EndOfStreamException`. The malformed transport was refused.

Continuous-tone corpus `wma-voice-duration-92f2df0a41304d64a7e20273e524992b`
passes reference lengths, but the 8 kHz Windows reference becomes saturated DC
after about 13 seconds despite a bounded, centered authored tone. Its first
five target conversions complete in `wma-voice-duration-convert-cf55b7f7e0f9447e8472c770a44a2d16`;
Opus correctly fails the unchanged RMS error bound. Diagnostic
`voice-long-opus-diagnostic-25721c3bf52446059ec0833d0453249d` reproduces this
from the retained decoded WAV: equal extents, approximately 0.0028 early RMS
error, approximately 1.0 after DC saturation. Opus removes the source DC.
No samples were trimmed, padded or shifted and no acceptance bound was relaxed.
The tone matrix remains failed; the separate speech matrix does not erase it.

Determine the scope of the Windows encoder/decoder tone instability before
broader Voice acceptance. Add real speech, music/mixed mode and discontinuous
timelines; retain explicit refusal where preservation cannot be verified. Missing
Windows components, actual application/worker publication/recovery, resource
stress, packaging, listening and combined-candidate acceptance remain separate.
Installed software and the reserved 1.1.1 stage are unchanged.
