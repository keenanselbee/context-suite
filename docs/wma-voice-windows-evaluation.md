Explicit Windows Voice Decoder Evaluation
=========================================

Status: isolated evaluation passes; no production backend selected, 2026-09-17.
The repaired FFmpeg Voice decoder still fails 32 waveform comparisons despite
matching all tested lengths. An explicitly created Windows Voice decoder now
matches the retained Windows reference output in all 36 Voice cases. This is a
candidate route for the remaining work, not permission to enable WMA commands.


Method and boundary
-------------------

[WmaVoiceMftProbe.cpp](../tools/audio-engine/WmaVoiceMftProbe.cpp) opens an owned
fixture read-only through a Media Foundation byte stream. The source reader
emits compressed audio; the helper explicitly creates `CWMSPDecMediaObject`,
sets its input/output types, supplies each compressed sample, collects output,
and drains the transform at EOF. It does not invoke FFmpeg's Voice decoder.
The tested system decoder rejects float32 output and accepts PCM16. Converting
that PCM16 to float32 is exact and matches the prior source-reader output bytes.

This follows Microsoft's [Voice decoder API](https://learn.microsoft.com/en-us/windows/win32/medfound/windowsmediaaudiovoicedecoder)
and [synchronous MFT processing model](https://learn.microsoft.com/en-us/windows/win32/medfound/basic-mft-processing-model).
Both comparison paths use the same Windows codec. Their agreement verifies the
explicit orchestration and retained reference samples; it is not independent
codec agreement, proof of lossless source compression, or listening acceptance.

The helper admits only the four mono evaluation rates: 8, 11.025, 16 and 22.05
kHz. Input is capped at 16 MiB, with bounded read/output loops and 262,144 decoded
frames. It restricts DLL search to System32, creates output exclusively, rejects
empty output and unexpected stream changes, and closes resources on failure.
The parent runner imposes a 30-second native deadline, verifies fixture/tool
hashes, and records loaded modules. These are evaluation limits; this tool does
not implement the application's inherited-handle/job/publication boundary.


Verified results
----------------

Run from the repository root using the completed authored corpora:

```powershell
python -B tools/audio-engine/Test-WmaVoiceMft.py '<system corpus>' '<Voice refusal corpus>'
```

Final evidence:
`.codex-temp/wma-voice-mft-95555f331c264f2590cf13e6884c6427`.
The input corpus is `wma-system-matrix-31c7ebfcc539400abea9be9722fcd50d`; the
negative corpus is `wma-voice-refusals-b80ca6229eac46f1bed63276ae83126c`.

- **36 exact output comparisons** pass, including one-sample clips and lengths
  surrounding 480/960-frame boundaries. Every output retains the authored extent.
- **Eight malformed-input refusals** pass. Excessive sample counts cause a
  decoder error; invalid music-marked bodies that yield no samples are rejected
  by the wrapper's empty-output check. These are not valid mixed-mode fixtures.
- **Four ownership/input guards** pass: existing output, non-Voice input,
  oversized input and invalid arguments. Existing output and inputs retain hashes.
- The native build has zero warnings/errors and verified x64, ASLR, DEP, CFG and
  CET flags. Loaded DLLs are under Windows; module hashes stay consistent across
  the run. This does not establish an OS network/filesystem sandbox.

The host reports Windows build 26200. Helper SHA-256 is
`26B3945A21460BA8C4EEDD2975118355CEF3ED6C1E5758CFA99ABFDDD1E3725B`;
source SHA-256 is
`3F9130EA7DA3ABF451B2EE01FC49E42E41978A76EEBD5B1AAD3FEFDCA1D735BA`.
The observed `WMSPDMOD.DLL` hash is
`4CC79FF73A27E4FD3DF2991F021390535E14A456954B26EBC826FEBF2B4F12E1`.
These identify this host's evidence; they are not a cross-version system-DLL
allowlist or authorization to redistribute Windows components.

Retained attempts: `wma-voice-mft-8c752345745f482583051964e359c9e1` fails
compilation because Windows' `max` macro collides with `std::max`; `NOMINMAX`
resolves that build issue. Prototype `6319a70f16d645139fa9bbee36718105` passes
36 comparisons but returns success for four empty malformed outputs. The owned
wrapper now rejects those. Run `5c81c520f66f48c08002481ed4ce03d1` passes the
44 media checks before the four ownership/input guards were added.


Next implementation and acceptance
----------------------------------

1. Define the production provider boundary and OS-component availability policy.
   Pin the owned helper payload and record system dependencies separately; do
   not copy Windows codec DLLs into the package or silently use another decoder.
2. Replace source paths with an inherited read-only snapshot handle. Apply the
   existing suspended-start, explicit handle list and kill-on-close job before
   parsing. Verify cancellation, timeout, worker termination and resource limits.
3. Integrate the typed decoded-sample transport with the existing six target
   recipes, loss/precision/resampling consent, ASF metadata inventory and safe
   publication. Test this through the real application/worker path.
4. Expand valid speech/music/mixed-mode and hostile/timeline coverage. Validate
   supported Windows environments and missing-codec behavior, then complete
   listening, redistribution/environment review and combined package adoption.

The [WMA codec evaluation](wma-system-codec-evaluation.md) retains the failing
FFmpeg sample results. No managed runtime pin, installed app, Explorer command,
reserved 1.1.1 package or commercial release gate changed in this evaluation.
