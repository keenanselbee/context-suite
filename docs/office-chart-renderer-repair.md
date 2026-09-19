Office Chart Renderer Repair
============================

Status: draft source patch prepared and statically checked; not compiled,
executed, adopted or accepted. The [chart-loss regression](excel-layout-acceptance.md)
still fails on the current candidate. This proposal keeps chart fidelity in the
[active goal](broad-file-support-goal.md) and keeps `DisableActiveContent=true`.


Evidence and proposed change
----------------------------

The retained runtime's `program/version.ini` identifies build
`8221e31b3ac356a1623c672912a3d2b492f7e3d1`, LibreOffice 26.2.6. The
[source manifest](../tools/office-engine/patches/chart-source.json) records nine
independently downloaded upstream files at that exact commit with byte lengths
and SHA-256 identities. The originals remain in
`.codex-temp/office-chart-source-175b82e333ab4edcad90919390a6e749/originals`.
No reference repository or private implementation source is included.

The pinned configuration declares one current internal chart class,
`12DCAE26-281F-416F-A234-C3086127382E`, with the internal embedded-object factory
and `com.sun.star.chart2.ChartDocument` service. The
[draft patch](../tools/office-engine/patches/allow-native-chart-rendering.patch)
changes three upstream embedded-object files:

- Allow only that exact chart class through the **new-object** factory path.
  Require the internal factory; do not allow a Windows OLE fallback.
- Permit only `RUNNING` for that class and chart service, with no link URL.
  Read the object fields while holding the existing mutex.
- Keep the existing permission to return objects to `LOADED`. Do not permit
  active, in-place-active or UI-active states under the restrictive policy.

Existing-entry, media-descriptor and user-init import paths stay blocked.
Windows OLE factories, DDE, external-link update paths, macro controls and Python
controls are unchanged. This describes the proposed source change, not tested
runtime enforcement. A forged class ID, unexpected factory/service, linked chart
or indirect activation must still fail in the eventual native tests.

The upstream setting blocks both OLE and DDE. The separate `DisableOLEAutomation`
setting only controls its automation service entry point; it is not an equivalent
replacement. The existing document preflight is not a whole-package safety scan.
Neither turning off the broad control nor trusting the file extension is the
selected repair.


Static verification
-------------------

```powershell
python -B tools/office-engine/Test-OfficeChartPatch.py `
  --source-id 175b82e333ab4edcad90919390a6e749
```

The tool validates the exact source snapshot, applies the patch to a fresh owned
copy, checks the chart's configured factory/service, compares untouched refusal
function bodies, and reverses a separate copy back to the original bytes. It
does not compile, install, change production pins or load the renderer.

Ten checks pass in
`.codex-temp/office-chart-patch-4725775edd8d4f008a3716a5a231530c/results.json`.
The earlier attempt `office-chart-patch-4244ed3959634eb29941f0905aa08a16`
failed exact restoration because Git converted upstream LF to CRLF. It remains
retained. Per-command Git settings now preserve upstream line endings; no global
Git configuration changed. The commit whitespace check also caught a trailing
blank context line in the patch; extending that hunk to the next source line
removed the warning without changing the proposed C++ bytes. The fresh static
replay above passes. These source checks do not prove native behavior.


Build prerequisites and authorization
------------------------------------

The pinned upstream README documents a Windows build using WSL as a helper and
Visual Studio. Its configure script also contains a Cygwin route and recognizes
Visual Studio 2022 and 2026. This machine has both Visual Studio generations, but
`wsl --list --quiet` reports WSL is not installed. No Cygwin was found on PATH or
at `C:\cygwin64`; this is not an exhaustive search of other drives.

The proposed route is WSL 2 with Ubuntu, following the upstream documented build
route. The initial setup command is `wsl --install --distribution Ubuntu --no-launch`.
It needs separate owner authorization under the existing no-install instruction;
an administrator or restart requirement is handled by the owner, never an
automatic restart. Compiler/build dependencies inside Ubuntu must be recorded
before use. No installation or Windows feature change has been performed.

Build sources and outputs should stay in repository-owned scratch, using the
upstream path handling for the repository's space-containing Windows path.
The observed C: free space is about 83.8 GB before source preparation; this is
not a proven sufficient build budget. Determine download/extraction/build space
requirements before starting the large build. Do not delete retained evidence
to make room or modify the installed Context Suite app, Explorer registrations,
the reserved 1.1.1 candidate or existing engine payloads.


Required execution before adoption
---------------------------------

1. Compile the exact patched source with a recorded toolchain/configuration and
   retain a reproducible source/runtime identity. A clean patch application does
   not prove compilation, linkage or redistribution readiness.
2. Exercise factory and state transitions through actual native APIs: the exact
   internal chart is allowed only on the intended path; wrong IDs, service or
   factory mappings, links, imported binary objects and interactive states fail.
   Retain positive controls so unavailable components cannot masquerade as denial.
3. Verify generated DDE/OLE/external-link and macro controls under the real
   restricted host, including process/file/network boundaries and cleanup.
   Preserve the unresolved network-enforcement gate rather than treating a
   timeout as denial.
4. Run the unchanged four-case D07 application/PDF matrix with the patched
   isolated engine and restrictive profile. Add Word/PowerPoint chart checks;
   repeat relevant calculation, revision, font, image and interruption regressions.
5. Review source delivery/notices, packaging impact, performance and human pages
   before changing native identities or composing the final combined candidate.

If this proposal needs broader object permissions to render, stop and review the
design rather than expand the exception to arbitrary embedded content. Other
acceptance work can continue while build prerequisites await authorization.
