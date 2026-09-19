Expanded Audio Recovery
========================

Status: real worker interruption evaluation, updated 2026-09-19. This extends the
[expanded publication checkpoint](expanded-audio-worker-verification.md) with a
fresh pinned native transport repair and disposable worker. The installed app,
reserved combined 1.1.1 stage, customer commands and quality limits are unchanged.


Coverage and observation
------------------------

The finite matrix covers five inputs: two-minute generated stereo AIFF PCM16,
WMA Standard PCM16, Pro PCM16 and Lossless PCM24, plus the retained five-minute
22.05 kHz mono generated-speech Voice fixture. Windows supplies the WMA fixture
encoder and Voice decoder; no system component is copied or installed.
The AIFF structure and PCM samples are authored locally. Fixture hashes and the
existing worker payload inventory are checked before running.

Each input first completes a validated FLAC copy. The test then injects three
faults during source decoding and again during output encoding: caller
cancellation, expiry of the production client's 150-second deadline, and abrupt
termination of that test client's worker alone. There are 30 interrupted
operations and a same-source FLAC retry after each one.

Fault injection requires all of the following observations together:

- The application operation remains active and its temporary output is locked.
- A native child belongs to the exact owned worker PID and has the expected
  executable path in the verified payload.
- The selected working file grows while that same child consumes CPU.

Source decoding observes `reference.f64le` for FFmpeg inputs or
`voice-decoded.wav` for Windows Voice. Encoding observes `encoded.candidate`.
The test does not count process creation or a dispatch callback as proof that
the native phase ran. A phase too short to observe fails the test rather than
being reported as verified. No unrelated process is terminated.

The application executor, local access admission and transactional publisher
handle the interruption. The test checks cancellation/failure result states,
worker and observed native-child exit, removal of owned working files,
reservation and journal, and absence of an unintended published copy. Original
hashes and timestamps and all earlier committed-copy hashes remain unchanged.
Every following same-source retry must publish another validated, collision-safe
FLAC copy, reparse as FLAC and use a fresh worker process. The test recycler
throws if called.


Reproduction and evidence
-------------------------

Run from the public repository:

```powershell
python -B tools/audio-engine/Test-ExpandedAudioRecovery.py `
  '<successful expanded worker evidence>' '<generated-speech duration corpus>'
```

The runner requires the successful 796-publication payload and verifies its
recorded file hashes. The owned duration fixture writer is pinned to SHA-256
`44D83BEDCCED1F2989737CC72A65D046F6958CFC248FB9581095792F8C14A900`.
The existing Voice source must match its original successful duration receipt.
All generated inputs, logs and output remain under a new `.codex-temp` directory.
The public contract host also exposes
`--expanded-audio-recovery <new results directory> <worker> <fixture manifest>`.

Run `expanded-audio-recovery-4031a31d43374c80a0aadef949e5c97d` passes the
12 Standard/Pro interruption scenarios, then stops at the Lossless baseline
conversion before injecting any fault. Direct native diagnosis yields all
11,520,000 samples exactly matching both the authored input extent and Windows
decoding, but writes 102,012 bytes of repeated raw-muxer timestamp diagnostics.
That exceeds the unchanged 64 KiB diagnostic limit and safely refuses publication.
That attempt did not pass the full five-input recovery matrix.

The later native fix adds an opt-in `--raw-audio-timestamp-repair` build switch.
Its patch skips the monotonic timestamp diagnostic only for a muxer declaring
`AVFMT_NOTIMESTAMPS`. The raw PCM muxer already ignores this timestamp failure;
the patch does not change sample decoding, append/trim samples, disable decoder
diagnostics or increase the diagnostic budget. Timestamp-bearing muxers retain
the previous behavior. This is a raw transport fix, not general WMA timeline
acceptance.

Fresh runtime `audio-ffmpeg-17580d153a4442f09e54822b865dfb31` has encoder SHA-256
`27159DC409F049BE855DCCAB1A28CC8C7647AB88508A49088424B5D2A5419C93`.
The loader replaces its earlier repaired candidate pins with this exact seven-file
identity. The earlier verified scratch worker remains immutable and available as
historical evidence. Build review `build-review-8fc4fb587bcf4be8b361f6e95e0583cf`
reviews 11 diagnostics and all seven files' hardening/imports. Source-selection
guards in `raw-audio-source-guards-f796f2e481e848edae064e675ca19160` pass six
positive/tampering checks. Smoke run `smoke-d23d5a02908e40b1acb6215fd60f780f`
passes six-target candidate/independent decoding and embedded PNG decoding.

`Test-RawAudioTimestamps.py` passes **101 checks** in
`raw-audio-timestamps-4207237ae9f54d71b6afebcda6b9afc6`: exact long Lossless
source/Windows comparisons, zero repaired raw stderr, byte-identical earlier
95-case WMA outputs, retained timestamp-sensitive direct WAV failures and
malformed-input errors. Those WAV failures are native controls; application
conversion uses its validated sample transport. FFmpeg Voice still has its
previous waveform failures and is not used by the application provider.

Worker replay `expanded-audio-worker-5c8d89bbbe3248a5b4ce52c07154466d` passes
**1,008 checks and 796 publications** against the new runtime. Recovery attempt
`expanded-audio-recovery-60def7fed5694d73b8720b4766253f6b` misses a fast Standard
decode phase. The observer is corrected to require nonzero CPU already consumed
by the identified live child, plus growth across two observations, rather than
requiring an additional coarse CPU-time tick within 15 ms. It still fails if the
phase cannot be observed and never treats completed work as an interruption.

Baseline replay `raw-transport-audio-regression-20260919` also exposes a transient
sharing violation while deleting an owned snapshot after cancellation. The adapter
now retries only sharing violations, at most 20 times with 100 ms asynchronous
delays, revalidating the exact path on each attempt. Persistent locks still throw;
other failures are not swallowed. Focused delayed-release and persistent-lock
tests accompany the existing real native cancellation check. This cleanup change
postdates the disposable worker above; its private-adapter regression is recorded
separately from worker recovery evidence.

Final recovery run `expanded-audio-recovery-873b7c484f1e4b429637f7c7cfef784e`
passes **265 checks**: all 30 native-phase interruptions across five inputs,
30 successful same-source retries and five baseline copies. Originals and earlier
copies remain unchanged, both observed process levels exit, and abandoned work is
removed. The payload inventory remains unchanged after testing. Private-adapter
replay `raw-transport-audio-regression-20260919-cleanup` passes **370 checks**,
including real native cancellation and both new cleanup controls. The public
test-host and private Release builds have zero warnings/errors.
Runtime/precision replay `raw-transport-runtime-20260919` passes 23 checks,
including altered/missing/mixed file identities and absent Voice-provider refusal.

The initial preparation directory
`expanded-audio-recovery-2c7ccc1b64e04df8b37e71bf63353c3a` retains the failed
attempt to request Windows Lossless 48 kHz stereo PCM16: the system encoder had
no matching profile. The corrected generator selects the already verified
48 kHz stereo PCM24 profile. An initial test-host compile lacked the namespace
import for the existing deadline clock; adding the import fixes that build.
Neither preparation failure is an application recovery result.


Remaining acceptance
--------------------

This is worker decoding/encoding interruption coverage with application
publication cleanup and retry. It does not verify a whole application crash at
each publication boundary for these expanded inputs, interactive cancellation,
Explorer dispatch, mixed selections, metadata/artwork through this composition,
overwrite/recycling or every target encoding recipe. Earlier general audio
publication-crash and multi-target encoder tests remain separately scoped.
Broader WMA/AIFF fidelity, surround Pro agreement, resource/performance budgets,
missing Windows-component environments, listening, visible acceptance and formal
combined packaging remain open. The release goal remains incomplete.
