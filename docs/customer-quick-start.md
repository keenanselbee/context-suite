Context Suite Quick Start
=========================

This guide describes the free edition. It has no trial, purchase, activation,
or installation limit. Older installed editions can differ; see
[project status](../README.md#project-status) for the verified candidate,
installation, and remaining release acceptance. This guide does not announce
an installation or a published release.


Everyday use
------------

Select one or more files in File Explorer, then right-click:

- **Analyze** explains file properties without changing the file.
- **Convert** creates the format you select. If a decision is necessary, such as
  a background color for a transparent image, Context Suite asks first.
- **Optimize** reduces supported files while keeping their format. Start with
  **Auto** for a tested default. **Lossless** preserves the supported media
  content, **Balanced** permits modest quality changes, and **Smallest** permits
  stronger changes. Available behavior depends on the format; an inapplicable
  preset explains its limit. No preset guarantees a particular size reduction.

Actions create copies by default, with names such as `Photo - Converted.webp`
or `Photo - Optimized.png`. Existing output names are not overwritten; another
copy receives `(2)`, `(3)`, and so on. Already efficient files can remain unchanged
without creating an extra copy.

Opening Context Suite directly opens Settings with Explorer instructions.
Run conversions and optimization from the context menu. Settings also provides
**Third-party notices**, which opens the bundled notices locally.

Successful quick actions stay quiet apart from the optional completion sound.
Longer batches can show progress. Problems and meaningful warnings show details;
other valid files can still finish. Cancelling stops pending work but does not
undo completed output. Retry handles eligible unsuccessful work, not already
completed copies. Missing or changed inputs can require a new selection.


Settings and safe replacement
-----------------------------

Use **Settings...** at the bottom of Convert or Optimize for saved preferences.
Settings apply to future work; an already queued batch keeps its chosen settings.
Both tools default to **Create copies**. To overwrite instead, select
**Overwrite originals** for that tool and save. Future commands use that consent
without asking again, but only where the app's safety checks permit replacement.
Choosing another output folder creates copies. PDF/document transformations,
JPEG/WebP/TGA optimization, and some image-information requirements also
keep originals regardless of this preference.

After the new file is validated and safely published, the original goes to the
Recycle Bin. If recycling fails, a recoverable original/backup is kept and its
location is shown. There is no permanent-delete fallback. Keep your own backups
of important originals.


Supported scope
---------------

Available operations depend on the selected file and included engines. Analyze
can identify many files that cannot be converted. Read the explanation for an
unsupported file; renaming its extension does not convert it. The
[capability matrix](launch-capability-matrix.md) records exact format and variant
boundaries. The examples below are not a promise to handle every file.

Image conversion includes PNG, JPEG, WebP, BMP, TGA, and bounded DDS textures,
plus selected HEIC/AVIF, ICO, TIFF, SVG, and GIF workflows. GIF frames become
numbered PNG copies; animation encoding is not supported. Transparent images
need an explicit background choice when converting to an opaque format.
Unsupported HDR, color, animation, or texture structures are declined.

Ordinary image conversion applies orientation and color correctly and preserves
information the new format supports. Incompatible extras may be omitted from a
new copy without a prompt; the original stays safe. Automatic handling is not a
privacy scrub: supported camera/location information can remain. DDS workflows
have their own explicit image-information choices.

Audio targets include **WAV**, **FLAC**, **MP3**, **M4A (AAC)**, **Ogg Vorbis**, and
**Opus**, with supported AIFF/AIFF-C and WMA inputs as well. A prompt asks before
another lossy conversion, required resampling, or reduced precision. A lossless
format cannot restore lost quality. Same-format audio conversion leaves the file
unchanged. Protected WMA and unsupported metadata or channel layouts are declined.
Selected MP4/MOV/WebM/MKV files support audio extraction; general video conversion
is outside scope. FLAC optimization preserves admitted audio and metadata.

PDF pages can become numbered PNG or JPEG copies. Page images do not retain
editable forms, attachments, or verifiable digital signatures. Completed pages
remain if later work stops; retry can process unfinished pages. If the source
changed, start a new command. Page retry state is not retained after the results
session ends.

**Convert > PDF** combines supported images into one PDF, with an order review
for multiple images. Supported Word, Excel, and PowerPoint documents produce
separate PDF copies when the Office engine is included. Images in a mixed
selection still form their own combined PDF. All these actions keep originals.
PDF-to-Word creates best-effort editable copies; inspect the result before use.
**PDF (Dark mode)** creates a reading copy within supported document limits;
photos and other preserved image regions may retain their original colors.

For spreadsheets, **Use saved values** uses stored formula results, while
**Recalculate** evaluates supported formulas locally. Saved values may be stale;
recalculation may change results. External data is not refreshed. Unsupported
charts, incomplete inspection, or affected early dates can prevent conversion.
PowerPoint export omits hidden slides and speaker notes. Missing fonts can
require a choice between a substituted PDF copy and skipping the file.
Check document layout before sharing; unsupported or protected files may need
export from their original application.

Optimization also covers supported JPEG, still WebP, PDF, and Office documents,
with Auto/Lossless paths for TGA, OpenDocument, and EPUB. PDF Auto/Lossless
preserves admitted document content without downsampling page images;
Balanced/Smallest can reduce eligible embedded-image quality. Only validated,
smaller results are published. Consult the [Optimizer design](optimizer-design.md)
for each format's preservation and quality limits.


Free use
--------

Supported features and future official updates are free. No key, account,
activation, or periodic licensing connection is needed. Existing legacy access
records are left untouched and do not control the free edition. See the
[license terms](../LICENSE.md) for permitted use and source redistribution.


Getting help
------------

Include the app version, Windows version, chosen operation/preset, and displayed
problem text. Use a small non-private sample when reporting a format problem;
avoid sharing sensitive media or old purchase credentials. Preserve any original
or recovery backup named in a warning.

For current release and acceptance status, see [project status](../README.md#project-status).
