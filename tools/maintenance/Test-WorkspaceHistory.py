"""Exercise workspace-history guards in a unique disposable .codex-temp run.

Passing runs retain only a compact result; failed harness runs retain fixtures.
"""
import hashlib
import io
import json
import importlib.util
import os
import re
import stat
import subprocess
import sys
import uuid
import zipfile
from datetime import datetime, timedelta, timezone
from pathlib import Path
from contextlib import redirect_stderr, redirect_stdout
from unittest.mock import patch

REPO = Path(__file__).resolve().parents[2]
TOOL = Path(__file__).with_name('Archive-WorkspaceHistory.py')
name = 'history-tool-test-' + uuid.uuid4().hex
task_root = REPO / '.codex-temp' / name
fixture = task_root / 'work'
fixture.mkdir(parents=True)
results = []


def check(condition, label):
    if not condition:
        raise AssertionError(label)
    results.append(label)


def manifest(label, roots):
    path = fixture / (label + '.json')
    path.write_text(json.dumps({'formatVersion': 1, 'roots': roots}), encoding='utf-8')
    protection = path.with_name(path.stem + '-protection.json')
    protection.write_text(json.dumps({
        'formatVersion': 2,
        'reviewedAtUtc': datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
        'reviewedSelectionSha256': hashlib.sha256(path.read_bytes()).hexdigest(),
        'consumerReview': 'Current test fixture consumers reviewed',
        'protectedRoots': {},
    }), encoding='utf-8')
    return path


def protection_for(selection):
    return selection.with_name(selection.stem + '-protection.json')


def call(mode, label, selection, accepted=True, reviewed=True):
    run = name + '-' + label
    output = io.StringIO()
    error = io.StringIO()
    argv = [str(TOOL), mode, '--manifest', str(selection),
            '--protection', str(protection_for(selection)), '--run', run]
    if reviewed and mode in ('plan', 'cleanup'):
        argv.append('--reviewed-current-consumers')
    exit_code = 0
    with patch.object(sys, 'argv', argv), redirect_stdout(output), redirect_stderr(error):
        try:
            history.main()
        except SystemExit as stopped:
            exit_code = stopped.code if isinstance(stopped.code, int) else 1
            error.write(str(stopped))
    if (exit_code == 0) != accepted:
        raise AssertionError(f'{label} {mode}: unexpected exit {exit_code}: {output.getvalue()} {error.getvalue()}')
    return history.ARCHIVES / run


def remove_success_run(run, label):
    archive_root = history.ARCHIVES
    if run.parent != archive_root or run.name != name + '-' + label:
        raise AssertionError('Unexpected successful run path: ' + str(run))
    history.ancestors(run)
    if {p.name for p in run.iterdir()} != {'plan.json', 'workspace.zip', 'verified.json', 'completed.json'}:
        raise AssertionError('Successful run contains unexpected files: ' + str(run))
    for path in run.iterdir():
        history.ancestors(path)
        if not path.is_file():
            raise AssertionError('Unexpected successful run entry: ' + str(path))
        path.unlink()
    run.rmdir()


def remove_owned_tree(path):
    if path != fixture or path.parent != task_root or task_root.parent != REPO / '.codex-temp':
        raise AssertionError('Unexpected owned fixture root: ' + str(path))
    history.ancestors(path)

    def remove_entry(entry):
        info = entry.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, 'st_file_attributes', 0) & 1024:
            if stat.S_ISDIR(info.st_mode):
                entry.rmdir()  # Removes the test-created junction itself, never its target.
            else:
                entry.unlink()
        elif stat.S_ISDIR(info.st_mode):
            for child in entry.iterdir():
                remove_entry(child)
            entry.rmdir()
        elif stat.S_ISREG(info.st_mode):
            entry.unlink()
        else:
            raise AssertionError('Unexpected filesystem entry: ' + str(entry))

    remove_entry(path)


try:
    specification = importlib.util.spec_from_file_location('workspace_history', TOOL)
    history = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(history)
    history.ARCHIVES = fixture / 'archives'
    history.ARCHIVES.mkdir()
    history.SCRATCH = fixture
    success = fixture / 'success'
    (success / 'empty').mkdir(parents=True)
    (success / 'nested').mkdir()
    (success / 'nested' / 'file.txt').write_text('safe fixture', encoding='utf-8')
    selected = manifest('success-selection', [str(success)])
    run = call('plan', 'success', selected)
    call('archive', 'success', selected)
    call('cleanup', 'success', selected)
    call('verify', 'success', selected)
    check(not success.exists() and (run / 'completed.json').is_file(), 'verified cleanup of exact fixture')
    remove_success_run(run, 'success')
    protection_for(selected).unlink()
    selected.unlink()
    check(not run.exists(), 'successful archive run cleaned')

    if os.name == 'nt':
        read_only_root = fixture / 'read-only'
        read_only_root.mkdir()
        earlier_file = read_only_root / 'a.txt'
        earlier_file.write_text('keep before obstruction', encoding='utf-8')
        read_only_file = read_only_root / 'z.txt'
        read_only_file.write_text('read-only fixture', encoding='utf-8')
        os.chmod(read_only_file, stat.S_IREAD)
        selected = manifest('read-only-selection', [str(read_only_root)])
        call('plan', 'read-only', selected)
        call('archive', 'read-only', selected)
        call('cleanup', 'read-only', selected, accepted=False)
        check(earlier_file.read_text(encoding='utf-8') == 'keep before obstruction' and
              read_only_file.read_text(encoding='utf-8') == 'read-only fixture',
              'read-only file refused before deleting any selected file')
        os.chmod(read_only_file, stat.S_IWRITE)

    for label, change in [('mutated', 'mutate'), ('added', 'add'), ('corrupt', 'corrupt')]:
        root = fixture / label
        root.mkdir()
        (root / 'file.txt').write_text('original', encoding='utf-8')
        selected = manifest(label + '-selection', [str(root)])
        run = call('plan', label, selected)
        call('archive', label, selected)
        if change == 'mutate':
            (root / 'file.txt').write_text('changed', encoding='utf-8')
        elif change == 'add':
            (root / 'extra.txt').write_text('new', encoding='utf-8')
        else:
            with (run / 'workspace.zip').open('ab') as output:
                output.write(b'changed')
            call('verify', label, selected, accepted=False)
        call('cleanup', label, selected, accepted=False)
        check(root.exists() and not (run / 'completed.json').exists(), label + ' retains source')

    root = fixture / 'archive-changed-during-verification'
    root.mkdir()
    (root / 'file.txt').write_text('original', encoding='utf-8')
    selected = manifest('archive-race-selection', [str(root)])
    run = call('plan', 'archive-race', selected)
    call('archive', 'archive-race', selected)
    original_verify = history.verify_archive

    def change_archive_after_restore(destination, record):
        original_verify(destination, record)
        with destination.open('ab') as output:
            output.write(b'changed after successful extraction')

    rejected = False
    with patch.object(history, 'verify_archive', change_archive_after_restore):
        try:
            history.cleanup(run, history.read_json(run / 'plan.json'), protection_for(selected), True)
        except ValueError:
            rejected = True
    check(rejected and (root / 'file.txt').read_text() == 'original' and
          not (run / 'completed.json').exists(), 'archive changed during restore retains source')

    root = fixture / 'protection-race'
    root.mkdir()
    (root / 'file.txt').write_text('original', encoding='utf-8')
    selected = manifest('protection-race-selection', [str(root)])
    run = call('plan', 'protection-race', selected)
    call('archive', 'protection-race', selected)
    protection = protection_for(selected)

    def change_protection_after_restore(destination, record):
        original_verify(destination, record)
        review = json.loads(protection.read_text(encoding='utf-8'))
        review['protectedRoots'][str(root)] = 'Consumer discovered during restore check'
        protection.write_text(json.dumps(review), encoding='utf-8')

    rejected = False
    with patch.object(history, 'verify_archive', change_protection_after_restore):
        try:
            history.cleanup(run, history.read_json(run / 'plan.json'), protection, True)
        except ValueError:
            rejected = True
    check(rejected and root.exists() and not (run / 'completed.json').exists(),
          'protection changed during restore retains source')

    root = fixture / 'changed-manifest'
    root.mkdir()
    (root / 'file.txt').write_text('original', encoding='utf-8')
    selected = manifest('changed-selection', [str(root)])
    run = call('plan', 'changed-manifest', selected)
    call('archive', 'changed-manifest', selected)
    selected.write_text(selected.read_text(encoding='utf-8') + ' ', encoding='utf-8')
    call('cleanup', 'changed-manifest', selected, accepted=False)
    check(root.exists() and not (run / 'completed.json').exists(), 'changed manifest retains source')

    root = fixture / 'stale-protection'
    root.mkdir()
    (root / 'file.txt').write_text('original', encoding='utf-8')
    selected = manifest('stale-protection-selection', [str(root)])
    protection = protection_for(selected)
    review = json.loads(protection.read_text(encoding='utf-8'))
    review['reviewedAtUtc'] = (datetime.now(timezone.utc) - timedelta(days=5)).strftime('%Y-%m-%dT%H:%M:%SZ')
    protection.write_text(json.dumps(review), encoding='utf-8')
    call('plan', 'stale-protection', selected, accepted=False, reviewed=False)
    run = call('plan', 'stale-protection', selected)
    call('archive', 'stale-protection', selected)
    call('cleanup', 'stale-protection', selected, accepted=False, reviewed=False)
    call('cleanup', 'stale-protection', selected)
    check(not root.exists() and (run / 'completed.json').is_file(), 'current review permits long-running archive')
    remove_success_run(run, 'stale-protection')

    root = fixture / 'changed-protection'
    root.mkdir()
    (root / 'file.txt').write_text('original', encoding='utf-8')
    selected = manifest('changed-protection-selection', [str(root)])
    run = call('plan', 'changed-protection', selected)
    call('archive', 'changed-protection', selected)
    protection = protection_for(selected)
    review = json.loads(protection.read_text(encoding='utf-8'))
    review['protectedRoots'][str(root)] = 'New consumer discovered after archival'
    protection.write_text(json.dumps(review), encoding='utf-8')
    call('cleanup', 'changed-protection', selected, accepted=False)
    check(root.exists() and not (run / 'completed.json').exists(), 'changed consumer protection retains source')

    root = fixture / 'missing-protection'
    root.mkdir()
    (root / 'file.txt').write_text('original', encoding='utf-8')
    selected = manifest('missing-protection-selection', [str(root)])
    run = call('plan', 'missing-protection', selected)
    call('archive', 'missing-protection', selected)
    protection_for(selected).unlink()
    call('verify', 'missing-protection', selected)
    call('cleanup', 'missing-protection', selected, accepted=False)
    check(root.exists() and not (run / 'completed.json').exists(), 'missing protection permits recovery but refuses deletion')

    root = fixture / 'changed-receipt'
    root.mkdir()
    (root / 'file.txt').write_text('original', encoding='utf-8')
    selected = manifest('changed-receipt-selection', [str(root)])
    run = call('plan', 'changed-receipt', selected)
    call('archive', 'changed-receipt', selected)
    receipt_path = run / 'verified.json'
    receipt = history.read_json(receipt_path)
    receipt['protectionSha256'] = '0' * 64
    receipt_path.write_text(json.dumps(receipt), encoding='utf-8')
    call('verify', 'changed-receipt', selected, accepted=False)
    call('cleanup', 'changed-receipt', selected, accepted=False)
    check(root.exists() and not (run / 'completed.json').exists(), 'changed protection receipt retains source')

    root = fixture / 'legacy-recovery'
    root.mkdir()
    (root / 'file.txt').write_text('original', encoding='utf-8')
    selected = manifest('legacy-recovery-selection', [str(root)])
    run = call('plan', 'legacy-recovery', selected)
    call('archive', 'legacy-recovery', selected)
    plan_path = run / 'plan.json'
    old_plan = history.read_json(plan_path)
    old_plan['formatVersion'] = 1
    del old_plan['protectionPath']
    plan_path.write_text(json.dumps(old_plan), encoding='utf-8')
    receipt_path = run / 'verified.json'
    old_receipt = history.read_json(receipt_path)
    old_receipt['formatVersion'] = 1
    del old_receipt['protectionSha256']
    old_receipt['planSha256'] = history.digest(plan_path)
    receipt_path.write_text(json.dumps(old_receipt), encoding='utf-8')
    call('verify', 'legacy-recovery', selected)
    call('cleanup', 'legacy-recovery', selected, accepted=False)
    check(root.exists() and not (run / 'completed.json').exists(), 'legacy receipt verifies but cannot delete')

    root = fixture / 'receipt-write-failure'
    root.mkdir()
    (root / 'file.txt').write_text('original', encoding='utf-8')
    selected = manifest('receipt-write-failure-selection', [str(root)])
    run = call('plan', 'receipt-write-failure', selected)
    original_write = history.write_new

    def fail_verified_write(path, data):
        if path.name == 'verified.json':
            raise OSError('simulated receipt write failure')
        return original_write(path, data)

    rejected = False
    with patch.object(history, 'write_new', fail_verified_write):
        try:
            history.archive(run, history.read_json(run / 'plan.json'))
        except OSError:
            rejected = True
    call('cleanup', 'receipt-write-failure', selected, accepted=False)
    check(rejected and root.exists() and (run / 'workspace.zip').is_file() and
          not (run / 'verified.json').exists(), 'failed receipt write retains source and archive')
    retry = call('plan', 'receipt-safe-retry', selected)
    call('archive', 'receipt-safe-retry', selected)
    call('cleanup', 'receipt-safe-retry', selected)
    check(not root.exists() and (retry / 'completed.json').is_file(), 'fresh plan safely retries failed receipt')
    remove_success_run(retry, 'receipt-safe-retry')
    protection_for(selected).unlink()
    selected.unlink()

    outside = REPO.parent / 'outside-fixture'
    for label, roots in [
        ('root-itself', ['.codex-temp']),
        ('outside', [str(outside)]),
        ('dot-dot', ['.codex-temp/../artifacts']),
        ('protected', ['.codex-temp/curated-engine']),
        ('overlap', [str(fixture), str(fixture / 'added')]),
        ('wildcard', ['.codex-temp/*']),
    ]:
        selected = manifest(label + '-selection', roots)
        call('plan', label, selected, accepted=False)
        check(True, label + ' refused')

    target = fixture / 'link-target'
    target.mkdir()
    link = fixture / 'linked-root'
    try:
        os.symlink(target, link, target_is_directory=True)
    except OSError:
        command = "New-Item -ItemType Junction -LiteralPath '{}' -Target '{}' | Out-Null".format(
            str(link).replace("'", "''"), str(target).replace("'", "''"))
        subprocess.run(['powershell', '-NoProfile', '-Command', command], check=True, capture_output=True)
    selected = manifest('linked-selection', [str(link)])
    call('plan', 'linked', selected, accepted=False)
    check(link.exists(), 'linked root refused')

    child_root = fixture / 'linked-child-root'
    child_root.mkdir()
    child = child_root / 'linked-child'
    try:
        os.symlink(target, child, target_is_directory=True)
    except OSError:
        command = "New-Item -ItemType Junction -LiteralPath '{}' -Target '{}' | Out-Null".format(
            str(child).replace("'", "''"), str(target).replace("'", "''"))
        subprocess.run(['powershell', '-NoProfile', '-Command', command], check=True, capture_output=True)
    selected = manifest('linked-child-selection', [str(child_root)])
    call('plan', 'linked-child', selected, accepted=False)
    check(child.exists(), 'linked descendant refused')

    bad_root = fixture / 'bad-extraction'
    bad_root.mkdir()
    (bad_root / 'file.txt').write_text('expected', encoding='utf-8')
    bad_entries = history.inventory([bad_root.relative_to(REPO).as_posix()])
    bad_zip = fixture / 'bad-extraction.zip'
    with zipfile.ZipFile(bad_zip, 'x') as output:
        for entry in bad_entries:
            output.writestr(entry['path'] + ('/' if entry['kind'] == 'dir' else ''),
                            b'' if entry['kind'] == 'dir' else b'different')
    retained = None
    try:
        history.verify_archive(bad_zip, {'entries': bad_entries})
    except ValueError as error:
        match = re.search(r'history-verify-[0-9a-f]+', str(error))
        if match:
            retained = fixture / match.group()
    check(retained is not None and retained.is_dir() and
          (retained / bad_root.relative_to(REPO) / 'file.txt').is_file(),
          'failed full extraction retained with diagnostic path')

    identity_root = fixture / 'changed-directory-id'
    identity_root.mkdir()
    identity_file = identity_root / 'file.txt'
    identity_file.write_text('expected', encoding='utf-8')
    selected = manifest('changed-directory-id-selection', [str(identity_root)])
    run = call('plan', 'changed-directory-id', selected)
    call('archive', 'changed-directory-id', selected)
    original_unlink = Path.unlink
    moved = fixture / 'changed-directory-id-original'

    def replace_directory_after_file_deletion(path, *args, **kwargs):
        result = original_unlink(path, *args, **kwargs)
        if path == identity_file:
            identity_root.rename(moved)
            identity_root.mkdir()
        return result

    rejected = False
    with patch.object(Path, 'unlink', replace_directory_after_file_deletion):
        try:
            history.cleanup(run, history.read_json(run / 'plan.json'), protection_for(selected), True)
        except ValueError:
            rejected = True
    check(rejected and identity_root.is_dir() and moved.is_dir() and
          not (run / 'completed.json').exists(), 'changed directory identity refused')

    rejected_runs = len(list(history.ARCHIVES.iterdir()))
    evidence = task_root / 'test-results.json'
    result = {
        'checks': results,
        'runsPrefix': name,
        'rejectedRunsInspected': rejected_runs,
        'failedExtractionDiagnosticInspected': retained is not None,
        'successfulFixtureCleanup': False,
        'reproductionCommand': f'python -B "{TOOL.parent / "Test-WorkspaceHistory.py"}"',
    }
    # Preserve the test result before deleting the fixtures that establish it.
    evidence.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    remove_owned_tree(fixture)
    result['successfulFixtureCleanup'] = not fixture.exists()
    evidence.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f'PASS: {len(results)} checks; evidence: {evidence}')
except Exception:
    print('Harness failed; inspect retained evidence and any remaining fixtures at:', task_root, file=sys.stderr)
    raise
