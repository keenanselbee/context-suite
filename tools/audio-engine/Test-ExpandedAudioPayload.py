"""Verify expanded audio staging and refusal boundaries in disposable payloads."""
import importlib.util
import json
import pathlib
import shutil
import subprocess
import sys
import uuid
from unittest import mock

TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('audio_payload', TOOLS / 'Stage-AudioPayload.py')
payload = importlib.util.module_from_spec(spec)
spec.loader.exec_module(payload)


def main():
    if len(sys.argv) != 3:
        raise ValueError('Use <expanded review bundle> <existing inventoried image/combined stage>')
    distribution, baseline = map(payload.local_path, sys.argv[1:])
    payload.verify_inventory(baseline)
    root = payload.ROOT / '.codex-temp' / ('expanded-audio-payload-' + uuid.uuid4().hex)
    stage = root / 'candidate'
    stage.mkdir(parents=True)
    print('Expanded audio payload evidence: ' + str(root), flush=True)
    # Copy only the already inventoried image/managed foundation for allowlist
    # testing. Do not copy the older audio or unrelated large engine directories.
    for source in baseline.iterdir():
        if source.is_file() and source.name != 'payload-inventory.json':
            shutil.copyfile(source, stage / source.name)
    shutil.copytree(baseline / 'Assets', stage / 'Assets')
    payload.stage(distribution, stage)
    record = payload.verify(stage)
    if record.get('profile') != 'expanded':
        raise AssertionError('Expanded profile was not selected')
    inventory = [{'path': path.relative_to(stage).as_posix(), 'bytes': path.stat().st_size,
                  'sha256': payload.digest(path.read_bytes())} for path in stage.rglob('*') if path.is_file()]
    (stage / 'payload-inventory.json').write_text(json.dumps(inventory, indent=2) + '\n')
    payload.verify_inventory(stage)
    results = ['Expanded eighteen-file deployment and inventory accepted']

    def refused(name, operation):
        try:
            operation()
        except ValueError as error:
            results.append(dict(name=name, refused=str(error)))
        else:
            raise AssertionError('Unexpected acceptance: ' + name)

    def altered(name, change):
        target = root / name
        shutil.copytree(stage / 'audio-engine', target / 'audio-engine')
        change(target / 'audio-engine')
        refused(name, lambda: payload.verify(target))

    altered('encoder-bytes', lambda path: (path / 'ffmpeg.exe').write_bytes(b'changed'))
    altered('library-bytes', lambda path: (path / 'avcodec-63.dll').write_bytes(b'changed'))
    altered('missing-probe', lambda path: (path / 'ffprobe.exe').unlink())
    altered('notice-bytes', lambda path: (path / 'THIRD-PARTY-NOTICES.txt').write_bytes(b'changed'))
    altered('missing-license', lambda path: (path / 'licenses/FFmpeg.LGPLv2.1.txt').unlink())
    altered('extra-library', lambda path: (path / 'unreviewed.dll').write_bytes(b'extra'))
    altered('mixed-profile', lambda path: shutil.copyfile(baseline / 'audio-engine/ffprobe.exe', path / 'ffprobe.exe'))
    refused('existing-payload', lambda: payload.stage(distribution, stage))
    refused('development-payload', lambda: payload.verify(payload.ROOT / 'artifacts/production/Release'))
    # An injected copy failure retains its partial owned candidate and refuses
    # an in-place retry. Nothing is silently promoted or deleted.
    partial = root / 'partial'
    partial.mkdir()
    original_open = pathlib.Path.open

    def fail_copy(path, *args, **kwargs):
        if path == partial / 'audio-engine/ffprobe.exe' and args and args[0] == 'xb':
            raise PermissionError('Injected output-copy failure')
        return original_open(path, *args, **kwargs)

    with mock.patch.object(pathlib.Path, 'open', fail_copy):
        try:
            payload.stage(distribution, partial)
        except PermissionError:
            results.append('Injected copy failure retained')
        else:
            raise AssertionError('Injected copy failure did not occur')
    refused('partial-payload', lambda: payload.verify(partial))
    refused('partial-retry', lambda: payload.stage(distribution, partial))
    for enabled in (False, True):
        arguments = ['powershell', '-NoProfile', '-File', str(payload.ROOT / 'tools/curated-engine/Test-ProductionPayload.ps1'),
                     '-Payload', str(stage)] + (['-AllowAudioCandidate'] if enabled else [])
        result = subprocess.run(arguments, cwd=payload.ROOT, capture_output=True, timeout=90)
        (root / ('allowlist-' + str(enabled) + '.log')).write_bytes(result.stdout + result.stderr)
        if enabled and result.returncode != 0 or not enabled and (result.returncode == 0 or b'Unreviewed production file' not in result.stdout + result.stderr):
            raise AssertionError('Real production allowlist profile guard differs')
        results.append('Explicit candidate accepted' if enabled else 'Default release allowlist refused expanded audio')
    payload.verify(stage)
    payload.verify_inventory(stage)
    payload.verify_inventory(baseline)
    (root / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
    print('Passed ' + str(len(results)) + ' expanded audio payload checks: ' + str(root))


if __name__ == '__main__':
    main()
