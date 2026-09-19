"""Prepare or verify the owned Voice host's local source/runtime review bundle."""
import argparse
import hashlib
import importlib.util
import json
import pathlib
import stat
import uuid
import zipfile

TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('audio_distribution', TOOLS / 'Prepare-AudioDistribution.py')
distribution = importlib.util.module_from_spec(spec)
spec.loader.exec_module(distribution)
EVIDENCE = ('WmaVoiceMftProbe.cpp', 'build.cmd', 'build.log', 'pe.txt', 'identity.json', 'results.json', 'guards.json')


def selection():
    pin = json.loads((TOOLS / 'voice-provider-candidate.json').read_text())
    if (pin['sourceSha256'] != distribution.build_tool.digest(TOOLS / 'WmaVoiceMftProbe.cpp') or
            len(pin['files']) != 2 or {row['path'] for row in pin['files']} != {'voice/WmaVoiceMftProbe.exe', 'voice/NOTICE.txt'}):
        raise ValueError('Voice source or payload membership differs')
    return pin


def source_files():
    return {'tools/audio-engine/WmaVoiceMftProbe.cpp': TOOLS / 'WmaVoiceMftProbe.cpp',
            'tools/audio-engine/Build-WindowsVoice.py': TOOLS / 'Build-WindowsVoice.py',
            'BUILD.md': TOOLS / 'distribution/BUILD-VOICE.md',
            'NOTICE.txt': TOOLS / 'distribution/WINDOWS-VOICE-NOTICE.txt'}


def verify(value):
    directory = distribution.build_tool.local_path(value)
    path = directory / 'bundle-inventory.json'
    if path.stat().st_size > 65536:
        raise ValueError('Oversized Voice bundle inventory')
    manifest = json.loads(path.read_text())
    pin = selection()
    if manifest['candidate'] != pin:
        raise ValueError('Voice bundle candidate differs')
    evidence = manifest['evidence']
    if len(evidence) != len(EVIDENCE) or {row['path'] for row in evidence} != set(EVIDENCE):
        raise ValueError('Voice build evidence membership differs')
    for row in evidence:
        path = distribution.build_tool.local_path(directory / 'evidence' / row['path'])
        if path.stat().st_size != row['bytes'] or distribution.build_tool.digest(path) != row['sha256']:
            raise ValueError('Voice retained evidence differs')
    identity = json.loads((directory / 'evidence/identity.json').read_text())
    native_pin = next(row for row in pin['files'] if row['path'].endswith('.exe'))
    if (identity['sourceSha256'] != pin['sourceSha256'] or identity['executableSha256'] != native_pin['sha256'] or
            distribution.build_tool.digest(directory / 'evidence/WmaVoiceMftProbe.cpp') != pin['sourceSha256']):
        raise ValueError('Voice native/source identity differs')
    results = json.loads((directory / 'evidence/results.json').read_text())
    guards = json.loads((directory / 'evidence/guards.json').read_text())
    if len(results) != 44 or len(guards) != 4 or not all(row['passed'] is True for row in results + guards):
        raise ValueError('Voice retained native acceptance is incomplete')
    expected = {'voice-source-review.zip': set(source_files()), 'voice-runtime-review.zip': {row['path'] for row in pin['files']}}
    if len(manifest['bundles']) != 2 or {row['path'] for row in manifest['bundles']} != set(expected):
        raise ValueError('Voice archive membership differs')
    runtime = {}
    for bundle in manifest['bundles']:
        path = distribution.build_tool.local_path(directory / bundle['path'])
        if path.stat().st_size != bundle['bytes'] or path.stat().st_size > 2 * 1024 * 1024 or distribution.build_tool.digest(path) != bundle['sha256']:
            raise ValueError('Voice archive identity differs')
        if len(bundle['files']) != len(expected[path.name]) or {row['path'] for row in bundle['files']} != expected[path.name]:
            raise ValueError('Voice archive file membership differs')
        with zipfile.ZipFile(path) as archive:
            if archive.namelist() != [row['path'] for row in bundle['files']]:
                raise ValueError('Voice ZIP membership differs')
            for row in bundle['files']:
                item = archive.getinfo(row['path'])
                if item.file_size != row['bytes'] or item.file_size > 1024 * 1024 or stat.S_IFMT(item.external_attr >> 16) != stat.S_IFREG:
                    raise ValueError('Invalid Voice ZIP entry')
                data = archive.read(item)
                digest = hashlib.sha256(data).hexdigest().upper()
                if digest != row['sha256']:
                    raise ValueError('Voice ZIP content differs')
                if path.name == 'voice-source-review.zip':
                    if data != source_files()[row['path']].read_bytes():
                        raise ValueError('Voice source kit differs from reviewed files')
                else:
                    selected = next(entry for entry in pin['files'] if entry['path'] == row['path'])
                    if digest != selected['sha256'] or len(data) != selected['bytes']:
                        raise ValueError('Voice runtime differs from selected identity')
                    runtime[row['path']] = data
    if runtime['voice/NOTICE.txt'] != (TOOLS / 'distribution/WINDOWS-VOICE-NOTICE.txt').read_bytes():
        raise ValueError('Voice provider notice differs')
    return runtime


def prepare(value):
    helper = distribution.build_tool.local_path(value)
    pin = selection()
    executable = helper / 'WmaVoiceMftProbe.exe'
    selected = next(row for row in pin['files'] if row['path'].endswith('.exe'))
    if executable.stat().st_size != selected['bytes'] or distribution.build_tool.digest(executable) != selected['sha256']:
        raise ValueError('Use the selected previously tested Voice helper')
    directory = distribution.build_tool.SCRATCH / ('voice-distribution-' + uuid.uuid4().hex)
    directory.mkdir()
    for name, source in source_files().items():
        distribution.copy_verified(source, directory / 'source-kit' / name, distribution.build_tool.digest(source))
    distribution.copy_verified(executable, directory / 'runtime/voice/WmaVoiceMftProbe.exe', selected['sha256'])
    notice = TOOLS / 'distribution/WINDOWS-VOICE-NOTICE.txt'
    distribution.copy_verified(notice, directory / 'runtime/voice/NOTICE.txt', distribution.build_tool.digest(notice))
    for name in EVIDENCE:
        source = distribution.build_tool.local_path(helper / name)
        distribution.copy_verified(source, directory / 'evidence' / name, distribution.build_tool.digest(source))
    bundles = [distribution.create_archive(directory / 'source-kit', directory / 'voice-source-review.zip'),
               distribution.create_archive(directory / 'runtime', directory / 'voice-runtime-review.zip')]
    distribution.write_json(directory / 'bundle-inventory.json', dict(candidate=pin, bundles=bundles,
        evidence=distribution.inventory(directory / 'evidence'), status='Local engineering review; release approval remains separate'))
    verify(directory)
    print('Verified Windows Voice review bundle: ' + str(directory))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument('--prepare')
    group.add_argument('--verify')
    args = parser.parse_args()
    if args.prepare:
        prepare(args.prepare)
    else:
        verify(args.verify)
        print('Verified Windows Voice source/runtime bundle; release approval remains separate.')
