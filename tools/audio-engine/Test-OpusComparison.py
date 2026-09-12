"""Compare authored Opus outputs with Xiph's spectral metric; not full conformance."""
import argparse
import array
import importlib.util
import json
import math
import os
import pathlib
import re
import subprocess
import uuid
import zipfile

TOOLS = pathlib.Path(__file__).resolve().parent
ROOT = TOOLS.parent.parent
spec = importlib.util.spec_from_file_location('opus_decoder', TOOLS / 'Test-OpusDecoder.py')
decoder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(decoder)


def run(command, directory):
    return subprocess.run([str(x) for x in command], cwd=directory, capture_output=True, timeout=60)


def pcm_channel(values, channels, channel):
    # opus_compare takes stereo PCM16 for its reference input. Duplicate one
    # speaker into both channels, preserving every frame without time alignment.
    samples = array.array('h')
    for value in values[channel::channels]:
        sample = max(-32768, min(32767, round(value * 32768)))
        samples.extend((sample, sample))
    return samples.tobytes()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--matrix', required=True)
    parser.add_argument('--decoder', required=True)
    parser.add_argument('--opus-source-archive', required=True)
    args = parser.parse_args()
    local = decoder.candidate.build_tool.local_path
    matrix, engine, archive = (local(x) for x in (args.matrix, args.decoder, args.opus_source_archive))
    runtime = decoder.candidate.verify_evaluation(engine)
    pins = json.loads((TOOLS / 'source-inputs.json').read_text())
    # Source input membership is owned by the existing curation manifest.
    pin = next(x for x in pins['archives'] if x['id'] == 'opus')
    if decoder.digest(archive).upper() != pin['sha256'] or archive.stat().st_size != pin['bytes']:
        raise ValueError('Pinned Opus source archive differs')
    evidence = ROOT / '.codex-temp' / ('opus-comparison-' + uuid.uuid4().hex)
    evidence.mkdir()
    print('Comparison evidence: ' + str(evidence), flush=True)
    with zipfile.ZipFile(archive) as package:
        source = package.read(pin['prefix'] + 'src/opus_compare.c')
        (evidence / 'opus_compare.c').write_bytes(source)
        (evidence / 'COPYING').write_bytes(package.read(pin['prefix'] + 'COPYING'))
    vswhere = pathlib.Path(os.environ['ProgramFiles(x86)']) / 'Microsoft Visual Studio/Installer/vswhere.exe'
    result = run([vswhere, '-latest', '-products', '*', '-requires',
                  'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '-property', 'installationPath'], evidence)
    if result.returncode:
        raise ValueError('Cannot locate the existing compiler')
    visual_studio = pathlib.Path(result.stdout.decode().strip())
    vcvars = visual_studio / 'VC/Auxiliary/Build/vcvars64.bat'
    if not vcvars.is_file() or any(c in str(vcvars) + str(evidence) for c in '%!&|^"'):
        raise ValueError('Unsupported native build path')
    build = evidence / 'build.cmd'
    build.write_text('@echo off\ncall "' + str(vcvars) + '" 10.0.26100.0\n'
                     'if errorlevel 1 exit /b 1\n'
                     'cl.exe /nologo /O2 /MT /D_CRT_SECURE_NO_WARNINGS opus_compare.c '
                     '/Fe:opus_compare.exe /Fo:opus_compare.obj /link /DYNAMICBASE /NXCOMPAT\n'
                     'exit /b %errorlevel%\n')
    result = run(['cmd.exe', '/d', '/c', str(build)], evidence)
    (evidence / 'build.log').write_bytes(result.stdout + result.stderr)
    if result.returncode:
        raise ValueError('Comparison-tool build failed; see build.log')
    comparator = evidence / 'opus_compare.exe'
    comparator_hash = decoder.digest(comparator)
    records = json.loads((matrix / 'matrix.json').read_text())
    selected = [x for x in records if x['target'] == 'Opus' and x['outcome'] == 'passed']
    rates = (8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 64000, 88200, 96000, 176400, 192000)
    layouts = {'mono': 1, 'stereo': 2, 'quad': 4, '5.0': 5, '5.1': 6, '6.1': 7, '7.1': 8}
    expected = {(rate, layout) for rate in rates for layout in ('mono', 'stereo')}
    expected |= {(48000, layout) for layout in ('quad', '5.0', '5.1', '6.1', '7.1')}
    if (len(selected) != 33 or {(x['Rate'], x['Layout']) for x in selected} != expected or
            any(x['Channels'] != layouts[x['Layout']] for x in selected)):
        raise ValueError('Use the complete 33-output authored Opus matrix')
    results = []
    controls = []
    for item in selected:
        name = str(item['Rate']) + '-' + item['Layout']
        if name != item['source']:
            raise ValueError('Unexpected authored source name')
        encoded = local(matrix / (name + '-to-Opus.opus'))
        if encoded.stat().st_size > 2 * 1024 * 1024:
            raise ValueError('Authored encoded output exceeds its size bound')
        before = (decoder.digest(encoded), encoded.stat().st_mtime_ns)
        if before[0].upper() != item['details']['outputSha256']:
            raise ValueError('Adapter output identity differs')
        samples = {}
        for implementation in ('opus', 'libopus'):
            result = decoder.decode(engine, encoded, implementation, evidence)
            if result.returncode or len(result.stdout) != 48000 * item['Channels'] * 8:
                raise ValueError('Incomplete decoder output')
            samples[implementation] = array.array('d', result.stdout)
            if not all(math.isfinite(x) for x in samples[implementation]):
                raise ValueError('Nonfinite decoder output')
        for channel in range(item['Channels']):
            reference = evidence / (name + '-' + str(channel) + '-reference.pcm')
            actual = evidence / (name + '-' + str(channel) + '-native.pcm')
            reference.write_bytes(pcm_channel(samples['libopus'], item['Channels'], channel))
            actual.write_bytes(pcm_channel(samples['opus'], item['Channels'], channel))
            result = run([comparator, '-s', reference, actual], evidence)
            metric = (result.stdout + result.stderr).decode(errors='replace')
            score = re.search(r'Opus quality metric: ([-.0-9]+)', metric)
            if result.returncode == 0 and ('Test vector PASSES' not in metric or score is None):
                raise ValueError('Comparator returned no recognized metric')
            results.append({'source': name, 'channel': channel, 'opusSha256': before[0],
                            'exitCode': result.returncode, 'score': float(score[1]) if score else None,
                            'metric': metric})
        if item['Layout'] == '5.0':
            reference = evidence / (name + '-2-reference.pcm')
            for label, data in [('silence', bytes(48000 * 4)),
                                ('wrong-speaker', pcm_channel(samples['opus'], 5, 0))]:
                altered = evidence / (label + '.pcm')
                altered.write_bytes(data)
                result = run([comparator, '-s', reference, altered], evidence)
                if result.returncode != 1 or b'Test vector FAILS' not in result.stderr:
                    raise ValueError('Comparison did not reject the ' + label + ' signal as expected')
                controls.append({'case': label, 'exitCode': result.returncode,
                                 'metric': (result.stdout + result.stderr).decode(errors='replace')})
        if before != (decoder.digest(encoded), encoded.stat().st_mtime_ns):
            raise ValueError('Adapter output changed')
    if decoder.candidate.verify_evaluation(engine) != runtime or decoder.digest(comparator) != comparator_hash:
        raise ValueError('Comparison runtime changed')
    failures = [x for x in results if x['exitCode'] != 0]
    listening_review = [x for x in results if x['score'] is not None and x['score'] <= 90]
    report = {'decoderRuntime': runtime, 'sourcePin': pin, 'comparatorSha256': comparator_hash,
              'sourceSha256': decoder.digest(evidence / 'opus_compare.c'),
              'matrixSha256': decoder.digest(matrix / 'matrix.json'),
              'results': results, 'controls': controls, 'metricStatus': 'failed' if failures else 'passed',
              'listeningReview': listening_review, 'fidelityAcceptance': 'incomplete',
              'limits': 'Authored tones, PCM16 spectral comparison per speaker. Not official test vectors, '
                        'range-state verification, listening or independent container/resampler acceptance.'}
    (evidence / 'comparison.json').write_text(json.dumps(report, indent=2) + '\n')
    if failures:
        raise ValueError(str(len(failures)) + ' speaker comparisons failed; see comparison.json')
    if len(controls) != 2:
        raise ValueError('Missing negative controls')
    print('Passed ' + str(len(results)) + ' speaker comparisons across 33 outputs and two negative controls.')
    print(str(len(listening_review)) + ' comparisons score at or below 90; listening/fidelity acceptance remains incomplete.')


if __name__ == '__main__':
    main()
