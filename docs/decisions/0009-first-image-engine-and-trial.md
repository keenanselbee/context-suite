First Image Engine And Local Trial
==================================

Status: accepted; bounded conversion and local-trial acceptance matrix passed locally
Date: 2026-09-06

Trial duration and pricing are superseded by
[decision 0020](0020-seven-day-trial-and-pricing.md): seven days and $5 CAD.
Other accepted access and safety rules remain in effect.


Decision
--------

Implement the first real image-conversion slice using **Magick.NET-Q16-x64
14.17.1**, not AnyCPU, Q8, HDRI, or the OpenMP variant. Pin the package and resolved
dependencies in the private adapter project and record the actual native payload
and notices during implementation. Do not install a machine-wide ImageMagick.
Keep decoding, thumbnails, encoding, and semantic output validation in the
existing on-demand worker. Public core/UI must not acquire a codec dependency.

NuGet lists net8.0 and .NET Standard 2.0 assets compatible with the current .NET
10 project. Upstream describes Q16 as 16-bit per channel and HDRI as requiring
additional memory. Q16 is a project-specific precision choice, not a promise of
HDR support. Release 14.17.1 fixes an asynchronous image-read deadlock.
These are package/documentation checks, not a passed production codec test.

Before accepting the adapter, restore/audit the exact package, verify Windows x64
native loading in the built worker, inventory bundled dependencies/licenses,
and exercise actual PNG/JPEG/WebP fixtures. Treat third-party engine code as
third-party code, not proprietary source. Retain required redistribution notices;
do not infer that the wrapper license describes every bundled component.

[Decision 0010](0010-first-release-formats-and-curated-engine.md) refines the
first-release format target. Its reviewed curated native payload now replaces
the stock development native assets; see the
[integration evidence](../bmp-tga-and-engine-integration.md). The managed package
remains pinned, and this decision's PNG/JPEG/WebP and local-trial policies are unchanged.


Initial Media Policy
--------------------

- Accept still PNG, JPEG, and WebP detected from bounded file content, not merely
  extensions. Allow only verified input/output coders; deny external delegates,
  network/pseudo-format inputs, and indirect file reads. Apply resource limits
  and bounded worker termination. The process boundary is not a security sandbox.
- Begin with all six cross-format pairs. Same-format re-encoding belongs to
  Optimize and is reported as not applicable here; DDS representation conversion
  is a later explicit exception. Reject animation, HDR/gain-map content, and
  unsupported color models rather than silently flattening or misinterpreting them.
- Default JPEG quality to 90. Offer WebP lossy quality 90 or explicit lossless;
  PNG is lossless. Quality scales are engine/format-specific, not equivalent
  fidelity scores. Warn and require acknowledgement for lossy-to-lossy output.
- Preserve supported bit depth in PNG. JPEG/WebP 8-bit output from 16-bit input
  requires a visible precision-reduction warning and acknowledgement. Reject
  content requiring an unimplemented HDR or color transformation.
- Apply EXIF orientation to pixels and normalize the resulting orientation tag.
  Preserve valid supported ICC profiles and descriptive metadata by default.
  Preserve-mode cannot silently drop unrepresentable metadata; report the loss
  and require a changed/acknowledged policy before execution.
- Offer explicit descriptive-metadata removal, including location and embedded
  thumbnails, while retaining color interpretation required for correct display.
  Explain that this is not a forensic anonymization guarantee. Do not blindly
  strip ICC/gamma information along with EXIF/XMP. Reject conflicting or malformed
  color information until a safe tested interpretation exists.
- Require an explicitly selected matte and preview for transparency to JPEG.
  Preserve alpha for PNG/WebP. Never interpret changing a color-profile tag as
  performing a pixel color conversion.
- No resize by default. Permit an optional maximum dimension, preserving aspect
  ratio and never enlarging. Show resulting dimensions and any capability loss.
- Use the completed output-safety publisher, immutable confirmed plans, and
  source-preserving names. A preview neither publishes an output nor grants
  permission to overwrite. Replacement needs both saved permission and explicit
  per-batch consent; retain the verified platform/location restrictions.


Minimal Trial Admission
-----------------------

Decision [0013](0013-lossless-png-optimization.md) extends first-use admission to
the first confirmed valid conversion **or optimization**, using the same record,
duration and failure rules. Existing trial records are never reset by this feature.

This settles the local-trial portion of decision 0006, not paid activation:

- Three days means **72 elapsed hours**, measured using UTC, not calendar dates
  or active-use hours. Time zones and daylight-saving changes do not extend it.
- Start only when the user confirms Convert for a valid plan with at least one
  executable item, after required warnings/matte/output choices. Persist the
  start record successfully before admitting work. Opening the app, selecting
  files, analysis for planning, previews, and invalid/cancelled plans do not start it.
- Once admitted, execution failures or cancellation do not reset the clock.
  An admitted batch may finish after expiry; a later batch must pass admission
  again. Preview work is bounded and must not expose full converted files as an
  alternative to commercial operation admission.
- Store one small versioned per-user record separately from settings, containing
  start time and greatest observed UTC time. Use atomic saves and serialization
  for concurrent admission. Do not scatter hidden records or collect hardware IDs.
- Never move observed time backwards. Clamp small clock corrections; a rollback
  greater than five minutes relative to recorded/monotonic elapsed time makes
  new admission unavailable with a clock-correction message. Forward time counts
  toward expiry. Use a monotonic clock in-process and persist observations at
  admission; do not build a polling service or remote time authority.
- Missing storage means not started. Malformed, unsupported-version, unreadable,
  or unsavable existing trial data means unavailable, not a fresh trial. Deleting
  the record may reset a local trial; that accepted limitation does not justify
  invasive anti-piracy machinery. Offer no shipping reset/override switch.
- After expiry, settings, viewing existing results, and opening output locations
  remain available. New conversion is blocked with an honest status. Paid-key
  entry must not appear functional until Polar integration actually exists.
- Automated clocks/storage/access doubles stay in test assemblies and isolated
  directories. Production uses real time and the real trial record. No development
  bypass, fake paid status, or automatically renewed trial is shipped.

The application owns this policy and persistence; the worker receives only
already-admitted work, never trial files or license keys. Private production
commercial composition can provide the policy through public contracts. Live
Polar transport, paid offline grace, transfers, refunds/revocation, and access to
the future standalone Analyzer after expiry remain separate commercial work.
Do not market this incomplete activation flow as ready for paid distribution.


Consequences And Alternatives
----------------------------

Q8 is smaller but cannot retain 16-bit channel precision; HDRI adds cost without
an initial HDR requirement. A dedicated DDS engine follows rather than stretching
this adapter's advertised capabilities. A small local trial admits real work now
without a website account system or fake production access. Full paid activation
is still required before a commercial release.

Implement and verify the [image-conversion goal](../image-conversion-goal.md)
before reporting any of these media capabilities as shipped.


Sources
-------

- [Exact NuGet package](https://www.nuget.org/packages/Magick.NET-Q16-x64/14.17.1)
- [Q16, HDRI, initialization and resource behavior](https://github.com/dlemstra/Magick.NET/blob/14.17.1/docs/Readme.md)
- [14.17.1 release notes](https://github.com/dlemstra/Magick.NET/releases/tag/14.17.1)
- [Wrapper license](https://github.com/dlemstra/Magick.NET/blob/14.17.1/License.txt)
