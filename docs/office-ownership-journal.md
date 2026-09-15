Office Ownership Journal
========================

Status: bounded persistence and native profile/grant integration verified;
production context construction and restart recovery remain incomplete

The application now has an `OfficeOwnershipJournal` for the
[Office profile/grant owner](office-profile-ownership.md). It records intended
changes separately from reported completion. The journaled owner now uses those
records around native profile and permission changes. Actual typed worker exports
use it in the isolated harness. Production context construction, restart recovery
and the customer conversion coordinator remain incomplete. The journal itself
does not execute Office or publish outputs.


Recorded identity and ordering
------------------------------

The version-three record binds a validated `OfficeExportWork`, expected runtime
directory, creator process ID and creator start time. Its filename is the item
GUID followed by `.ownership`. The context must be a direct child of the expected
context root. The runtime and context cannot overlap, and the journal must remain
outside both. Reopening requires the expected context root and runtime supplied
by the application, rather than accepting arbitrary roots from the record.

The allowed sequence is:

| Step | Required evidence represented by the record |
| --- | --- |
| Profile intent, then profile created | A fresh creation attempt, followed by its matching derived AppContainer SID, expected profile path and live directory identity |
| Grant intent, then grant applied | Runtime/input read access and output/profile/temp write access, in that fixed order |
| Engine intent, then processes stopped | All five grants completed; the assigned worker's named lifetime and creator/session identity recorded before dispatch; confirmed shutdown before cleanup |
| Cleanup intent, then revoke intent/revoked | Revoke every intended grant in reverse order, including a grant with uncertain completion |
| Delete intent, then profile deleted | All intended grants revoked before recording profile deletion |

Profile creation and grant intents carry a 48-hex-character directory identity field for the volume
and file ID. Its syntax is checked; reading the journal does not verify the live
directory object. The coordinator must obtain these facts from held handles and
verify them again during recovery. Similarly, recorded process shutdown or a
profile-created entry is a report from the writer, not independent proof of the
current Windows state. An unconfirmed creation intent cannot be promoted to
confirmed ownership by replay.

Version-one and version-two records remain readable under their original schemas,
including complete lifecycles and pending engine intents. They cannot authorize
new creation, worker dispatch or appended mutations. They remain unchanged for
review; the reader never invents missing profile-directory or worker identities.
Version one also cannot authorize native profile verification because it lacks
the directory identity. Mixed-version chains and unsupported versions are refused.


Persistence and refusal behavior
--------------------------------

The file permits one writer. The parent chain is held through the existing
ordinary-directory leases, preventing directory replacement while it is open.
The native leaf open uses `FILE_FLAG_OPEN_REPARSE_POINT`; reparse and multi-link
files are refused. Creation uses create-new semantics and never overwrites an
existing record. Journals currently require local NTFS.

Each record contains a bounded JSON payload, sequence number and the complete
immutable owner identity. A SHA-256 chain covers the preceding digest, frame
length and payload. This detects corruption and reordered/replayed frames; it
is not authentication against another full-trust process running as the same
user. Limits are 32 records, 16 KiB per payload and JSON depth eight. Unknown or
duplicate fields, missing required constructor fields, unknown/numeric actions,
invalid transitions and unexpected roots are refused.

Each append uses write-through opening and `Flush(true)` before returning. The
caller must wait for that return before performing the associated mutation.
A write/flush failure prevents further appends through that instance. Reopening
requires the entire existing chain to validate. Truncated or corrupt evidence is
left unchanged for review; the reader does not truncate it to a convenient prefix.
These checks cover process interruption, not physical power-loss durability or
storage hardware behavior.


Initial persistence verification
--------------------------------

All **3,110 foundation contracts** pass, including **87 journal checks**. Evidence
and matching source hashes are retained under
`.codex-temp/office-journal-foundation-81bd842e37034a08b4f55984513c9c03`.
The journal cases cover the complete 27-record lifecycle, invalid ordering without
changed bytes, mismatched identities, hard-link refusal, protected parent lifetime,
an uncertain grant, malformed frames and two actual killed writer processes.

The killed-writer fixtures prove that a flushed creation intent remains an intent
and that a torn trailing record is refused without changing the evidence. They use
synthetic ownership facts; they never create a Windows profile or execute Office.
The initial crash test encountered a transient sharing violation after the writer
was signaled. The corrected test retries only sharing/lock errors while reading
the same file, for at most 100 attempts with 20 ms delays and an outer deadline.
The journal itself reports an unavailable file to its caller for later recovery.
The failed run is retained at
`.codex-temp/office-journal-foundation-b7d465ff151f477d9b1e4f5c342b48cd`.

The earlier ownership, real-Office and broader media-engine suites were not rerun
for that initial journal-only slice. Their dated evidence remains separate. No new
packaged payload, visible UI, installed lifecycle or commerce acceptance is claimed.


Native owner and actual exports
-------------------------------

`OfficeSandboxOwner.Create(journal)` requires the original creator process ID and
start time, an open unfaulted journal and only its initial creation-intent entry.
It confirms the actual new SID after successful creation. Existing profile
collisions remain refused. An error appending the successful-creation confirmation
retains the owner in an `OfficeOwnershipCreationException` for caller recovery;
that failure path still needs injected I/O and process-loss acceptance.

Each grant intent now obtains its directory identity from the held Windows handle,
then flushes before ACL mutation. Successful native root verification precedes
the applied record. Cleanup records intent before revocation, verifies root and
child access removal, then records completion. A journaled cleanup stops at its
first failure, retaining reverse order and the same intent for retries. Profile
deletion is recorded only after all grants have completed revocation. Once native
deletion succeeds, the old owner cannot delete a later profile even if writing
the final journal record fails.

The native ownership suite passes **93 checks** at
`.codex-temp/office-owner/5864a910dc2f4c569248ba30cf17267b`, with its matching
fixture/result stage under `.codex-temp/office-isolation`. The added cases compare
all five recorded directory identities with independent held Windows handles,
refuse an unplanned grant before mutation, preserve real permissions when shutdown
is unrecorded, retain ordered cleanup across repeated sharing failures, and
complete native profile deletion after the test releases its lock. The recorded
engine-lifetime gate in this focused case is synthetic; it launches no Office engine.

The actual Office harness separately passes **42 worker checks** at
`.codex-temp/office-worker/3d7660c88a6d43adbb5c1e74da593700`. Each DOCX/XLSX/PPTX
export uses the journaled owner. The harness records shutdown only after successful
client disposal, then awaits journaled cleanup. All three complete 27-record
lifecycles. Independent inspection at
`inspection-a8a1750fb1ae4fdf9206be95d9355216` verifies qpdf structure, authored text,
page geometry and exact PDFium control pixels for the three candidate PDFs.

`.codex-temp/office-journal-owner-verification.json` independently checks all
108 frames across four journals, all 20 live directory identities, current
worker/source identities and candidate/source hashes. All four distinct profile
identities are absent from Windows folders/mappings and 22,059 checked
runtime/context ACLs. These are ordinary exports and focused cleanup failures;
the earlier 90 active interruption checks were not rerun with journaled ownership.

All **3,113 foundation contracts** pass, including **90 journal checks**, at
`.codex-temp/office-journal-owner-foundation-6312d81b779740dd883af9aaae11829a`.
The extra persistence cases refuse creating a profile from a completed journal
or another process's unconfirmed intent. Application and private-harness Release
builds have zero warnings/errors. No packaged payload or installed state changes.


Next integration
----------------

Construct the production application-owned source/context and journal together
using the journaled owner. Bind restart recovery to the actual profile, directory objects and
terminated processes before revocation. An incomplete creation, replaced object,
hostile journal/tree or unresolved process must remain reviewable.

The profile binding below supplies the profile directory's live object identity.
The subsequent version-three dispatch binding supplies the assigned worker's named
job identity. Before automatic reclamation, verify actual application-loss recovery
using both identities. The creator PID/start time alone does not identify surviving
engine children. Preserve earlier records without inventing missing evidence.

Test actual application loss at those mutation boundaries, including interruption
during grant application/revocation and profile deletion. Add hostile leaf-link
and concurrent replacement acceptance. Finally connect independent PDF validation
and transactional copy publication, followed by the direct customer Office command.
The journal does not authorize publishing a candidate PDF or complete these gates.


Profile directory binding
-------------------------

New journals use version two. After native creation, the owner resolves Windows'
AppContainer storage location and verifies that it belongs to the expected profile
folder. It records that folder's volume/file identity from a held ordinary-directory
handle along with the actual SID. Before revoking any grant, cleanup resolves the
location again and compares the live object with the creation record. An absent,
unconfirmed or replaced object is retained for review rather than adopted.

The verified directory and its parents remain held during permission cleanup.
The handle is released immediately before native profile deletion because
[Windows requires profile-storage handles to be closed](https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-deleteappcontainerprofile).
This does not prove protection against concurrent replacement in that final gap,
or establish recovery after partially successful native deletion.

All **97 native ownership checks** pass at
`.codex-temp/office-owner/b0f4e017622149e7987dec24e1ca3c50`, with the matching
fixture/result stage under `.codex-temp/office-isolation`. The native test compares
the recorded profile identity with an independent held Windows handle. It then
moves only its disposable profile folder to a checked fresh sibling and creates
an empty replacement. Cleanup refuses before revoking grants, retaining the
original folder, mapping and all five grants. The test restores the original
folder and verifies successful cleanup after the existing sharing-failure cases.
No replacement sibling remains.

All **42 actual worker checks** pass at
`.codex-temp/office-worker/19bf203a5c114e1392c42f2e6c6a2423`. Its three DOCX/XLSX/PPTX
exports complete version-two ownership journals. Independent inspection at
`inspection-3df7407f16f444cd8bf7dc740b24fe2f` verifies qpdf structure, authored text,
page geometry and exact PDFium control pixels. These are ordinary exports; the
earlier active interruption matrix was not rerun for this profile-binding change.

`.codex-temp/office-profile-identity-verification.json` reconciles current source,
worker and candidate identities, all 108 frames, 20 live grant-directory identities,
four absent profile folders/mappings and 22,059 ACL entries without the test SIDs.
The deleted profile folders cannot be compared live afterward; the focused native
test performs that comparison before cleanup. All **3,123 foundation contracts**
pass, including **100 journal checks**, with matching source hashes at
`.codex-temp/office-profile-identity-foundation-a720943e8d2846628ef9e3505bd77cfb`.
The added journal checks cover required profile identity/path and read-only review
of both incomplete and complete version-one records. Production context creation,
restart recovery, independent publication and the customer command remain open.

The subsequent [named worker lifetime component](office-worker-lifetime.md#recoverable-named-job-component)
now verifies native owner-loss and retained-handle process shutdown independently.
That component checkpoint did not record its identity in version-two journals.
The version-three binding below connects it to request dispatch; profile recovery
still requires actual application-loss acceptance.


Worker lifetime binding before dispatch
--------------------------------------

The application client now accepts a synchronous pre-dispatch recorder for Office
requests. It creates and assigns a recoverable named job, connects to and verifies
the worker, then calls the recorder before sending any request bytes. An existing
unnamed worker is stopped before the new sequential worker is created. Calls
without this recorder retain their previous behavior; customer Office orchestration
is not yet enabled.

`RecordEngineIntent` requires the original journal creator and records the actual
named job identity in a version-three engine-intent frame. The job creator must
match the journal creator. Repeated requests may use the same recorded lifetime
without appending another frame; a different lifetime or a completed/cleaning
context is refused. A failed write leaves the journal faulted and prevents
dispatch. Recovery must revalidate both the journal and the current Windows job
state rather than treating the recorded intent as proof that an engine is running
or stopped.

All **3,169 foundation contracts** pass, including **117 journal checks** and the
29 native lifetime checks, at
`.codex-temp/office-lifetime-binding-foundation-844b83584ac048708825403262b60c2b`.
The added journal cases require the lifetime, reject another creator/object name,
permit repeated intent only for the same lifetime, refuse dispatch after shutdown,
and preserve version-one/two complete and pending-engine records without mutation.

All **97 native ownership checks** pass at
`.codex-temp/office-owner/b5617c33bea64d199f5f3f7272400f1f`; its fixture/results use
the matching identity under `.codex-temp/office-isolation`. Its engine lifetime
is a synthetic schema fixture, not evidence of native engine execution.

The actual Office harness passes **54 worker checks** at
`.codex-temp/office-worker/100f8a9d38de41cfafa7687564954ad2`. For each DOCX/XLSX/PPTX
fixture, it first starts an ordinary worker and verifies that process exits before
the journaled request. A simulated recorder exception then prevents dispatch,
stops the idle replacement worker and leaves the engine profile, PDF and journal
intent absent. The next attempt verifies native membership in the named job,
records its identity, exercises existing input refusals and completes the export.
The same lifetime remains bound across those requests. This injects a recorder
exception; it does not simulate disk-full or physical write/flush failure.

Independent inspection at `inspection-89ebd0f557964c44a7cb21ea3aed923d` verifies
qpdf structure, authored text, page geometry and exact PDFium control pixels for
all three outputs. `.codex-temp/office-lifetime-binding-verification.json` verifies
current source/worker/candidate identities, 108 journal frames, 20 live grant
directory identities, absence of the three actual named jobs and four profile
folders/mappings, and 22,059 ACL entries without their test SIDs. Application and
private-harness Release builds have zero warnings/errors.

The preceding 51-check run, before the ordinary-worker transition case was added,
is retained at `.codex-temp/office-worker/59c0395ce8da4031a56104935fa915f7`, with
independent inspection at `inspection-2f5bb7cc28c24a8896efd6b5d626e438`.
These tests verify dispatch ordering and ordinary export cleanup. They do not
establish actual Office application-loss recovery, native profile adoption,
AppContainer access denial to the named job, validated publication, customer
commands or expanded-release acceptance. The reserved production payload is unchanged.


Profile recovery from a reopened journal
---------------------------------------

`OfficeSandboxOwner.RecoverAsync` now reconstructs cleanup ownership from an open
version-three journal. It refuses a live original creator, unconfirmed creation,
completed deletion, legacy records or a faulted writer. An unlocked journal alone
does not establish that the original app has exited.

Recovery verifies and holds the recorded profile directory, stops the recorded
worker job when an engine intent exists, then records confirmed shutdown if needed.
Before changing permissions it reopens every recorded grant directory, compares
its native identity and checks its descendants. Unexpected root entries for the
profile SID are refused. Previously revoked grants must still be free of that SID,
including in descendants. Outstanding grants retain their original order and any
existing revoke intent. Only after reconstruction succeeds does recovery record
cleanup intent and return an owner that permits cleanup, never new grants.
Failure releases the newly acquired leases without invoking permission cleanup.

All **138 native ownership checks** pass at
`.codex-temp/office-owner/4e0e4f59d3fc4444998c2b69b40fc0ff`, with matching fixture
results under `.codex-temp/office-isolation`. Five actual disposable owner crashes
cover confirmed creation with no grants, partial grant setup, an active named
process group, pending revocation and a substituted grant directory. Each first
refuses recovery while the creator is still live despite its released journal.
After owner death, recovery removes its profile and recorded grants while keeping
the source snapshot. The substitution case refuses the changed object without
revoking the retained original, then completes after the fixture restores it.
The process-group case uses an authored waiting process, not the Office renderer.

The first run at `.codex-temp/office-owner/22b05c6fa5064526aefbcfbc8c40b734`
recovered four cases, then failed to rename the final fixture's grant directory
with a sharing error. The corrected fixture substitutes between journal sessions
and retries only sharing/lock failures for up to five seconds. The exact retained
profile was recovered through the new API using the harness's explicit
`--office-recovery-cleanup` mode. The failed run remains failed evidence.

`.codex-temp/office-recovery-verification.json` reconciles both runs: all 12 distinct
profile folders/mappings are absent, 244 frames across 12 journals verify with
confirmed deletion at each journal's end, 44 grant-directory identities match live handles, both recorded test
jobs are absent, ten source snapshots are unchanged, and 213 checked ACL entries
contain none of the test SIDs. Source hashes match the successful native receipt.
All **3,173 foundation contracts** pass, including **121 journal checks**, at
`.codex-temp/office-recovery-foundation-2a46a680de424d03add69201f43b4042`.
Application and private-harness Release builds have zero warnings/errors.

This verifies recovery of generated native ownership fixtures. Actual Office
application loss during rendering, creation/deletion confirmation failures,
partially completed native deletion, hostile descendant recovery and concurrent
replacement still require acceptance. Missing or changed profile objects remain
reviewable rather than being silently treated as deleted. Production context
construction, independent PDF publication and the customer command remain open.
The earlier real-Office export suite was not rerun for this recovery-only slice.


Application loss during Office rendering
---------------------------------------

The private `OfficeOwnerLossContracts` harness now starts a disposable application
owner for each generated DOCX/XLSX/PPTX fixture. It observes two increasing PDF
sizes while the exact AppContainer host is live, retains worker/host process
handles with matching creation times, then terminates only the owner. A separate
process reopens the persisted journal and uses `OfficeSandboxOwner.RecoverAsync`.
Both worker and renderer must have exited before recovered ownership permits
permission/profile cleanup. Recovery retries only sharing/lock failures within
the harness's existing five-second bound.

All **81 checks** pass at
`.codex-temp/office-worker/4e5ea25a4f1a4eceada9613cf14e0d28`: 27 owner-loss checks
across three live renderers, followed by the existing 54 ordinary export checks.
The original fixtures and six read-only snapshots remain unchanged. Three
interrupted PDFs are retained as evidence, without being accepted or published.
The six journals each finish with 27 frames and confirmed profile deletion.
The Release harness build has zero warnings/errors.

Independent inspection at
`contracts/inspection-8b7c36be607a4c4fb35aee41cfda51f2` verifies qpdf structure,
authored text, page geometry and exact PDFium control pixels for all three
following exports. The first inspection invocation used the aggregate report;
the inspector correctly refused its nested candidate paths. Inspecting the
existing `contracts/following/results.json` passes without changing the report,
candidate files or inspector. That initial refusal remains at
`inspection-787897159b0241c2b89e415df9775997`.

`.codex-temp/office-owner-loss-verification.json` reconciles current test sources,
managed/worker binaries, source fixtures, candidate hashes, all 162 journal
frames and 30 live grant-directory identities. All six profile folders/mappings
and named jobs are absent; 23,059 runtime/context ACL entries contain none of
their test SIDs. The earlier 3,173-contract foundation receipt still matches its
sources; that suite was not rerun for these harness-only changes.

Use `tools/office-engine/Test-OfficeWorkerStop.py --mode owner-loss` with its
required retained worker, build receipt and generated fixture arguments, plus
explicit disposable-profile authorization. The existing `all` mode still covers
cancel, worker loss and deadline; owner loss is a separate explicit mode. The
runner checks retained worker implementation sources independently of the
current application/harness source snapshot, allowing test-only evolution while
preserving the binary identity check.

This verifies actual Office rendering with the application infrastructure in a
private test owner. It does not provide customer startup recovery, production
context construction, independent PDF publication or a customer Office command.
Creation/deletion confirmation failures, ambiguous native deletion, hostile
descendant recovery, concurrent replacement and the other documented integration
gates remain open. Excel uses explicit cached values in these fixtures; the
customer calculation default remains undecided. The reserved production payload
is unchanged, and no visual or installed-shell acceptance is implied.

The subsequent [application context preparation](office-context-preparation.md)
now creates and verifies the original/snapshot leases and durable intent before
native profile creation. Both actual crash fixtures and following exports use
that factory in a new 90-check replay. Production startup recovery, independent
PDF validation/publication and the customer command remain open.
