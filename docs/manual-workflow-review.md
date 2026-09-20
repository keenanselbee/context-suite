Manual Workflow Review
======================

Status: prepared; expanded H04 visual acceptance not performed.

Use the test-only host to review the existing application with disposable copies,
an expired trial and the simulated `TEST-ONLY` license. The installed app, Explorer
registration and real license state are outside this workflow. This implements
review preparation for the [local acceptance contract](local-candidate-acceptance.md);
headless admission checks do not certify visual usability.


Prepared local cases
--------------------

The 2026-09-19 preparation is retained under
`.codex-temp/manual-workflows/a8da8f5f670043debd81ec1eedfb51c1`.
Its `verified.json` binds eight command manifests to authored document/image
fixtures and the generated expanded-audio listening pack. All eight manifests
pass headless copy/request verification. No app window or worker ran for this
preparation. The source fixtures remain unchanged.

| Manifest | Review after activation where required |
| --- | --- |
| `01-analyze.json` | Six files: PNG, DOCX, Opus, empty, unknown binary and a deliberately unreadable PDF copy. Analyze works with expired access, gives useful bounded facts, and explains unreadable/unknown cases without crashing. |
| `02-quiet-image.json` | Opaque PNG to TGA. Expired access explains activation. Activate, close License and choose Try again. A named copy appears with no routine options planner or metadata acknowledgement. |
| `03-matte.json` | Transparent PNG to JPEG. Require an explicit background choice, readable preview/options and sensible focus; cancellation creates no output. |
| `04-audio-quality.json` | Generated Opus to MP3. Explain lossy-to-lossy quality loss before proceeding. Cancellation leaves the copied input intact. |
| `05-pdf-order.json` | Two distinct PNGs to one PDF. Show both filenames and the explicit page order; change the order and confirm it in the two-page output. |
| `06-office-calculation.json` | Ordinary chart-free XLSX to PDF. Show saved-values/recalculate choice and retain it through relevant License interactions. |
| `07-office-font.json` | DOCX with an actively used missing font. Explain substitution and require the relevant choice before saving the PDF. |
| `08-cancel-retry.json` | Ordinary two-page DOCX to PDF. Cancel while work is active, inspect readable status and confirm no partial published file. Retry if offered; record actual behavior rather than assuming retry is available for cancellation. |

The two Office choice cases belong to one H04 group. Standalone Settings/License
is the remaining group. These small fixtures do not cover every declared media
variant, hostile input, output failure or layout requirement.


Run one case
------------

Close Context Suite first; leave unrelated programs open. The harness refuses
when another Context Suite application/test host/worker is running. Avoid Explorer
commands during the session because the router is shared per Windows user.

From the repository in PowerShell:

```powershell
$worker = Join-Path (Get-Location) '.codex-temp\combined-inputs\e549e5e3509942b4b8ec8e0d6545f93a\combined\ContextSuite.Worker.exe'
$cases = Join-Path (Get-Location) '.codex-temp\manual-workflows\a8da8f5f670043debd81ec1eedfb51c1'
.\tools\Test-LicenseWorkflow.ps1 -Launch -AllowScratchWorker -WorkerPath $worker -RequestManifest (Join-Path $cases '02-quiet-image.json')
```

Change only the manifest filename for another case. Omit `-Launch` for headless
preflight. The script verifies the complete payload allowlist, engine identities
and inventory before building the host and running its checks. This can take a
few minutes for the Office runtime. It does not reserve or change a release
version. The existing formal 1.1.1 payload remains preserved.

Every launch creates a fresh isolated profile with default copy settings and an
expired trial. Use only `TEST-ONLY` in License. An activation failure/result may
need Try again before the requested prompt appears. Confirm the actual target,
files and choices survive License closing; never enter a customer key. Analyze
does not require activation. No real Polar transport is used.

The printed profile contains `Inputs` and `manual-inputs.json`, identifying each
retained source, its copied input and SHA-256. Names receive ordered `01 -`,
`02 -` prefixes so duplicate source names remain distinct. Outputs belong beside
these copies by default. Unreadable Analyze copies stay locked for the test
process lifetime; the retained original is never locked. Close the test app before
launching another case. Quiet success may close it automatically.

For standalone Settings:

```powershell
.\tools\Test-LicenseWorkflow.ps1 -Launch -Settings -AllowScratchWorker -WorkerPath $worker
```

Check tool tabs, expansion/collapse resizing, copy/overwrite wording, Save/Cancel,
License activation/deactivation and focus restoration. A fresh invocation resets
the test profile deliberately; it is not evidence that Save failed. Preference
persistence across installed-app launches remains a separate test. Reviewing or
saving the overwrite setting does not authorize executing native recycling in
this session; use copy output for the prepared transformations.


Record observations
--------------------

For each case record the manifest hash, printed profile path, worker inventory
hash, Windows theme/scaling and observed result. Note filenames and output counts,
original preservation, prompt clarity, keyboard focus, Tab/Shift+Tab, Escape,
window resizing and status text. Record screenshots or reproduction steps for
failures. Do not report a predicted prompt as one that was observed.

The Computer Use connection still fails with native-pipe error 2. No helper was
started and no configuration was changed. Actual visible H04 review is pending.
Screen-reader announcements, other themes, 100%/150%/200% scaling and two-monitor
movement remain unverified unless explicitly exercised. Installed Explorer,
native installer lifecycle and live licensing have separate acceptance gates.


Harness boundary
-----------------

`-RequestManifest` is supported only by the simulated test host. It accepts a
single existing public command and 1-32 inputs, each at most 64 MiB and at most
256 MiB combined. Manifests are at most 64 KiB. Both manifest and input paths must
be absolute paths inside this checkout's `.codex-temp`; traversal, reparse links,
changed SHA-256, unknown JSON fields and invalid commands are rejected. `Locked`
is optional and permitted only for Analyze. Use generated, reviewed fixtures.

```json
{
  "Operation": "convert",
  "Action": "pdf",
  "Files": [
    { "Path": "<absolute scratch fixture path>", "Sha256": "<uppercase SHA-256>", "Locked": false }
  ]
}
```

The test host copies and validates inputs before normal application activation;
shipping code does not read this test environment variable. `-AllowScratchWorker`
is an explicit opt-in for an inventoried repository scratch payload. Without it,
workers must be under `artifacts/production-staging`. Payload checks still require
all included optional engines to pass their existing validators.

The focused selection suite passes 15 admission/copy/lock/preservation checks.
These verify test infrastructure only; they add no human acceptance claims.
The complete expanded-worker preflight passes its existing engine validators and
inventory check, a Release test-host build with zero warnings/errors, and all ten
simulated licensing checks. Evidence is retained in
`.codex-temp/manual-workflow-preflight.log`. The worker inventory remains
`ABA4BCE94D364BC6327BB8545D884D636D0B2A31E031DEBF8186E414D10AE5A6`.
