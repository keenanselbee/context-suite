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
Explorer or visual acceptance. Metadata/artwork through this exact composition,
older-engine refusal, additional variants, surround Pro agreement, hostile
timelines, listening, missing Windows components, overwrite/recycling, resource
budgets and final combined packaging remain separate gates. Earlier adapter
metadata and worker fidelity evidence retains its original scope. The local
release goal remains incomplete.
