Expanded Audio Direct Commands
==============================

Status: isolated application command checks, updated 2026-09-19. The existing
Convert commands require no new menu or general planner for AIFF/WMA. With the
repaired optional engine, they probe content and use the existing fixed target,
quality-decision, access-admission and transactional publication paths. This
does not enable these inputs in the installed app or reserved 1.1.1 candidate.


Verified scope
--------------

`Test-ExpandedAudioDirect.py '<successful expanded worker evidence>'` builds a
fresh managed worker and test host, copies the verified native files from the
[worker evaluation](expanded-audio-worker-verification.md), and inventories the
new payload before and after execution. A separate copy omits the optional
Windows Voice helper. The earlier payload remains unchanged. This composition
includes the later private delayed-handle cleanup correction described in the
[recovery record](expanded-audio-recovery.md).

Seven generated inputs cover stereo AIFF PCM16, AIFF-C float32, WMA v1, Standard,
Pro and Lossless, plus mono 22.05 kHz Voice. Each passes all six existing targets
through `MainViewModel.Admit`, the actual worker, and copy publication. The
checks verify the selected output extension, exact required consent flags,
one admission/completion per batch and quiet success. Voice-to-FLAC needs no
precision warning because the selected Windows decoder supplies exact PCM16.

Further checks cover a malformed file mixed with three supported files, one
shared quality decision, declining conversion, expired access, activation and
deactivation in an open decision, retained plan identity, retry with current
settings, duplicate retry suppression, absent Voice provider alongside valid
AIFF, and an entirely absent audio engine. Originals retain their hashes and
last-write timestamps; earlier copies remain unchanged. Native recycling is
forbidden by the test recycler. No Explorer activation, live licensing, install
or registration occurs.

Final run `expanded-audio-direct-9d570fbacab54471bafde43bb4d2a610` passes
**211 checks**, with 42 single-input target copies and five additional copies
from mixed, retry and absent-provider cases. Results, build logs and both
payload inventories are retained under that directory. Release builds report
zero warnings/errors.


Publication journal correction
------------------------------

The first run, `expanded-audio-direct-4e604f97d99340379f0a225a553ddf2c`, publishes
a WMA-to-M4A copy but cannot replace its recovery record. The retained `.json`
contains Publishing and the complete `.json.tmp` contains Committed; the copy
and original remain intact. That run fails quiet-completion acceptance.

A controlled Windows reader lock reproduces the same publication warning.
Native rename reports access denied when the destination reader denies rename,
not necessarily sharing violation. The journal now retries only its final
atomic rename on these two errors, at most twenty 100 ms waits. Both paths are
checked for links before each attempt. Persistent denial, including actual
permission failures, still retains evidence and reports a warning; other errors
are not retried. No original/output move or deletion is retried by this change.

The focused `--publication-journal <absolute scratch directory>` tests hold a
real reader lock until 250 ms after publication, then verify quiet completion
and record cleanup. A persistent-lock control verifies bounded failure,
retained records and unchanged original/output bytes. All four checks pass;
the transient case demonstrably fails before the correction. These tests also
run in the foundation suite. The initial relative-path invocation failed the
existing absolute-path guard and is not publication evidence.

Run `expanded-audio-direct-6729943e3bfa41749c4cd1b9dabd562d` subsequently stops
because its test incorrectly expects a Voice-to-FLAC precision prompt. The
expected result is corrected to the existing PCM16 policy; production quality
rules are unchanged. Run `expanded-audio-direct-1660bdfe91a54432994cd1184f78f075`
passes 211 checks before the final added extension/timestamp assertions; the
final run above includes those assertions.


Foundation regression
---------------------

The final Release foundation run passes **4,384 contracts**, including journal
locking and publication crash checks. Its retained log is
`.codex-temp/expanded-direct-foundation-final.log`.

Two earlier foundation attempts exposed test isolation problems, recorded in
`expanded-direct-foundation.log` and `expanded-direct-foundation-fresh-trials.log`.
The combined-PDF admission test reused a disposable trial started September 11
and correctly rejected it after expiry. Both PDF admission tests now use unique
profile paths. The Office preparation crash test also tried to acquire its
fixture lock before Windows released the killed writer's source handle. It now
waits only for sharing/lock violations, with the existing deadline and a finite
retry count, before acquiring the real lock and testing recovery. Production
licensing and Office recovery rules are unchanged.


Remaining acceptance
--------------------

These are application view-model and actual-worker checks, not interactive
Explorer or visual acceptance. The later metadata checkpoint below verifies
its declared text/artwork cases. Older-engine refusal, additional variants, surround Pro agreement, hostile
timelines, listening, missing Windows components, overwrite/recycling, resource
budgets and final combined packaging remain separate gates. Earlier adapter
metadata and worker fidelity evidence retains its original scope. The local
release goal remains incomplete.


Metadata and artwork checkpoint
-------------------------------

`Test-ExpandedAudioMetadata.py '<worker evidence>' '<AIFF adapter evidence>'
'<WMA adapter evidence>' '<Voice adapter evidence>' '<supplier bin>'` reuses
generated fixtures from the earlier adapter evaluations. It adds Pro/Lossless
cases by placing the authored ASF descriptive object into independently
Windows-encoded sources, retaining their exact stream configuration and packets.
The new copies and manifest stay inside a fresh repository scratch directory.
The shared direct-command runner builds fresh managed components and verifies
the pinned native payload before and after execution.

The seven cases are AIFF legacy text, AIFF Unicode ID3/artwork, WMA v1/v2
Unicode/artwork, WMA Voice Unicode/artwork/regional language, and stereo WMA
Pro/Lossless Unicode/artwork. Each passes all six existing target commands,
creating 42 validated copies. Complete descriptive inventories are compared,
including multiline comments and `en-us` language where present. Picture bytes,
type, description and MIME value are preserved. Encoder provenance is excluded;
M4A may add `und` only when the input has no language. Neither exception permits
dropping a source description or accepting an invented known language.

Two negative cases add an unhandled AIFF marker chunk or an ASF protection
declaration. Both refuse conversion before access admission or publication,
without a metadata acknowledgement prompt. Source hashes and timestamps remain
unchanged. The test forbids native recycling and checks for abandoned publication
files and recovery records.

Final evidence `expanded-audio-metadata-direct-bfd0dbb297ba4922aff56fe509ab9fd6`
passes **173 command/metadata checks**, followed by **78 independent checks**.
The independently selected supplier runtime is verified against its pinned
archive. It reads the descriptive values in all 42 outputs and decodes each of
the 36 covers to the exact authored two-pixel RGBA values, including partial
transparency. Runtime identities, fixture identities, output hashes, independent
probe reports and both result files are retained. Both Release builds have zero
warnings/errors. No production metadata or encoding code changed in this slice.

Initial runs `f2bf53d17a6c490590a96db779656837` and
`25c16f00d5e74247a8fb651365dc2aac` fail overly strict test expectations for WAV
encoder provenance and M4A's default `und` language respectively. The final
comparisons implement the existing documented policy; no source description
or preservation requirement was removed. These earlier attempts are not passes.

This closes the declared finite metadata composition check, not every possible
AIFF/ASF tag or artwork variant. Broader malformed/timeline and fidelity
acceptance, listening, Windows component availability, overwrite, visual checks,
distribution and combined packaging remain open. The preceding 4,384-contract
foundation result is unchanged historical evidence; this test-only slice builds
and runs its focused new mode rather than claiming a new full foundation run.
