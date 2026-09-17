"""Author bounded WMA with Windows codecs and compare independent decoding.

Evaluation evidence, not a support/adoption switch. Does not install, register,
play audio, use customer files or redistribute Microsoft codecs.
"""
import argparse
import hashlib
import importlib.util
import itertools
import json
import math
import os
import pathlib
import re
import shutil
import struct
import subprocess
import sys
import uuid

TOOLS = pathlib.Path(__file__).resolve().parent
ROOT = TOOLS.parent.parent
spec = importlib.util.spec_from_file_location('audio_smoke', TOOLS / 'Test-AudioCandidate.py')
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)
spec = importlib.util.spec_from_file_location('wma_pro_frames', TOOLS / 'Inspect-WmaProFrames.py')
pro_frames = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pro_frames)

# Freeze the small matrix before running it. Sub-block, frame-boundary and
# one-second signals exercise flushing; they do not replace listening tests.
PROFILES = [('standard', 44100, 1, 16), ('standard', 48000, 2, 16),
            ('pro', 48000, 2, 16), ('pro', 48000, 2, 24), ('pro', 48000, 6, 24),
            ('lossless', 44100, 2, 16), ('lossless', 48000, 2, 24), ('lossless', 96000, 6, 24),
            ('voice', 8000, 1, 16), ('voice', 11025, 1, 16),
            ('voice', 16000, 1, 16), ('voice', 22050, 1, 16)]
PRO_TAIL_FRAMES = (511, 512, 513, 1023, 1024, 1025, 1535, 1536, 1537,
                   2047, 2048, 2050, 4095, 4096, 4097, 47999, 48001)
# Frozen before the repair replay: existing stereo prefix peak < 6e-7,
# RMS < 1.6e-7. Apply the limits to the complete independent Windows output.
PRO_STEREO_MAXIMUM_ERROR = 1e-6
PRO_STEREO_RMS_ERROR = 3e-7


def digest(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest().upper()


def native(arguments, directory, timeout=30):
    environment = dict(os.environ)
    environment.pop('FFREPORT', None)
    return subprocess.run([str(value) for value in arguments], cwd=directory,
                          env=environment, timeout=timeout, capture_output=True)


def compare(first, second):
    count = min(len(first), len(second))
    if any(not math.isfinite(value) for value in itertools.chain(first, second)):
        raise ValueError('Nonfinite decoded samples')
    if not count:
        return {'firstSamples': len(first), 'secondSamples': len(second),
                'sameExtent': len(first) == len(second), 'maximum': None, 'rms': None}
    errors = [abs(a - b) for a, b in zip(first, second)]
    return {'firstSamples': len(first), 'secondSamples': len(second),
            'sameExtent': len(first) == len(second), 'maximum': max(errors),
            'rms': math.sqrt(sum(value * value for value in errors) / count)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('expanded_build')
    args = parser.parse_args()
    if os.name != 'nt' or sys.version_info < (3, 14):
        raise ValueError('Use the existing Windows x64 / Python 3.14 toolchain')
    candidate = smoke.build_tool.local_path(args.expanded_build)
    record = json.loads((candidate / 'build-result.json').read_text())
    inventory = json.loads((candidate / 'runtime-files.json').read_text())
    expected = {'ffmpeg.exe', 'ffprobe.exe', *(pathlib.PurePosixPath(name).name for name in smoke.build_tool.DLLS)}
    binary = candidate / 'bin'
    if (record['exitCode'] or record.get('expandedInputs') is not True
            or record['scriptSha256'] != digest(TOOLS / 'Build-AudioEngine.py')
            or record['checkerSha256'] != digest(TOOLS / 'Check-AudioConfiguration.py')
            or len(inventory) != len(expected) or {item['path'] for item in inventory} != expected
            or {path.name for path in binary.iterdir()} != expected):
        raise ValueError('Use the completed restricted expanded-input runtime')
    for item in inventory:
        path = smoke.build_tool.local_path(binary / item['path'])
        if path.stat().st_size != item['bytes'] or digest(path) != item['sha256']:
            raise ValueError('Expanded runtime identity differs')
    evidence = ROOT / '.codex-temp' / ('wma-system-matrix-' + uuid.uuid4().hex)
    evidence.mkdir()
    print('WMA system evidence: ' + str(evidence), flush=True)
    source = TOOLS / 'WmaFixtureWriter.cpp'
    shutil.copy2(source, evidence / source.name)
    vswhere = pathlib.Path(os.environ['ProgramFiles(x86)']) / 'Microsoft Visual Studio/Installer/vswhere.exe'
    found = native([vswhere, '-latest', '-products', '*', '-requires',
                    'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '-property', 'installationPath'], evidence)
    if found.returncode:
        raise ValueError('Cannot locate the existing compiler')
    vcvars = pathlib.Path(found.stdout.decode().strip()) / 'VC/Auxiliary/Build/vcvars64.bat'
    if not vcvars.is_file() or any(char in str(vcvars) + str(evidence) for char in '%!&|^"'):
        raise ValueError('Unsupported native build path')
    command = evidence / 'build.cmd'
    command.write_text('@echo off\ncall "' + str(vcvars) + '" 10.0.26100.0\n'
                       'if errorlevel 1 exit /b 1\n'
                       'cl.exe /nologo /W4 /WX /O2 /MT /EHsc /std:c++20 WmaFixtureWriter.cpp '
                       '/link mfplat.lib mfreadwrite.lib mfuuid.lib mf.lib ole32.lib /DYNAMICBASE /NXCOMPAT\n'
                       'exit /b %errorlevel%\n')
    built = native(['cmd.exe', '/d', '/c', command], evidence, 60)
    (evidence / 'build.log').write_bytes(built.stdout + built.stderr)
    if built.returncode:
        raise ValueError('Fixture tool build failed; see retained build.log')
    writer = evidence / 'WmaFixtureWriter.exe'
    identity = {'toolSourceSha256': digest(source), 'writerSha256': digest(writer),
                'scriptSha256': digest(pathlib.Path(__file__)), 'windows': str(sys.getwindowsversion()),
                'proInspectorSha256': digest(TOOLS / 'Inspect-WmaProFrames.py'),
                'candidateRuntime': inventory, 'profiles': PROFILES,
                'candidateSourcePatches': record.get('patches', []),
                'lengths': ['1', '2049', 'one-second'],
                'losslessSurroundTailFrames': [65535, 65536, 65537, 192000],
                'proStereo16TailFrames': PRO_TAIL_FRAMES,
                'proStereoMaximumError': PRO_STEREO_MAXIMUM_ERROR,
                'proStereoRmsError': PRO_STEREO_RMS_ERROR,
                'criteria': 'Lossless must match authored samples exactly. Compare complete decoded extents for all codecs; report prefix error separately, never align, trim or pad to pass. Lossy listening quality remains unverified.'}
    (evidence / 'identity.json').write_text(json.dumps(identity, indent=2) + '\n')
    results = []
    for codec, rate, channels, bits in PROFILES:
        lengths = (1, 2049, rate)
        if (codec, rate, channels, bits) == ('lossless', 96000, 6, 24):
            lengths += (65535, 65536, 65537, 192000)
        if (codec, rate, channels, bits) == ('pro', 48000, 2, 16):
            lengths += PRO_TAIL_FRAMES
        for frames in lengths:
            name = f'{codec}-{rate}-{channels}-{bits}-{frames}'
            directory = evidence / name
            directory.mkdir()
            item = {'name': name, 'codec': codec, 'rate': rate, 'channels': channels,
                    'bits': bits, 'sourceFrames': frames}
            try:
                if digest(writer) != identity['writerSha256']:
                    raise ValueError('Fixture writer changed')
                generated = native([writer, codec, rate, channels, bits, frames], directory)
                (directory / 'writer.log').write_bytes(generated.stdout + generated.stderr)
                if generated.returncode:
                    raise ValueError('System fixture/decode failed: ' + generated.stderr.decode(errors='replace').strip())
                modules = re.findall(r'^Module: (.+)\r?$', generated.stdout.decode(errors='strict'), re.MULTILINE)
                if not modules:
                    raise ValueError('System codec module evidence is missing')
                module_paths = [pathlib.Path(value.strip()).resolve(strict=True) for value in modules]
                windows_root = pathlib.Path(os.environ['SystemRoot']).resolve(strict=True)
                if any(path != writer and not path.is_relative_to(windows_root) for path in module_paths):
                    raise ValueError('Fixture loaded a module outside Windows and its owned tool')
                item['modules'] = [{'path': str(path), 'sha256': digest(path)} for path in module_paths]
                fixture = directory / 'fixture.wma'
                item['sourceSha256'] = digest(directory / 'source.pcm')
                item['wmaSha256'] = digest(fixture)
                packet_options = ['-show_packets', '-show_data'] if codec == 'pro' else []
                probe = native([binary / 'ffprobe.exe', '-v', 'error', '-show_streams', '-show_format',
                                *packet_options, '-of', 'json', fixture], directory)
                (directory / 'probe.json').write_bytes(probe.stdout)
                if probe.returncode:
                    raise ValueError('Candidate inspection failed')
                facts = json.loads(probe.stdout)
                audio = [stream for stream in facts['streams'] if stream['codec_type'] == 'audio']
                expected_codec = {'standard': 'wmav2', 'pro': 'wmapro', 'lossless': 'wmalossless', 'voice': 'wmavoice'}[codec]
                if len(audio) != 1 or audio[0]['codec_name'] != expected_codec or int(audio[0]['sample_rate']) != rate or audio[0]['channels'] != channels:
                    raise ValueError('System encoded fixture does not match its selected profile')
                decoded = native([binary / 'ffmpeg.exe', '-v', 'error', '-nostdin', '-xerror', '-err_detect', 'explode',
                                  '-i', fixture, '-map', '0:a:0', '-c:a', 'pcm_f64le', '-f', 'f64le',
                                  '-fs', str(32 * 1024 * 1024), 'pipe:1'], directory)
                (directory / 'candidate.f64').write_bytes(decoded.stdout)
                (directory / 'candidate.log').write_bytes(decoded.stderr)
                if decoded.returncode or len(decoded.stdout) > 32 * 1024 * 1024 or len(decoded.stdout) % (channels * 8):
                    raise ValueError('Candidate decoding failed or exceeded its finite extent')
                samples = (directory / 'source.pcm').read_bytes()
                windows = (directory / 'windows.f32').read_bytes()
                if len(samples) != frames * channels * (bits // 8) or len(windows) % (channels * 4):
                    raise ValueError('Source or system output framing differs')
                authored = [int.from_bytes(samples[offset:offset + bits // 8], 'little', signed=True) / (1 << (bits - 1))
                            for offset in range(0, len(samples), bits // 8)]
                actual = list(struct.unpack('<' + 'd' * (len(decoded.stdout) // 8), decoded.stdout))
                reference = list(struct.unpack('<' + 'f' * (len(windows) // 4), windows))
                item['candidateVsWindows'] = compare(actual, reference)
                item['candidateVsAuthored'] = compare(actual, authored)
                item['windowsVsAuthored'] = compare(reference, authored)
                item['candidateFrames'] = len(actual) // channels
                item['windowsFrames'] = len(reference) // channels
                item['acceptedExtent'] = bool(actual and reference) and item['candidateVsWindows']['sameExtent']
                if codec == 'pro':
                    declarations = pro_frames.inspect(facts)
                    (directory / 'frame-headers.json').write_text(json.dumps(declarations, indent=2) + '\n')
                    predicted = declarations['samplesWithTerminalOverlapHypothesis']
                    item['proOverlapHypothesisMatches'] = predicted == item['windowsFrames'] == frames
                    # Diagnostic agreement never substitutes for actual decoded samples.
                    item['proDeclaredOverlap'] = declarations['terminalOverlapHypothesis']
                    if channels == 2:
                        comparison = item['candidateVsWindows']
                        item['acceptedProSamples'] = (item['acceptedExtent']
                            and item['candidateFrames'] == frames
                            and comparison['maximum'] <= PRO_STEREO_MAXIMUM_ERROR
                            and comparison['rms'] <= PRO_STEREO_RMS_ERROR)
                if codec == 'lossless':
                    item['acceptedLossless'] = all(item[field]['sameExtent'] and item[field]['maximum'] == 0
                                                   for field in ('candidateVsWindows', 'candidateVsAuthored', 'windowsVsAuthored'))
                if digest(fixture) != item['wmaSha256'] or digest(directory / 'source.pcm') != item['sourceSha256']:
                    raise ValueError('Fixture changed during comparison')
                item['status'] = 'observed'
            except (ValueError, OSError, subprocess.TimeoutExpired) as error:
                item['status'] = 'failed'; item['error'] = str(error)
            results.append(item)
            (evidence / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
            print(name + ': ' + item['status'] + ', equal extent=' + str(item.get('acceptedExtent')), flush=True)
    for item in inventory:
        if digest(binary / item['path']) != item['sha256']:
            raise ValueError('Candidate changed during evaluation')
    complete = all(item['status'] == 'observed' and item['acceptedExtent']
                   and item.get('acceptedLossless', True) and item.get('acceptedProSamples', True) for item in results)
    (evidence / 'summary.json').write_text(json.dumps({'cases': len(results), 'accepted': complete,
        'observed': sum(item['status'] == 'observed' for item in results),
        'equalExtents': sum(item.get('acceptedExtent', False) for item in results)}, indent=2) + '\n')
    print('Full matrix acceptance: ' + str(complete), flush=True)
    return 0 if complete else 1


if __name__ == '__main__':
    raise SystemExit(main())
