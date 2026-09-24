DDS And Palette Source Review
=============================

Status: engineering source/notice mapping completed on 2026-09-23 for the
selected binaries. No native engine was rebuilt. This does not close Microsoft
tool/runtime eligibility, commercial adoption or final-candidate acceptance.


DDS inputs
----------

The [DDS source record](../tools/dds-engine/source-review.json) binds all 204
files in the retained DirectXTex source archive to the unchanged source cache.
The retained project compiles 17 upstream source files, and its compiler read
tracker identifies seven additional upstream headers. All have Microsoft MIT
headers. The link map's timestamp matches the selected DLL's PE timestamp;
the existing DDS verifier still confirms its source, build definition, archive,
license and runtime hashes.

The selected CMake switches disable GPU support, OpenMP, tools, samples, shared
DirectXTex and auxiliary OpenEXR/JPEG/PNG dependencies. The map's object-library
labels are DirectXTex, MSVCRT, msvcprt, uuid and vcruntime. This records the
observed build inputs, not a conclusion about Microsoft redistribution rights.

The read tracker also identifies seven DirectXMath headers from Windows SDK
10.0.26100.0. They declare version 3.19 and Microsoft MIT terms. Their current
bytes are retained alongside the pinned public February 2024 source archive.
Five match the public versions after line-ending normalization; `DirectXMath.h`
and `DirectXMathVector.inl` have supplier differences. The public release is
therefore not described as an exact source match for the SDK variant.

The DirectXMath and DirectXTex upstream LICENSE files are byte-identical.
The product acknowledgment now explicitly names DirectXMath and points to that
existing bundled Microsoft MIT text. No extra notice file or changed DLL is
needed. The [upstream release history](https://github.com/microsoft/DirectXMath/releases/tag/feb2024)
identifies the 3.19 release; local header hashes and retained tracker paths are
the evidence for the selected SDK inputs. These newly captured SDK hashes are
not a historical checksum receipt or a reproduced native build.


Palette inputs
--------------

The [palette source record](../tools/palette-engine/source-review.json) binds all
20 upstream ExoQuant archive files and the ten upstream modules used by the
retained direct-rustc composition. It does not enable the optional rand feature
or development lodepng dependency. The authored histogram and color adapters
remain private; the public companion contains no private implementation source.

All selected palette runtime and notice hashes still match. Three authored
files exactly match the retained candidate's source hashes. The fourth,
`main.rs`, differs only by one final newline: appending exactly one LF byte to
the current file reproduces the recorded pre-commit SHA256. Both identities and
that exact transformation are recorded. No source or binary pin was changed to
hide the discrepancy, and no rebuild was necessary.

The existing ExoQuant MIT license and Rust 1.90.0 standard-library notice retain
their selected hashes and are included in the companion. The compiler itself,
private adapters, SDK binaries and proprietary bridge are not distributed in it.


Review companion and verification
---------------------------------

The retained archive is
`.codex-temp/base-component-review/2702f753b7a84302a1cb84d06b6760f3/dds-palette-source-notices.zip`.
It contains four source archives (DirectXTex, public DirectXMath, the seven SDK
DirectXMath headers with their license, and ExoQuant), three existing notice
files and two component records. Size: **1,038,151 bytes**. SHA256:
`E7522EFBA6ED8CB33F39D0F0FA41A994B7BDAC4F40BB0E8544878C328EDDEC81`.

`python -B tools/release/Test-BaseComponentSources.py <archive>` verifies exact
membership, archive and individual source hashes, all seven SDK headers, the
component records and shared license/notice bindings without extracting or
executing anything. The existing DDS and palette payload verifiers also pass.
The source review did not execute either helper or change the staged engines,
reserved packages, installed app, SDK or compiler installations.

This archive is ready for inclusion in the eventual consolidated source review
pack. Recipient-facing source delivery, product terms and Microsoft eligibility
remain owner decisions under the [redistribution checklist](release-redistribution.md).
Other document-engine component reviews remain separate.
