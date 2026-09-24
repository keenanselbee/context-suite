Expanded Release Archive Composition
===================================

Status: archive composition and engine verification implemented and exercised
on 2026-09-23. This is packaging evidence, not the newly versioned candidate or
the final A01-A08 execution replay. Reserved version 1.1.1 remains unchanged.


Candidate workflow
------------------

`tools/release/New-ReleaseCandidate.ps1` now forwards the seven selected engine
inputs to the production builder:

```powershell
./tools/release/New-ReleaseCandidate.ps1 -AllowDirty `
    -AudioDistributionDirectory '<reviewed expanded audio distribution>' `
    -VoiceDistributionDirectory '<reviewed Windows Voice distribution>' `
    -QpdfPreparedDirectory '<prepared qpdf and validator host>' `
    -PdfiumPreparedDirectory '<prepared PDFium and renderer host>' `
    -QpdfSourceArchive '<pinned qpdf source archive>' `
    -OfficeEngineDirectory '<complete reviewed Office engine in repository scratch>' `
    -HeifDistributionDirectory '<reviewed HEIF distribution>'
```

`-AllowDirty` records local verification provenance; it does not turn unrelated
uncommitted changes into an exact committed-source release. Existing production
checks still require complete PDF inputs for Office and expanded audio for Voice.
A real invocation reserves the next product version before building. Do not run
it while Version.props still names a previously reserved version.

The wrapper verifies and reuses DDS instead of automatically rebuilding it.
The existing DDS guard compares the native DLL, license, pinned upstream archive,
bridge source and build definition. A mismatch stops before production staging;
use the explicit DDS build command only when those inputs need rebuilding.
Office and the other selected engine bundles retain their existing exact pins.

Candidate managed builds now use
`.codex-temp/managed-staging-<id>/bin` and `obj`, including referenced private
projects. The new `ContextSuiteManagedBuildRoot` property retains the existing
`artifacts/managed` default for ordinary builds. The production builder refuses
an already existing candidate managed directory before reserving a version.
This prevents stale development output from being copied into a fresh package
without deleting or cleaning the user's development directories.

The manifest records `optionalEngines`: audio profile (`none`, `curated` or
`expanded`) and PDF, Office, HEIF and Voice Boolean selections. Verification
checks that record against the actual engine directories and pinned audio
probe, then runs the owning full payload verifiers. Missing or inconsistent
selection cannot silently turn an expanded candidate into a base-only check.
Historical base manifests without the new field remain supported.

Read-only engine verification now admits `artifacts/release-candidates` as well
as existing scratch/staging roots. Engine staging writes still reject release
directories. Directory traversal, linked paths, changed identities and unknown
engine membership keep their existing guards. The archive copier preserves
directories as well as files and excludes symbols and the superseded production
inventory. The ZIP is extracted and verified again before its hash is reported.


Actual composition evidence
---------------------------

Evidence is retained at
`.codex-temp/release-composition/6b4d5c8c504f40c6a029a30d1a7631f9`.
The fixture combines retained verified application/worker files, current image,
expanded audio/Voice, PDF, Office runtime04 and HEIF payloads, all notice
supplements, and a fresh build of the authored shell. No DDS or Office build
was run.

The actual archive wrapper copied, inventoried, compressed, extracted and
verified this payload. Its production-build call and Git-status inputs were
stubbed to supply the retained fixture and avoid reserving a product version;
the archive code, prerequisite generation, hashes, engine selection and payload
verifiers were real. All seven engine arguments reached the build boundary.
The resulting manifest explicitly records dirty sources. This fixture must
not be mistaken for a fresh production build or final acceptance result.

| Measurement | Result |
| --- | ---: |
| Application payload files | 3,972 |
| Application payload bytes, excluding archive wrapper/manifest | 611,831,371 |
| Complete ZIP bytes | 241,264,492 |
| ZIP SHA-256 | `C6B56812B8BBCED5A267530ACF8A44690F6E9DD9E4FB2105593EB996D58EDD34` |

`composition.json` records all inputs and the archive location. The extracted
copy at `artifacts/release-candidates/fe55094e602545dc93e25498e1f01934/ContextSuite`
also passes the actual release-candidate verifier in its new read-only location.
Every payload file agrees with the prepared input inventory. This measurement
is more current than the earlier approximately 1.58-GB Office candidate, but
the final fresh version still needs its own size and performance measurements.

`guards.json` records 23 passing refusal checks: all four engine stagers still
reject release-directory writes; root/traversal checks remain active; missing,
inconsistent or incorrectly typed selections are rejected; changing a notice
and its manifest hash together does not bypass the engine pin. A changed DDS
cache stops before the production-build boundary and does not trigger a rebuild.
The disposable candidate is restored after each change. A later explicit
audio-array type check and complete positive verification are recorded in
`final-verification.json`; JSON arrays cannot exploit PowerShell's collection
comparison to masquerade as the selected profile string.

Separately, a real `ContextSuite.Production.slnx` Release build using the new
managed root succeeds with zero warnings and errors. Its application and worker
outputs have separate project paths and the expected dependency files; ordinary
Core build paths still resolve to the original development directories. The
pre-existing managed-directory refusal leaves all version-receipt hashes
unchanged and creates no production stage. These results are retained in
`managed-build.log` and `managed-preflight.json`.

No application UI, media processing, installer, Windows profile, registration,
signing, live licensing or publishing was exercised. The
[finite acceptance contract](local-candidate-acceptance.md) still requires the
fresh candidate's exact-byte execution suites and performance workloads.
