"""Verify recipient library replacement using isolated, disposable native hosts."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import uuid


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('heif', 'heif-replacements', 'audio', 'audio-replacements', 'fixture'):
        parser.add_argument('--' + name, required=True, type=Path)
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    inputs = {}
    for path in vars(args).values():
        path.resolve(strict=True).relative_to(repo / '.codex-temp')
        for item in path.rglob('*') if path.is_dir() else [path]:
            if item.is_file():
                inputs[str(item)] = digest(item)
    for folder in ('proprietary/src/ContextSuite.Private', 'proprietary/tests/ContextSuite.Image.ContractTests',
                   'proprietary/tests/ContextSuite.Audio.ContractTests'):
        for item in (repo / folder).rglob('*'):
            if item.is_file() and item.suffix in ('.cs', '.csproj', '.icc'):
                inputs[str(item)] = digest(item)
    for item in (Path(__file__).resolve(), repo / 'tools/New-LocalLibraryManifest.ps1'):
        inputs[str(item)] = digest(item)
    root = repo / '.codex-temp/local-library-review' / uuid.uuid4().hex
    root.mkdir(parents=True)
    (repo / '.codex-temp/local-library-review-current.txt').write_text(str(root), encoding='utf-8')
    (root / 'inputs.json').write_text(json.dumps(inputs, indent=2), encoding='utf-8')
    print('Local library review: ' + str(root), flush=True)

    def run(name, command, success=True):
        command = list(map(str, command))
        result = subprocess.run(command, cwd=repo, capture_output=True, timeout=180)
        (root / (name + '.stdout.log')).write_bytes(result.stdout)
        (root / (name + '.stderr.log')).write_bytes(result.stderr)
        (root / (name + '.command.json')).write_text(json.dumps({'command': command, 'exit': result.returncode}, indent=2))
        if (result.returncode == 0) != success:
            raise RuntimeError('Unexpected result for ' + name + '; see ' + str(root))
        return result

    for kind in ('Image', 'Audio'):
        run('build-' + kind, ['dotnet', 'build', repo / f'proprietary/tests/ContextSuite.{kind}.ContractTests/ContextSuite.{kind}.ContractTests.csproj',
                             '-c', 'Release', '--nologo'])
    image_host = repo / 'artifacts/managed/bin/ContextSuite.Image.ContractTests/Release/net10.0'
    audio_host = repo / 'artifacts/managed/bin/ContextSuite.Audio.ContractTests/Release/net10.0'
    run('manifest', [image_host / 'ContextSuite.Image.ContractTests.exe', root / 'manifest-results', '--local-libraries'])
    results = []
    for kind, baseline, replacements, host_source, anchor in (
        ('Heif', args.heif, args.heif_replacements, image_host, 'ContextSuite.Heif.Native.dll'),
        ('Audio', args.audio, args.audio_replacements, audio_host, 'ffprobe.exe')):
        native_names = [p.name for p in baseline.iterdir() if p.suffix.lower() in ('.dll', '.exe')]
        replacement_files = list(replacements.glob('*.dll'))
        if not replacement_files or any(p.name == anchor or p.name not in native_names for p in replacement_files):
            raise ValueError('Choose replacement DLLs with existing baseline names, excluding the anchor')
        if not any(digest(path) != digest(baseline / path.name) for path in replacement_files):
            raise ValueError('Replacement test needs different library bytes')
        for scenario in ('default', 'rebuilt', 'undeclared', 'bad-hash', 'bad-anchor', 'malformed', 'missing-entry', 'extra-undeclared', 'extra-declared', 'changed-executable', 'linked-manifest'):
            name = kind.lower() + '-' + scenario
            work = root / name
            host = work / 'host'
            if kind == 'Heif':
                shutil.copytree(host_source, host)
                engine = host / 'heif'
            else:
                engine = work / 'audio'
            engine.mkdir(parents=True)
            for filename in native_names:
                shutil.copy2(baseline / filename, engine / filename)
            if scenario != 'default':
                for path in replacement_files:
                    shutil.copy2(path, engine / path.name)
            manifest = engine / 'ContextSuite.LocalLibraries.json'
            if scenario not in ('default', 'undeclared'):
                if scenario == 'extra-declared':
                    shutil.copy2(replacement_files[0], engine / 'recipient-support.dll')
                run(name + '-manifest', ['powershell', '-NoProfile', '-NonInteractive', '-File', repo / 'tools/New-LocalLibraryManifest.ps1',
                                         '-Engine', kind, '-EngineDirectory', engine])
                saved = manifest.read_bytes()
                run(name + '-no-overwrite', ['powershell', '-NoProfile', '-NonInteractive', '-File', repo / 'tools/New-LocalLibraryManifest.ps1',
                                             '-Engine', kind, '-EngineDirectory', engine], success=False)
                if manifest.read_bytes() != saved:
                    raise ValueError('Generator overwrote existing manifest')
                record = json.loads(saved)
                first = replacement_files[0].name
                if scenario == 'bad-hash': record['libraries'][first] = '0' * 64
                if scenario == 'bad-anchor': record['anchorSha256'] = '0' * 64
                if scenario == 'missing-entry': del record['libraries'][first]
                if scenario == 'extra-undeclared': shutil.copy2(replacement_files[0], engine / 'unexpected.dll')
                if scenario == 'changed-executable':
                    # Even a matching local record cannot replace the application anchor.
                    path = engine / anchor
                    data = bytearray(path.read_bytes()); data[-1] ^= 1; path.write_bytes(data)
                    record['anchorSha256'] = digest(path)
                manifest.write_text('invalid' if scenario == 'malformed' else json.dumps(record), encoding='utf-8')
                if scenario == 'linked-manifest':
                    os.link(manifest, work / 'manifest-hardlink.json')
            payload = {str(p): digest(p) for p in engine.iterdir() if p.is_file()}
            mode = 'valid' if scenario == 'default' else 'local' if scenario in ('rebuilt', 'extra-declared') else scenario
            if kind == 'Heif':
                command = [host / 'ContextSuite.Image.ContractTests.exe', work / 'results', '--heif-loader', args.fixture, mode]
            else:
                command = [audio_host / 'ContextSuite.Audio.ContractTests.exe', '--local-libraries', engine, work / 'results', mode]
            run(name, command)
            if any(digest(Path(p)) != value for p, value in payload.items()):
                raise ValueError('Test changed engine bytes')
            result = json.loads((work / 'results/results.json').read_text())
            result['Kind'] = kind
            result['Case'] = scenario
            result['Payload'] = payload
            results.append(result)
            print(name + ' passed', flush=True)
        field = 'PixelSha256' if kind == 'Heif' else 'OutputSha256'
        hashes = {r[field] for r in results if r['Kind'] == kind and r['Case'] in ('default', 'rebuilt', 'extra-declared')}
        if len(hashes) != 1 or None in hashes:
            raise ValueError('Rebuilt library output differs from baseline')
    if any(digest(Path(path)) != value for path, value in inputs.items()):
        raise ValueError('Original source or runtime inputs changed')
    (root / 'results.json').write_text(json.dumps({'Passed': True, 'Cases': results, 'InputsUnchanged': True}, indent=2), encoding='utf-8')
    print(f'Passed {len(results)} native library scenarios and manifest contracts; baseline output identities preserved.', flush=True)


if __name__ == '__main__':
    main()
