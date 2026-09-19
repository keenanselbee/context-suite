"""Exercise existing Convert commands with a disposable repaired audio worker.

No Explorer, installation, real licensing, native recycling or customer files.
"""
import hashlib
import json
import pathlib
import shutil
import subprocess
import sys
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()


def inventory(folder):
    return {str(path.relative_to(folder)): digest(path) for path in folder.rglob('*') if path.is_file()}


def run(source, mode='--expanded-audio-direct', manifest=None):
    if mode not in ('--expanded-audio-direct', '--expanded-audio-metadata-direct'):
        raise ValueError('Unknown expanded command test mode')
    source = pathlib.Path(source).resolve(strict=True)
    if not source.is_relative_to(ROOT / '.codex-temp'):
        raise ValueError('Use repository-owned worker evidence')
    expected = json.loads((source / 'payload-identities.json').read_text())
    if inventory(source / 'worker') != expected:
        raise ValueError('Prior worker payload changed')
    evidence = ROOT / '.codex-temp' / (mode.removeprefix('--') + '-' + uuid.uuid4().hex)
    evidence.mkdir()
    print('Expanded direct evidence: ' + str(evidence), flush=True)
    for project in ('src/ContextSuite.Worker/ContextSuite.Worker.csproj',
                    'tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj'):
        result = subprocess.run(['dotnet', 'build', str(ROOT / project), '-c', 'Release', '--no-restore', '--nologo'],
                                cwd=ROOT, capture_output=True, timeout=120)
        (evidence / (pathlib.Path(project).stem + '-build.log')).write_bytes(result.stdout + result.stderr)
        if result.returncode:
            raise ValueError('Managed build failed; see retained log')
    payload = evidence / 'worker'
    shutil.copytree(source / 'worker', payload)
    managed = ROOT / 'artifacts/managed/bin/ContextSuite.Worker/Release/net10.0-windows'
    for path in managed.iterdir():
        if path.is_file():
            shutil.copy2(path, payload / path.name)
    # Compose the absent-provider case separately; never mutate a running payload.
    shutil.copytree(payload, evidence / 'worker-no-voice', ignore=shutil.ignore_patterns('voice'))
    shutil.copy2(manifest or source / 'fixtures.json', evidence / 'fixtures.json')
    identities = {name: inventory(evidence / name) for name in ('worker', 'worker-no-voice')}
    (evidence / 'payload-identities.json').write_text(json.dumps(identities, indent=2) + '\n')
    test = ROOT / 'artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.exe'
    with (evidence / 'run.log').open('xb') as log:
        result = subprocess.run([str(test), mode, str(evidence / 'results'),
                                 str(payload / 'ContextSuite.Worker.exe'), str(evidence / 'fixtures.json')],
                                cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, timeout=1800)
    if any(inventory(evidence / name) != files for name, files in identities.items()) or inventory(source / 'worker') != expected:
        raise ValueError('Worker identity changed during direct tests')
    if result.returncode:
        raise ValueError('Direct command checks failed; see retained run.log')
    print((evidence / 'run.log').read_text().splitlines()[-1], flush=True)
    return evidence


if __name__ == '__main__':
    if len(sys.argv) != 2:
        raise ValueError('Test-ExpandedAudioDirect.py <successful expanded worker evidence>')
    run(sys.argv[1])
