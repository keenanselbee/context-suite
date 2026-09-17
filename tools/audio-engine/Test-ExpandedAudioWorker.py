"""Build a disposable worker payload and publish the finite AIFF/WMA corpus.

No installer, Explorer registration, production stage, customer files or network.
"""
import hashlib
import importlib.util
import json
import math
import pathlib
import shutil
import subprocess
import struct
import sys
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]


def boundary_refusals(name, channels):
    # Diagnosed against the unchanged 0.15 RMS bound, with the selected runtime.
    # These full-scale, discontinuous precision vectors are lossless controls,
    # not representative positive lossy-encoding signals.
    if name.startswith(('aiff-', 'aifc-none-')):
        return ['Mp3', 'Opus'] if channels == 1 else ['Mp3', 'M4a', 'Opus']
    if name.startswith('aifc-float'):
        return ['M4a'] if channels == 1 else ['Mp3', 'M4a', 'Vorbis', 'Opus']
    if name.startswith('aifc-sowt'):
        return ['Mp3', 'M4a', 'Vorbis', 'Opus']
    return []


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()


def local(value):
    path = pathlib.Path(value).resolve(strict=True)
    if not path.is_relative_to(ROOT / '.codex-temp'):
        raise ValueError('Use repository-owned evaluation inputs')
    return path


def main():
    if len(sys.argv) != 5:
        raise ValueError('Test-ExpandedAudioWorker.py <repaired build> <Voice helper directory> <system corpus> <expanded fixtures>')
    build, helper, corpus, expanded = map(local, sys.argv[1:])
    inventory = json.loads((build / 'runtime-files.json').read_text())
    expected = {'ffmpeg.exe', 'ffprobe.exe', 'avcodec-63.dll', 'avfilter-12.dll', 'avformat-63.dll', 'avutil-61.dll', 'swresample-7.dll'}
    if len(inventory) != 7 or {row['path'] for row in inventory} != expected:
        raise ValueError('Unexpected repaired runtime inventory')
    if digest(build / 'bin/ffmpeg.exe') != '466873A38C3846C5D96A839D6A0D4DCF6621290B96F72A4F196A56DD6555E004':
        raise ValueError('Use the selected repaired runtime')
    for row in inventory:
        path = build / 'bin' / row['path']
        if digest(path) != row['sha256'] or path.stat().st_size != row['bytes']:
            raise ValueError('Runtime identity changed')
    helper = helper / 'WmaVoiceMftProbe.exe'
    if digest(helper) != 'B03029719B338363193C6111A204B144A23EF52947301D6CA5493267C3D0DBA5':
        raise ValueError('Use the selected streaming Voice helper')
    evidence = ROOT / '.codex-temp' / ('expanded-audio-worker-' + uuid.uuid4().hex)
    evidence.mkdir()
    print('Expanded audio worker evidence: ' + str(evidence), flush=True)
    for project in ('src/ContextSuite.Worker/ContextSuite.Worker.csproj',
                    'tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj'):
        result = subprocess.run(['dotnet', 'build', str(ROOT / project), '-c', 'Release', '--no-restore', '--nologo'],
                                cwd=ROOT, capture_output=True, timeout=120)
        (evidence / (pathlib.Path(project).stem + '-build.log')).write_bytes(result.stdout + result.stderr)
        if result.returncode:
            raise ValueError('Managed build failed; see retained log')
    payload = evidence / 'worker'; payload.mkdir()
    managed = ROOT / 'artifacts/managed/bin/ContextSuite.Worker/Release/net10.0-windows'
    for path in managed.iterdir():
        if path.is_file():
            shutil.copy2(path, payload / path.name)
    audio = payload / 'audio-engine'; audio.mkdir()
    for row in inventory:
        shutil.copy2(build / 'bin' / row['path'], audio / row['path'])
    voice = audio / 'voice'; voice.mkdir()
    shutil.copy2(helper, voice / helper.name)
    rows = json.loads((corpus / 'results.json').read_text())
    if len(rows) != 95:
        raise ValueError('Use the complete 95-case system corpus')
    fixtures = []
    for row in rows:
        name = row['name']
        if pathlib.Path(name).name != name:
            raise ValueError('Invalid fixture name')
        path = corpus / name / 'fixture.wma'
        if digest(path) != row['wmaSha256']:
            raise ValueError('System fixture changed')
        lossless = row['codec'] == 'lossless'
        reference = corpus / name / ('source.pcm' if lossless else 'windows.f32')
        unit = row['bits'] // 8 if lossless else 4
        if reference.stat().st_size % (unit * row['channels']):
            raise ValueError('Incomplete independent reference sample')
        frames = reference.stat().st_size // (unit * row['channels'])
        # Surround Pro numeric agreement remains recorded, not accepted; native
        # evaluation established different kernels beyond the stereo tolerance.
        tolerance = None if row['codec'] == 'pro' and row['channels'] > 2 else 0 if lossless or row['codec'] == 'voice' else 1e-6
        fixtures.append(dict(Name=name, Path=str(path), Sha256=digest(path),
            Codec={'standard': 'wmav2', 'pro': 'wmapro', 'lossless': 'wmalossless', 'voice': 'wmavoice'}[row['codec']],
            Rate=row['rate'], Channels=row['channels'], Frames=frames, AuthoredFrames=row['sourceFrames'], ReferencePath=str(reference),
            ReferenceHash=digest(reference), ReferenceEncoding='s' + str(row['bits']) if lossless else 'f32',
            MaximumError=tolerance, RmsError=3e-7 if tolerance == 1e-6 else tolerance))
    extras = json.loads((expanded / 'result.json').read_text())['fixtures']
    if len(extras) != 27:
        raise ValueError('Use the complete expanded-input corpus')
    for row in extras:
        path = expanded / row['fixture']; name = path.stem
        if path.parent != expanded or digest(path) != row['sha256']:
            raise ValueError('Expanded fixture changed')
        probe = json.loads((expanded / (name + '-candidate-probe.json')).read_text())
        stream = next(item for item in probe['streams'] if item['codec_type'] == 'audio')
        reference = expanded / (name + '-candidate.f64')
        if digest(reference) != row['decoders']['candidate']['sha256']:
            raise ValueError('Expanded reference changed')
        fixtures.append(dict(Name='expanded-' + name, Path=str(path), Sha256=digest(path), Codec=row['codec'],
            Rate=int(stream['sample_rate']), Channels=row['channels'], Frames=row['decoders']['candidate']['frames'],
            ReferencePath=str(reference), ReferenceHash=digest(reference), ReferenceEncoding='f64', MaximumError=0, RmsError=0,
            RefusedTargets=boundary_refusals(name, row['channels'])))
    spec = importlib.util.spec_from_file_location('expanded_inputs', ROOT / 'tools/audio-engine/Test-AudioExpandedInputs.py')
    authored = importlib.util.module_from_spec(spec); spec.loader.exec_module(authored)
    tones = evidence / 'tones'; tones.mkdir()
    for channels in (1, 2):
        values = [.2 * math.sin(2 * math.pi * (233 + 127 * channel) * frame / 48000)
                  for frame in range(48000) for channel in range(channels)]
        variants = [(f'aiff-{bits}', bits, None) for bits in (8, 16, 24, 32)]
        variants += [(f'aifc-none-{bits}', bits, b'NONE') for bits in (8, 16, 24, 32)]
        variants += [('aifc-float32', 32, b'fl32'), ('aifc-float64', 64, b'fl64'), ('aifc-sowt', 16, b'sowt')]
        for label, bits, compression in variants:
            floating = compression in (b'fl32', b'fl64')
            if floating:
                samples = [struct.unpack('>f', struct.pack('>f', value))[0] for value in values] if bits == 32 else values
                decoded = samples
            else:
                samples = [round(value * ((1 << (bits - 1)) - 1)) for value in values]
                decoded = [value / (1 << (bits - 1)) for value in samples]
            name = f'tone-{label}-{channels}'
            path = tones / (name + ('.aifc' if compression else '.aif'))
            path.write_bytes(authored.aiff(samples, bits, channels, compression))
            reference = tones / (name + '.f64'); reference.write_bytes(b''.join(struct.pack('<d', value) for value in decoded))
            codec = f'pcm_f{bits}be' if floating else 'pcm_s16le' if compression == b'sowt' else 'pcm_s8' if bits == 8 else f'pcm_s{bits}be'
            fixtures.append(dict(Name=name, Path=str(path), Sha256=digest(path), Codec=codec, Rate=48000, Channels=channels,
                Frames=48000, ReferencePath=str(reference), ReferenceHash=digest(reference), ReferenceEncoding='f64', MaximumError=0, RmsError=0))
    manifest = evidence / 'fixtures.json'; manifest.write_text(json.dumps(fixtures, indent=2) + '\n')
    identities = {str(path.relative_to(payload)): digest(path) for path in payload.rglob('*') if path.is_file()}
    (evidence / 'payload-identities.json').write_text(json.dumps(identities, indent=2) + '\n')
    test = ROOT / 'artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.exe'
    with (evidence / 'run.log').open('xb') as log:
        result = subprocess.run([str(test), '--expanded-audio-worker', str(evidence / 'results'),
                                 str(payload / 'ContextSuite.Worker.exe'), str(manifest)], cwd=ROOT, stdout=log, stderr=log, timeout=600)
    print((evidence / 'run.log').read_text(errors='replace')[-4000:], flush=True)
    if any(digest(payload / name) != expected_hash for name, expected_hash in identities.items()):
        raise ValueError('Worker payload changed during verification')
    if result.returncode:
        raise ValueError('Expanded audio worker acceptance failed; see retained run.log and results.json')


if __name__ == '__main__':
    main()
