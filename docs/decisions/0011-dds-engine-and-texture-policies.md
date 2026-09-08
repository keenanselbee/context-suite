DDS Engine And Texture Policies
===============================

Status: accepted; bounded implementation and local acceptance completed
Date: 2026-09-07

See [scope, evidence and remaining release gates](../dds-conversion-goal.md).

Use a pinned Microsoft DirectXTex CPU build through a narrow native bridge loaded
only by the existing sequential worker. Keep the public C# DDS parser independent
of that bridge. The application owns confirmation, trial admission and output
publication. No engine loads in Explorer or the WPF process. No extra service,
worker pool, GPU requirement or general-purpose native command interface.

Initial conversion scope is ordinary 2D textures and their mip chains: BC1/DXT1,
BC2/DXT3, BC3/DXT5, BC4/BC5 (including explicit signed variants), BC7 and selected
R8, RG8, RGBA8/BGRA8 uncompressed representations. BC6H/HDR conversion and
cube/array/volume conversion are deferred. Analysis may report these structures;
conversion must reject them instead of flattening. Single-image export explicitly
selects a mip; it never silently discards an entire structure.

Source interpretation, texture purpose and destination encoding are separate.
Suggest sRGB for color/albedo; never apply color transfer to normals, masks,
packed data or alpha. Legacy DDS does not declare sRGB usage, and an UNORM storage
format alone does not prove authored linear color. Require a source interpretation
before color-dependent operations when uncertain. Typeless formats need an explicit
supported typed interpretation; otherwise conversion rejects them.

Normal conversion transforms pixel values to preserve the selected appearance.
Advanced reinterpretation changes only the declaration and warns about appearance.
There are no sRGB BC4/BC5/BC6H targets. Channel mapping, discarded channels, BC1
cutout threshold, alpha loss and lossy recompression must be explicit. Never imply
that BC1-to-BC7 restores detail. No silent tone mapping or HDR quantization.

Preserve existing mip levels by default for DDS inputs, converting each level
independently as needed. Preserve compressed bytes when no pixel/format change
is necessary. For new game textures generate a full chain to 1x1; UI textures may
choose one level. Resize requires an explicit regeneration choice. Initial area/box
filtering is linear-light for color, alpha-aware for transparency and normalized
vector filtering for normals. Cutout coverage has an explicit threshold. Clamp
is the default edge mode; wrap requires an explicit tileable choice.

DX10 headers are the default. Explicit legacy output is available only where it
can represent the chosen format; disclose unavailable color/alpha declarations.
Never silently downgrade a modern format to satisfy an old header. Do not resize
or round texture dimensions merely to satisfy compression alignment.

Pin source revisions, native hashes, build inputs and notices before deployment.
DirectXTex is MIT licensed; retain its notice and inventory actual dependencies.
The existing redistribution checklist still applies. First prove codec semantics,
then enable each capability through worker, planner and publication tests.

References: [DirectXTex](https://github.com/microsoft/DirectXTex),
[compression](https://github.com/microsoft/DirectXTex/wiki/Compress),
[mip generation](https://github.com/microsoft/DirectXTex/wiki/GenerateMipMaps),
[DDS structure](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-header-dxt10).
