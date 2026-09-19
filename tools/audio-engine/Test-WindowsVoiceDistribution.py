"""Verify Voice bundle identities and optional isolated payload composition."""
import copy
import importlib.util
import json
import pathlib
import shutil
import subprocess
import sys
import uuid
import warnings
import zipfile
from unittest import mock

TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('audio_payload', TOOLS / 'Stage-AudioPayload.py')
payload = importlib.util.module_from_spec(spec)
spec.loader.exec_module(payload)
voice = payload.voice_provider()


def main():
    if len(sys.argv) != 5:
        raise ValueError('Use <Voice review bundle> <expanded audio review bundle> <inventoried image/audio stage> <curated stage>')
    original, audio, baseline, curated = map(payload.local_path, sys.argv[1:])
    voice.verify(original)
    payload.verify_inventory(baseline)
    assert payload.verify(curated).get('profile', 'curated') == 'curated'
    curated_before = voice.distribution.inventory(curated / 'audio-engine')
    root = payload.ROOT / '.codex-temp' / ('voice-distribution-tests-' + uuid.uuid4().hex)
    root.mkdir()
    print('Voice distribution evidence: ' + str(root), flush=True)
    results = ['Selected source/runtime bundle accepted']
    manifest = json.loads((original / 'bundle-inventory.json').read_text())

    def refused(name, operation):
        try:
            operation()
        except ValueError as error:
            results.append(dict(name=name, refused=str(error)))
        else:
            raise AssertionError('Unexpected acceptance: ' + name)

    for case in ('missing-host', 'changed-host', 'changed-source', 'changed-recipe', 'changed-notice',
                 'extra-windows-dll', 'extra-private-entry', 'duplicate-entry', 'changed-evidence',
                 'failed-native-result', 'changed-native-identity'):
        directory = root / case
        directory.mkdir()
        current = copy.deepcopy(manifest)
        shutil.copytree(original / 'evidence', directory / 'evidence')
        for bundle in current['bundles']:
            entries = []
            with zipfile.ZipFile(original / bundle['path']) as archive:
                for entry in archive.infolist():
                    data = archive.read(entry)
                    if case == 'missing-host' and entry.filename == 'voice/WmaVoiceMftProbe.exe':
                        continue
                    changed = ((case == 'changed-host' and entry.filename == 'voice/WmaVoiceMftProbe.exe') or
                               (case == 'changed-source' and entry.filename.endswith('.cpp')) or
                               (case == 'changed-recipe' and entry.filename.endswith('.py')) or
                               (case == 'changed-notice' and entry.filename == 'voice/NOTICE.txt'))
                    if changed:
                        data = bytes([data[0] ^ 1]) + data[1:]
                    entries.append((entry, data))
            if bundle['path'] == 'voice-runtime-review.zip':
                if case in ('extra-windows-dll', 'extra-private-entry'):
                    name = 'voice/mfplat.dll' if case == 'extra-windows-dll' else 'proprietary/synthetic-canary.txt'
                    entry = zipfile.ZipInfo(name)
                    entry.external_attr = 0o100644 << 16
                    entries.append((entry, b'Authored test canary, no system or private content.'))
                if case == 'duplicate-entry':
                    entries.append(entries[0])
            path = directory / bundle['path']
            with warnings.catch_warnings():
                warnings.simplefilter('ignore', UserWarning)
                with zipfile.ZipFile(path, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
                    for entry, data in entries:
                        archive.writestr(entry, data)
            # Bind altered outer hashes so refusal tests trusted content/membership.
            bundle['files'] = [dict(path=entry.filename, bytes=len(data), sha256=payload.digest(data)) for entry, data in entries]
            bundle['bytes'] = path.stat().st_size
            bundle['sha256'] = payload.digest(path.read_bytes())
        if case == 'changed-evidence':
            (directory / 'evidence/build.log').write_bytes(b'changed')
        if case in ('failed-native-result', 'changed-native-identity'):
            name = 'results.json' if case == 'failed-native-result' else 'identity.json'
            path = directory / 'evidence' / name
            value = json.loads(path.read_text())
            if case == 'failed-native-result':
                value[0]['passed'] = False
            else:
                value['executableSha256'] = '0' * 64
            voice.distribution.write_json(path, value)
            current['evidence'] = voice.distribution.inventory(directory / 'evidence')
        voice.distribution.write_json(directory / 'bundle-inventory.json', current)
        refused(case, lambda: voice.verify(directory))

    stage = root / 'candidate'
    stage.mkdir()
    for source in baseline.iterdir():
        if source.is_file() and source.name != 'payload-inventory.json':
            shutil.copyfile(source, stage / source.name)
    shutil.copytree(baseline / 'Assets', stage / 'Assets')
    payload.stage(audio, stage, original)
    record = payload.verify(stage)
    assert record['profile'] == 'expanded' and len(record['files']) == 20
    voice.distribution.write_json(stage / 'payload-inventory.json', voice.distribution.inventory(stage))
    payload.verify_inventory(stage)
    results.append('Twenty-file expanded/Voice payload and inventory accepted')

    def altered(name, change):
        target = root / name
        shutil.copytree(stage / 'audio-engine', target / 'audio-engine')
        change(target / 'audio-engine/voice')
        refused(name, lambda: payload.verify(target))

    altered('payload-host-bytes', lambda path: (path / 'WmaVoiceMftProbe.exe').write_bytes(b'changed'))
    altered('payload-missing-host', lambda path: (path / 'WmaVoiceMftProbe.exe').unlink())
    altered('payload-missing-notice', lambda path: (path / 'NOTICE.txt').unlink())
    altered('payload-notice-bytes', lambda path: (path / 'NOTICE.txt').write_bytes(b'changed'))
    altered('payload-extra-dll', lambda path: (path / 'mfplat.dll').write_bytes(b'canary'))
    refused('existing-payload', lambda: payload.stage(audio, stage, original))
    rejected = root / 'invalid-bundle-stage'
    rejected.mkdir()
    refused('invalid-bundle-before-copy', lambda: payload.stage(audio, rejected, root / 'changed-host'))
    assert not (rejected / 'audio-engine').exists()
    mixed = root / 'curated-with-voice'
    shutil.copytree(curated / 'audio-engine', mixed / 'audio-engine')
    shutil.copytree(stage / 'audio-engine/voice', mixed / 'audio-engine/voice')
    refused('curated-with-voice', lambda: payload.verify(mixed))

    receipts = payload.ROOT / 'artifacts/production-version-receipts'
    before_receipts = voice.distribution.inventory(receipts)
    for name, extra in (('no-audio', ['-StagingId', str(uuid.uuid4())]),
                        ('no-staging-id', ['-AudioDistributionDirectory', str(audio)])):
        arguments = ['powershell', '-NoProfile', '-File', str(payload.ROOT / 'tools/Build-Production.ps1'),
                     '-VoiceDistributionDirectory', str(original), *extra]
        result = subprocess.run(arguments, cwd=payload.ROOT, capture_output=True, timeout=30)
        (root / ('build-guard-' + name + '.log')).write_bytes(result.stdout + result.stderr)
        if result.returncode == 0 or b'Windows Voice candidate requires' not in result.stdout + result.stderr:
            raise AssertionError('Voice production build prerequisite guard differs')
        results.append('Production build refused ' + name + ' before version reservation')
    assert voice.distribution.inventory(receipts) == before_receipts

    partial = root / 'partial'
    partial.mkdir()
    original_open = pathlib.Path.open

    def fail_copy(path, *args, **kwargs):
        if path == partial / 'audio-engine/voice/NOTICE.txt' and args and args[0] == 'xb':
            raise PermissionError('Injected Voice notice copy failure')
        return original_open(path, *args, **kwargs)

    with mock.patch.object(pathlib.Path, 'open', fail_copy):
        try:
            payload.stage(audio, partial, original)
        except PermissionError:
            results.append('Partial Voice copy failure retained')
        else:
            raise AssertionError('Injected copy failure did not occur')
    refused('partial-payload', lambda: payload.verify(partial))
    refused('partial-retry', lambda: payload.stage(audio, partial, original))
    for enabled in (False, True):
        arguments = ['powershell', '-NoProfile', '-File', str(payload.ROOT / 'tools/curated-engine/Test-ProductionPayload.ps1'),
                     '-Payload', str(stage)] + (['-AllowAudioCandidate'] if enabled else [])
        result = subprocess.run(arguments, cwd=payload.ROOT, capture_output=True, timeout=90)
        (root / ('allowlist-' + str(enabled) + '.log')).write_bytes(result.stdout + result.stderr)
        if enabled and result.returncode != 0 or not enabled and (result.returncode == 0 or b'Unreviewed production file' not in result.stdout + result.stderr):
            raise AssertionError('Production allowlist guard differs')
        results.append('Explicit Voice candidate accepted' if enabled else 'Default release allowlist refused Voice candidate')
    payload.verify(stage)
    payload.verify_inventory(stage)
    payload.verify_inventory(baseline)
    assert voice.distribution.inventory(curated / 'audio-engine') == curated_before
    voice.verify(original)
    voice.distribution.write_json(root / 'results.json', results)
    print('Passed ' + str(len(results)) + ' Voice distribution/payload checks: ' + str(root))


if __name__ == '__main__':
    main()
