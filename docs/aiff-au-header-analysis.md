AIFF and AU Header Analysis
===========================

Analyzer version `aiff-au-headers-1` adds likely content identification and bounded
header facts for AIFF, AIFF-C and NeXT/Sun AU files. These use the existing 64 KiB
read-only prefix and need no worker, decoder, license admission or external
resource. Recognition adds no conversion or optimization capability.


Reported declarations and limits
--------------------------------

AIFF identification requires `FORM` plus `AIFF` or `AIFC`. At most 256 chunk
headers are visited, respecting sizes and odd-byte padding. Container/chunk
extents must fit the actual file. A complete bounded Common Chunk supplies
channels, original sample bits, decoded frames per channel, raw 80-bit rate and
approximate numeric rate/timing. Only AIFF and AIFF-C `NONE` map to PCM here;
other compression codes remain uninterpreted, using hex for nonprintable bytes.
Compression-name text is not displayed.

Positive normalized 80-bit rates representable as finite positive doubles include
fractional rates. The complete ten-byte value is retained; numeric rate/timing
is labeled approximate and derived. Zero, negative, nonfinite, denormal or
unrepresentable rates leave those facts unavailable while retaining other Common
declarations. Original precision outside 1 through 32 bits leaves optional
Common facts unavailable. This is an implementation limit, not an assertion that
all later AIFF-C extensions are invalid.

AU uses the `.snd` signature and a 24-byte big-endian header. It reports raw
encoding, rate, channels and audio offset/extent. Offsets below 24, out-of-file
extents and zero rate/channels leave properties unavailable. An offset of exactly
24 is accepted without requiring annotation bytes. The unknown-size sentinel
derives extent from actual file length and labels it derived. Declared extents
do not absorb trailing bytes. Codes 2 through 7 identify signed integer PCM at
8/16/24/32 bits or IEEE float at 32/64 bits. Only these fixed-width encodings
derive frames/timing, requiring whole-frame alignment and overflow-safe duration
arithmetic. Other encodings keep raw identifiers and unavailable timing/precision.
DSP-related identifiers are never executed.

These readers do not validate samples, metadata, loops, instruments, compression
or playback. AIFF-C version chunks and required sound-data relationships are not
checked. Missing/incomplete optional Common Chunks retain likely identity with
warnings; duplicate or contradictory inspected declarations discard Common facts.
Later uninspected chunks and trailing bytes are qualified. File-declared lengths
cause no unbounded allocations, reads or loops.


Sources
-------

Fields follow Apple's [AIFF 1.3 specification](https://www.mmsp.ece.mcgill.ca/Documents/AudioFormats/AIFF/Docs/AIFF-1.3.pdf)
and its [AIFF-C draft copy](https://nagasm.org/ASL/sound05/aifc.pdf), with the latter
not treated as exhaustive coverage of later compression extensions. AU fields
follow the [Oracle/Sun manual](https://docs.oracle.com/cd/E19253-01/816-5174/6mbb98ucf/index.html);
numeric encoding identifiers were checked against
[Sun's retained definitions](https://raw.githubusercontent.com/illumos/illumos-gate/master/usr/src/head/audio/au.h).
Only factual field meanings inform the independently authored implementation.
No source code, SDK, reference binaries, document text or runtime is imported.


Verification
------------

All **2,182 foundation contracts pass**, including 50 new checks: actual
source-preserving reads under misleading filenames, every fixture truncation,
partial Common Chunks, fractional/unsupported rates, six AU PCM codes, unknown
encodings, extreme lengths, duplicate/odd/over-budget chunks and 500 deterministic
mutations. These are header contracts, not listening or independent encoder
interoperability acceptance. Log: `.codex-temp/aiff-au-foundation-final.log`.

The initial run's new audio checks passed, then an existing image test failed
when an async continuation accessed an encoder created on another thread. Its
test-only fix creates each encoder immediately before synchronous use. The failed
full run remains at `.codex-temp/aiff-au-foundation.log` and is not counted as a pass.

Fresh combined stage `artifacts/production-staging/b87b75ba08f84445b831ff93d40e2da6`
builds with zero warnings/errors and passes image/audio/PDF candidate, notice,
dependency and file-inventory checks. Log:
`.codex-temp/aiff-au-production-b87b75ba08f84445b831ff93d40e2da6.log`.
The build uses `-SkipShell`; no fresh native-shell, installed, visible,
screen-reader or real-worker workflow acceptance is implied.
