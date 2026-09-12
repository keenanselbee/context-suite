Audio Catalog Description Review
================================

Catalog revision 2026-09-11.5 reviews the names, Audio-family grouping and
plain-language purposes of all eighteen current Audio records. Fourteen records
receive clearer wording or more specific source links; four retain their existing
description/reference. This advances factual description review, separately from
the historical link-retrieval audit and the audio engine's conversion matrix.

The catalog remains at 243 records. IDs, names, family values, extensions, exact
filenames, MIME identifiers and text-compatibility hints are unchanged. There are
no new detectors, executable commands, dependencies or runtime lookups. Existing
confidence rules still qualify filename hints; a reviewed description does not
validate an inspected file, identify its codec or make it convertible.


Reviewed purposes and provenance
--------------------------------

The following table records the specific purpose/variant distinction checked on
2026-09-11. Descriptions are independently written; no registry data, source prose,
parser code or reference-tree material is copied into the product.

| Catalog ID | Reviewed purpose or distinction | Primary documentation |
| --- | --- | --- |
| aac | Compressed audio distribution; ADTS packages AAC packets | [AAC description](https://www.loc.gov/preservation/digital/formats/fdd/fdd000036.shtml), [Microsoft ADTS sink](https://learn.microsoft.com/en-us/windows/win32/api/mfidl/nf-mfidl-mfcreateadtsmediasink) |
| aiff | Audio exchange; AIFF-C permits compressed and uncompressed samples | [Apple file/data format table](https://developer.apple.com/library/archive/documentation/MusicAudio/Conceptual/CoreAudioOverview/SupportedAudioFormatsMacOSX/SupportedAudioFormatsMacOSX.html), [Oracle AIFF/AIFF-C support](https://docs.oracle.com/database/121/AIVUG/ap_audfmts.htm) |
| ape | Lossless music storage | [Monkey's Audio author](https://www.monkeysaudio.com/) |
| au | NeXT/Sun audio with several possible sample encodings | [Oracle/Sun AU manual](https://docs.oracle.com/cd/E19253-01/816-5174/6mbb98ucf/index.html), [Apple format table](https://developer.apple.com/library/archive/documentation/MusicAudio/Conceptual/CoreAudioOverview/SupportedAudioFormatsMacOSX/SupportedAudioFormatsMacOSX.html) |
| audacity | Editable tracks/clips; older AUP projects require associated data | [Audacity project manual](https://manual.audacityteam.org/man/audacity_projects.html) |
| caf | Audio container and metadata; encoding varies | [Apple CAF overview](https://developer.apple.com/library/archive/documentation/MusicAudio/Reference/CAFSpec/CAF_overview/CAF_overview.html) |
| cue | Track layout plus references to separately stored media | [GNU CUE format](https://www.gnu.org/software/ccd2cue/manual/html_node/CUE-sheet-format.html), [file-set explanation](https://www.gnu.org/software/ccd2cue/manual/ccd2cue.pdf) |
| flac | Losslessly compressed audio storage | [RFC 9639](https://www.rfc-editor.org/rfc/rfc9639.html) |
| m3u | Media-location playlists, including HTTP Live Streaming | [RFC 8216](https://www.rfc-editor.org/rfc/rfc8216.html) |
| m4a | MPEG-4 audio can use AAC or ALAC; audiobook association | [Apple container/codec table](https://developer.apple.com/library/archive/documentation/MusicAudio/Conceptual/CoreAudioOverview/SupportedAudioFormatsMacOSX/SupportedAudioFormatsMacOSX.html), [Apple M4B audiobook naming](https://support.apple.com/guide/itunes/buy-or-download-items-itns5bcdf353/windows) |
| midi | Timed musical events for instruments/software | [MIDI Association](https://midi.org/standard-midi-files) |
| mp3 | Compressed audio distribution and everyday playback | [Fraunhofer MP3 overview](https://www.iis.fraunhofer.de/en/ff/amm/consumer-electronics/mp3.html) |
| pls | Playlist locations, titles and durations | [VideoLAN's PLS reader](https://github.com/videolan/vlc/blob/master/modules/demux/playlist/pls.c) |
| soundfont | Instrument samples/settings used by synthesizers | [FluidSynth introduction](https://www.fluidsynth.org/wiki/GettingStarted/), [SF2/SF3 overview](https://www.fluidsynth.org/) |
| tracker | Sequenced patterns and sample/instrument information | [OpenMPT module formats](https://wiki.openmpt.org/Manual:_Module_formats) |
| wave | Audio exchange container; encoding varies | [Microsoft RIFF/WAVE description](https://learn.microsoft.com/en-us/windows/win32/xaudio2/resource-interchange-file-format--riff-) |
| wavpack | Lossless/lossy/hybrid audio; hybrid restoration needs correction data | [WavPack author](https://www.wavpack.com/) |
| wma | Windows Media playback/streaming with different encoding variants | [Microsoft ASF](https://learn.microsoft.com/en-us/windows/win32/wmformat/overview-of-the-asf-format), [encoding variants](https://learn.microsoft.com/en-us/windows/win32/medfound/configuring-standard--professional--or-lossless-audio-encoding) |

The PLS description is an inference from VideoLAN's reader handling media-location,
title and duration fields. Its code was inspected only; no implementation is
copied or executed. GNU CUE documentation was available through indexed primary
excerpts/PDF content after direct HTML retrieval failed. Direct retrieval of the
old Library of Congress AIFF and MP3 references also failed; replacement primary
documentation supplied the reviewed facts. Failure to retrieve is not proof of a
broken public link. The AU reference replaces a generic historical-standards
landing page with the actual format description.

Apple's archived platform support tables establish examples of container/codec
associations, not Context Suite capabilities or an exhaustive current codec list.
Likewise, no vendor's playback, licensing or quality marketing is imported as a
Context Suite guarantee. Description review is not redistribution approval.


Boundaries and remaining review
-------------------------------

This review covers purpose descriptions and their references. It does not claim
exhaustive extension/variant evidence, binary validity, recognition fixtures or
complete MIME aliases. In particular, the existing MIDI `.kar` alias still needs
separate variant provenance; the MIDI page establishes timed-event semantics but
does not by itself document that suffix. Keep this gap visible rather than
counting it as verified by the purpose review. Other catalog families and formats
grouped outside Audio, including the Ogg container, retain their own review scope.

The source/reference changes are additive factual maintenance under catalog
schema 1. No review status is displayed as file confidence, and no playlist is
followed or project opened during Analyze.


Verification
------------

A before/after JSON comparison verifies that only `commonUses`, `source` and the
catalog revision changed; all 243 records retain their recognition-related data.
All **2,132 foundation contracts pass**, exercising embedded-catalog loading/validation,
record lookup, ambiguity, metadata descriptions and operation separation.
Actual run evidence is retained in `.codex-temp/catalog-audio-review-foundation.log`.
Public-source boundary, system-theme policy, all 87 documentation files and
both repositories' whitespace checks also pass.
No new parser or test-only copy of the descriptions is introduced for this edit.
Full worker, packaging, visible UI and installed acceptance are separate.
