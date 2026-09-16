Context Suite Quick Start
=========================

Draft for the expanded internal candidate. The retained 1.1.0 package includes
images, audio and PDF tools; Office conversion is tested in separate isolated
builds. Default release packaging has not adopted the optional engines. Signed
installation and live license acceptance are pending.

Everyday use
------------

Select one or more files in File Explorer, then right-click:

- **Analyze** shows information without modifying files.
- **Convert** creates the selected image, audio or PDF format when the build and
  source support it. Choose the target from the menu.
  If the conversion needs a decision, such as a background color for a transparent
  image, Context Suite asks before proceeding.
- **Optimize** reduces supported PNG, FLAC or PDF file size while keeping its
  format. For PNG, start with
  **Auto**; it aims for a useful size reduction with gentle quality changes.
  **Lossless** keeps pixels identical, **Balanced** permits modest changes, and
  **Smallest** permits more changes to reduce size further. No preset guarantees
  a particular file size. FLAC and PDF use Auto or Lossless. Already efficient
  files can remain unchanged.

Routine menu actions create copies by default. Original filenames are retained, with new
names such as `Photo - Converted.webp` or `Photo - Optimized.png`. Existing output
names are not overwritten; another copy receives `(2)`, `(3)`, and so on.
Reprocessing an already efficient image need not create an extra copy.

Conversion normally takes care of image information automatically: it handles
orientation and color and keeps information the new format supports. Incompatible
extras may be left out of the copy without interrupting you. Your original stays
safe. Automatic is not a privacy scrub; supported camera/location information can
remain. DDS workflows offer their own explicit image-information choices.

Opening Context Suite directly opens Settings with Explorer instructions and
License access. Run conversions and optimization from the context menu.

Successful quick actions stay quiet apart from the completion sound when enabled.
Longer batches can show progress. Problems or meaningful warnings show details;
other valid files can still finish. **Cancel work** stops pending work but does not
undo completed output. **Try again** retries eligible unsuccessful work, not
already completed copies. Missing or changed inputs can require a new selection.

Settings and safe replacement
-----------------------------

Use **Settings...** at the bottom of Convert or Optimize for saved preferences.
Settings apply to future work; an already queued batch keeps its chosen settings.
Convert and Optimize each default to **Create copies**. To overwrite instead,
select **Overwrite originals** for that tool and save. Future context-menu
commands use that choice without asking again. Choosing another output folder
always creates copies; some image-information requirements also keep originals.

Replacement is available only where the app's safety checks allow it. After the
new file is validated, the original goes to the Recycle Bin. If recycling fails, a recoverable
original/backup is kept and its location is shown. There is no permanent-delete
fallback. Keep your own backups of important originals.

Supported scope
---------------

Image conversion includes PNG, JPEG, WebP, BMP, TGA and bounded DDS textures.
Not every variant is supported: animation, oversized files and
unsupported color/texture structures may need another workflow. Read the
file's explanation; renaming its extension does not convert it. Analyze also gives basic
identification and common-use descriptions for other readable regular files;
recognition does not mean conversion is available.

In isolated audio testing, Convert also offers **WAV**, **FLAC**, **MP3**,
**M4A (AAC)**, **Ogg Vorbis** and **Opus**. Routine conversions use fixed settings.
A short prompt asks before another lossy conversion, required resampling or
reduced precision. A lossless format cannot restore quality already lost.
Converting a file to its existing format leaves it unchanged.

In that same isolated testing, **Auto** and **Lossless** also optimize supported FLAC
files while preserving decoded audio and admitted metadata. They share the same
copy settings, batch progress and retry behavior. Balanced and Smallest are PNG
presets. If the extension does not match the identified content, the app asks you
to correct the name before optimizing. Normal packaging does not yet include the
audio engine, although the retained combined 1.1.0 candidate includes it. Raw AAC,
ALAC and other audio codecs can be recognized without being convertible. Video
processing is not available.

In isolated PDF testing, **Convert > PNG** saves each PDF page as a numbered image,
such as `Document - Page 001.png`. It always keeps the PDF, even with Overwrite
originals selected. Page images retain visible content; they do not carry editable
forms, attachments or verifiable digital signatures. Protected or oversized PDFs
can be declined. The retained combined candidate includes this renderer; default
release packaging has not adopted it.

The result shows how many page copies were saved. Expand file details to see their
locations. If work stops partway through, completed copies stay in place. **Try
again** converts only unfinished pages, using current settings for those new copies.
If the source PDF changed, start a new Convert command. Page retry information is
kept for the current results session; restarting the app does not resume that list.

PDF **Auto** and **Lossless** optimize supported document structure without
downsampling page images. They always keep the original PDF and publish only a
validated smaller copy. Encrypted, signed and unsupported PDFs can be declined.

The optional **Convert > PDF** workflow combines selected supported images into
one PDF copy. With several images, review their page order before converting.
When the optional Office engine is present, selected Word `.docx`, Excel `.xlsx`
and PowerPoint `.pptx` documents each get a separate PDF copy. Images in a mixed
selection still form their own combined PDF. Originals are always kept.

For spreadsheets, choose **Use saved values** to use formula results stored in
the workbook, or **Recalculate** to evaluate supported formulas locally before
exporting. Saved values may be out of date; recalculation may change results.
External data is not refreshed. **Try again** keeps this choice for a failed
document. These optional Office paths are undergoing isolated acceptance and
are not included in the reserved packaged candidate. Only ordinary DOCX, XLSX
and PPTX variants are currently admitted; legacy files, templates and macro-enabled
documents have no current conversion path. If Context Suite detects affected
dates before March 1, 1900, it keeps the workbook and asks you to export the PDF
from Excel. Spreadsheet date fidelity remains under development; broader
spreadsheet acceptance is incomplete. Analyze remains available.

License and transfer
--------------------

The trial starts with the first confirmed valid conversion or optimization and
lasts seven days (168 hours). Analyze and viewing results do not start it. A $5 CAD
one-time purchase includes all future updates and allows one active installation
at a time. Existing trials retain their original start time.

Open **License...** from results, Settings or a conversion prompt, paste your purchase key,
and select **Activate**. A successful activation is saved using Windows per-user
protection. Media stays local; licensing sends no image contents or selected paths.
If activation is declined, check the key and whether another installation already
uses it, then try again explicitly. An uncertain interrupted request is different
and requires the portal recovery steps below.
Paid access checks online daily while running and allows up to 30 days offline
after a successful check. Use **Validate online** to refresh immediately;
successful validation displays **License validated online**.

To move to another installation, **Deactivate...** the current one first, or use
the Polar customer portal from your purchase email. An interrupted activation may
need portal recovery; check/deactivate the old instance before clearing its local
recovery state. Never share your key in screenshots, bug reports or source control.

Expiry stops new Convert/Optimize work, not admitted batches. Analyze remains
available, along with Settings and license recovery. Reinstalling is not a
supported way to reset a trial or recover a remote activation.

Getting help
------------

Include the app version, Windows version, chosen operation/preset and the displayed
problem text. Do not include a license key or private image unless you intentionally
choose to share it. A small non-private sample is preferable when reporting a format
problem. Preserve any original or recovery backup named in a warning.

For exact format/build boundaries, see the [launch matrix](launch-capability-matrix.md).
For detailed internal acceptance status, see
[licensing verification](licensing-verification.md) and the
[commercial-release goal](commercial-release-candidate-goal.md).
