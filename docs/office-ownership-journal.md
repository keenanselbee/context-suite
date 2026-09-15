Office Ownership Journal
========================

Status: bounded persistence component and isolated writer-loss checks implemented;
profile mutation integration and restart recovery remain incomplete

The application now has an `OfficeOwnershipJournal` for the
[Office profile/grant owner](office-profile-ownership.md). It records intended
changes separately from reported completion. This component does not create or
delete Windows profiles, change permissions, execute Office or publish outputs.
The owner and conversion coordinator must still be connected to it.


Recorded identity and ordering
------------------------------

The version-one record binds a validated `OfficeExportWork`, expected runtime
directory, creator process ID and creator start time. Its filename is the item
GUID followed by `.ownership`. The context must be a direct child of the expected
context root. The runtime and context cannot overlap, and the journal must remain
outside both. Reopening requires the expected context root and runtime supplied
by the application, rather than accepting arbitrary roots from the record.

The allowed sequence is:

| Step | Required evidence represented by the record |
| --- | --- |
| Profile intent, then profile created | A fresh creation attempt, followed by its matching derived AppContainer SID |
| Grant intent, then grant applied | Runtime/input read access and output/profile/temp write access, in that fixed order |
| Engine intent, then processes stopped | All five grants completed before work; confirmed shutdown before subsequent cleanup |
| Cleanup intent, then revoke intent/revoked | Revoke every intended grant in reverse order, including a grant with uncertain completion |
| Delete intent, then profile deleted | All intended grants revoked before recording profile deletion |

Grant intents carry a 48-hex-character directory identity field for the volume
and file ID. Its syntax is checked; reading the journal does not verify the live
directory object. The coordinator must obtain these facts from held handles and
verify them again during recovery. Similarly, recorded process shutdown or a
profile-created entry is a report from the writer, not independent proof of the
current Windows state. An unconfirmed creation intent cannot be promoted to
confirmed ownership by replay.


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


Verification
------------

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
for this journal-only slice. Their dated evidence remains separate. No new
packaged payload, visible UI, installed lifecycle or commerce acceptance is claimed.


Next integration
----------------

Construct the application-owned source/context and journal together. Write intent
before profile creation or ACL mutation, and completion only after successful
native verification. Persist unresolved cleanup and refuse an existing profile
collision. Bind restart recovery to the actual profile, directory objects and
terminated processes before revocation. An incomplete creation, replaced object,
hostile journal/tree or unresolved process must remain reviewable.

Test actual application loss at those mutation boundaries, including interruption
during grant application/revocation and profile deletion. Add hostile leaf-link
and concurrent replacement acceptance. Finally connect independent PDF validation
and transactional copy publication, followed by the direct customer Office command.
The journal does not authorize publishing a candidate PDF or complete these gates.
