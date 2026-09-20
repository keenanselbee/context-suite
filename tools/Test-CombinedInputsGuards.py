"""Check combined scratch preparation refuses invalid inputs before copying a payload."""
import contextlib
import hashlib
import importlib.util
import io
import json
import pathlib
import subprocess
import sys
import uuid

root = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('combined', root / 'tools/Test-CombinedInputs.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
stage = root / '.codex-temp/combined-input-guards' / uuid.uuid4().hex
stage.mkdir(parents=True)
base = stage / 'base'
base.mkdir()
(base / 'file.bin').write_bytes(b'original')
record = {'path': 'file.bin', 'bytes': 8, 'sha256': hashlib.sha256(b'original').hexdigest().upper()}
(base / 'payload-inventory.json').write_text(json.dumps([record]))
bundle = stage / 'bundle'
bundle.mkdir()
manifest = stage / 'fixtures.json'
manifest.write_text('[]')
args = ['--base-payload', str(base), '--heif-distribution', str(bundle), '--audio-distribution', str(bundle),
        '--voice-distribution', str(bundle), '--audio-manifest', str(manifest)]
results = []

def refused(name, changed, expected):
    old = sys.argv
    sys.argv = ['Test-CombinedInputs.py', *changed]
    output = io.StringIO()
    try:
        with contextlib.redirect_stdout(output):
            module.main()
    except ValueError as error:
        assert expected in str(error), (name, str(error))
        for line in output.getvalue().splitlines():
            if line.startswith('Combined input evidence: '):
                assert not (pathlib.Path(line.removeprefix('Combined input evidence: ')) / 'combined').exists()
        results.append(name)
    else:
        raise AssertionError(name + ' was accepted')
    finally:
        sys.argv = old

outside = args.copy(); outside[1] = str(root / 'uncreated-combined-guard')
refused('non-scratch base refuses before writes', outside, 'isolated')
traversal = args.copy(); traversal[1] = str(stage / '..' / stage.name / 'base')
refused('traversal refuses before writes', traversal, 'isolated')
missing = args.copy(); missing[-1] = str(stage / 'missing.json')
refused('missing fixture manifest refuses before composition', missing, 'Existing verified')
(base / 'file.bin').write_bytes(b'modified')
refused('changed baseline bytes refuse before copying', args, 'identity differs')
(base / 'file.bin').write_bytes(b'original')
(base / 'payload-inventory.json').write_text(json.dumps([record, record]))
refused('duplicate baseline inventory refuses before copying', args, 'Duplicate')
(base / 'payload-inventory.json').write_text(json.dumps([record]))
(base / 'extra.bin').write_bytes(b'keep')
refused('uninventoried baseline member refuses before copying', args, 'Uninventoried')
assert (base / 'file.bin').read_bytes() == b'original' and (base / 'extra.bin').read_bytes() == b'keep'
assert not (root / 'uncreated-combined-guard').exists()

# Small authored archive controls test identity binding and overwrite refusal;
# they are not engine payloads or acceptance evidence for a product build.
def archive_fixture():
    folder = root / '.codex-temp/combined-inputs' / uuid.uuid4().hex
    payload = folder / 'combined'; payload.mkdir(parents=True)
    (payload / 'file.bin').write_bytes(b'original')
    inventory = payload / 'payload-inventory.json'; inventory.write_text(json.dumps([record]))
    (folder / 'results.json').write_text(json.dumps({'Passed': True, 'InventorySha256': hashlib.sha256(inventory.read_bytes()).hexdigest().upper()}))
    (folder / 'exit.json').write_text('{"ExitCode":0}')
    return folder

def measure(folder):
    return subprocess.run([sys.executable, '-B', '-O', str(root / 'tools/Measure-CombinedArchive.py'), str(folder)],
                          capture_output=True, text=True, timeout=30)

valid = archive_fixture()
assert measure(valid).returncode == 0
receipt = json.loads((valid / 'archive-review.json').read_text())
assert receipt['Passed'] and receipt['MemberCount'] == 2
results.append('archive member hashes and complete membership verified')
archive = valid / 'combined-inputs-review.zip'
archive_hash = hashlib.sha256(archive.read_bytes()).hexdigest()
retry = measure(valid)
assert retry.returncode != 0 and 'FileExistsError' in retry.stderr and hashlib.sha256(archive.read_bytes()).hexdigest() == archive_hash
results.append('existing archive refused without changing bytes')
changed = archive_fixture(); (changed / 'combined/file.bin').write_bytes(b'modified')
refusal = measure(changed)
assert refusal.returncode != 0 and 'Candidate file changed' in refusal.stderr and not (changed / 'archive-review.json').exists()
results.append('changed payload cannot produce a passing archive receipt, including optimized Python')
changed_inventory = archive_fixture(); (changed_inventory / 'combined/payload-inventory.json').write_text('[]')
refusal = measure(changed_inventory)
assert refusal.returncode != 0 and 'Candidate inventory changed' in refusal.stderr and not (changed_inventory / 'combined-inputs-review.zip').exists()
results.append('changed inventory refuses before archive creation')
(stage / 'results.json').write_text(json.dumps({'Passed': True, 'Checks': results}, indent=2) + '\n')
print(f'Passed {len(results)} combined-input guards: {stage}')
