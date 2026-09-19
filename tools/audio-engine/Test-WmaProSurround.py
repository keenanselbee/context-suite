"""Finite WMA Pro surround agreement and unchanged-output regression.

Prepare generated Windows fixtures before evaluating the candidate. This does
not adopt a runtime, install anything, play audio, or use customer files.
"""
import array
import hashlib
import json
import math
import os
import pathlib
import random
import subprocess
import sys
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]
LENGTHS = (1, 127, 128, 129, 255, 256, 257, 511, 512, 513, 1023, 1024, 1025,
           2047, 2048, 2049, 4095, 4096, 4097, 48000, 48001)
SIGNALS = ('silence', 'impulses', 'noise', 'correlated', 'sparse')
PEAK = 1e-6
RMS = 3e-7


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()


def local(value):
    path = pathlib.Path(value).resolve(strict=True)
    if not path.is_relative_to(ROOT / '.codex-temp'):
        raise ValueError('Use repository-owned evaluation paths')
    return path


def run(arguments, directory):
    environment = dict(os.environ)
    environment.pop('FFREPORT', None)
    result = subprocess.run([str(value) for value in arguments], cwd=directory, env=environment,
                            timeout=60, capture_output=True)
    if result.returncode:
        (directory / 'native-failure.log').write_bytes(result.stdout + result.stderr)
        raise ValueError('Native operation failed in ' + str(directory))
    return result


def pcm(signal, frames):
    noise = random.Random(192619)
    result = bytearray()
    for frame in range(frames):
        for channel in range(6):
            frequency = 55 if channel == 3 else 233 + channel * 97
            value = .23 * math.sin(2 * math.pi * frequency * frame / 48000 + channel * .07)
            if signal == 'silence':
                value = 0
            elif signal == 'impulses':
                value = .3 if frame in (127 + 349 * channel, 16000 + 509 * channel, 47999 - channel) else 0
            elif signal == 'noise':
                value = noise.uniform(-.2, .2)
            elif signal == 'correlated':
                value = .2 * math.sin(2 * math.pi * 317 * frame / 48000) * (-1 if channel % 2 else 1)
            elif signal == 'sparse' and frame // 8000 != channel:
                value = 0
            result.extend(round(value * 8388607).to_bytes(3, 'little', signed=True))
    return bytes(result)


def prepare(corpus, writer_directory):
    writer = writer_directory / 'WmaFixtureWriter.exe'
    writer_hash = '44D83BEDCCED1F2989737CC72A65D046F6958CFC248FB9581095792F8C14A900'
    if digest(writer) != writer_hash:
        raise ValueError('Use the retained explicit-PCM Windows fixture writer')
    rows = json.loads((corpus / 'results.json').read_text())
    if len(rows) != 95:
        raise ValueError('Use the retained 95-input Windows corpus')
    evidence = ROOT / '.codex-temp' / ('wma-pro-surround-' + uuid.uuid4().hex)
    evidence.mkdir()
    print('Surround evidence: ' + str(evidence), flush=True)
    fixtures = []
    for row in rows:
        if pathlib.Path(row['name']).name != row['name']:
            raise ValueError('Invalid retained fixture name')
        path = corpus / row['name'] / 'fixture.wma'
        original = path.with_name('source.pcm')
        if digest(path) != row['wmaSha256'] or digest(original) != row['sourceSha256']:
            raise ValueError('Retained encoded/authored input changed')
        windows = path.with_name('windows.f32')
        previous = path.with_name('candidate.f64')
        fixtures.append(dict(name=row['name'], path=str(path), sha256=digest(path), windows=str(windows),
            windowsSha256=digest(windows), previous=str(previous), previousSha256=digest(previous),
            channels=row['channels'], frames=row['sourceFrames'], surround=row['codec'] == 'pro' and row['channels'] == 6))
    for signal, frames in [('tone', value) for value in LENGTHS] + [(signal, 48000) for signal in SIGNALS]:
        name = signal + '-' + str(frames)
        directory = evidence / name
        directory.mkdir()
        authored = pcm(signal, frames)
        (directory / 'input.pcm').write_bytes(authored)
        result = run([writer, 'pro', '48000', '6', '24', str(frames)], directory)
        (directory / 'generation.log').write_bytes(result.stdout + result.stderr)
        if (directory / 'source.pcm').read_bytes() != authored or (directory / 'windows.f32').stat().st_size != frames * 6 * 4:
            raise ValueError('Windows generation changed authored samples or decoded extent: ' + name)
        path = directory / 'fixture.wma'; windows = directory / 'windows.f32'
        fixtures.append(dict(name=name, path=str(path), sha256=digest(path), windows=str(windows), windowsSha256=digest(windows),
            channels=6, frames=frames, surround=True))
        print('Prepared ' + name, flush=True)
    if digest(writer) != writer_hash:
        raise ValueError('Windows writer changed during preparation')
    (evidence / 'fixtures.json').write_text(json.dumps(fixtures, indent=2) + '\n')
    (evidence / 'criteria.json').write_text(json.dumps(dict(peak=PEAK, rms=RMS, frames=list(LENGTHS), signals=list(SIGNALS),
        writerSha256=writer_hash, corpus=str(corpus), retained=95, fresh=26), indent=2) + '\n')
    print('Prepared 26 fresh fixtures and bound 95 retained inputs: ' + str(evidence), flush=True)


def verify(build, evidence):
    record = json.loads((build / 'build-result.json').read_text())
    if record['exitCode'] or not record.get('wmaProMatrixRepair'):
        raise ValueError('Use a completed explicit matrix-repair build')
    inventory = json.loads((build / 'runtime-files.json').read_text())
    for item in inventory:
        if pathlib.Path(item['path']).name != item['path'] or digest(build / 'bin' / item['path']) != item['sha256']:
            raise ValueError('Candidate runtime differs')
    criteria = json.loads((evidence / 'criteria.json').read_text())
    if criteria['peak'] != PEAK or criteria['rms'] != RMS or criteria['frames'] != list(LENGTHS) or criteria['signals'] != list(SIGNALS):
        raise ValueError('Frozen criteria differ')
    fixtures = json.loads((evidence / 'fixtures.json').read_text())
    if len(fixtures) != 121 or sum(bool(row['surround']) for row in fixtures) != 29:
        raise ValueError('Incomplete finite corpus')
    output_root = evidence / ('candidate-' + uuid.uuid4().hex); output_root.mkdir()
    print('Candidate results: ' + str(output_root), flush=True)
    results = []
    for fixture in fixtures:
        path = local(fixture['path']); windows = local(fixture['windows'])
        if digest(path) != fixture['sha256'] or digest(windows) != fixture['windowsSha256']:
            raise ValueError('Encoded input or Windows reference changed')
        output = output_root / (fixture['name'] + '.f64')
        result = run([build / 'bin/ffmpeg.exe', '-v', 'error', '-nostdin', '-n', '-protocol_whitelist', 'file,pipe',
                      '-i', path, '-map', '0:a:0', '-c:a', 'pcm_f64le', '-f', 'f64le', output], output_root)
        (output_root / (fixture['name'] + '.log')).write_bytes(result.stderr)
        if fixture['surround']:
            actual = array.array('d', output.read_bytes()); expected = array.array('f', windows.read_bytes())
            if len(actual) != len(expected) or len(actual) != fixture['frames'] * 6 or not all(math.isfinite(value) for value in actual):
                raise ValueError('Surround output extent or finite samples differ')
            deltas = [abs(x-y) for x,y in zip(actual, expected)]
            measurements = []
            for channel in range(6):
                errors = deltas[channel::6]
                measurements.append(dict(channel=channel, peak=max(errors), rms=math.sqrt(sum(value*value for value in errors)/len(errors))))
            peak = max(deltas); rms = math.sqrt(sum(value*value for value in deltas)/len(deltas))
            passed = peak <= PEAK and rms <= RMS and all(row['peak'] <= PEAK and row['rms'] <= RMS for row in measurements)
            results.append(dict(name=fixture['name'], passed=passed, peak=peak, rms=rms, channels=measurements))
        else:
            previous = local(fixture['previous'])
            if digest(previous) != fixture['previousSha256']:
                raise ValueError('Prior decoded output changed')
            results.append(dict(name=fixture['name'], passed=digest(output) == fixture['previousSha256'], scope='unchanged decoded bytes'))
        results[-1]['outputSha256'] = digest(output)
        (output_root / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
        print(('PASS ' if results[-1]['passed'] else 'FAIL ') + fixture['name'], flush=True)
    for item in inventory:
        if digest(build / 'bin' / item['path']) != item['sha256']:
            raise ValueError('Candidate runtime changed during evaluation')
    accepted = all(row['passed'] for row in results)
    (output_root / 'summary.json').write_text(json.dumps(dict(passed=accepted, cases=len(results), surround=29, unchanged=92,
        build=str(build), runtime=inventory, peakLimit=PEAK, rmsLimit=RMS), indent=2) + '\n')
    if not accepted:
        raise ValueError('Surround fidelity or unchanged-output controls failed; retain all results')
    print('Passed all 121 finite surround/regression cases: ' + str(output_root), flush=True)


if __name__ == '__main__':
    if len(sys.argv) != 4 or sys.argv[1] not in ('--prepare', '--verify'):
        raise ValueError('Use --prepare <95-case corpus> <writer directory> or --verify <matrix build> <prepared evidence>')
    if sys.argv[1] == '--prepare':
        prepare(local(sys.argv[2]), local(sys.argv[3]))
    else:
        verify(local(sys.argv[2]), local(sys.argv[3]))
