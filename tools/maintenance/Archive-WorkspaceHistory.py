"""Archive selected .codex-temp trees, verify a full round trip, then allow guarded cleanup.

Manifest: {"formatVersion": 1, "roots": [".codex-temp\\literal-directory", ...]}.
Protection: {"formatVersion": 2, "reviewedAtUtc": "2026-10-17T12:00:00Z",
 "reviewedSelectionSha256": "<SHA-256 of selection file>",
 "consumerReview": "Current dependencies and consumers checked", "protectedRoots": {"path": "reason"}}.
Review current consumers before planning and again before cleanup. Pass
--reviewed-current-consumers for those two modes; the tool cannot discover consumers.
Run: python tools/maintenance/Archive-WorkspaceHistory.py plan|archive|cleanup --manifest FILE --protection FILE --run NAME
Restore check: python tools/maintenance/Archive-WorkspaceHistory.py verify --run NAME
After that passes, extract artifacts/history-archives/NAME/workspace.zip into a new,
empty directory (for example with python -m zipfile -e ARCHIVE DESTINATION), then
copy only chosen files back when their original paths are absent. ZIP members
preserve repository-relative names. Never extract over existing workspace files.
"""
import argparse
import hashlib
import json
import os
import re
import stat
import sys
import uuid
import zipfile
from datetime import datetime
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SCRATCH = REPO / '.codex-temp'
ARCHIVES = REPO / 'artifacts' / 'history-archives'
BLOCK = 1024 * 1024
# Known reserved scratch inputs supplement, but cannot replace, a current review.
MANDATORY_PROTECTED_ROOTS = (
    '.codex-temp/pdf-engine', '.codex-temp/pdfium-engine', '.codex-temp/curated-engine',
    '.codex-temp/office-repaired-application', '.codex-temp/distribution-review',
    '.codex-temp/svg-engine-build', '.codex-temp/wem-engine-build',
    '.codex-temp/video-audio-engine', '.codex-temp/audio-engine', '.codex-temp/heif-engine',
    '.codex-temp/avif-native-agent', '.codex-temp/office-execution',
    '.codex-temp/office-reduction', '.codex-temp/office-reduction-tests',
    '.codex-temp/office-python-free-reduced-01',
    '.codex-temp/waterfall-readability-assessment', '.codex-temp/waterfall-build6-review',
    '.codex-temp/waterfall-suites6', '.codex-temp/waterfall310-6',
    '.codex-temp/waterfall304-6', '.codex-temp/waterfall-audit6',
)


def fail(message):
    raise ValueError(message)


def digest(path):
    if not stat.S_ISREG(ordinary(path).st_mode):
        fail('Expected an ordinary file: ' + str(path))
    value = hashlib.sha256()
    with path.open('rb') as source:
        for block in iter(lambda: source.read(BLOCK), b''):
            value.update(block)
    return value.hexdigest()


def write_new(path, data):
    with path.open('x', encoding='utf-8', newline='\n') as output:
        json.dump(data, output, indent=2, sort_keys=True)
        output.write('\n')


def read_json(path):
    if not stat.S_ISREG(ordinary(path).st_mode):
        fail('Expected an ordinary JSON file: ' + str(path))
    with path.open('r', encoding='utf-8') as source:
        return json.load(source)


def ordinary(path):
    info = path.lstat()
    if stat.S_ISLNK(info.st_mode) or getattr(info, 'st_file_attributes', 0) & 1024:
        fail('Linked or reparse path: ' + str(path))
    if not (stat.S_ISDIR(info.st_mode) or stat.S_ISREG(info.st_mode)):
        fail('Unsupported filesystem entry: ' + str(path))
    return info


def ancestors(path):
    current = Path(os.path.abspath(path))
    try:
        current.relative_to(REPO)
    except ValueError:
        fail('Path is outside repository: ' + str(path))
    while True:
        ordinary(current)
        if current == current.parent:
            return
        current = current.parent


def repository_path(raw):
    if not isinstance(raw, str) or not raw or any(c in raw for c in '*?[]\r\n\0'):
        fail('Paths must be literal strings without wildcards.')
    parts = re.split(r'[\\/]', raw)
    if any(p in ('', '.', '..') for p in parts):
        fail('Empty, dot and dot-dot path components are forbidden.')
    path = Path(raw)
    if not path.is_absolute():
        path = REPO / path
    try:
        relative = path.relative_to(REPO)
    except ValueError:
        fail('Selected path is outside repository: ' + raw)
    return path, relative.as_posix()


def relative_path(raw):
    path, relative = repository_path(raw)
    if not relative.lower().startswith('.codex-temp/') and relative.lower() != '.codex-temp':
        fail('Selected path must be under .codex-temp: ' + raw)
    return path, relative


def overlaps(a, b):
    a, b = a.lower(), b.lower()
    if a == '.' or b == '.':
        return True
    return a == b or a.startswith(b + '/') or b.startswith(a + '/')


def load_protection(path, selection_hash):
    ancestors(path)
    protected = read_json(path)
    if not isinstance(protected, dict) or \
            set(protected) != {'formatVersion', 'reviewedAtUtc', 'reviewedSelectionSha256',
                               'consumerReview', 'protectedRoots'} or \
            protected['formatVersion'] != 2 or not isinstance(protected['protectedRoots'], dict) or \
            protected['reviewedSelectionSha256'] != selection_hash:
        fail('Fresh protection must review this exact selection with formatVersion 2.')
    if not isinstance(protected['consumerReview'], str) or not protected['consumerReview'].strip():
        fail('Protection must describe its current consumer review.')
    try:
        datetime.strptime(protected['reviewedAtUtc'], '%Y-%m-%dT%H:%M:%SZ')
    except (TypeError, ValueError):
        fail('Protection review must have a UTC timestamp ending in Z.')
    roots = [relative_path(name)[1] for name in protected['protectedRoots']]
    if any(not isinstance(reason, str) or not reason.strip() for reason in protected['protectedRoots'].values()):
        fail('Every protected root needs a review reason.')
    return roots + list(MANDATORY_PROTECTED_ROOTS), digest(path)


def load_selection(manifest, protection):
    manifest_hash = digest(manifest)
    protected_roots, protection_hash = load_protection(protection, manifest_hash)
    selected = read_json(manifest)
    if set(selected) != {'formatVersion', 'roots'} or selected['formatVersion'] != 1 or not isinstance(selected['roots'], list):
        fail('Selection manifest must have formatVersion 1 and roots only.')
    if not selected['roots']:
        fail('Selection manifest has no roots.')
    protection_relative = protection.relative_to(REPO).as_posix()
    manifest_relative = manifest.relative_to(REPO).as_posix()
    roots = []
    for raw in selected['roots']:
        path, relative = relative_path(raw)
        if relative.lower() == '.codex-temp':
            fail('Cannot select .codex-temp itself.')
        if any(overlaps(relative, prior) for prior in roots + protected_roots):
            fail('Selected root overlaps another or a protected root: ' + relative)
        if protection_relative.lower().startswith(relative.lower() + '/') or \
                manifest_relative.lower().startswith(relative.lower() + '/'):
            fail('Selected root contains its selection or protection input: ' + relative)
        ancestors(path)
        if not path.is_dir():
            fail('Selected root is not a directory: ' + relative)
        roots.append(relative)
    return roots, manifest_hash, protection_hash


def inventory(roots):
    entries = []
    for root in roots:
        stack = [REPO / Path(root)]
        while stack:
            path = stack.pop()
            info = ordinary(path)
            rel = path.relative_to(REPO).as_posix()
            if stat.S_ISDIR(info.st_mode):
                entries.append({'path': rel, 'kind': 'dir', 'mtimeNs': info.st_mtime_ns, 'id': info.st_ino})
                with os.scandir(path) as listing:
                    children = sorted((Path(child.path) for child in listing), reverse=True)
                stack.extend(children)
            else:
                entries.append({'path': rel, 'kind': 'file', 'size': info.st_size,
                                'mtimeNs': info.st_mtime_ns, 'id': info.st_ino, 'sha256': digest(path)})
    return sorted(entries, key=lambda entry: entry['path'])


def matches(record):
    validate_record(record)
    roots = record['roots']
    for root in roots:
        ancestors(REPO / Path(root))
    if inventory(roots) != record['entries']:
        fail('Workspace inventory changed; cleanup refused.')


def validate_record(record):
    roots = record.get('roots')
    entries = record.get('entries')
    if not isinstance(roots, list) or not isinstance(entries, list) or not roots:
        fail('Invalid planned inventory.')
    roots = [relative_path(root)[1] for root in roots]
    if len(roots) != len(set(roots)) or any(root == '.codex-temp' for root in roots):
        fail('Invalid planned roots.')
    seen = set()
    for entry in entries:
        if not isinstance(entry, dict) or entry.get('kind') not in ('file', 'dir'):
            fail('Invalid planned entry.')
        path = relative_path(entry.get('path'))[1]
        if path in seen or not any(path == root or path.startswith(root + '/') for root in roots):
            fail('Duplicate or out-of-root planned entry: ' + path)
        seen.add(path)
    if not all(any(e['path'] == root and e['kind'] == 'dir' for e in entries) for root in roots):
        fail('Planned root directory is missing.')


def check_run(run):
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]{0,79}', run) or '..' in run:
        fail('Run name must be a simple literal name.')
    return ARCHIVES / run


def prepare(args):
    if args.mode in ('plan', 'cleanup') and not args.reviewed_current_consumers:
        fail('Review current consumers and pass --reviewed-current-consumers before planning or cleanup.')
    manifest = Path(args.manifest).absolute()
    ancestors(manifest)
    if not manifest.is_file():
        fail('Selection manifest is missing.')
    protection = Path(args.protection).absolute()
    roots, manifest_hash, protection_hash = load_selection(manifest, protection)
    run = check_run(args.run)
    ancestors(ARCHIVES)
    if args.mode == 'plan':
        run.mkdir(exist_ok=False)
        write_new(run / 'plan.json', {'formatVersion': 2, 'roots': roots, 'entries': inventory(roots),
                                      'manifestSha256': manifest_hash, 'protectionSha256': protection_hash,
                                      'protectionPath': protection.relative_to(REPO).as_posix()})
        print('Planned:', run)
        return
    ancestors(run)
    record = read_json(run / 'plan.json')
    if record.get('formatVersion') != 2 or record.get('roots') != roots or record.get('manifestSha256') != manifest_hash:
        fail('Selection manifest differs from the immutable plan.')
    if record.get('protectionPath') != protection.relative_to(REPO).as_posix() or \
            record.get('protectionSha256') != protection_hash:
        fail('Protection manifest changed; replan before proceeding.')
    matches(record)
    if args.mode == 'archive':
        archive(run, record)
    else:
        cleanup(run, record, protection, args.reviewed_current_consumers)


def archive(run, record):
    destination = run / 'workspace.zip'
    if destination.exists() or (run / 'verified.json').exists() or (run / 'completed.json').exists():
        fail('Archive or verification receipt already exists.')
    with zipfile.ZipFile(destination, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6,
                         allowZip64=True) as output:
        for entry in record['entries']:
            name = entry['path'] + ('/' if entry['kind'] == 'dir' else '')
            member = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            member.external_attr = (0o40755 if entry['kind'] == 'dir' else 0o100644) << 16
            member.compress_type = zipfile.ZIP_DEFLATED
            if entry['kind'] == 'dir':
                output.writestr(member, b'')
            else:
                with (REPO / Path(entry['path'])).open('rb') as source, output.open(member, 'w') as target:
                    for block in iter(lambda: source.read(BLOCK), b''):
                        target.write(block)
    matches(record)
    archive_hash = digest(destination)
    verify_archive(destination, record)
    if digest(destination) != archive_hash:
        fail('Archive changed during verification.')
    write_new(run / 'verified.json', {'formatVersion': 2, 'planSha256': digest(run / 'plan.json'),
                                      'protectionSha256': record['protectionSha256'],
                                      'archiveSha256': archive_hash, 'archiveBytes': destination.stat().st_size,
                                      'files': sum(e['kind'] == 'file' for e in record['entries'])})
    print('Verified archive:', destination)


def verify_archive(destination, record):
    ancestors(SCRATCH)
    temporary = SCRATCH / ('history-verify-' + uuid.uuid4().hex)
    temporary.mkdir(exist_ok=False)
    try:
        expected = [e['path'] + ('/' if e['kind'] == 'dir' else '') for e in record['entries']]
        with zipfile.ZipFile(destination) as source:
            if sorted(source.namelist()) != sorted(expected) or source.testzip() is not None:
                fail('Archive membership or CRC differs from inventory.')
            for entry in record['entries']:
                target = temporary / Path(entry['path'])
                if entry['kind'] == 'dir':
                    target.mkdir(parents=True, exist_ok=True)
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with source.open(entry['path']) as packed, target.open('xb') as extracted:
                        for block in iter(lambda: packed.read(BLOCK), b''):
                            extracted.write(block)
                    if target.stat().st_size != entry['size'] or digest(target) != entry['sha256']:
                        fail('Full extraction hash differs: ' + entry['path'])
    except Exception as error:
        fail(f'Archive verification failed; extraction retained at {temporary}: {error}')
    try:
        directories = set()
        for entry in record['entries']:
            target = temporary / Path(entry['path'])
            if entry['kind'] == 'file':
                ancestors(target)
                if not stat.S_ISREG(ordinary(target).st_mode):
                    fail('Extracted file changed before cleanup: ' + str(target))
                target.unlink()
            else:
                directories.add(target)
            parent = target.parent
            while parent != temporary:
                directories.add(parent)
                parent = parent.parent
        for directory in sorted(directories, key=lambda path: len(path.parts), reverse=True):
            ancestors(directory)
            if not stat.S_ISDIR(ordinary(directory).st_mode):
                fail('Extracted directory changed before cleanup: ' + str(directory))
            directory.rmdir()
        ancestors(temporary)
        temporary.rmdir()
    except Exception as error:
        fail(f'Archive verified but extraction cleanup failed; retained at {temporary}: {error}')


def cleanup(run, record, protection=None, consumers_reviewed=False):
    if (run / 'completed.json').exists():
        fail('Cleanup was already completed.')
    if not consumers_reviewed:
        fail('Current consumers must be reviewed again before deletion.')
    if record.get('formatVersion') != 2 or protection is None:
        fail('Legacy or missing protection cannot authorize new deletion; replan with a fresh review.')
    protection_path, protection_relative = repository_path(str(protection))
    if protection_relative != record.get('protectionPath'):
        fail('Protection path differs from the plan.')
    _, protection_hash = load_protection(protection_path, record.get('manifestSha256'))
    if protection_hash != record.get('protectionSha256'):
        fail('Protection manifest changed; replan before cleanup.')
    verify_receipt(run, record)
    matches(record)
    _, current_hash = load_protection(protection_path, record['manifestSha256'])
    if current_hash != protection_hash:
        fail('Protection manifest changed during restore check; cleanup refused.')
    files = [e for e in record['entries'] if e['kind'] == 'file']
    dirs = [e for e in record['entries'] if e['kind'] == 'dir']
    for entry in files:
        path = REPO / Path(entry['path'])
        ancestors(path)
        info = ordinary(path)
        if (info.st_ino, info.st_mtime_ns, info.st_size, digest(path)) != \
                (entry['id'], entry['mtimeNs'], entry['size'], entry['sha256']):
            fail('File changed before deletion: ' + entry['path'])
        path.unlink()
    for entry in sorted(dirs, key=lambda e: len(Path(e['path']).parts), reverse=True):
        path = REPO / Path(entry['path'])
        ancestors(path)
        info = ordinary(path)
        if not stat.S_ISDIR(info.st_mode) or info.st_ino != entry['id']:
            fail('Directory identity changed before deletion: ' + entry['path'])
        path.rmdir()  # Refuses added files or directories.
    write_new(run / 'completed.json', {'formatVersion': 2, 'verifiedSha256': digest(run / 'verified.json'),
                                       'protectionSha256': protection_hash,
                                       'deletedFiles': len(files), 'deletedDirectories': len(dirs)})
    print('Completed exact inventoried cleanup:', run)


def verify_receipt(run, record):
    validate_record(record)
    receipt = read_json(run / 'verified.json')
    destination = run / 'workspace.zip'
    version = record.get('formatVersion')
    if version not in (1, 2) or receipt.get('formatVersion') != version or \
            (version == 2 and receipt.get('protectionSha256') != record.get('protectionSha256')) or \
            receipt.get('planSha256') != digest(run / 'plan.json') or \
            receipt.get('archiveSha256') != digest(destination) or receipt.get('archiveBytes') != destination.stat().st_size:
        fail('Verified archive identity or receipt differs.')
    verify_archive(destination, record)
    if receipt['archiveSha256'] != digest(destination) or receipt['archiveBytes'] != destination.stat().st_size:
        fail('Archive changed during the full restore check.')
    print('Verified receipt and full archive extraction:', run)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('plan', 'archive', 'verify', 'cleanup'))
    parser.add_argument('--manifest', help='JSON selection manifest; exact roots only')
    parser.add_argument('--protection', help='Fresh protection review for the exact selection')
    parser.add_argument('--reviewed-current-consumers', action='store_true',
                        help='Confirm a current consumer/dependency review for plan or cleanup')
    parser.add_argument('--run', required=True, help='New history archive run name')
    args = parser.parse_args()
    try:
        if args.mode == 'verify':
            run = check_run(args.run)
            ancestors(run)
            verify_receipt(run, read_json(run / 'plan.json'))
        else:
            if not args.manifest or not args.protection:
                parser.error('--manifest and --protection are required for plan, archive and cleanup')
            prepare(args)
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as error:
        raise SystemExit('Refused: ' + str(error)) from error


if __name__ == '__main__':
    main()
