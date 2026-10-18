Convert Format Icons Plan
=========================

Status: implementation and initial native verification on 2026-10-08 under the
owner-approved [icons and WEM goal](wem-conversion-goal.md). Renderer, shell
mapping and packaging checks are updated; installed assets are unchanged.

Objective
---------

Give every currently supported Convert output a distinct, readable icon that
extends the existing Compact monochrome direction. Keep the format label beside
the icon: symbols help scanning, but cannot universally communicate codecs.

Current implementation
----------------------

[ShellCommand.cpp](../src/ContextSuite.Shell/ShellCommand.cpp) declares 15 output
titles. Its `GetIcon` now selects a destination-specific icon for each action
and a slider glyph for Settings, falling back to the tool icon when absent.
[ConversionTargets.h](../src/ContextSuite.Shell/ConversionTargets.h) filters
targets by selected filename hints. Preserve that filtering and target order.
An icon is a destination identity, not a promise that every selected input works.

[CompactIcons.cs](../tools/CompactIcons.cs) owns the existing original 24-unit
solid glyphs. It renders transparent monochrome assets with eight independently
rasterized 32-bit DIB frames: 16, 20, 24, 32, 40, 48, 64 and 128 pixels.
The shell chooses the light or dark glyph from Windows appearance and contrast
menu text. [The style guide](style-guide.md) owns this visual direction.

Scope and proposed vocabulary
-----------------------------

The initial deliverable is 14 output glyphs plus one Settings glyph. The approved
WEM extension adds a hexagonal sound glyph and appends WEM after those outputs.
Preserve the
existing Analyze, Convert and Optimize root artwork. HEIC/HEIF/AVIF, AIFF/WMA,
XLSX and PPTX are supported inputs, not additional output commands. JPEG aliases
share one destination icon. The 245-entry Analyze catalog is not a Convert menu.
General Explorer file associations and file thumbnails are outside this proposal.

| Current menu label | Proposed distinctive silhouette |
| --- | --- |
| PNG | Simple framed two-by-two transparency checker |
| JPEG | Wide photograph with sun and mountain cutouts |
| WebP (lossless) | Solid rounded image tile with sun and landscape cutouts |
| BMP | Four large bitmap blocks, one subdivided |
| TGA | Stepped image planes with a triangular foreground |
| DDS... | Isometric texture cube with separated faces |
| WAV | Continuous sampled waveform bars |
| FLAC | Three waveform bars between open square brackets |
| MP3 | Two connected musical notes |
| M4A (AAC) | Disc with a central audio mark |
| Ogg Vorbis | Broad headphone arch with solid ear pads |
| Opus | Speaker wedge and separated sound bands |
| PDF | Folded fixed-layout page with an image block and text |
| Word (DOCX) | Folded page with a large W cutout |
| WEM... | Hexagonal audio tile with waveform cutouts |
| Settings... | Three solid adjustment sliders |

These are original visual mnemonics, not official format logos or capability
claims. In particular, waveforms do not promise source quality restoration and
the PNG checker is not an exclusive claim about transparency support.

The second design pass replaces Word's ambiguous page/caret combination with a
single W, removes Ogg's hyperlink-like loops and WebP's globe, reduces PNG's
checker to four large cells, and opens FLAC's warning-like enclosure into square
brackets. Headphones and landscape shapes identify media families rather than
claiming to be established format logos. Exact format labels remain essential.

Mockup review
-------------

[Interactive mockup source](mockups/convert-format-icons.html) compares **Distinct
silhouettes** (recommended) with **File silhouettes** (a uniform page containing
each symbol). The latter is deliberately included to assess consistency against
small-size legibility; its reduced internal space is a risk at 16 pixels.

The preview retains current menu labels/order and a separated Settings item.
Design controls select light/dark/system appearance, 16/20/24-pixel menu icons,
and mixed, image, audio, PDF, Office or HEIF selections. They are review controls,
not proposed application settings. The sheet below each menu shows all 14 output
icons together and at 16, 20 and 24 pixels. Menu rows can be selected for inspection;
they do not launch conversions. This is an approximation of native Windows menu
layout, not installed Explorer evidence.

Implementation sequence
-----------------------

1. Review silhouettes at actual 16-pixel size first, then 20/24 pixels in both
   appearances. Refine confusable pairs, especially JPEG/TGA and WAV/FLAC.
   Maintain solid weight, generous negative space, transparent backgrounds and
   stable optical alignment with the existing root glyphs. Avoid tiny text,
   color coding, gradients, sparkles and detailed brand marks.
2. Add the selected original geometry to the existing asset renderer. Use
   size-specific simplification/pixel alignment where needed, keeping the
   existing 32-bit DIB encoding and eight frame sizes. Generate unique names
   such as `ConvertPng.ico` / `ConvertPngDark.ico` and a Settings pair.
3. Add a bounded mapping from Convert action target to asset name. Select the
   Settings icon by command role and keep separators icon-free. Reuse the
   existing appearance logic; return the root icon if a target asset is absent.
   No media probing, association lookup, network request or engine load is needed.
4. Trace generated assets through native and production build, payload inventory,
   archive, installer and local-copy paths. Extend only paths that currently
   enumerate the three root assets explicitly. Do not change command IDs,
   execution, availability, output policy or file associations.
5. Extend [icon verification](../tools/Test-IconAssets.ps1) to the complete asset
   list, including native decoding of every frame, alpha and monochrome colors.
   Add bounded shell checks for all output mappings, Settings, missing-asset fallback
   and appearance selection. Re-run existing selection/activation checks.
6. Inspect real native rendering at 100%, 125%, 150% and 200% scaling, light/dark
   and contrast themes, including selected/disabled rows and mixed-selection
   menu height. Record installed Explorer acceptance separately after an
   explicitly authorized install. Ship only when the small frames are readable.

Acceptance
----------

- Every current destination has a distinct asset in both appearances; every
  supported alias uses the same destination identity.
- The native 16-pixel render remains recognizable beside its exact text label;
  larger renders alone cannot pass the design review.
- Root glyph geometry, command order and contextual availability remain intact.
- Current image, audio, PDF, Office, HEIF and mixed selections expose the same
  targets before and after the icon change, with Settings last.
- Assets are present throughout packaging; absent assets degrade safely.
- Human visual acceptance and native integration are recorded independently
  from the browser mockup and automated checks.


Mockup verification
-------------------

The browser preview passes 118 checks across seven selection scenarios, both
treatments, light/dark appearance, 16/20/24-pixel menu icons and 320/736-pixel
viewport widths, with no JavaScript errors. Checks include target counts,
Settings placement, full icon-sheet coverage, selection feedback, horizontal
fit and separation between the menu and the sheet. Both treatments were rendered
and visually inspected. The distinct silhouettes retain more useful detail;
the page treatment makes the smallest internal marks harder to distinguish.

The repository documentation/link, whitespace, theme and public-source-boundary
check passes for 219 documents. Review screenshots, the standalone preview and
its temporary check script are retained under ignored `.codex-temp/convert-icons/`.
These checks do not establish native ICO quality or installed Explorer acceptance.

The implemented renderer now passes 304 Windows-decoded frames, including the
three unchanged roots and enabled WEM glyph. A native decoded contact sheet
was inspected in light/dark at 16/20/24/32/48 pixels. The shell build and contract
suite pass all destination mappings, Settings, separator behavior, root fallback,
240 filename availability checks (including MP3-to-WEM) and full-selection host activation. Installed
Explorer, contrast-theme and full DPI acceptance remain open. See the
goal for retained scratch evidence and the acceptance handoff.


Image expansion integration, 2026-10-09
-------------------------------------

Unsigned candidate 1.2.5 adds distinct ICO and AVIF destination glyphs while
retaining Word's W cutout and the existing Compact vocabulary. The combined
native run passes 476 filename-availability assertions and all 336 icon frames.
The [review handoff](image-expansion-review.md) binds 152 decoded frames from
38 light/dark icon files to that exact candidate, covering all 17 destinations,
Settings and the Convert root at 16/20/24/32 pixels. Native Explorer,
contrast/DPI/keyboard and owner visual acceptance remain pending.
