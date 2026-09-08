Bounded PNG Precision Presets
=============================

Status: accepted and implemented locally; interactive/release acceptance pending
Date: 2026-09-08

Decision
--------

The user approved the [expanded visual evaluation](../png-quantization-improvements.md)
and continued implementation. Add Balanced and Smallest to the existing Optimize
planner. Lossless remains the default every time it opens. Lossy selection is a
per-batch choice, not a saved default. Changing presets clears replacement consent.
Explorer continues to use Choose preset; no new quick actions.

- `png-rgb7-preserve-v1` (Balanced): round visible RGB samples to 128 levels per
  channel including endpoints. Maximum absolute sample change: 1 out of 255.
- `png-rgb6-preserve-v1` (Smallest): 64 levels per channel; maximum change: 2.
- Both store ordinary 8-bit PNG samples, with no dithering, resizing, palette
  creation, alpha reduction, orientation normalization or color transform. Alpha
  and RGB beneath fully transparent pixels stay exact. Banding is possible;
  neither preset promises invisible changes or a universally smallest file.
- Admit only non-interlaced 8-bit RGB/RGBA PNGs up to 16,000,000 pixels, also
  satisfying existing parser/decoder/color admission. Untagged RGB is interpreted
  as sRGB for measurement. Embedded ICC (including textual ICC), indexed/grayscale,
  tRNS keys, sBIT, palettes and unknown metadata require Lossless or remain
  unsupported. The lossy chunk allowlist is IHDR/IDAT/IEND, sRGB/gAMA/cHRM,
  pHYs/tIME, tEXt/zTXt/iTXt and eXIf. Existing profile, gamma, animation, HDR and
  provenance checks still apply; this allowlist does not bypass them.
- Preserve every admitted non-IDAT chunk byte-for-byte and retain the original
  IHDR. Never strip unsupported metadata, including Afterburner's undocumented
  unsafe-to-copy fdEC chunk. Research removal was not production authorization.
- Apply the reviewed integer transform to independently unfiltered samples.
  Serialize rows using .NET's existing zlib support, then run pinned oxipng
  cleanup. Magick.NET remains the admission/reopen decoder. Avoid native
  quantization/re-palettization and metadata rewriting; no new dependency,
  reference asset or paid quantizer is introduced.
- Verify final samples against the prescribed transformed samples, then check
  alpha/hidden RGB and the sample-error bound. Retain research quality floors at
  full resolution on sRGB code values, composited over black and white: Balanced
  RMSE <= 2, worst 8x8 tile RMSE <= 6, mean tile SSIM >= .98; Smallest <= 4,
  <= 12, >= .95. No weakened retries or alternate presets.
- A lossy candidate must be strictly smaller than both the original and a
  validated lossless baseline. Otherwise return Unchanged and publish nothing;
  do not silently substitute a lossless output. The user may choose Lossless.
- Keep one sequential worker and the existing outer 120-second deadline for the
  complete request, not per encoding. The two sequential oxipng runs retain
  their existing child limits. Managed row, transform and metric loops check
  cancellation. The child executable receives no source/output paths.
- Reuse trial admission, immutable plans, safe copies, collision naming and
  explicitly consented transactional replacement. Results identify the selected
  lossy policy; Unchanged contributes no savings.

Consequences
------------

Precision reduction is simpler and more tightly bounded than the tested palette
searches. Retaining the original representation can save less than the research
writer's automatic RGB/RGBA choice, especially on opaque RGBA inputs. Research
percentage savings are not production guarantees.

Public facts carry a separate lossy-eligibility reason, defaulting to blocked
until probed. The private worker re-probes before writing. Build public and private
changes together; compatibility with old private binaries is not claimed.

See [implementation evidence and remaining checks](../png-lossy-presets.md).
