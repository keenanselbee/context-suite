Office Host Candidate
=====================

The private fixed-purpose native host now exports the three authored modern
Office fixtures inside the disposable AppContainer. Its terminal reply binds a
completed export to the request, policy, source and output hashes. It remains a
component candidate: no customer Office command, worker adapter, production
launcher or packaged Office runtime is enabled by this checkpoint.


Host and launcher responsibilities
----------------------------------

The host takes no command arguments. Before opening configured media paths or
loading engine DLLs, it requires an AppContainer token with zero capabilities and
the SID derived from the configured owned profile name. The launcher must verify
the pinned runtime, source admission and complete path chains, retain the required
leases, supply a bounded job and own Windows profile cleanup. These launcher duties
are exercised by an evaluation supervisor here; customer orchestration is pending.

The configured source is a nonempty read-only, single-link regular DOCX, XLSX or
PPTX file, at most 64 MiB. The host checks its package prefix and keeps a read lease
through export and hashing. This is not complete document admission; the public
Open XML preflight and remaining content checks belong before launch. The output
must be a fresh PDF path and the engine profile a fresh directory.

Path configuration includes the volume serial number and 128-bit file identifier
for the source, output parent, profile parent and engine program directory. The
full-trust supervisor verifies normalized paths and non-reparse ancestors under
retained handles. The host compares its opened handles to these identities and
rejects reparse targets. This uses Windows' documented
[file identity comparison](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info).
The ancestor leases remain the launcher's responsibility; an identity string by
itself does not prevent later path replacement.

An earlier ancestor-walking guard failed because the restricted process could
not inspect ungranted ancestors. A following normalized DOS-path query failed
with Windows error 5 on a granted handle. The passing host uses handle identities
instead of expanding filesystem grants or accepting unresolved path strings.

The host creates fixed settings disabling macros, active content, Python, update
checks/downloads and OpenCL. The export recipe requests PDF 1.7, lossless image
compression without downsampling, tags and bookmarks, and excludes notes, hidden
slides, forms, source attachment, encryption and tracked-change markup. These
settings are policy, not proof of isolation or fidelity for all document features.
Word's selected final-text policy remains required. Excel requires an explicit
`cached` or `recalculate` request; only `cached` runs in this checkpoint, and the
product default remains undecided.

Initialization and document work run through the checked Windows main loop.
The terminal reply is written only after document export, engine destruction,
source revalidation and output hashing. Engine diagnostics use stderr; stdout is
reserved for one bounded JSON reply. Output must be a nonempty single-link PDF
candidate of at most 128 MiB. This is a post-export acceptance bound, **not** a
live disk quota. The launcher must enforce timeout, cancellation and resource
bounds; independent PDF validation must precede app-owned publication.

`OfficeHostProtocol` rejects nonzero completion, missing/truncated/oversized or
contaminated replies, duplicate/unknown fields, mismatched requests, policies,
source identities and invalid output identities. A valid reply is neither a PDF
validation result nor permission to publish. The earlier
[owner-crash evidence](office-embedded-recovery.md#owner-crash-during-export)
still requires checking the owning operation's outcome.


Verification and retained evidence
----------------------------------

The foundation suite passes **2,986 contracts**, including 120 host-protocol
checks. The native host builds as Windows x64 C++20 with `/W4 /WX`; the updated
managed evaluator builds successfully. `tools/office-engine/Build-OfficeHost.ps1`
snapshots the three private source files before compilation and records source
and executable hashes under an isolated build directory. It adopts no runtime.

The source-bound passing host build is
`.codex-temp/office-host/b6f5f9468aaa45d5981aeb8e0e31a6ea`.
An ordinary-token launch refuses before profile/output creation. Native evaluation
cases beneath `.codex-temp/office-isolation/1a880d93be994b9a9a3467bb77aa1ba3`
record:

- `cs48`: Word, Excel and PowerPoint exports complete with clean terminal replies.
  Output sizes are 44,319, 26,915 and 26,626 bytes, respectively. Independent qpdf
  and PDFium checks pass structure, authored text, page count and geometry.
  Rendered pixels match the retained `cs41` ordinary controls exactly. Those
  controls are re-inspected existing outputs, not new ordinary host exports.
- `cs49`: empty Word source, wrong Excel profile identity and preexisting
  PowerPoint output are refused. No success reply or engine profile is created;
  the existing output remains unchanged.
- `cs50`: incorrect source, output-parent and program-directory identities are
  refused, one per format, before engine profile/output creation.

The positive/refusal supervisor build is
`host-identity-75af3f1237154102b5f709adbaab8394`; the identity-refusal build is
`host-refusal-8279f388f2b84b788b4e1883d424b525`. Each retains its exact source and
build receipt. Independent PDF results are in
`inspection-fe85a9b3bb044020b0ff65760e2b8676/results.json`. Host completion hashes
and actual settings are checked separately in
`.codex-temp/office-host-completions.json`. Earlier failed `cs42`, `cs44`, `cs45`
and path-only `cs47` diagnostics are retained, not counted as passes.

The final reconciliation is `.codex-temp/office-host-final-verification.json`.
It checks all nine attempt results, read-only source bytes/write times, 16
exclusive file opens after cleanup, unchanged existing output and removal of the
owned Windows profile folder and registry mapping. All 19,332 runtime files in
both source and copy and exact copied membership are checked against the retained
inventory. No application installation, Explorer registration or live licensing
is involved.

The inspector's optional `-ControlCaseName` selects an explicitly retained control
case; it does not run Office or invent an ordinary-host success. The host-specific
`--inspect-host-completions` evaluator checks the three actual replies separately.


Next integration gates
----------------------

Implement the production launcher and private adapter, pinned payload composition,
complete source admission, worker transport, cancellation/owner-loss handling,
independent validation and app-owned transactional PDF-copy publication. Test
resource/disk failures and candidate preservation across these boundaries.
Resolve spreadsheet calculation and missing-font behavior; verify the selected
Word policy and broader fidelity through the new host. Content/network isolation
and engine redistribution/adoption remain separate gates. No desktop, screen-reader,
theme/DPI, installed lifecycle or commercial-release acceptance is claimed.

The next [managed launcher checkpoint](office-managed-launcher.md) implements the
contained process boundary and corrects CRT environment initialization exposed
by a clean launch environment. It retains production profile/grant ownership,
worker/publication and broader acceptance as separate remaining work.
