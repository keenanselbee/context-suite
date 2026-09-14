Document Catalog Alias Review
=============================

Reviewed 2026-09-14 against catalog revision 2026-09-13.1. This review accounts
for all 58 existing extension associations across the 29 Document records plus
`pptx` and `xlsx`, matching the 31-record [purpose review](catalog-document-review.md).
The listed associations have evidence for their stated uses; no catalog data
change is needed. This is not an exhaustive inventory of document extensions,
competing meanings or accepted format versions.

Filename associations remain hints. They do not establish actual contents,
macro presence, structural validity, rendering fidelity or an executable
capability. Required Office conversion remains unfinished under the
[broad file support goal](broad-file-support-goal.md).


Existing associations and evidence
----------------------------------

Each row preserves the exact current extension array. Registration and format
specifications establish some conventions; authoring/import/export implementations
establish others. An implementation's supported suffix is evidence for that use,
not a claim that Context Suite adopts its parser or supports its transformations.

| Catalog ID | Existing extensions | Reviewed association and boundary | Source |
| --- | --- | --- | --- |
| ass | `.ass`, `.ssa` | Related Advanced SubStation Alpha and SubStation Alpha subtitle variants | [FFmpeg SSA/ASS output declaration](https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/libavformat/assenc.c) |
| chm | `.chm` | Compiled HTML Help file | [Microsoft compiled-help guidance](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/htmlhelp/running-a-compiled-help-file-from-the-web) |
| djvu | `.djvu`, `.djv` | Two registered DjVu filename conventions | [IANA DjVu registration](https://www.iana.org/assignments/media-types/image/vnd.djvu) |
| doc | `.doc`, `.dot` | Legacy Word document and template | [Microsoft Office formats](https://learn.microsoft.com/en-us/office/compatibility/office-file-format-reference) |
| docx | `.docx`, `.docm`, `.dotx`, `.dotm` | Modern document/template variants, including macro-enabled forms | [Microsoft Office formats](https://learn.microsoft.com/en-us/office/compatibility/office-file-format-reference) |
| eml | `.eml` | Saved email-file convention | [Microsoft Windows extension table](https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/common-file-name-extensions-in-windows) |
| epub | `.epub` | Packaged electronic publication | [W3C EPUB 3.3, file extension and container](https://www.w3.org/TR/epub-33/) |
| ical | `.ics`, `.ical` | Registered calendar suffix and a separately evidenced application convention | [IANA calendar registration](https://www.iana.org/assignments/media-types/text/calendar), [Talk Calendar export documentation](https://github.com/crispinprojects/talkcalendar) |
| keynote | `.key` | Apple Keynote import convention; shared suffix remains qualified | [LibreOffice import filters](https://help.libreoffice.org/latest/en-US/text/shared/guide/convertfilters.html) |
| kindle | `.azw`, `.azw3` | Kindle e-book variants; extension alone does not settle the internal generation | [calibre format distinctions](https://manual.calibre-ebook.com/faq.html#what-formats-does-calibre-support-conversion-to-from) |
| mhtml | `.mht`, `.mhtml` | Single-file web-page conventions | [Microsoft Office formats](https://learn.microsoft.com/en-us/office/compatibility/office-file-format-reference) |
| mobi | `.mobi`, `.prc` | Mobipocket association; PRC also has broader non-Mobipocket uses | [calibre format distinctions](https://manual.calibre-ebook.com/faq.html#what-formats-does-calibre-support-conversion-to-from) |
| msg | `.msg` | Outlook item, including non-email items | [Microsoft MSG specification](https://learn.microsoft.com/en-us/openspecs/exchange_server_protocols/ms-oxmsg/b046868c-9fbf-41ae-9ffb-8de2bd4eec82) |
| numbers | `.numbers` | Apple Numbers spreadsheet import convention | [LibreOffice import filters](https://help.libreoffice.org/latest/en-US/text/shared/guide/convertfilters.html) |
| odg | `.odg`, `.otg` | OpenDocument drawing and drawing template | [LibreOffice OpenDocument extension table](https://help.libreoffice.org/latest/en-US/text/shared/00/00000021.html) |
| odp | `.odp`, `.otp` | OpenDocument presentation and presentation template | [LibreOffice OpenDocument extension table](https://help.libreoffice.org/latest/en-US/text/shared/00/00000021.html) |
| ods | `.ods`, `.ots` | OpenDocument spreadsheet and spreadsheet template | [LibreOffice OpenDocument extension table](https://help.libreoffice.org/latest/en-US/text/shared/00/00000021.html) |
| odt | `.odt`, `.ott` | OpenDocument text and text template | [LibreOffice OpenDocument extension table](https://help.libreoffice.org/latest/en-US/text/shared/00/00000021.html) |
| pages | `.pages` | Apple Pages document import convention | [LibreOffice import filters](https://help.libreoffice.org/latest/en-US/text/shared/guide/convertfilters.html) |
| pdf | `.pdf` | Registered PDF filename convention | [IANA PDF registration](https://www.iana.org/assignments/media-types/application/pdf) |
| postscript | `.ps` | PostScript filename convention; evidence retrieved as an indexed excerpt | [Adobe Acrobat X Pro help](https://helpx.adobe.com/archive/acrobat/X/pro/acrobat_X_pro_help.pdf) |
| ppt | `.ppt`, `.pot`, `.pps` | Legacy presentation, template and slideshow | [Microsoft Office formats](https://learn.microsoft.com/en-us/office/compatibility/office-file-format-reference) |
| pptx | `.pptx`, `.pptm`, `.potx`, `.potm`, `.ppsx`, `.ppsm` | Modern presentation/template/slideshow variants, including macro-enabled forms | [Microsoft Office formats](https://learn.microsoft.com/en-us/office/compatibility/office-file-format-reference) |
| rtf | `.rtf` | Rich Text Format exchange convention | [Microsoft Office formats](https://learn.microsoft.com/en-us/office/compatibility/office-file-format-reference) |
| srt | `.srt` | SubRip subtitle output convention | [FFmpeg SubRip output declaration](https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/libavformat/srtenc.c) |
| vcard | `.vcf`, `.vcard` | Two vCard filename conventions; neither proves a particular version | [RFC 6350 section 10.1](https://www.rfc-editor.org/rfc/rfc6350.html#section-10.1) |
| webvtt | `.vtt` | Timed-text filename convention in the WebVTT draft | [W3C WebVTT section 10.1](https://www.w3.org/TR/webvtt1/) |
| xls | `.xls`, `.xlt` | Legacy Excel workbook and template | [Microsoft Office formats](https://learn.microsoft.com/en-us/office/compatibility/office-file-format-reference) |
| xlsb | `.xlsb` | Modern binary workbook, distinct from XML workbook forms | [Microsoft Office formats](https://learn.microsoft.com/en-us/office/compatibility/office-file-format-reference) |
| xlsx | `.xlsx`, `.xlsm`, `.xltx`, `.xltm` | Modern workbook/template variants, including macro-enabled forms | [Microsoft Office formats](https://learn.microsoft.com/en-us/office/compatibility/office-file-format-reference) |
| xps | `.xps`, `.oxps` | XPS and OpenXPS respectively; related but distinct formats | [Microsoft OpenXPS distinction](https://learn.microsoft.com/en-us/windows-hardware/drivers/print/driver-support-for-openxps) |


Qualifications and retrieval limits
-----------------------------------

The iCalendar registration names `.ics` for calendars. Its separate Macintosh
file-type code `iCal` is not evidence of a `.ical` filename extension. Talk Calendar's
documented default export, `talkcalendar.ical`, supplies a concrete implementer
convention for the second existing alias. This does not make that suffix an IETF
registration or establish all calendar variants.

Microsoft's table distinguishes template, slideshow and macro-enabled forms.
Macro-enabled names indicate a format capability, not observed macros in an
individual file. Strict and other Open XML profiles can share extensions.
The catalog grouping does not declare their internal structures interchangeable.

LibreOffice's Apple import filters establish the listed suffix associations,
not compatibility with every historical Apple package or file generation.
Kindle/Mobipocket suffixes do not prove DRM status or current reader support.
PRC remains a qualified association; this review does not enumerate every
alternative PRC meaning. The [earlier alias review](catalog-alias-review.md)
retains the separate meanings already cataloged for shared suffixes such as `.key`.

The subtitle sources are inspected implementation declarations. No source code
is copied or executed. WebVTT's draft names `.vtt`; this review does not assert
completed IANA registration from the draft alone. Adobe's indexed Acrobat help
excerpt explicitly recommends `.ps` for PostScript files; direct PDF retrieval
failed, so the complete manual was not reviewed. A failed retrieval does not
establish a broken public link.

No new aliases, MIME claims, content detectors or conversion permissions are
added. The historical source-retrieval audit remains unchanged. Other catalog
families, competing meanings and deeper version/structure acceptance retain
their separate scope. Provenance review is not redistribution approval.


Verification
------------

The review table is reconciled against the current 31 records and 58 extension
associations, with no missing, extra or duplicate IDs or associations. The catalog
stays byte-identical at SHA-256
`F578EB98CA1C824FD36ACF9F614F42001E3A59FC1FDFAF0E01C93D526E14F047`.
The reconciliation receipt is `.codex-temp/catalog-document-alias-review.json`.
Repository documentation/link/whitespace validation is recorded in
`.codex-temp/catalog-document-alias-repository.log`.

No product source or packaged file changes. This documentation review requires
no runtime rebuild and claims no new worker, visual, accessibility, installation
or packaging acceptance. Reserved candidate 1.0.5 remains unchanged.
