Independent FLAC Decoder Acceptance
==================================

The 2026-09-11 checkpoint independently verifies 16-, 24- and 32-bit FLAC
conversion and optimization outputs with Xiph FLAC 1.5.0. All eighteen outputs
decode to exactly the authored integer samples. This advances the previously
open 32-bit decoder-compatibility gate; it does not establish older-player or
device compatibility, listening acceptance or production redistribution approval.


Independent tool identity
-------------------------

The executable is downloaded independently from the
[Xiph 1.5.0 release](https://github.com/xiph/flac/releases/tag/1.5.0), which publishes
the Windows ZIP's SHA-256:
`53f1500f0d6e7c61379d7fee50d4a9f7f504c650009506d9ba015530d76c0dde`.
The retained archive is 1,318,000 bytes at
`.codex-temp/flac-independent-6373c459e39b49af8f72b948c416fc9a/upstream.zip`.
The test extracts the x64 executable and library into new owned scratch, along
with the original GPL/Xiph notices. Exact runtime hashes are:

| File | SHA-256 |
| --- | --- |
| flac.exe | `ff23d9cbc11d18c02f262c3ee455830ea13fbe8d9876249f0bdf101e3ad66709` |
| libFLAC.dll | `f93499172875fc2c0df80b57086f32e3f39e835283952ee2a59a3d4ffb097644` |

These are test-only files, excluded from production payloads. No reference-tree
binary, installer or PATH change is involved.


Matrix and observations
-----------------------

The real private `AudioEncodingAdapter` creates every output using the pinned
curated runtime from verified stage
`artifacts/production-staging/8a91b541545f435c87456f69d99aeea6`. The harness does not
replace adapter policy with a standalone FFmpeg encoding command.

Each precision (16/24/32 bits) is tested with mono/stereo at 48 kHz and six
channels at 96 kHz: nine source cases. Every case contains 4,097 frames, including
signed extrema, adjacent extrema, zero, small integer values and a generated
waveform. Channels use offset patterns. A non-block-aligned tail exercises final
frame decoding. The odd-length mono PCM24 RIFF payload includes its required
padding byte; the expected sample stream excludes padding.

Each source is converted to FLAC, then optimized. The adapter reports exact
samples and the expected frame count/precision. All nine optimization outputs
are smaller in this matrix; that observation does not promise savings generally.
Xiph's [test and raw decoding modes](https://xiph.org/flac/documentation_tools_flac.html)
validate both outputs. Raw signed little-endian interleaved bytes must equal the
retained authored sample bytes without float conversion, trimming or tolerance.
All eighteen checks pass with empty diagnostics, and source/output/decoder hashes
remain unchanged.

Two negative controls truncate or corrupt a generated 32-bit stereo output.
Xiph refuses both: the corrupt frame reports CRC mismatch and the truncated
stream reports lost synchronization after 4,096 samples. These refusals help
establish that independent validation examines the final frame.


Reproduction and evidence
-------------------------

```powershell
python -B tools/audio-engine/Test-IndependentFlac.py --archive '<retained publisher-verified ZIP>' --production-stage '<verified combined stage>'
```

The final run is
`.codex-temp/flac-independent-run-2ca02d94bcac438bb9f3aeb82e61e54d`:
`build.log`, `adapter.log`, `fixtures/fixtures.json`, generated WAV/raw/FLAC
files, negative diagnostics and `independent-flac.json`. The Release private
contract host builds with zero warnings/errors. Native test calls have a
30-second deadline and bounded expected output; only authored small fixtures run.
The adapter host runs directly so timeout termination closes its native jobs.

An earlier fixture attempt failed to compile, then the corrected build exposed
missing RIFF padding in the new odd-length PCM24 fixture. The product correctly
rejected it. Partial evidence remains under the archive directory's `fixtures`;
it is not counted as passing. The final fixture fixes padding without weakening
production validation. An intermediate complete run remains at
`.codex-temp/flac-independent-run-59cacfc21d124767905177997074697d`.

Two wrapper guards reject a changed archive and an archive outside repository
scratch before native execution. Their evidence is
`.codex-temp/flac-oracle-guards-4cbc324b3a1d482d84d3ac19d58c0bf8/results.json`.
The combined stage inventory passes again after execution. Public-source boundary,
theme policy, 85 documentation files, Python syntax and both roots' whitespace
checks pass.

The broader 298 private adapter/116 worker suites and visible/player/listening
acceptance were not rerun. No production policy, engine pin, installation,
Explorer registration, native recycling or live licensing changed.
