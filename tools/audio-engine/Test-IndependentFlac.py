"""Decode authored adapter outputs with the separately pinned Xiph FLAC decoder."""
import argparse
import hashlib
import json
import pathlib
import subprocess
import uuid
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
ARCHIVE_HASH = '53f1500f0d6e7c61379d7fee50d4a9f7f504c650009506d9ba015530d76c0dde'
FILES = {'flac.exe': 'ff23d9cbc11d18c02f262c3ee455830ea13fbe8d9876249f0bdf101e3ad66709',
         'libFLAC.dll': 'f93499172875fc2c0df80b57086f32e3f39e835283952ee2a59a3d4ffb097644'}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def local(value, parent):
    path = pathlib.Path(value).absolute()
    if not path.resolve().is_relative_to(parent.resolve()):
        raise ValueError('Use retained repository scratch and isolated staging')
    for ancestor in (path, *path.parents):
        if ancestor == ROOT:
            break
        if ancestor.is_symlink() or ancestor.is_junction():
            raise ValueError('Linked test input')
    return path


def run(args, directory, expected=0):
    result = subprocess.run([str(arg) for arg in args], cwd=directory, capture_output=True, timeout=30)
    if len(result.stdout) > 2 * 1024 * 1024 or len(result.stderr) > 65536:
        raise ValueError('Authored test output exceeds expected bound')
    if (result.returncode == 0) != (expected == 0):
        raise ValueError('Unexpected test exit: ' + result.stderr.decode(errors='replace'))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', required=True)
    parser.add_argument('--production-stage', required=True)
    args = parser.parse_args()
    archive = local(args.archive, ROOT / '.codex-temp')
    stage = local(args.production_stage, ROOT / 'artifacts/production-staging')
    if archive.stat().st_size != 1318000 or digest(archive) != ARCHIVE_HASH:
        raise ValueError('Xiph 1.5.0 Windows archive differs from publisher checksum')
    run(['python', '-B', ROOT / 'tools/audio-engine/Stage-AudioPayload.py', '--payload', stage, '--inventory'], ROOT)
    root = ROOT / '.codex-temp' / ('flac-independent-run-' + uuid.uuid4().hex)
    root.mkdir(); binary = root / 'bin'; binary.mkdir()
    with zipfile.ZipFile(archive) as package:
        for name, expected in FILES.items():
            data = package.read('flac-1.5.0-win/Win64/' + name)
            if hashlib.sha256(data).hexdigest() != expected:
                raise ValueError('Independent decoder identity changed')
            (binary / name).write_bytes(data)
        for name in ['COPYING.GPL', 'COPYING.Xiph']:
            (root / name).write_bytes(package.read('flac-1.5.0-win/' + name))
    decoder = binary / 'flac.exe'
    version = run([decoder, '--version'], root).stdout.decode().strip()
    if version != 'flac 1.5.0':
        raise ValueError('Unexpected decoder version')
    print('Evidence: ' + str(root), flush=True)
    # This invokes the real private adapter; no direct FFmpeg recipe substitutes
    # for production conversion/optimization policy in the generated fixtures.
    built = run(['dotnet', 'build', ROOT / 'proprietary/tests/ContextSuite.Audio.ContractTests', '-c', 'Release', '--nologo'], ROOT)
    (root / 'build.log').write_bytes(built.stdout + built.stderr)
    # Run the adapter host directly, so a subprocess timeout terminates the owner
    # of the production native jobs rather than only a dotnet-run intermediary.
    generated = run(['dotnet', ROOT / 'artifacts/managed/bin/ContextSuite.Audio.ContractTests/Release/net10.0/ContextSuite.Audio.ContractTests.dll',
                     '--flac-independent-fixtures', stage / 'audio-engine', root / 'fixtures'], ROOT)
    (root / 'adapter.log').write_bytes(generated.stdout + generated.stderr)
    fixtures = json.loads((root / 'fixtures/fixtures.json').read_text())
    if {(f['bits'], f['channels']) for f in fixtures} != {(b, c) for b in (16, 24, 32) for c in (1, 2, 6)} or len(fixtures) != 9:
        raise ValueError('Incomplete precision/layout matrix')
    observed = []
    for item in fixtures:
        name = f"pcm{item['bits']}-{item['channels']}ch"
        if item['name'] != name or item['frames'] != 4097:
            raise ValueError('Unexpected fixture identity')
        raw = root / 'fixtures' / (name + '.raw')
        source = root / 'fixtures' / (name + '.wav')
        if digest(raw).upper() != item['RawSha256'] or digest(source).upper() != item['SourceSha256']:
            raise ValueError('Authored source changed')
        expected = raw.read_bytes()
        if len(expected) != item['frames'] * item['channels'] * item['bits'] // 8:
            raise ValueError('Unexpected authored raw frame count')
        for suffix, field in [('.flac', 'FlacSha256'), ('.optimized.flac', 'OptimizedSha256')]:
            path = root / 'fixtures' / (name + suffix)
            before = digest(path)
            if before.upper() != item[field]:
                raise ValueError('Adapter result changed')
            test = run([decoder, '--test', '--silent', path], root)
            decoded = run([decoder, '--decode', '--stdout', '--silent', '--force-raw-format', '--endian=little', '--sign=signed', path], root)
            if decoded.stdout != expected or digest(path) != before or digest(source).upper() != item['SourceSha256']:
                raise ValueError('Independent sample mismatch or changed original: ' + path.name)
            observed.append({'file': path.name, 'sha256': before, 'rawSha256': item['RawSha256'], 'exactSamples': True,
                             'testDiagnostics': test.stderr.decode(errors='replace'), 'decodeDiagnostics': decoded.stderr.decode(errors='replace')})
            print('PASS: Xiph exact decode ' + path.name, flush=True)
    original = (root / 'fixtures/pcm32-2ch.flac').read_bytes()
    for name, data in [('truncated', original[:-8]), ('corrupt', original[:-1] + bytes([original[-1] ^ 1]))]:
        path = root / (name + '.flac'); path.write_bytes(data)
        failed = run([decoder, '--test', '--silent', path], root, expected=1)
        (root / (name + '.diagnostics.txt')).write_bytes(failed.stderr)
    if any(digest(binary / name) != expected for name, expected in FILES.items()):
        raise ValueError('Independent decoder changed during test')
    (root / 'independent-flac.json').write_text(json.dumps({'version': version, 'archiveSha256': ARCHIVE_HASH,
        'productionStage': str(stage), 'results': observed, 'negativeCases': ['truncated', 'corrupt'],
        'scope': 'Generated adapter conversion and optimization outputs; exact Xiph decoding, not player/listening or release acceptance.'}, indent=2))


if __name__ == '__main__':
    main()
