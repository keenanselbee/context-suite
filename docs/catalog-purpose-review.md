Catalog Purpose Review and Reconciliation
=========================================

Revision **2026-09-12.1** reviews the remaining 34 catalog purposes: eighteen
3D/CAD/game records, eight development records, three system records and five
generic program/container/text records. Fifteen records receive clearer wording
or more useful references. Together with the earlier reviews below, this completes
a typical-use description review for the current **243 records**.

This closes the purpose-description inventory, not the entire catalog acceptance
gate. Every alias, exact variant, MIME identifier and detector still needs its own
evidence. A familiar suffix or description does not prove a selected file's actual
contents, safety, compatibility or available transformations. No parser, execution
permission or operation is added by this checkpoint.


Remaining purpose evidence
--------------------------

References were consulted on 2026-09-12. Descriptions are independently phrased.
Publisher specifications establish format intent; tool-author manuals establish
documented uses and supported distinctions, not a complete format specification.
No external database, source prose, sample, executable or reference-tree material
was imported.

| Catalog ID | Purpose or distinction reviewed | Evidence |
| --- | --- | --- |
| 3mf | Printing geometry and additional manufacturing properties | [3MF Consortium specification overview](https://3mf.io/spec/) |
| bethesda-archive | Resources bundled for Bethesda games; BSA/BA2 layouts are game-dependent | [BSA Browser author's format and game support](https://github.com/AlexxEG/BSA_Browser) |
| blend | Editable Blender scenes, objects, materials and animation projects | [Blender scene documentation](https://docs.blender.org/manual/en/5.0/scene_layout/scene/introduction.html) |
| collada | XML interchange of 3D scenes and assets | [Khronos COLLADA overview](https://www.khronos.org/collada/) |
| dwg | Native AutoCAD drawing data, with version compatibility constraints | [Autodesk drawing-format compatibility](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/AutoCAD-drawing-file-format.html) |
| dxf | CAD drawing interchange in text or binary form | [Autodesk DXF introduction](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-73E9E797-3BAA-4795-BBD8-4CE7A03E93CF.htm) |
| elf | Executable, relocatable, shared-object and core-file uses | [ELF header type definitions](https://gabi.xinuos.com/elf/02-eheader.html) |
| event-log | Windows diagnostic/event records | [Microsoft Windows Event Log overview](https://learn.microsoft.com/en-us/windows/win32/wes/windows-event-log) |
| fbx | Interchange of models, rigs and animation between creation tools | [Autodesk Maya FBX documentation](https://help.autodesk.com/cloudhelp/2022/ENU/Maya-DataExchange/files/GUID-18A2CDD7-3334-4FC1-A1B3-A308AD331BB2.htm) |
| gltf | Efficient delivery and loading of 3D assets; GLB binary packaging | [Khronos glTF specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html) |
| godot-resource | Godot scene/resource data, with text and binary representations | [Godot resource documentation](https://docs.godotengine.org/en/stable/tutorials/scripting/resources.html) |
| iges | Geometry exchanged between CAD systems | [NIST comparison of IGES and STEP](https://www.nist.gov/publications/introduction-iso-10303-step-standard-product-data-exchange) |
| java-class | JVM code and class metadata | [Oracle JVM class-file specification](https://docs.oracle.com/javase/specs/jvms/se25/html/jvms-4.html) |
| minidump | User-mode or kernel-mode diagnostic memory/state, with different dump types | [Microsoft dump-type definitions](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/dbgeng/nf-dbgeng-idebugclient4-writedumpfile), [minidump purpose](https://learn.microsoft.com/en-us/windows/win32/debug/minidump-files) |
| mz | DOS program/header evidence that does not alone identify a later executable format | [Microsoft PE layout and DOS stub](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format) |
| object-code | Linker input containing compiled code/data | [Microsoft object-file definition](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format) |
| ogg | Media container, distinct from an enclosed codec | [RFC 3533](https://www.rfc-editor.org/rfc/rfc3533.html), [existing audio review](catalog-audio-review.md) |
| ole | Several application-specific streams stored in one compound file | [Microsoft Compound File Binary specification](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-cfb/53989ce4-7b05-4f8d-829b-d08d6148375b) |
| pdb | Program symbols/debug information; molecular PDB is a separate meaning | [Microsoft PDB documentation](https://github.com/microsoft/microsoft-pdb), [molecular record review](catalog-data-review.md) |
| pe | Windows executable images, libraries and system components | [Microsoft PE specification](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format) |
| ply | Meshes and scanned point/range data, potentially including attributes such as color | [Stanford scanning repository's format explanation](https://graphics.stanford.edu/data/3Dscanrep/), [Blender PLY attributes](https://docs.blender.org/manual/en/4.5/files/import_export/ply.html) |
| python-bytecode | Cached compiled Python code, with version-dependent compatibility | [Python importlib documentation](https://docs.python.org/3/library/importlib.html) |
| static-library | Compiled objects or shared-library export references for linking | [Microsoft library inputs](https://learn.microsoft.com/en-us/cpp/build/reference/dot-lib-files-as-linker-input?view=msvc-170) |
| step | Engineering product geometry and related lifecycle data | [NIST STEP introduction](https://www.nist.gov/publications/introduction-iso-10303-step-standard-product-data-exchange) |
| stl | Triangulated surfaces used in CAD and 3D printing | [Blender STL geometry documentation](https://docs.blender.org/manual/en/5.0/modeling/geometry_nodes/input/import/stl.html), [STL importer/exporter](https://docs.blender.org/manual/en/5.0/files/import_export/stl.html) |
| text | Readable character data that alone does not establish an application's purpose | [Unicode plain-text discussion](https://www.unicode.org/versions/Unicode17.0.0/core-spec/chapter-2/) |
| unitypackage | Assets and associated metadata transferred between Unity projects | [Unity asset packages](https://docs.unity3d.com/Manual/AssetPackages.html) |
| unreal-asset | Unreal project content whose compatibility depends on engine version and references | [Epic asset overview](https://dev.epicgames.com/documentation/en-us/unreal-engine/assets-and-content-packs-in-unreal-engine), [versioning and references](https://dev.epicgames.com/documentation/unreal-engine/versioning-of-assets-and-packages-in-unreal-engine) |
| usd | Composed scenes/assets; text, binary and packaged forms | [OpenUSD introduction](https://openusd.org/release/intro.html), [format distinctions](https://openusd.org/release/usdfaq.html), [USDZ specification](https://openusd.org/release/spec_usdz.html) |
| valve-package | Packaged Source-engine game resources | [VPKEdit author's supported-format table](https://github.com/craftablescience/VPKEdit) |
| wasm | Portable compiled modules for WebAssembly execution environments | [WebAssembly goals](https://webassembly.org/docs/high-level-goals/), [binary format](https://webassembly.github.io/spec/core/binary/index.html) |
| wavefront | OBJ geometry and companion MTL material descriptions; object code is a separate suffix meaning | [Blender OBJ/MTL documentation](https://docs.blender.org/manual/en/3.3/files/import_export/obj.html), [object-file definition](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format) |
| windows-resource | Compiled UI resources linked into Windows applications | [Microsoft resource workflow](https://learn.microsoft.com/en-us/windows/win32/menurc/about-resource-files) |
| windows-shortcut | Target-location and launch-setting references | [Microsoft Shell links](https://learn.microsoft.com/en-us/windows/win32/shell/links) |

Changed IDs: `3mf`, `bethesda-archive`, `blend`, `dwg`, `dxf`, `elf`, `fbx`,
`iges`, `minidump`, `ply`, `stl`, `unreal-asset`, `valve-package`, `wavefront`
and `windows-shortcut`. ELF now mentions crash data; OBJ and MTL roles are explicit.
The dump reference covers the broad existing Crash dump record rather than only
user-mode minidumps. Accurate remaining descriptions are retained.


Inventory reconciliation
------------------------

The following review groups form a disjoint partition of the current catalog IDs.
Counts refer to records, not extensions or supported operations. The earlier
static-library alias correction and molecular-PDB cross-reference do not count
again here. The reconciliation compares the review tables with actual catalog
IDs, rather than inferring completeness from a sum of historical test counts.

| Review | Records | Scope |
| --- | ---: | --- |
| [Common image headers](image-header-analysis.md) | 4 | bmp, gif, jpeg, webp |
| [Remaining images](catalog-image-review.md) | 26 | Other Image-family records |
| [Audio purposes](catalog-audio-review.md) | 18 | Audio family |
| [Document purposes](catalog-document-review.md) | 31 | Document family plus pptx and xlsx |
| [Archive/package purposes](catalog-archive-review.md) | 29 | Archive or package and Archive or packaged document families |
| [Source/configuration purposes](catalog-source-code-review.md) | 49 | Source code and Configuration or system data families |
| [Data purposes](catalog-data-review.md) | 37 | Data family |
| [Video purposes](catalog-video-review.md) | 10 | Video family |
| [Font headers](font-header-analysis.md) | 5 | truetype, opentype, font-collection, woff, woff2 |
| This review | 34 | The explicit ID table above |
| Total | 243 | No missing or duplicate IDs in this partition |


Retrieval and remaining acceptance
----------------------------------

The original FBX help landing page supplied no readable body. The old PLY page
redirected to a crawler notice, and the old Unreal article could not be retrieved.
Their replacements support concrete uses. Autodesk's broad product page was too
large to retrieve; the replacement drawing-format article was available through
primary search text. Blender scene, OBJ, PLY and STL evidence was also available
through primary indexed manual text; direct opens of several Blender pages failed.
The IGES purpose uses NIST's authored comparison, not a claim that the entire
older IGES specification PDF was reviewed.

Valve's developer-wiki pages returned 403. The BSA Browser and VPKEdit authors'
own documentation supplies implementation-use evidence for the two game-archive
records. These are not Bethesda/Valve specifications or permission to copy those
implementations. Exact game versions, layouts and alternate suffix meanings still
require separate review. No game archive was opened or extracted.

The historical [retrieval audit](catalog-source-review.json) remains historical;
this checkpoint does not rewrite it as factual acceptance. Complete alias/variant,
MIME and detector provenance remains tracked in [coverage](file-type-coverage.md).
Analyze does not execute programs, import projects, follow shortcuts or validate
the safety of recognized content.

An independent JSON delta permits only `commonUses`, `source` and the revision
change. All 243 IDs, names, families, aliases, MIME fields, text flags, detectors
and capability boundaries are preserved. The local evidence is recorded in
`.codex-temp/catalog-remaining-delta.json` and
`.codex-temp/catalog-purpose-reconciliation.json`. The Release foundation run
passes **2,367 contracts**, and **104 documentation files**, source-boundary,
theme-policy and both repositories' whitespace checks pass. The first foundation
run lost its session without a terminal result; after confirming its process was
absent, a fresh run completed successfully. Its log is
`.codex-temp/catalog-remaining-foundation-retry.log`, with exit code 0 recorded in
`.codex-temp/catalog-remaining-foundation-exit.txt`. This description review makes
no new worker, production-stage or manual-acceptance claim.
