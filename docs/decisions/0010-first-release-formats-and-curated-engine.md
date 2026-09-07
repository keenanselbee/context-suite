First Release Formats And Curated Image Engine
==============================================

Status: accepted; curated development packaging and bounded BMP/TGA conversion implemented
Date: 2026-09-07


Context
-------

The first image release should offer useful general-image and game-texture
workflows without adopting every capability bundled with an upstream engine.
The stock Magick.NET native payload includes dependencies beyond our enabled
formats. Enabling those formats would not remove redistribution obligations.
Conversely, aggressively stripping every unused built-in coder would add
maintenance without necessarily simplifying compliance.


Decision
--------

Target PNG, JPEG, WebP, DDS, TGA, and BMP for the first image release.
The bounded PNG/JPEG/WebP/BMP/TGA conversion slice is now implemented and
verified. DDS remains planned. This target is not a claim that every variant, conversion pair, or
Analyze/Optimize operation is supported.

- Retain Magick.NET Q16 and the current worker/adapter architecture for general
  images. Keep PNG/JPEG/WebP behavior from decision 0009 unchanged.
- Add tested BMP/TGA input and output as bounded general-image capabilities.
  Define supported headers, compression, orientation, alpha, bit depth, and
  metadata/color-loss policies before advertising each capability.
- Use a separately selected and pinned DDS toolchain. DirectXTex remains a
  candidate, not an accepted production dependency. Settle BC formats,
  linear/sRGB conversion versus reinterpretation, mip generation, texture
  structures, HDR, and validation policies in its own implementation brief.
- Prioritize a curated native build aligned with this release target. Retain
  ImageMagick/Magick.Native, PNG/zlib, the required JPEG variants, WebP, and
  Little CMS, and libxml2 (required by the verified XMP preservation path).
  Keep useful built-in BMP/TGA support; do not add a second engine just for it.
- Exclude unused H.264/HEVC, RAW, SVG/text-rendering, and liquid-rescaling
  dependency families from the proposed native payload. A runtime coder policy
  is still required but is not evidence that dependency code is absent.
- TIFF, GIF, ICO, and AVIF are later candidates, not first-release requirements.
  HEIC/HEIF, camera RAW, and SVG import require separate demand, capability, and
  redistribution decisions. Video and documents remain outside initial scope.

This refines decision 0009's release-packaging direction without changing its
PNG/JPEG/WebP media/trial policy. The stock engine was retained until the isolated
candidate passed the acceptance criteria below and its integration was approved. No new formats are
enabled merely by compiling their coders.


Bounded Prototype Acceptance
----------------------------

Build and evaluate one isolated curated Windows x64 Q16 native candidate using
pinned upstream sources and a small build/link overlay. Do not change codec
algorithms, create a separate application edition, or install ImageMagick
machine-wide. Prefer retaining the existing managed Magick.NET package/API.

Acceptance criteria:

1. Record source revisions, toolchain, dependency inputs, applied patches,
   license texts, and output hashes in a repeatable build recipe.
2. Inspect generated configuration and linker evidence to establish which
   libraries enter the binary. DLL imports alone cannot inventory static code.
3. Load the candidate through the existing adapter in isolated test output and
   pass the existing engine, worker/integration, and conversion-UI contracts.
   Use isolated trial data; do not replace the working production output.
4. Check BMP/TGA coder availability as preparation only. Application-level
   support remains pending its own plans, fixtures, and validation contracts.
5. Define packaging checks that reject the stock native payload or an unexpected
   dependency set. Preserve correct upstream attribution and required notices;
   determine any remaining source/rebuild obligations from actual components.
6. Report patch size, repeat-build evidence, maintenance cost, and unresolved
   compliance issues before promoting the candidate into production packaging.

Stop and reassess if codec patches, a large native fork, or changes to the
application architecture become necessary. Retaining the stock bundle remains
a fallback only with a concrete compliance plan; prototype failure does not
clear it for distribution. License terms and any remaining patent questions
must be resolved before distributing the application, including a free trial.


Consequences
------------

The product gains a focused texture workflow rather than a format-count target.
Dependency maintenance becomes explicit, but a curated build adds work on each
upstream update. Existing source safety, trial, and worker boundaries remain.
Third-party notices and any required source/rebuild materials must be
distributable separately from proprietary application implementations.

The isolated prototype now builds and passes the existing engine, integration
and conversion-UI contracts, a loaded-module/coder probe and negative packaging
guards. A fresh second build passes engine contracts, but is not byte-identical.
See the [evaluation report](../curated-engine-prototype.md) for the XML finding,
exact evidence and maintenance cost. The subsequent approved
[production-development integration](../bmp-tga-and-engine-integration.md) now
uses the hash-pinned curated engine and passes bounded BMP/TGA codec, worker and
UI contracts. NuGet native fallback is disabled. Customer distribution still
requires the [release redistribution review](../release-redistribution.md).


Related Records And Evidence
---------------------------

- [Initial engine and trial policy](0009-first-image-engine-and-trial.md)
- [Converter scope and DDS policy boundaries](../converter-design.md)
- [Completed PNG/JPEG/WebP slice](../image-conversion-goal.md)
- [Pinned Windows native build](https://github.com/dlemstra/Magick.Native/blob/77c935e38d379b22d981770a91c466c16cba148b/build/windows/build.cmd)
- [Pinned native link list](https://github.com/dlemstra/Magick.Native/blob/77c935e38d379b22d981770a91c466c16cba148b/src/Magick.Native/Stdafx.h)
- [Dependency-driven Windows configuration](https://github.com/ImageMagick/Configure/blob/2026.08.23.0743/src/Configs.cpp)
