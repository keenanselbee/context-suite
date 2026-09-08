Lossless PNG Recompression
==========================

Status: accepted for bounded implementation; customer release clearance pending
Date: 2026-09-08

[Decision 0014](0014-png-precision-presets.md) subsequently extends the planner
with bounded lossy presets; this lossless policy and its preservation rules remain unchanged.

Decision
--------

- Use the independently downloaded upstream oxipng 10.1.0 Windows x64 executable,
  with archive, executable and license hashes pinned in
  [production.json](../../tools/png-engine/production.json). This is a selected
  version, not a claim to track the newest upstream release. No PATH lookup,
  runtime downloads, reference binaries, stock fallback or arbitrary arguments.
- Policy `png-lossless-preserve-v1` uses level 2, one thread, no reductions,
  existing interlace mode, no alpha optimization and no metadata stripping.
  The initial product admits non-interlaced PNG only. Grayscale/indexed
  1/2/4/8-bit and applicable grayscale/RGB/alpha 8/16-bit variants are covered.
- The existing bounded image probe and decoder remain admission checks. Their
  color/profile and malformed-input restrictions still apply. Reject animation,
  HDR/gain maps, interlacing, unknown critical/unsafe structural chunks, offset
  metadata (`iDOT`) and provenance-bearing `caBX`, rather than silently changing
  their meaning. Later support requires new fixtures and an explicit policy.
- Permit only IDAT recompression. Independently check identical IHDR, palette,
  transparency and unfiltered packed sample bytes, including hidden RGB and all
  precision bits. Copy every original non-IDAT chunk back unchanged and reopen
  the final PNG. Do not normalize orientation or color, reorder the palette,
  strip metadata, resize or reduce bit depth.
- The private worker invokes the encoder over bounded stdin/stdout, never with
  user media/output paths. A Windows kill-on-close job owns its lifetime and
  limits encoder process memory to 1 GiB. A 90-second child deadline and existing
  120-second application deadline bound work; the encoder's 60-second setting
  is advisory, not relied upon for forced interruption. Raw input remains
  bounded to 320,000,000 bytes, file streams to 128 MiB, stderr to 64 KiB.
- Reuse immutable Optimize settings, application trial admission, safe
  reservations, source fingerprints, semantic digests, publication and recovery.
  Equal/larger results are Unchanged, with no extra output. Copy names are
  `name - Optimized.png`, then `(2)` etc. Replacement requires existing settings
  permission, per-batch confirmation and the existing native platform allowlist.
- Opening the planner does not start the trial. The first confirmed executable
  conversion **or optimization** starts the same 72-hour trial; existing trial
  records are not reset. This extends decision 0009 to the new transformation.
  Once admitted, a batch can finish after expiry. Analyze remains separate.
- Retain the existing Explorer `Optimize > Choose preset...` activation, now
  opening a single fixed lossless planner. Add an in-app PNG picker and retry
  support. Mixed selections show unsupported files; no hidden format conversion.
  Dedicated direct Lossless quick actions and lossy presets remain later work.

Alternatives And Consequences
----------------------------

A dedicated optimizer avoids treating ordinary image conversion as lossless
optimization. Reusing Magick.NET alone would still require proving sample and
metadata preservation across its re-encoding choices. Writing a new compressor
would add maintenance without improving the product boundary.

Preserving representation and all original metadata can save less space than
palette/depth reduction or stripping. That is intentional: this first policy has
a stronger invariant than visual equivalence. No promise of maximal compression
or a smaller result for every input is made.

Upstream documents its CLI flags, including the fact that alpha optimization can
alter hidden RGB and some chunks may change during optimization; see the
[pinned CLI definition](https://github.com/oxipng/oxipng/blob/v10.1.0/src/cli.rs).
The product's independent validation and metadata restoration enforce our
stricter policy. The upstream executable/license pins and notices are engineering
evidence, not a complete transitive redistribution review.

See [implementation and acceptance](../png-optimization-goal.md) and the separate
[commercial release gates](../release-redistribution.md).
