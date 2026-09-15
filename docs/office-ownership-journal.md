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

The version-two record binds a validated `OfficeExportWork`, expected runtime
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
| Engine intent, then processes stopped | All five grants completed before work; confirmed shutdown before subsequent cleanup |
| Cleanup intent, then revoke intent/revoked | Revoke every intended grant in reverse order, including a grant with uncertain completion |
| Delete intent, then profile deleted | All intended grants revoked before recording profile deletion |

Profile creation and grant intents carry a 48-hex-character directory identity field for the volume
and file ID. Its syntax is checked; reading the journal does not verify the live
directory object. The coordinator must obtain these facts from held handles and
verify them again during recovery. Similarly, recorded process shutdown or a
profile-created entry is a report from the writer, not independent proof of the
current Windows state. An unconfirmed creation intent cannot be promoted to
confirmed ownership by replay.

Version-one records remain readable under their original schema, including
complete lifecycles. They cannot authorize new creation, native profile verification
or appended mutations. They remain unchanged for review; the reader never invents
the profile-directory identity that the earlier schema did not record. Mixed-version
chains and unsupported versions are refused.


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

The version-two profile binding below supplies the profile directory's live object
identity. Before automatic reclamation, add recoverable evidence for the engine
processes. The creator PID/start time alone does not identify surviving engine
children. Version additional persisted fields explicitly and preserve earlier records.

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
It is not yet recorded in these version-two journals or connected to Office
requests. Add that durable binding before using it to authorize profile recovery.
