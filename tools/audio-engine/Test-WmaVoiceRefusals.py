"""Verify Voice EOF errors using disposable mutations of the authored corpus."""
import importlib.util
import json
import pathlib
import sys
import uuid

TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('wma_system', TOOLS / 'Test-WmaSystemCodecs.py')
system = importlib.util.module_from_spec(spec)
spec.loader.exec_module(system)


def main():
    if len(sys.argv) != 3:
        raise ValueError('Usage: Test-WmaVoiceRefusals.py <repaired build> <completed system corpus>')
    candidate, corpus = (system.smoke.build_tool.local_path(value) for value in sys.argv[1:])
    record = json.loads((candidate / 'build-result.json').read_text())
    inventory = json.loads((candidate / 'runtime-files.json').read_text())
    identity = json.loads((corpus / 'identity.json').read_text())
    if (record['exitCode'] or record.get('wmaVoiceCacheRepair') is not True
            or record['scriptSha256'] != system.digest(TOOLS / 'Build-AudioEngine.py')
            or identity['candidateRuntime'] != inventory
            or identity['toolSourceSha256'] != system.digest(TOOLS / 'WmaFixtureWriter.cpp')):
        raise ValueError('Use matching repaired runtime and authored corpus identities')
    binary = candidate / 'bin'
    for item in inventory:
        if system.digest(binary / item['path']) != item['sha256']:
            raise ValueError('Runtime identity changed')
    originals = {row['name']: row for row in json.loads((corpus / 'results.json').read_text())}
    evidence = system.ROOT / '.codex-temp' / ('wma-voice-refusals-' + uuid.uuid4().hex)
    evidence.mkdir()
    print('Voice refusal evidence: ' + str(evidence), flush=True)
    results = []
    for rate in (8000, 11025, 16000, 22050):
        name = f'voice-{rate}-1-16-1'
        source = corpus / name / 'fixture.wma'
        if (originals[name]['status'] != 'observed' or source.stat().st_size > 1024 * 1024
                or system.digest(source) != originals[name]['wmaSha256']):
            raise ValueError('Authored one-frame source changed')
        original = source.read_bytes()
        probe = system.native([binary / 'ffprobe.exe', '-v', 'error', '-show_packets',
                               '-show_data', '-of', 'json', source], evidence)
        if probe.returncode or len(probe.stdout) > 1024 * 1024:
            raise ValueError('Cannot inspect bounded authored packet')
        packets = json.loads(probe.stdout)['packets']
        if len(packets) != 1:
            raise ValueError('One-frame fixture no longer has one codec packet')
        packet = system.pro_frames.unpack(packets[0]['data'])
        if not 3 <= len(packet) <= 4096:
            raise ValueError('Unexpected authored packet size')
        bits = ''.join(f'{value:08b}' for value in packet)
        reader = system.pro_frames.Bits(bits)
        if reader.take(4) != 0 or reader.take(1) != 1 or reader.take(6) != 1:
            raise ValueError('Unexpected authored Voice header')
        if reader.take(3 + (len(packet) - 1).bit_length()) != 0:
            raise ValueError('Unexpected spillover in first packet')
        header = reader.position
        if reader.take(1) != 1 or reader.take(1) != 1 or reader.take(12) != 1:
            raise ValueError('Fixture must declare one speech sample')
        offset = original.find(packet)
        if offset < 0 or original.find(packet, offset + 1) >= 0:
            raise ValueError('Expected one complete, uniquely located packet')
        for kind in ('positive', 'unsupported-music', 'invalid-sample-count'):
            changed = list(bits)
            if kind == 'unsupported-music':
                changed[header] = '0'
            elif kind == 'invalid-sample-count':
                changed[header + 2:header + 14] = '1' * 12
            mutated = bytes(int(''.join(changed[i:i + 8]), 2) for i in range(0, len(changed), 8))
            fixture = evidence / (name + '-' + kind + '.wma')
            fixture.write_bytes(original[:offset] + mutated + original[offset + len(packet):])
            before = system.digest(fixture)
            result = system.native([binary / 'ffmpeg.exe', '-v', 'error', '-nostdin', '-xerror',
                '-err_detect', 'explode', '-i', fixture, '-map', '0:a:0', '-c:a', 'pcm_f64le',
                '-f', 'f64le', '-fs', '131072', 'pipe:1'], evidence)
            (evidence / (fixture.stem + '.log')).write_bytes(result.stderr)
            if len(result.stdout) > 131072 or len(result.stderr) > 131072:
                raise ValueError('Refusal test exceeded its output bound')
            if kind == 'positive':
                passed = result.returncode == 0 and len(result.stdout) == 8 and not result.stderr
            else:
                expected = -1163346256 if kind == 'unsupported-music' else -1094995529
                message = ('Decoding error: Not yet implemented in FFmpeg, patches welcome'
                    if kind == 'unsupported-music' else 'Superframe encodes > 480 samples (4095)')
                passed = (result.returncode & 0xFFFFFFFF) == (expected & 0xFFFFFFFF)
                passed &= not result.stdout and message in result.stderr.decode(errors='replace')
            passed &= system.digest(fixture) == before
            results.append({'name': fixture.name, 'sha256': before, 'exitCode': result.returncode,
                            'outputBytes': len(result.stdout), 'passed': bool(passed)})
            (evidence / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
        if system.digest(source) != originals[name]['wmaSha256']:
            raise ValueError('Original source changed')
    for item in inventory:
        if system.digest(binary / item['path']) != item['sha256']:
            raise ValueError('Runtime changed during verification')
    (evidence / 'identity.json').write_text(json.dumps({'runtime': inventory,
        'scriptSha256': system.digest(pathlib.Path(__file__)), 'corpus': str(corpus)}, indent=2) + '\n')
    if not all(row['passed'] for row in results):
        raise ValueError('Voice refusal or positive control failed; inspect retained results')
    print('Four positive controls and eight exact decoder-error checks passed.')


if __name__ == '__main__':
    main()
