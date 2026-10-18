Image Expansion Owner Review
============================

Status: unsigned 1.2.5 installed locally, 2026-10-09. Native Explorer visual review, full lifecycle,
representative content and owner acceptance remain pending.

Open the [local interactive review](../.codex-temp/image-expansion-review/index.html)
in a browser. It uses decoded 16, 20, 24 and 32-pixel frames from the compiled
light and dark `.ico` files in `.codex-temp/svg-shell-final/Assets`, covering all
17 Convert destinations, Settings and the Convert root. The
[asset manifest](../.codex-temp/image-expansion-review/manifest.json) records
the exact compiled source and SHA-256 of every copied review frame. The
[candidate binding](../.codex-temp/image-expansion-review/candidate-icon-binding.json)
confirms that all 38 source icon files and 152 review frames match the exact
1.2.5 production stage `4ceab4687b07416aa76083d63f3e7c31`. Word retains the
selected W cutout; ICO and AVIF extend the same Compact monochrome vocabulary.
This HTML
is a menu and dialog representation. It does not prove installed Explorer
appearance, accessibility or command behavior.

The menu examples follow the current filename allowlist. A destination appears
when at least one selected filename is eligible; each selected file is validated
later by the application. Inspect the complete icon sheet as well as the
filtered examples, including SVG to PNG, TIFF to PNG/JPEG/PDF, PDF to PNG/Word,
and image to ICO/AVIF. The SVG size section depicts the focused Automatic or
custom-width choice and the place where worker-reported font substitutions
appear before conversion. It does not add a product setting.

Selected paired fixtures in the review are copied from named actual-worker
outputs: a 20 by 10 PNG and its ICO, page 1 of a two-page TIFF and its PNG,
and a simple SVG and its 48 by 24 PNG. The AVIF pair is an authored PNG and the
actual line-art AVIF whose input/output hashes match
`.codex-temp/avif-benchmark-95-1024-lineart/results.json`. ICO frames, the TIFF
source preview and the AVIF preview were decoded from the copied output or
source files. These procedural fixtures support visual inspection; they are
not representative customer material.

Owner acceptance checklist
--------------------------

- Pending: inspect the installed Convert submenu in native Explorer in light,
  dark and Windows contrast themes. Check all 17 icons and Settings at 16, 20,
  24 and 32 pixels, icon/text alignment, separators, command order and missing
  asset fallback.
- Pending: inspect 100%, 125%, 150% and 200% DPI, keyboard navigation, visible
  focus, screen-reader names and contrast. Check the actual SVG size dialog at
  small work areas and after a font substitution notice.
- Pending: convert representative photographs, transparent artwork, ICC-tagged
  images, real multi-page TIFFs, SVGs with fonts and filters, and game assets.
  Compare output dimensions, alpha edges, color, page order and ICO padding in
  the target applications and games.
- Pending: verify installed cancellation, selective retry, source preservation,
  output naming and per-file problems with mixed selections.

The local page and selected assets are temporary review artifacts under
`.codex-temp/image-expansion-review/`. Rebuild them from the compiled assets and
evidence using its `build-assets.py` if the candidate changes. Do not treat the
procedural preview or browser-rendered SVG source as native rendering evidence.


Candidate identity
------------------

The unsigned archive is retained at
`artifacts/release-candidates/c2f67f2d8f83417c9ad087b65ae4cad1/ContextSuite-win-x64-unsigned.zip`.
It contains 280,273,558 bytes; SHA-256 is
`7B8452405CD3A788F23C94477F5599928FDFCFC71F14A6C9543532EF5B796218`.
The canonical packaging workflow verified its fresh extraction, engine
identities, exact payload membership and 281 local notice links. Its manifest
records dirty public/private source honestly. This is an internal review
artifact, not a signed or commercially approved release. Reserved baseline
1.2.4 remains unchanged. Installation and Explorer registration were not part
of the engineering goal; the owner subsequently requested the local update below.


Local installation, 2026-10-09
-----------------------------

The verified 1.2.5 stage was copied without rebuilding to
`artifacts/production/Release`. All 4,257 inventoried files match their recorded
hashes, and the three local Explorer identities report version 1.2.5.0 with
healthy packaged COM activation. The installed root icon paths resolve to the
new payload. Settings, trial and license files retain their pre-install hashes.
The previous 1.2.3 payload and manifests are retained at
`artifacts/local-update-backups/20261009-172321-1.2.3`.

The installation receipt is `.codex-temp/install-125-receipt.json`; the log is
`.codex-temp/install-125.log`. This uses the existing local development
registration workflow, not a signed Inno customer installer or full lifecycle
acceptance. The installed executable passes actual WPF startup, repeated
activation, request consumption and worker cleanup. Its installed worker passes
128 workflow checks: ICO 65, AVIF 33, TIFF 19 and SVG 11, with unchanged user-state
hashes. Results are in `.codex-temp/installed125-verification/results.json`.
Native visual, accessibility and representative-content checks
above remain pending.

Owner-requested cleanup of the previously misplaced 26 test files and empty
task directories was attempted separately. Automatic approval review rejected
their relocation/removal as "blocked by policy" before execution. They remain
in their previous locations; no cleanup bypass was attempted. The review pack,
reserved artifacts and rollback payload are retained intentionally.
