Context Suite Changelog
=======================

1.0.4 - Local candidate, 2026-09-14
-----------------------------------

- Preserve supported embedded ID3 tags and PNG/JPEG artwork when converting WAV
  to FLAC, MP3, Vorbis, Opus and representable M4A. Retain picture bytes, labels,
  descriptions and order, source samples and required quality decisions.
- Keep duplicate metadata stores, unsupported chunks and malformed tags blocked.
  This adds WAV source handling; WAV artwork output remains unfinished.


1.0.3 - Local candidate, 2026-09-14
-----------------------------------

- Preserve supported PNG/JPEG artwork when converting FLAC, MP3, Vorbis and Opus
  to M4A. Keep image bytes and order, descriptive tags and validated audio.
  Refuse picture labels or descriptions that the M4A cover format cannot retain.
- Retain fast-start placement and repair audio chunk offsets after adding covers.
  Existing same-format no-op and required audio-quality prompts remain unchanged.


1.0.2 - Local candidate, 2026-09-14
-----------------------------------

- Apply PDF page scaling when converting to PNG at the fixed 150 DPI. Read each
  page's UserUnit through the selected qpdf parser and apply size limits to the
  scaled dimensions. Keep inherited crop/rotation behavior and original files.
- Package and verify qpdf's existing runtime dependencies with the PDF renderer.
  These remain optional local candidates; release adoption is separate.


1.0.1 - Local candidate, 2026-09-14
-----------------------------------

- Add PNG/JPEG cover preservation when converting FLAC, Vorbis, Opus and M4A to
  MP3. Retain picture bytes, type, description and order; refuse conflicting
  geometry, duplicate descriptions and unsupported metadata rather than changing
  them silently. Audio quality decisions remain explicit.
- Verify Word, Excel and PowerPoint PDF export interruption and same-profile
  recovery in the isolated evaluation harness. The customer Office converter
  and filesystem/network isolation remain incomplete.
- Establish `Version.props` as the product-version source. Managed assembly/file
  versions and the three package templates use its four-component Windows form.
  Candidate staging reserves each version before building and retains its receipt
  and inventory identity. Installation and registration are separate actions.


Earlier development snapshots
-----------------------------

Earlier managed development outputs used the SDK's implicit 1.0.0 version; the
three shell prototype manifests used 0.1.0.0. Multiple historical candidate
payloads were distinguished by staging GUID and inventory. Their directories,
inventories, test evidence and Git history remain unchanged. They are historical
development snapshots, not a claim that one released version had those differing
contents. Detailed feature history remains in Git and the linked goal documents.

The [broad support goal](docs/broad-file-support-goal.md) records completed work
and remaining acceptance. Signing, installer lifecycle and live commerce remain
separate release gates.
