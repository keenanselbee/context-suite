HEIC And AVIF Conversion Performance
===================================

Status: all four pairs pass against the isolated combined HEIF payload;
final expanded-candidate replay remains pending. This exercises the
12-megapixel image workload in the [local acceptance contract](local-candidate-acceptance.md).
It does not replace broader color, variant, recovery or human fidelity acceptance.


Declared method
---------------

Generate one 4000-by-3000, opaque, upright, 8-bit SDR image in each input format
using the independently prepared fixture-only libheif encoder SDK. Both use
lossless encoding, 4:2:0 chroma, full-range BT.709 matrix/primaries and the sRGB
transfer declaration. The signal has a luma gradient with deterministic fine
detail and smoothly varying chroma. No customer files, metadata extras, HDR,
animation or auxiliary images are included. The encoder is never shipped.

Run HEIC and AVIF to both JPEG and PNG five times each, interleaved, with a fresh
application model and worker for every command. The fixed limits are a ten-second
median and twenty-second maximum per input/target pair. Retain all five samples;
do not drop the first run or relax limits after observing a failure.

Time model/worker construction through direct-command completion, including
inspection, local trial admission, decoding, encoding, validation and named-copy
publication. No routine planner may open. Exclude fixture generation, source and
output audit hashing, independent output inspection and disposal. Input/payload
verification warms filesystem caches; these are fresh-worker, not cold-storage,
measurements. Record managed worker peak working set before post-export probing;
native job peaks and other environments are not measured.

Every result must reopen with the correct format, dimensions, precision,
upright pixels and ICC profile. An absent orientation tag or explicit top-left
tag is valid; any transform tag is refused. An independent Windows bitmap
decoder samples the authored signal at a 31-pixel stride, visiting every phase
of its fine detail. Full-range BT.709 arithmetic supplies expected RGB values;
the predeclared sampled RGB RMSE ceilings are 3 for PNG and 8 for JPEG. These
bounded signal checks do not establish general photographic fidelity. Verify
source bytes/timestamps, copy outcome, worker exit and scratch/publication cleanup.


Execution
---------

```powershell
python -B tools/heif-engine/Test-HeifPerformance.py --payload '<combined candidate>' `
  --encoder-sdk '<isolated fixture encoder SDK>' --decoder-sdk '<isolated decoder SDK>' `
  --cmake '<installed cmake.exe>'
```

The runner accepts only repository scratch/staging payloads with the selected
HEIF runtime, verifies the complete payload/inventory, builds the fixture encoder
and test host, and retains source, SDK, generated input and executable identities
under a fresh `.codex-temp/heif-performance/<guid>` directory. It executes the
candidate's existing worker without rebuilding or modifying that payload.
Inventory and identity checks run again after measurement. Final versioned
expanded-candidate replay remains required even if this isolated benchmark passes.


Retained harness attempts
-------------------------

Attempt `82ed8ac3355c4f00a9971704d2e40c8b` verified the candidate and generated
both fixtures, then stopped after successfully publishing its first copy. The
harness incorrectly required the PDF/audio result's engine-identity field,
which image publication does not populate. The complete payload is independently
identity-pinned by the runner; no production reporting behavior was changed.

Smoke `fd91053ae7ad4cd7b9506bbd590c6cce` and diagnostic
`ab5c6dbb9ee34a03a8ea70feae80baf5` then identified an overly strict orientation-tag
assertion: the correct upright JPEG has no orientation tag (reported as zero).
Its dimensions, precision, opacity and ICC profile match. The corrected check
admits absent or explicit top-left tags and retains independent pixel placement
verification. No performance or pixel-error threshold changed.

Smoke `de07429940d74563b40ec867882503dd` completes all twenty conversions and
their pixel/preservation/cleanup checks. It includes a 15.004-second HEIC-to-PNG
sample; no sample was discarded. All four groups meet the declared limits, but
this smoke is excluded from the full identity-verified benchmark below.


Measured baseline (2026-09-19)
------------------------------

Evidence `69a95e7608024fbfaf12a3358cca1784` completes all twenty fresh-worker
commands with unchanged source, fixture, SDK and executable identities. The
exact worker belongs to the [combined scratch HEIF payload](heif-production-payload.md)
at `.codex-temp/heif-payload-tests/a35b0137522a40ce8e4da4da7a763e29/combined`.
The payload inventory verifies before and after execution. This is not reserved
candidate 1.1.1 or a new formal version, and the installed app is untouched.

| Pair | Median seconds | Maximum seconds | Result |
| --- | --- | --- | --- |
| HEIC to JPEG | 3.150 | 3.564 | Pass |
| HEIC to PNG | 6.983 | 8.344 | Pass |
| AVIF to JPEG | 3.456 | 3.647 | Pass |
| AVIF to PNG | 7.297 | 7.639 | Pass |

All twenty named copies preserve the declared geometry, precision, opacity and
ICC profile. Sampled RGB RMSE is 0.2833 for PNG and 3.0971 or less for JPEG,
within the fixed 3/8 limits. Originals retain their bytes and write timestamps;
workers exit and publication/worker scratch is cleared. Managed worker peak
working set ranges from 406.3 to 508.8 MiB; this is not native job memory.
The machine is an Intel Core i9-9900K, sixteen logical processors, with
34,270,330,880 bytes of physical memory (approximately 32 GiB).

The fixture and test-host Release builds have zero warnings/errors. Three runner
boundary checks pass at
`.codex-temp/heif-performance-guards-be17454efe4643e5afa7a7783eb3a1fb`:
missing arguments, payloads outside repository scratch/staging, and existing
evidence-directory reuse. Human image review, Office timing and final combined
candidate acceptance remain separate gates.
The Release foundation host passes all 4,384 contracts at
`.codex-temp/heif-performance-foundation-cef429fdee1f4080a9fdcb64b41ce932`.
