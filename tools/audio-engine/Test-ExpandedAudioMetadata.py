"""Verify expanded audio descriptions and artwork through real Convert commands."""
import importlib.util
import json
import os
import pathlib
import shutil
import struct
import subprocess
import sys
import uuid

TOOLS = pathlib.Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, TOOLS / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


direct = module('expanded_direct', 'Test-ExpandedAudioDirect.py')
candidate = module('audio_candidate', 'Test-AudioCandidate.py')
EXTENDED_CONTENT = uuid.UUID('d2d0a440-e307-11d2-97f0-00a0c95ea850').bytes_le
FILE_PROPERTIES = uuid.UUID('8cabdca1-a947-11cf-8ee4-00c00c205365').bytes_le


def header_objects(data):
    if data[:16] != uuid.UUID('75b22630-668e-11cf-a6d9-00aa0062ce6c').bytes_le:
        raise ValueError('Expected generated ASF input')
    end = struct.unpack_from('<Q', data, 16)[0]
    if not 30 <= end <= min(len(data), 16 * 1024 * 1024):
        raise ValueError('Invalid generated ASF header')
    offset = 30
    objects = []
    while offset < end:
        size = struct.unpack_from('<Q', data, offset + 16)[0]
        if size < 24 or size > end - offset:
            raise ValueError('Invalid generated ASF object')
        objects.append(data[offset:offset + size])
        offset += size
    if len(objects) != struct.unpack_from('<I', data, 24)[0]:
        raise ValueError('Generated ASF object count changed')
    return end, objects


def with_description(data, description):
    end, objects = header_objects(data)
    # Copy only an authored fixture's descriptive object; keep the destination's
    # stream configuration and packet bytes. Update both file/header extents.
    objects = [item for item in objects if item[:16] != EXTENDED_CONTENT] + [description]
    size = 30 + sum(map(len, objects))
    length = size + len(data) - end
    fixed = []
    for item in objects:
        item = bytearray(item)
        if item[:16] == FILE_PROPERTIES:
            struct.pack_into('<Q', item, 40, length)
        fixed.append(item)
    header = bytearray(data[:30])
    struct.pack_into('<QI', header, 16, size, len(fixed))
    result = bytes(header) + b''.join(fixed) + data[end:]
    if result[size:] != data[end:]:
        raise ValueError('Fixture packet bytes changed')
    return result


def native(arguments, cwd):
    environment = dict(os.environ)
    environment.pop('FFREPORT', None)
    result = subprocess.run([str(value) for value in arguments], cwd=cwd, env=environment,
                            capture_output=True, timeout=30)
    if result.returncode:
        raise ValueError('Independent metadata check failed: ' + result.stderr.decode(errors='replace'))
    return result.stdout


def main():
    if len(sys.argv) != 6:
        raise ValueError('Test-ExpandedAudioMetadata.py <worker evidence> <AIFF evidence> <WMA evidence> <Voice evidence> <supplier bin>')
    worker, aiff, wma, voice, supplier = (pathlib.Path(value).resolve(strict=True) for value in sys.argv[1:])
    if any(not path.is_relative_to(ROOT / '.codex-temp') for path in (worker, aiff, wma, voice, supplier)):
        raise ValueError('Use repository-owned disposable evidence')
    supplier_identity = candidate.verify_evaluation(supplier)
    prepared = ROOT / '.codex-temp' / ('expanded-audio-metadata-fixtures-' + uuid.uuid4().hex)
    prepared.mkdir()
    fixtures = []

    def add(name, source, tags, picture, extension='.wma', refuse=False):
        path = prepared / (name + extension)
        if isinstance(source, bytes):
            path.write_bytes(source)
        else:
            shutil.copy2(source, path)
        fixtures.append(dict(Name=name, Path=str(path), Sha256=direct.digest(path), Tags=tags, Picture=picture, Refuse=refuse))

    tags = {'title': 'Title \u00fc', 'artist': 'Artist \u4e2d', 'album': 'Album', 'date': '2026', 'track': '2'}
    add('aiff-legacy', aiff / 'tone-legacy-text-Wave.source', {'title': 'Title', 'comment': 'Comment'}, False, '.aif')
    add('aiff-id3', aiff / 'tone-id3-picture-Wave.source',
        {'title': 'Title \u00fc', 'artist': 'Artist \u4e2d', 'comment': 'Line 1\nLine 2'}, True, '.aif')
    for codec in ('wmav1', 'wmav2'):
        add(codec, wma / (codec + '-picture-Wave.wma'), tags, True)
    add('voice', voice / 'voice-metadata.wma',
        {'title': 'Voice title \u00fc', 'artist': 'Voice artist \u4e2d', 'album': 'Voice album', 'date': '2026', 'track': '2', 'language': 'en-us'}, True)
    donor = (wma / 'wmav2-picture-Wave.wma').read_bytes()
    descriptions = [item for item in header_objects(donor)[1] if item[:16] == EXTENDED_CONTENT]
    if len(descriptions) != 1:
        raise ValueError('Expected one authored descriptive object')
    corpus = json.loads((worker / 'fixtures.json').read_text())
    for label in ('pro', 'lossless'):
        fixture = next(item for item in corpus if item['Name'] == label + '-48000-2-24-48000')
        path = pathlib.Path(fixture['Path'])
        if direct.digest(path) != fixture['Sha256']:
            raise ValueError('Independent system-encoded fixture changed')
        add(label, with_description(path.read_bytes(), descriptions[0]), tags, True)
    marked = bytearray((prepared / 'aiff-id3.aif').read_bytes())
    marked += b'MARK' + struct.pack('>I', 2) + b'\0\0'
    struct.pack_into('>I', marked, 4, len(marked) - 8)
    add('aiff-markers-refused', bytes(marked), {}, True, '.aif', True)
    protection = uuid.UUID('2211b3fb-bd23-11d2-b4b7-00a0c955fc6e').bytes_le + struct.pack('<Q', 24)
    add('wma-protection-refused', with_description(donor, protection), {}, False, refuse=True)
    manifest = prepared / 'fixtures.json'
    manifest.write_text(json.dumps(fixtures, indent=2) + '\n')
    evidence = direct.run(worker, '--expanded-audio-metadata-direct', manifest)
    (evidence / 'independent-identity.json').write_text(json.dumps(supplier_identity, indent=2) + '\n')
    reports = json.loads((evidence / 'results/expanded-metadata.json').read_text())
    if len(reports) != 42:
        raise ValueError('Incomplete seven-input/six-target publication matrix')
    checks = []
    for index, row in enumerate(reports):
        path = pathlib.Path(row['Output'])
        if direct.digest(path) != row['OutputSha256'] or direct.digest(pathlib.Path(row['Source'])) != row['SourceSha256']:
            raise ValueError('Published copy or original changed')
        probe = json.loads(native([supplier / 'ffprobe.exe', '-v', 'error', '-protocol_whitelist', 'file,pipe',
            '-show_streams', '-show_format', '-of', 'json', path], evidence))
        (evidence / ('independent-probe-' + str(index) + '.json')).write_text(json.dumps(probe, indent=2) + '\n')
        actual = [(key.lower(), value) for node in [probe.get('format', {}), *probe.get('streams', [])]
                  for key, value in node.get('tags', {}).items()]
        aliases = {'tracknumber': 'track', 'description': 'comment', 'albumartist': 'album_artist'}
        actual = [(aliases.get(key, key), value) for key, value in actual]
        if any((key, value) not in actual for key, value in row['Tags'].items()):
            raise ValueError('Independent descriptive values differ: ' + str(path))
        checks.append(row['Name'] + '/' + row['Target'] + ': independent descriptive values')
        if row['Picture']:
            pixels = native([supplier / 'ffmpeg.exe', '-v', 'error', '-nostdin', '-protocol_whitelist', 'file,pipe',
                '-i', path, '-map', '0:v:0', '-frames:v', '1', '-threads', '1', '-f', 'rawvideo', '-pix_fmt', 'rgba', 'pipe:1'], evidence)
            if pixels != bytes([255, 0, 128, 255, 0, 255, 0, 128]):
                raise ValueError('Independent artwork pixels differ: ' + str(path))
            checks.append(row['Name'] + '/' + row['Target'] + ': exact authored RGBA pixels')
    if candidate.verify_evaluation(supplier) != supplier_identity:
        raise ValueError('Independent runtime changed during verification')
    if any(direct.digest(pathlib.Path(item['Path'])) != item['Sha256'] for item in fixtures):
        raise ValueError('Prepared source fixture changed')
    (evidence / 'independent-results.json').write_text(json.dumps(dict(checks=checks, outputs=len(reports)), indent=2) + '\n')
    print('Passed ' + str(len(checks)) + ' independent metadata/artwork checks; evidence: ' + str(evidence))


if __name__ == '__main__':
    main()
