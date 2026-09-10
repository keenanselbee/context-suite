Context Suite: Image Quick Start
===============================

Draft for the internal candidate. Do not distribute the unsigned test installer
as a customer release. Signed installation and live license acceptance are pending.

Everyday use
------------

Select one or more files in File Explorer, then right-click:

- **Analyze** shows information without modifying files.
- **Convert** creates a different image format. Choose the target from the menu.
  If the conversion needs a decision, such as a background color for a transparent
  image, Context Suite asks before proceeding.
- **Optimize** reduces PNG file size while keeping the PNG format. Start with
  **Auto**; it aims for a useful size reduction with gentle quality changes.
  **Lossless** keeps pixels identical, **Balanced** permits modest changes, and
  **Smallest** permits more changes to reduce size further. No preset guarantees
  a particular file size. Already efficient files can remain unchanged.

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

This candidate focuses on PNG, JPEG, WebP, BMP, TGA and bounded DDS texture
conversion. Not every variant is supported: animation, oversized files and
unsupported color/texture structures may need another workflow. Read the
file's explanation; renaming its extension does not convert it. Optimize currently
targets PNG, not every format that Convert supports. Audio and video are not part
of this image candidate.

License and transfer
--------------------

The trial starts with the first confirmed valid conversion or optimization and
lasts 72 hours. Analyze and viewing results do not start it. One purchase includes
all future updates and allows one active installation at a time.

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

For detailed internal acceptance status, see
[licensing verification](licensing-verification.md) and the
[commercial-release goal](commercial-release-candidate-goal.md).
