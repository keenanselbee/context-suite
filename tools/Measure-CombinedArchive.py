"""Measure and verify one completed Test-CombinedInputs scratch payload as a local ZIP.

Usage: python -B tools/Measure-CombinedArchive.py <combined-inputs evidence directory>
This is not a versioned or published release artifact. Existing archives are refused.
"""
import hashlib
import json
import pathlib
import sys
import zipfile

root = pathlib.Path(__file__).resolve().parent.parent

def require(condition, message):
    if not condition:
        raise ValueError(message)

if len(sys.argv) != 2:
    raise ValueError('Supply one completed combined-inputs evidence directory.')
evidence = pathlib.Path(sys.argv[1]).resolve(strict=True)
require(evidence.is_relative_to(root / '.codex-temp/combined-inputs'), 'Use repository combined-input evidence.')
results = json.loads((evidence / 'results.json').read_text())
require(results['Passed'] and json.loads((evidence / 'exit.json').read_text())['ExitCode'] == 0, 'Completed passing evidence is required.')
payload = evidence / 'combined'

def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()

inventory = payload / 'payload-inventory.json'
require(digest(inventory) == results['InventorySha256'], 'Candidate inventory changed.')
records = json.loads(inventory.read_text())
expected = {item['path']: (item['bytes'], item['sha256']) for item in records}
require(len(expected) == len(records), 'Duplicate candidate inventory members.')
expected['payload-inventory.json'] = (inventory.stat().st_size, digest(inventory))
require({path.relative_to(payload).as_posix() for path in payload.rglob('*') if path.is_file()} == set(expected), 'Candidate membership differs.')
archive = evidence / 'combined-inputs-review.zip'
print('Writing local compressed review archive:', archive, flush=True)
with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as output:
    for name, (size, identity) in sorted(expected.items()):
        source = payload / name
        require(source.stat().st_size == size and digest(source) == identity, 'Candidate file changed: ' + name)
        output.write(source, name)
print('Verifying every archived member against the candidate inventory.', flush=True)
with zipfile.ZipFile(archive, 'r') as opened:
    require(len(opened.infolist()) == len(expected) and set(opened.namelist()) == set(expected), 'Archive membership differs.')
    for item in opened.infolist():
        size, identity = expected[item.filename]
        require(item.file_size == size, 'Archive member size differs: ' + item.filename)
        with opened.open(item) as stream:
            require(hashlib.file_digest(stream, 'sha256').hexdigest().upper() == identity, 'Archive bytes differ: ' + item.filename)
        require(digest(payload / item.filename) == identity, 'Candidate changed during archive creation: ' + item.filename)
receipt = {'Passed': True, 'Archive': str(archive), 'ArchiveBytes': archive.stat().st_size,
           'ArchiveSha256': digest(archive), 'MemberCount': len(expected),
           'UncompressedBytes': sum(size for size, _ in expected.values()), 'InventorySha256': results['InventorySha256'],
           'Scope': 'Local scratch archive measurement only. Not a versioned or published release artifact.'}
(evidence / 'archive-review.json').write_text(json.dumps(receipt, indent=2) + '\n')
print(json.dumps(receipt, indent=2), flush=True)
