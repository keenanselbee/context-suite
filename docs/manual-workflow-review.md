Manual Workflow Review
======================

Status: supported headless checks; expanded H04 visual acceptance not performed.
The former `Test-LicenseWorkflow.ps1` launch wrapper was removed in the free
migration. Its `-Launch`, `-Settings`, and `-RequestManifest` examples are retired;
there is no supported replacement visual wrapper. Current source has free
admission and no License window. See [the transition goal](free-access-goal.md)
for current candidate evidence and [the acceptance contract](local-candidate-acceptance.md)
for remaining human review. Headless checks do not certify visual usability.


Supported Headless Checks
-------------------------

From the repository root, the current test-host entry point supports:

```powershell
dotnet run --project tests/ContextSuite.Application.TestHost -c Release -- --view-contracts
dotnet run --project tests/ContextSuite.Application.TestHost -c Release -- --manual-workflow-contracts
dotnet run --project tests/ContextSuite.Application.TestHost -c Release -- --verify-manual-selection '<absolute reviewed scratch manifest>'
```

The selection modes validate manifests, create fresh disposable input copies and
activation requests under .codex-temp/command-workflow, and retain evidence. They
show no window and start no worker. Selection checks do not verify a complete
payload or perform conversion. The test host is excluded from production output.

For focused application/view-model dispatch and publication contracts with an
existing verified worker, use:

```powershell
dotnet run --project tests/ContextSuite.Application.TestHost -c Release -- --direct-command-contracts '<verified isolated stage>/ContextSuite.Worker.exe'
```

This mode starts the real worker, uses disposable media and free admission, and
substitutes a retaining recycler rather than native recycling. Verify the chosen
stage with its owning payload checks first. These commands build test outputs,
not the installed production directory, and do not reserve candidate versions.
Close Context Suite and run worker/router checks sequentially; do not overlap
foundation integration or desktop harnesses.


Human Review Still Required
--------------------------

Revalidate retained manifests and fixture hashes with current source before
using them for new evidence. The eight prepared cases cover:

| Manifest | Review with free admission |
| --- | --- |
| `01-analyze.json` | Six files: PNG, DOCX, Opus, empty, unknown binary, and a deliberately unreadable PDF copy. Analyze works without access state, gives useful bounded facts, and explains unreadable/unknown cases without crashing. |
| `02-quiet-image.json` | Opaque PNG to TGA. A named copy appears with no routine options planner or metadata acknowledgement. |
| `03-matte.json` | Transparent PNG to JPEG. Require an explicit background choice, readable preview/options, and sensible focus; cancellation creates no output. |
| `04-audio-quality.json` | Generated Opus to MP3. Explain lossy-to-lossy quality loss before proceeding. Cancellation leaves the copied input intact. |
| `05-pdf-order.json` | Two distinct PNGs to one PDF. Show both filenames and the explicit page order; change the order and confirm it in the two-page output. |
| `06-office-calculation.json` | Ordinary chart-free XLSX to PDF. Show saved-values/recalculate choice and retain it through the operation. |
| `07-office-font.json` | DOCX with an actively used missing font. Explain substitution and require the relevant choice before saving the PDF. |
| `08-cancel-retry.json` | Ordinary two-page DOCX to PDF. Cancel while work is active, inspect readable status and confirm no partial published file. Retry if offered; record actual behavior rather than assuming retry is available for cancellation. |

Standalone Settings adds tool tabs, expansion/collapse, copy/overwrite wording,
Save/Cancel, notices, and focus restoration. Preference persistence across launches
needs separate observation. Saving overwrite consent does not authorize native
recycling during review; use copies. Licensing interaction is no longer a case.

Use [desktop smoke guidance](desktop-smoke-tests.md) for the supported opt-in image
UI sequence and its limits. It does not provide the complete eight-case manual
launcher. Restoring a bounded current visual launcher and observing H04 remain
acceptance work; do not substitute ad hoc production activation that could use
real settings or originals.

Record manifest and worker inventory hashes, disposable profile location, Windows
theme/scaling, outputs, original preservation, prompt clarity, Tab/Shift+Tab,
Escape, resizing, and status. Record screenshots or reproduction steps for
failures. Screen-reader announcements, 100%/150%/200% scaling, and two-monitor
movement remain unverified unless exercised. Installed Explorer and native
installer lifecycle retain separate acceptance gates.


Manifest Boundary
-----------------

The test-only selection reader accepts one existing public command and 1-32
inputs, each at most 64 MiB and at most 256 MiB combined. Manifests are at most
64 KiB. Manifest/input paths must be absolute and inside this checkout's
.codex-temp; traversal, reparse links, changed SHA-256, unknown JSON fields, and
invalid commands are rejected. `Locked` is optional and only valid for Analyze.
Use generated, reviewed fixtures.

```json
{
  "Operation": "convert",
  "Action": "pdf",
  "Files": [
    { "Path": "<absolute scratch fixture path>", "Sha256": "<uppercase SHA-256>", "Locked": false }
  ]
}
```

Copies receive ordered `01 -`, `02 -` prefixes. Unreadable Analyze controls lock
only disposable copies for the selection lifetime. Production does not read the
test-only selection environment variable. The old wrapper's worker allowlist and
`-AllowScratchWorker` flag are not current host CLI options.


Historical 1.1.2 Preparation
----------------------------

Local-only cases remain at
.codex-temp/candidate-112-workflows-aa99d8233a5d4fb296792fffbd93b40d/cases.
The matching stage was artifacts/production-staging/5d97eab3d78e4e6199717450ad835264.
All 3,972 released files matched that stage; its full inventory passed. The
then-current source-linked host used the shipped Core DLL and passed ten
simulated-license checks, fifteen selection checks, and eight headless
copy/request preflights. No window or media worker ran. Those licensing results
are historical, not current free-source evidence.

The [review collection](document-review.md#current-candidate-collection-2026-09-24)
retains review.html and workflows.html. Original selections remain at the
local-only path .codex-temp/manual-workflows/a8da8f5f670043debd81ec1eedfb51c1;
preserve fixtures, manifests, and receipts. Local-only preflight log:
.codex-temp/manual-workflow-preflight.log. Recorded worker inventory SHA-256:
ABA4BCE94D364BC6327BB8545D884D636D0B2A31E031DEBF8186E414D10AE5A6.
The last recorded Computer Use attempt failed with native-pipe error 2; visible
H04 review was not performed. Neither that failure nor historical headless passes
establish current desktop availability or acceptance.
