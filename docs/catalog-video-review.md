Video Catalog Purpose Review
============================

Catalog revision **2026-09-11.10** reviews all ten Video-family entries and
improves seven descriptions or references. It retains 243 records, unchanged
identifiers, names, families, extensions, MIME fields and recognition rules.
No video converter, player, demuxer or new executable capability is introduced.

The changes distinguish containers from codecs, related mobile formats and the
two Flash-era containers. Generic MPEG index links are replaced with a primary
systems-standard reference. Accurate ASF, Matroska and QuickTime descriptions
remain unchanged.


Purpose evidence
----------------

Primary references were consulted on 2026-09-11. Descriptions are independently
phrased; no registry dataset, source implementation, sample media or reference
binary was imported or executed.

| Catalog ID | Reviewed purpose or distinction | Primary evidence |
| --- | --- | --- |
| 3gp | Mobile multimedia playback, messaging and streaming; 3GP and 3G2 have separate specifications | [3GP registration](https://www.rfc-editor.org/rfc/rfc3839.html), [3G2 registration](https://www.rfc-editor.org/rfc/rfc4393.html), [3GPP format introduction](https://www.3gpp.org/ftp/TSG_SA/TSG_SA/TSGS_23/Docs/PDF/SP-040065.pdf) |
| asf | Synchronized media for local playback and network delivery; Windows Media is a common use, not the only possible encoding | [Microsoft ASF overview](https://learn.microsoft.com/en-us/windows/win32/wmformat/overview-of-the-asf-format) |
| avi | Capture, editing and playback of audio/video sequences with varying stream formats | [Microsoft AVI structure](https://learn.microsoft.com/en-us/windows/win32/directshow/avi-riff-file-reference) |
| flv | FLV and F4V package media for older Flash playback/streaming; their typical codecs differ | [Adobe format explanation](https://helpx.adobe.com/media-encoder/desktop/encoding-quick-start-and-basics/file-formats-supported-import.html), [historical Media Encoder manual](https://helpx.adobe.com/en/pdf/cs6/mediaencoder_reference.pdf) |
| matroska | Audio, video, subtitles and other media information together; a container rather than an encoding algorithm | [RFC 9559 introduction](https://www.rfc-editor.org/rfc/rfc9559.html#section-1) |
| mp4 | Media storage for display, interchange, editing and streaming; MP4 derives from ISO base media | [ISO MP4 abstract](https://www.iso.org/standard/79110.html), [IEC historical purpose abstract](https://webstore.iec.ch/en/publication/9959) |
| mpeg | Combined audio/video program streams, including DVD video objects | [ITU H.222.0](https://www.itu.int/rec/T-REC-H.222.0), [Microsoft DVD stream description](https://learn.microsoft.com/en-us/windows/win32/directshow/dvd-navigator-filter) |
| mpeg-ts | Packetized media transport, including multiple programs and broadcast/recording uses | [ITU H.222.0](https://www.itu.int/rec/T-REC-H.222.0), [reviewed 2014 systems text](https://www.itu.int/rec/dologin_pub.asp?id=T-REC-H.222.0-201410-S%21%21PDF-E&lang=e&type=items) |
| quicktime | Multimedia tracks used for playback, editing and production | [Apple QuickTime overview](https://developer.apple.com/documentation/quicktime-file-format), [track/media structures](https://developer.apple.com/documentation/quicktime-file-format/structuring_movie_data_and_features) |
| webm | Web media delivery using a restricted Matroska container | [WebM container guidelines](https://www.webmproject.org/docs/container/) |

Changed IDs: `3gp`, `avi`, `flv`, `mp4`, `mpeg`, `mpeg-ts` and `webm`.

The current Adobe format page supplied the FLV/F4V container distinction. The
older Adobe specification URL returned a retrieval-time 404; the historical
manual's relevant text was available through indexed primary excerpts, while
direct opening redirected to general support. The new catalog source uses the
working format page, not that redirect. The former 3GPP codec landing page could
not be retrieved; RFC registrations and the published 3GPP introduction supplied
the mobile-format purpose evidence.

Apple's overview required JavaScript and its linked Markdown view could not be
retrieved. Indexed Apple overview and track-structure passages supplied the
purpose evidence. An attempted archived overview also failed retrieval; no full
QuickTime specification review is claimed.

The ISO 2018 MP4 page points to the 2020 edition used by the catalog. The older
IEC abstract supplies historical usage context, not a claim that its edition is
current. The inspected H.222.0 body is the publicly accessible 2014 edition; the
current recommendation page supplies its publication lineage. No paid standard
was purchased or accessed. Purpose continuity does not establish current-version
bitstream conformance, codec support or redistribution approval.


Boundaries and verification
---------------------------

This reviews typical purposes, not every suffix, brand, codec or storage variant.
The existing `.3g2` alias remains grouped with `3gp`, and `.f4v` with `flv`, without
claiming identical specifications or binary layouts. Audio-only members such as
`.mka` can belong to a family grouped under Video. That family label is not proof
of an actual video track. A `.ts` name can also suggest TypeScript; the catalog's
conflict handling is unchanged. Generic MPEG suffixes do not prove a program
stream, and a container identity does not reveal every contained codec.

Specific 3GPP releases/brands, AVI extensions, MPEG-1/system-stream variants,
camera transport wrappers, fragmented MP4, QuickTime external media and subtitle
variants still need separate evidence before any deeper parser or executable
capability claim. No Windows/Adobe/Apple codec support matrix is adopted for
Context Suite. Existing video conversion remains outside the selected scope.

An independent JSON delta limits this change to `commonUses`, `source` and
revision, retaining all 243 records and recognition fields. The record is
`.codex-temp/catalog-video-delta.json`. Existing catalog loading, conflict and
capability-separation contracts provide verification without adding tests that
duplicate the prose. All 2,320 foundation contracts passed; the final log is
`.codex-temp/catalog-video-foundation.log`. No new production staging, native
engine, worker or interactive acceptance run is claimed for this prose change.
