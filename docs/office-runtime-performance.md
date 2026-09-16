Office Runtime Verification Performance
=======================================

Status: file-opening improvement implemented; native safety, timing, application
export and independent PDF comparisons pass. Office release
adoption and broader fidelity remain incomplete.

The optional Office worker verifies the entire pinned runtime for every export.
The application stops that worker and revokes native access before independent
PDF validation. Runtime verification therefore cannot simply be cached in the
worker across documents without changing the established cleanup boundary.

The original file-opening path checked every ancestor by pathname for every
runtime file, even though runtime verification already held verified directory
handles. The new Office-specific path reuses those directory handles within
one verification. It still checks canonical local paths, opens every file with
read sharing only and without following a leaf reparse point, and validates each
opened handle's resolved path, attributes and link count. Generic media-file
opening and other engines are unchanged.

All 19,332 files and 1,517,294,910 bytes are still hashed against the pinned
inventory. Exact directory membership is checked before and after execution.
The hash algorithm, cancellation checks, handle lifetime, per-document worker
shutdown and AppContainer profile lifecycle are unchanged. There is no persistent
trust cache or reduced verification policy.

Local timing evidence
---------------------

`Measure-OfficeRuntime.py` acquires the complete pinned lease, then measures
ordinary file reopening and two hash strategies on the already verified files.
The strategies alternate first/second order per file, and both must match every
inventory hash. The retained original lease protects inputs throughout. The
64-KiB block strategy is a measurement control; production hashing is unchanged.

| Measured phase | Before | After |
| --- | ---: | ---: |
| Complete runtime acquisition | 18.94 s | 9.75 s |
| Ordinary reopening control | 11.01 s | 11.97 s |
| Existing asynchronous hash control | 4.94 s | 5.18 s |
| Synchronous 64-KiB hash control | 3.51 s | 3.60 s |

The measured acquisition reduction is approximately 49%. These are local cached
observations, not a timing guarantee or a cold-start benchmark. The reopening
and hash controls remain similar, while the changed acquisition path improves.
No machine-dependent timing threshold is used as a correctness test.

Before evidence: `.codex-temp/office-runtime/ae0cc63c74fa40dab3eff4bd19b6e740`.
After evidence: `.codex-temp/office-runtime/bec8deb944c9467c84dd4f51f3f7594b`.
Both bind source and executable inputs, pass all 19,332 hashes, and preserve the
runtime. No Office process or Windows profile is created by this measurement.

Native file safety
------------------

All **45 runtime contracts** pass at
`.codex-temp/office-runtime/7601d881af124a8f9b3e29e56198d740`. The fifteen new checks
cover ordinary reads, blocked writes, handle release, noncanonical/alternate
paths, hard links, existing junctions and a directory changed into a junction
after its handle has been cached. The last case is rejected by the opened file's
resolved identity. Both authored junctions are removed and their target bytes
remain unchanged. All junction and hard-link fixtures stay in repository scratch.

The existing thirty checks retain pinned host/inventory and membership failures,
cancellation, write/rename protection and cleanup after both success and failure.
The first attempted run exposed a changed exception for a missing runtime file;
the file-access classification was restored before the successful run. That
failed receipt is retained at `6fb8cff61e9b498e8ecc6e1cb3762713`.

Microsoft documents the relevant [file sharing and reparse-point flags](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilea),
[junction data layout](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_reparse_data_buffer)
and [removal operation](https://learn.microsoft.com/en-us/windows-hardware/drivers/ifs/fsctl-delete-reparse-point).
Directory handles alone are not treated as proof against later reparse changes;
the final file-handle check and complete membership checks remain required.
The original limits concerning hostile full-trust processes and pre-existing
writable memory mappings still apply.

Repeatable commands
-------------------

Run from the public root with the separate private checkout available:

```powershell
python tools/office-engine/Test-OfficeRuntime.py --package '<scratch worker>/office-engine'
python tools/office-engine/Measure-OfficeRuntime.py --package '<scratch worker>/office-engine'
```

The existing two-receipt runtime-test mode also remains available. Both commands
create fresh evidence under `.codex-temp/office-runtime`, build the private
harness, and validate source/binary consistency. The runtime test creates only
owned scratch file/link fixtures; it does not launch Office or create profiles.
The private PDF test fixture was updated to supply the required explicit font
report field, restoring compilation of that harness; production font review is
unchanged.

Application export verification
-------------------------------

Execution `f8c3e213cc634163af7e2dd88484aa83` passes **80 application checks**.
All fifteen validated copies publish, originals retain their bytes and write
times, and only the two actively missing-font cases request review. Every native
profile and context is removed. The wrapper's final source, engine and binary
integrity checks pass.

Independent inspection `style-inspection-fd13eb3056624b83b7af53d035359d14` passes
all fifteen text/page checks, three available-font controls, eleven exact
style/control comparisons and three distinct-font controls. Its receipt is
`style-inspection-receipt-a44c811353fc439982ede04dadcf235e.json` under the execution
directory, with unchanged inputs and pinned PDF readers.

The retained `runtime-export-comparison.json` additionally verifies identical
authored package parts and exact PDF text, font names and rendered pixels against
the preceding `04f2dcae50ac4f6a8b20059604ded307` run. The only execution-source
differences are the two runtime implementation files and the wrapper's new wall
timer. Font review remains unchanged.

The new monotonic application-harness timer records **398.43 seconds**. The
earlier log's creation/last-write metadata indicates approximately **509.37
seconds**, suggesting about 22% less batch time. This comparison uses an
approximate historical baseline and includes fixture setup and checks; it is
not a controlled cold-start or general performance guarantee. Worker staging
and the wrapper's final integrity hashing are outside these application times.

Release worker, private harness and inspection builds complete with zero warnings
and errors. Public boundary, system-theme policy and 162-document checks pass.
The reserved 1.1.0 payload, native host and installed state remain unchanged.
Broader Office fidelity, integrated packaging and actual visible/accessibility
acceptance remain open.
