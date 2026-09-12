Document Catalog Description Review
===================================

Catalog revision 2026-09-11.6 reviews the plain-language purposes of 31 existing
records: all 29 Document records plus the separately grouped PPTX and XLSX records.
Fourteen receive clearer wording or more specific references. The other seventeen
retain their descriptions and sources. The catalog still contains 243 records.

This is factual description maintenance. IDs, display names, family groupings,
extensions, exact filenames, MIME identifiers and text hints are unchanged.
Recognition, detailed parsing and conversion permissions keep their existing
boundaries. Required Word/Excel/PowerPoint-to-PDF implementation remains separate;
a reviewed catalog description does not make that converter available.


Reviewed purposes and provenance
--------------------------------

The following review was performed on 2026-09-11. Descriptions are independently
written from the cited primary documentation and implementation manuals. No
database, source prose, parser code or reference-tree material was imported.

| Catalog IDs | Purpose or distinction reviewed | Documentation |
| --- | --- | --- |
| ass | Subtitle timing and styled presentation | [Aegisub ASS tags](https://aegisub.org/docs/latest/ass_tags/), [Matroska subtitle formats](https://www.matroska.org/technical/subtitles.html) |
| chm | Packaged HTML help with navigation | [Microsoft HTML Help](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/htmlhelp/microsoft-html-help-1-4-sdk) |
| djvu | Compressed scanned pages; optional searchable text | [DjVu overview](https://djvu.org/), [calibre embedded-text support](https://manual.calibre-ebook.com/faq.html) |
| doc, docx | Editable text documents and templates; binary and XML families | [Microsoft DOC specification](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/ccd7b486-7881-484c-a137-51170af7cc22), [Office format table](https://learn.microsoft.com/en-us/office/compatibility/office-file-format-reference), [ECMA-376](https://ecma-international.org/publications-and-standards/standards/ecma-376/) |
| eml | Saved message headers/body with possible MIME attachments | [RFC 5322](https://www.rfc-editor.org/rfc/rfc5322.html), [MIME message bodies](https://www.rfc-editor.org/rfc/rfc2045.html) |
| epub | Packaged electronic publications with text, images and navigation | [EPUB 3.3](https://www.w3.org/TR/epub-33/) |
| ical | Exchange of calendar events, tasks and scheduling data | [RFC 5545](https://www.rfc-editor.org/rfc/rfc5545.html) |
| keynote | Editable Apple slides; shared suffix remains qualified | [Keynote guide](https://support.apple.com/guide/keynote/welcome/mac), [prior alias review](catalog-alias-review.md) |
| kindle, mobi | E-books with different internal variants; PRC has broader uses | [calibre format distinctions](https://manual.calibre-ebook.com/faq.html), [Amazon publishing guidance](https://kdp.amazon.com/en_US/help/topic/GU72M65VRFPH43L6) |
| mhtml | HTML and associated resources stored together | [RFC 2557](https://www.rfc-editor.org/rfc/rfc2557.html) |
| msg | Saved Outlook items include appointments, contacts and tasks | [Microsoft MSG specification](https://learn.microsoft.com/en-us/openspecs/exchange_server_protocols/ms-oxmsg/b046868c-9fbf-41ae-9ffb-8de2bd4eec82) |
| numbers | Editable spreadsheet tables, calculations and charts | [Numbers guide](https://support.apple.com/guide/numbers/welcome/mac) |
| odg, odp, ods, odt | OpenDocument drawings, slides, spreadsheets and text; templates | [OASIS introduction](https://docs.oasis-open.org/office/OpenDocument/v1.3/os/part1-introduction/OpenDocument-v1.3-os-part1-introduction.html), [LibreOffice format table](https://help.libreoffice.org/latest/en-GB/text/shared/00/00000021.html) |
| pages | Word-processing and page-layout documents | [Pages guide](https://support.apple.com/guide/pages/welcome/mac) |
| pdf | Fixed page layout for sharing and printing | [Adobe PDF explanation](https://helpx.adobe.com/uk/incopy/desktop/save-export-and-print/pdf.html) |
| postscript | Instructions describing page text and graphics | [Adobe language reference, introduction and basic ideas](https://www.adobe.com/jp/print/postscript/pdfs/PLRM.pdf) |
| ppt, pptx | Presentations, templates and slideshow variants | [Microsoft PPT specification](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-ppt/6be79dde-33c1-4c1b-8ccc-4b2301c08662), [Office format table](https://learn.microsoft.com/en-us/office/compatibility/office-file-format-reference) |
| rtf | Formatted document exchange | [Microsoft format table](https://learn.microsoft.com/en-us/office/compatibility/office-file-format-reference) |
| srt | Subtitle text with playback timing | [Library of Congress description](https://www.loc.gov/preservation/digital/formats/fdd/fdd000569.shtml), retrieved through its search-indexed excerpt |
| vcard | Contact information exchange | [RFC 6350](https://www.rfc-editor.org/rfc/rfc6350.html) |
| webvtt | Timed captions, subtitles, chapters and other media text | [W3C WebVTT draft](https://www.w3.org/TR/webvtt1/) |
| xls, xlsx, xlsb | Workbooks and templates; binary versus XML storage | [Microsoft XLS specification](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-xls/cd03cb5f-ca02-4934-a391-bb674cb8aa06), [XLSB specification](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-xlsb/acc8aa92-1f02-4167-99f5-84f9f676b95a), [Office format table](https://learn.microsoft.com/en-us/office/compatibility/office-file-format-reference) |
| xps | Related XPS and OpenXPS fixed-page formats | [Microsoft distinction](https://learn.microsoft.com/en-us/windows-hardware/drivers/print/driver-support-for-openxps), [ECMA-388 OpenXPS scope](https://ecma-international.org/publications-and-standards/standards/ecma-388/) |

The changed IDs are `djvu`, `docx`, `mhtml`, `msg`, `numbers`, `pages`, `pdf`,
`postscript`, `pptx`, `rtf`, `webvtt`, `xlsb`, `xlsx` and `xps`. The MSG reference
previously used Microsoft's Office-file-formats route; the actual specification
is under Exchange-server-protocols. The replacement was retrieved successfully.
The DjVu resources URL redirects to its overview. Direct retrieval of the prior
PDF/RTF Library of Congress references failed; Adobe and Microsoft supplied the
reviewed facts. This does not establish that those old public URLs are broken.

Microsoft's format table supplies the macro/template/slideshow distinctions that
the broad ECMA-376 landing page does not explain. Macro-enabled describes a
variant's ability to store macros, not proof that a particular file contains them.
ECMA-388 defines OpenXPS, so it cannot alone establish the legacy XPS variant.
RFC 5322 describes message syntax; MIME supplies the additional body-part model.
The WebVTT reference is a draft, and only its general purpose is used here.


Limits and verification
-----------------------

This review does not certify every extension, historical format version, MIME
alias, detector or binary structure. Apple guides establish application purposes,
not all generations of their file packaging. The Kindle/Mobipocket descriptions
are historical format associations, not claims about current device compatibility,
DRM access or upload-service acceptance. PRC remains a qualified shared suffix;
no additional PRC detector or catalog candidate is added. Families outside the
listed 31 records retain their separate review scope, including HTML and BibTeX.

No document is opened in its authoring application, no external resource is
followed by Analyze, and no new engine or runtime lookup is introduced. Vendor
claims about perfect fidelity, accessibility or security are not product promises.
Description review is not permission to redistribute a database or engine.

A before/after JSON comparison confirms that only `commonUses`, `source` and
the revision changed. All **2,182 foundation contracts pass**, exercising embedded
catalog loading, lookup and separation from recognition and operations. Evidence
is retained in `.codex-temp/catalog-document-delta.json` and
`.codex-temp/catalog-document-review-foundation.log`.

Public-source boundary, system-theme policy, all 89 documentation files and both
repositories' whitespace checks pass. No duplicate description tests were added.
Previous worker, packaging and manual evidence remains historical; this edit
does not claim fresh visible, installed or full-engine acceptance.
