Analyze Memory-Mapped File Acceptance
=====================================

Status: 37 local mapped-file checks pass, 2026-09-14. The existing production
read lease already enforces the required behavior; this checkpoint changes tests
and documentation only. Candidate 1.0.5 remains the latest reserved payload.


Why this case matters
---------------------

A program can retain a mapped view after closing its original file handle.
Checking only ordinary open handles is therefore insufficient evidence of stable
reading. Microsoft also notes that mapped writes may leave the last-write timestamp
unchanged in its [file mapping documentation](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createfilemappinga).
Length/time comparisons alone must not be described as detecting every such write.

Analyze opens a regular file with read sharing and no write/delete sharing. The
[CreateFileW sharing contract](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)
specifically includes existing writable mappings when enforcing write sharing.
The tests below exercise this through `FileAnalysisReader` and the actual
application view model. They do not substitute a timestamp check for that lease.


Actual coverage
---------------

`AnalysisMappingContracts` generates a small PDF-header fixture for each case.
It is deliberately not a complete PDF, and the injected deeper probe is a test
callback rather than document rendering. Source streams are explicitly closed
before Analyze starts; the tests check their safe handles are closed.

| Existing mapping | Observed Analyze behavior |
| --- | --- |
| Read-only | Reads the unchanged snapshot; a later writer cannot open during the deeper-probe callback |
| Copy-on-write, with a modified private view | Reads the unchanged underlying file; private view bytes do not enter the report |
| Writable mapping and view | Refuses with Windows sharing error 32 before the deeper probe |
| Writable view after both original file and mapping handles close | Still refuses with sharing error 32 before the deeper probe |

The writable cases modify an authored byte through their views, and a reader
with compatible sharing confirms those file-backed changes are visible. This
proves a live mapping is being tested, not an inert object or inaccessible path.
Analyze preserves the baseline bytes and write timestamp in every case. After
all authored views close, exclusive reopening and a new Analyze call succeed.

Both writable cases also enter actual mixed Analyze batches. Their first rows
show "Another program is using this file" guidance, while the following ordinary
file succeeds. No output is published and transformation retry stays unavailable.
A nonexistent worker path confirms these header-only batches need no engine.


Evidence and reproduction
-------------------------

- **2,809 foundation contracts pass**, including these **37 new checks**.
- Release test-host build passes with zero warnings/errors.
- Local environment: Windows build 26200, x64 application, NTFS on C:.
- Observations and authored files are retained under
  `.codex-temp/foundation-tests/analysis-mapping-9f8d88ddebee42e995c91c7d63b77479`.
  The log is `.codex-temp/analysis-mapping-foundation.log`.

The first standalone investigation passed an empty mapping name through the
PowerShell binder and failed before creating a mapping; it is not counted as
evidence. The first focused test-host run caught a capitalization mismatch in
its expected sharing guidance; the corrected foundation run above covers all
four cases. Its earlier log is `.codex-temp/analysis-mapping-focused.log`.

```powershell
.\tools\Test-Foundation.ps1 -Configuration Release
dotnet artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.dll --analysis-mapping '<absolute scratch directory>'
```

Each invocation generates a unique evidence directory. It closes only its own
file handles/mappings and does not install software, register Explorer commands,
recycle files, execute document content or contact live services.


Remaining acceptance
--------------------

This closes the local NTFS mapping-sharing cases above, not the entire advertised
regular-file matrix. Remote filesystems, non-cooperative drivers, metadata-query
stalls, cloud hydration and reparse-path races still need dedicated acceptance.
The test mappings are in-process; it does not claim a separate editor/application
compatibility matrix. Visible report delivery, keyboard interaction, screen
readers, themes/DPI and installed Explorer were not tested. The earlier
[I/O cancellation evidence](analyze-io-cancellation.md) and
[broad support goal](broad-file-support-goal.md) retain their separate boundaries.
