Context Suite Changelog
=======================

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
