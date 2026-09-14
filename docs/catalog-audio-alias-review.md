Audio Catalog Alias Review
==========================

Reviewed 2026-09-14 against catalog revision 2026-09-13.1. This review accounts
for all 33 existing extension associations across the eighteen Audio records.
Each association has evidence for the stated use; none requires a catalog change.
This completes review of these existing associations, not an exhaustive inventory
of audio extensions or every format variant. The Ogg container is cataloged
outside Audio and retains its separate review scope.

The [purpose review](catalog-audio-review.md) established what these files are
generally used for. This follow-up checks the actual suffixes and distinguishes
different formats grouped under one description. Filename evidence remains a
hint: it does not prove contents, a codec, structural validity or transformability.


Association evidence
--------------------

The table lists the current catalog arrays exactly. Sources are format-owner
documents, registry records, implementer documentation or inspected source.
The less common `.wave` convention is documented by the author of McGill's audio
format notes; that observation is not presented as a Microsoft registration.
All descriptions below are independently written. No source code or reference
binary was copied or executed.

| Catalog ID | Existing extensions | Evidence and variant boundary |
| --- | --- | --- |
| aac | `.aac`, `.adts` | [AAC registration](https://www.iana.org/assignments/media-types/audio/aac), Additional information, associates both with ADTS. Its separate LATM/LOAS suffixes are not existing catalog aliases and are not added here. A suffix does not establish framing or AAC profile. |
| aiff | `.aif`, `.aiff`, `.aifc` | [Apple's file-format table](https://developer.apple.com/library/archive/documentation/MusicAudio/Conceptual/CoreAudioOverview/SupportedAudioFormatsMacOSX/SupportedAudioFormatsMacOSX.html), D-1, lists all three under AIFC and `.aiff` under AIFF. These are a grouped family, not proof of compression or endianness. |
| ape | `.ape` | [Monkey's Audio history](https://www.monkeysaudio.com/versionhistory.html), version 3.00, records adoption of `.ape`. Older `.mac` naming is historical evidence, not a newly added alias. |
| au | `.au`, `.snd` | [Apple's file-format table](https://developer.apple.com/library/archive/documentation/MusicAudio/Conceptual/CoreAudioOverview/SupportedAudioFormatsMacOSX/SupportedAudioFormatsMacOSX.html), D-1, lists both for NeXT/Sun audio. This establishes that use of `.snd`, not exclusivity across all software. |
| audacity | `.aup`, `.aup3` | [Audacity project documentation](https://manual.audacityteam.org/man/audacity_projects.html), Save Project and Older Projects, distinguishes AUP3 from earlier AUP plus its associated data folder. Grouping does not make the two storage layouts interchangeable. |
| caf | `.caf` | [Apple's file-format table](https://developer.apple.com/library/archive/documentation/MusicAudio/Conceptual/CoreAudioOverview/SupportedAudioFormatsMacOSX/SupportedAudioFormatsMacOSX.html), D-1, names the CAF container suffix. The container does not select an audio encoding. |
| cue | `.cue` | [WavPack's own manual](https://www.wavpack.com/wavpack_doc.html), WvUnpack `-cc`, explicitly names cuesheet output as `.cue` and describes its separate media reference. This is an implementer's convention, not evidence that Analyze reads or follows the sheet. |
| flac | `.flac` | [FLAC registration](https://www.iana.org/assignments/media-types/audio/flac), Additional information, names the extension. The naming evidence does not validate frames or metadata. |
| m3u | `.m3u`, `.m3u8` | [RFC 8216 section 4](https://www.rfc-editor.org/rfc/rfc8216.html#section-4) allows both suffixes for HLS playlists. HLS imposes its own UTF-8 and tag rules; the grouped catalog record does not assert that any M3U file is HLS or contains only audio. |
| m4a | `.m4a`, `.m4b` | [Apple's file-format table](https://developer.apple.com/library/archive/documentation/MusicAudio/Conceptual/CoreAudioOverview/SupportedAudioFormatsMacOSX/SupportedAudioFormatsMacOSX.html) associates M4A with MPEG-4 audio; [Apple's audiobook instructions](https://support.apple.com/guide/itunes/buy-or-download-items-itns5bcdf353/windows) name M4B. Neither suffix guarantees AAC, ALAC, chapter support or lack of protection. |
| midi | `.mid`, `.midi`, `.kar` | [MuseScore 2's MIDI-import documentation](https://musescore.org/en/handbook/2/midi-import) lists all three. [Power Karaoke's manual](https://www.powerkaraoke.com/download/KaraokeCDGCreator.pdf), printed page 12, describes KAR as MIDI karaoke and notes that MID can also contain lyrics. Lyrics are not guaranteed by a filename. |
| mp3 | `.mp3` | [Apple's file-format table](https://developer.apple.com/library/archive/documentation/MusicAudio/Conceptual/CoreAudioOverview/SupportedAudioFormatsMacOSX/SupportedAudioFormatsMacOSX.html), D-1, associates the suffix with MPEG Layer 3. It is not a generic extension for every MPEG audio layer. |
| pls | `.pls` | [VideoLAN's PLS reader](https://github.com/videolan/vlc/blob/master/modules/demux/playlist/pls.c), `Import_PLS`, explicitly checks this suffix. This is an inference from the implementation's input convention; its admission and decoding logic are not adopted. |
| soundfont | `.sf2`, `.sf3` | [FluidSynth's getting-started documentation](https://www.fluidsynth.org/wiki/GettingStarted/) names both extensions; its [feature description](https://www.fluidsynth.org/) distinguishes Vorbis-compressed SF3. These are instrument banks, not completed audio recordings. |
| tracker | `.mod`, `.xm`, `.it`, `.s3m`, `.mptm` | [OpenMPT's module-format documentation](https://wiki.openmpt.org/Manual:_Module_formats) separately names each suffix and describes format differences. In particular, MOD itself has multiple variants; a grouped tracker description does not establish playback compatibility. |
| wave | `.wav`, `.wave` | [Microsoft's extension table](https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/common-file-name-extensions-in-windows) names WAV. [Peter Kabal's McGill WAVE notes](https://www.mmsp.ece.mcgill.ca/Documents/AudioFormats/WAVE/WAVE.html) document WAV and the less common WAVE suffix. Neither implies PCM or a particular bit depth. |
| wavpack | `.wv` | [WavPack's own manual](https://www.wavpack.com/wavpack_doc.html), introduction and hybrid-mode discussion, distinguishes the main WV file from WVC correction data. The existing WV alias does not establish lossless mode or the presence of its companion. |
| wma | `.wma` | [Microsoft's extension table](https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/common-file-name-extensions-in-windows) maps WMA to Windows Media Audio. The suffix alone does not select its encoding variant. |


Review outcome and limits
-------------------------

The prior `.kar` provenance gap is resolved by an explicit implementer suffix
list and a separate karaoke application's explanation. Historical Apple and
MuseScore manuals establish established conventions; they are not claims about
current versions of those applications or Context Suite playback support.

The GNU CUE manual could not be retrieved directly in this review. WavPack's
retrieved manual supplies explicit `.cue` evidence instead. A failed retrieval
does not establish a broken public link. The historical source-retrieval audit
is unchanged. An attempted IANA `audio/vnd.wave` retrieval also failed and is
not evidence of registration; this review adds no MIME identifiers.

This review checks that each currently listed association has a documented
meaning. It does not claim that extensions are unique, that every competing
meaning is cataloged, or that all historical/codec/metadata variants are accepted.
Wider catalog alias review, additional meaningful MIME coverage and the
[audio conversion matrix](audio-conversion-policy.md) remain separate work.
Playlist references, project data, soundfonts and modules are not opened or
rendered by this documentation change.


Verification
------------

The review table is reconciled against all eighteen Audio records and 33
extension associations with no missing, extra or duplicate ID/extension pairs.
The catalog stays byte-identical at SHA-256
`F578EB98CA1C824FD36ACF9F614F42001E3A59FC1FDFAF0E01C93D526E14F047`.
The reconciliation receipt is `.codex-temp/catalog-audio-alias-review.json`.
Repository documentation/link/whitespace validation is recorded in
`.codex-temp/catalog-audio-alias-repository.log`.

No product source or payload changes. No new runtime, worker, UI or packaging
acceptance is claimed; candidate 1.0.1 and its reserved payload remain unchanged.
