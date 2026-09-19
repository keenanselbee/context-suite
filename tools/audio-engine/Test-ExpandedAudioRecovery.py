"""Exercise real worker interruption for five expanded input families.

Uses an existing verified disposable worker, the owned Windows fixture writer,
and generated media. Does not install, register, recycle or contact a service.
"""
import hashlib
import json
import math
import pathlib
import struct
import subprocess
import sys
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()


def local(value):
    path = pathlib.Path(value).resolve(strict=True)
    if not path.is_relative_to(ROOT / '.codex-temp'):
        raise ValueError('Use repository-owned evidence')
    return path


def main():
    if len(sys.argv) != 3:
        raise ValueError('Test-ExpandedAudioRecovery.py <successful expanded worker evidence> <speech duration corpus>')
    verified, duration = map(local, sys.argv[1:])
    previous = json.loads((verified / 'results/results.json').read_text())
    if previous['failures'] or len(previous['results']) != 796:
        raise ValueError('Use the successful expanded worker publication payload')
    payload = verified / 'worker'
    identities = json.loads((verified / 'payload-identities.json').read_text())
    for name, expected in identities.items():
        path = payload / name
        if not path.resolve().is_relative_to(payload) or digest(path) != expected:
            raise ValueError('Worker identity changed')
    writer = duration / 'WmaFixtureWriter.exe'
    if digest(writer) != '44D83BEDCCED1F2989737CC72A65D046F6958CFC248FB9581095792F8C14A900':
        raise ValueError('Use the owned duration writer with explicit PCM input')
    evidence = ROOT / '.codex-temp' / ('expanded-audio-recovery-' + uuid.uuid4().hex)
    evidence.mkdir()
    print('Expanded recovery evidence: ' + str(evidence), flush=True)
    cycle = [round(.2 * 32767 * math.sin(2 * math.pi * frequency * frame / 48000))
             for frame in range(48000) for frequency in (233, 360)]
    little = b''.join(struct.pack('<h', value) for value in cycle)
    fixtures = []
    for codec in ('standard', 'pro', 'lossless'):
        directory = evidence / codec; directory.mkdir()
        bits = 24 if codec == 'lossless' else 16
        samples = b''.join((value << 8).to_bytes(3, 'little', signed=True) for value in cycle) if bits == 24 else little
        (directory / 'input.pcm').write_bytes(samples * 120)
        result = subprocess.run([str(writer), codec, '48000', '2', str(bits), str(48000 * 120)],
                                cwd=directory, capture_output=True, timeout=120)
        (directory / 'generation.log').write_bytes(result.stdout + result.stderr)
        if result.returncode:
            raise ValueError('Windows fixture generation failed: ' + codec)
        path = directory / 'fixture.wma'
        fixtures.append(dict(Name=codec, Path=str(path), Sha256=digest(path),
                             Codec={'standard': 'wmav2', 'pro': 'wmapro', 'lossless': 'wmalossless'}[codec]))
    data = b''.join(struct.pack('>h', value) for value in cycle) * 120
    common = struct.pack('>HIHHQ', 2, 48000 * 120, 16, 16383 + 15, 48000 << 48)
    body = b'AIFFCOMM' + struct.pack('>I', len(common)) + common + b'SSND' + struct.pack('>I', len(data) + 8) + bytes(8) + data
    path = evidence / 'fixture.aif'; path.write_bytes(b'FORM' + struct.pack('>I', len(body)) + body)
    fixtures.append(dict(Name='aiff', Path=str(path), Sha256=digest(path), Codec='pcm_s16be'))
    voice = duration / 'voice-22050-five-minute-tail/fixture.wma'
    rows = json.loads((duration / 'results.json').read_text())
    # Bind the retained five-minute generated-speech input to its original receipt.
    if not any(row['name'] == 'voice-22050-five-minute-tail' and row['passed'] and
               row['fixture.wma']['sha256'] == digest(voice) for row in rows):
        raise ValueError('Voice source identity differs from duration evidence')
    fixtures.append(dict(Name='voice', Path=str(voice), Sha256=digest(voice), Codec='wmavoice'))
    manifest = evidence / 'fixtures.json'; manifest.write_text(json.dumps(fixtures, indent=2) + '\n')
    (evidence / 'identities.json').write_text(json.dumps(dict(payload=identities, writerSha256=digest(writer)), indent=2) + '\n')
    project = ROOT / 'tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj'
    build = subprocess.run(['dotnet', 'build', str(project), '-c', 'Release', '--no-restore', '--nologo'],
                           cwd=ROOT, capture_output=True, timeout=120)
    (evidence / 'build.log').write_bytes(build.stdout + build.stderr)
    if build.returncode:
        raise ValueError('Test-host build failed')
    test = ROOT / 'artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.exe'
    with (evidence / 'run.log').open('xb') as log:
        result = subprocess.run([str(test), '--expanded-audio-recovery', str(evidence / 'results'),
                                 str(payload / 'ContextSuite.Worker.exe'), str(manifest)], cwd=ROOT, stdout=log, stderr=log, timeout=600)
    print((evidence / 'run.log').read_text(errors='replace')[-4000:], flush=True)
    if any(digest(payload / name) != expected for name, expected in identities.items()):
        raise ValueError('Verified payload changed')
    if result.returncode:
        raise ValueError('Expanded recovery failed; retained evidence identifies the scenario')


if __name__ == '__main__':
    main()
