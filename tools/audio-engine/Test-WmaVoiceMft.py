"""Evaluate an explicit Windows Voice decoder; never enables application support."""
import importlib.util
import json
import os
import pathlib
import re
import shutil
import sys
import uuid

TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('wma_system', TOOLS / 'Test-WmaSystemCodecs.py')
system = importlib.util.module_from_spec(spec)
spec.loader.exec_module(system)


def main():
    if len(sys.argv) != 3 or os.name != 'nt' or sys.version_info < (3, 14):
        raise ValueError('Use Windows/Python 3.14: Test-WmaVoiceMft.py <system corpus> <Voice refusal corpus>')
    corpus, refusals = (system.smoke.build_tool.local_path(value) for value in sys.argv[1:])
    identity = json.loads((corpus / 'identity.json').read_text())
    if identity['toolSourceSha256'] != system.digest(TOOLS / 'WmaFixtureWriter.cpp'):
        raise ValueError('Use the current owned fixture generator')
    rows = json.loads((corpus / 'results.json').read_text())
    voices = [row for row in rows if row['codec'] == 'voice']
    expected = {f'voice-{rate}-1-16-{frames}' for rate in (8000, 11025, 16000, 22050)
                for frames in (1, 2049, rate, *system.VOICE_BOUNDARY_FRAMES)}
    negatives = [row for row in json.loads((refusals / 'results.json').read_text())
                 if not row['name'].endswith('-positive.wma')]
    expected_negative = {f'voice-{rate}-1-16-1-{kind}.wma' for rate in (8000, 11025, 16000, 22050)
                         for kind in ('unsupported-music', 'invalid-sample-count')}
    if (len(voices) != 36 or {row['name'] for row in voices} != expected
            or len(negatives) != 8 or {row['name'] for row in negatives} != expected_negative
            or any(row['status'] != 'observed' for row in voices)):
        raise ValueError('Use the complete finite Voice and refusal corpora')
    evidence = system.ROOT / '.codex-temp' / ('wma-voice-mft-' + uuid.uuid4().hex)
    evidence.mkdir()
    print('Explicit Voice MFT evidence: ' + str(evidence), flush=True)
    source = TOOLS / 'WmaVoiceMftProbe.cpp'
    shutil.copy2(source, evidence / source.name)
    vswhere = pathlib.Path(os.environ['ProgramFiles(x86)']) / 'Microsoft Visual Studio/Installer/vswhere.exe'
    found = system.native([vswhere, '-latest', '-products', '*', '-requires',
        'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '-property', 'installationPath'], evidence)
    if found.returncode:
        raise ValueError('Cannot locate the existing compiler')
    visual_studio = pathlib.Path(found.stdout.decode().strip())
    vcvars = visual_studio / 'VC/Auxiliary/Build/vcvars64.bat'
    if not vcvars.is_file() or any(c in str(vcvars) + str(evidence) for c in '%!&|^"'):
        raise ValueError('Unsupported native build path')
    command = evidence / 'build.cmd'
    command.write_text('@echo off\ncall "' + str(vcvars) + '" 10.0.26100.0\n'
        'if errorlevel 1 exit /b 1\n'
        'cl.exe /nologo /W4 /WX /O2 /MT /EHsc /std:c++20 /GS /guard:cf /guard:ehcont WmaVoiceMftProbe.cpp '
        '/link mfplat.lib mfreadwrite.lib mfuuid.lib mf.lib ole32.lib '
        '/guard:cf /guard:ehcont /CETCOMPAT /DYNAMICBASE /HIGHENTROPYVA /NXCOMPAT\n'
        'exit /b %errorlevel%\n')
    built = system.native(['cmd.exe', '/d', '/c', command], evidence, 60)
    (evidence / 'build.log').write_bytes(built.stdout + built.stderr)
    if built.returncode:
        raise ValueError('Native evaluation tool build failed; see retained log')
    executable = evidence / 'WmaVoiceMftProbe.exe'
    executable_hash = system.digest(executable)
    version = (visual_studio / 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt').read_text().strip()
    dumpbin = visual_studio / 'VC/Tools/MSVC' / version / 'bin/Hostx64/x64/dumpbin.exe'
    pe = system.native([dumpbin, '/headers', '/loadconfig', '/dependents', executable], evidence)
    (evidence / 'pe.txt').write_bytes(pe.stdout + pe.stderr)
    for required in ('8664 machine (x64)', 'High Entropy Virtual Addresses', 'Dynamic base',
                     'NX compatible', 'Control Flow Guard', 'CET compatible', 'CF instrumented', 'FID table present'):
        if pe.returncode or required not in pe.stdout.decode():
            raise ValueError('Missing native hardening: ' + required)
    results, module_hashes = [], {}
    windows = pathlib.Path(os.environ['SystemRoot']).resolve(strict=True)
    for row, negative in [(row, False) for row in voices] + [(row, True) for row in negatives]:
        path = refusals / row['name'] if negative else corpus / row['name'] / 'fixture.wma'
        before = row['sha256'] if negative else row['wmaSha256']
        if path.stat().st_size > 1024 * 1024 or system.digest(path) != before:
            raise ValueError('Owned fixture changed or exceeds its bound')
        directory = evidence / (('negative-' if negative else '') + row['name'])
        directory.mkdir()
        child = system.native([executable, path], directory)
        (directory / 'run.log').write_bytes(child.stdout + child.stderr)
        output = directory / 'decoded.f32'
        size = output.stat().st_size if output.exists() else 0
        if size > 1024 * 1024:
            raise ValueError('Native sample output exceeds evaluation bound')
        result = {'name': row['name'], 'negative': negative, 'exitCode': child.returncode,
                  'outputBytes': size, 'sourceSha256': before}
        if negative:
            result['passed'] = child.returncode != 0 and size == 0
        else:
            reference = corpus / row['name'] / 'windows.f32'
            result['exactWindows'] = output.exists() and output.read_bytes() == reference.read_bytes()
            result['passed'] = child.returncode == 0 and result['exactWindows'] and size == row['sourceFrames'] * 4
            result['referenceSha256'] = system.digest(reference)
            modules = [pathlib.Path(value.strip()).resolve(strict=True) for value in
                       re.findall(r'^Module: (.+)\r?$', child.stdout.decode(), re.MULTILINE)]
            if (not modules or any(p != executable and not p.is_relative_to(windows) for p in modules)
                    or not any(p.name.lower() == 'wmspdmod.dll' for p in modules)):
                raise ValueError('Missing Voice codec or unexpected loaded module')
            result['modules'] = []
            for module in modules:
                digest = system.digest(module)
                if str(module) in module_hashes and module_hashes[str(module)] != digest:
                    raise ValueError('System codec module changed during evaluation')
                module_hashes[str(module)] = digest
                result['modules'].append({'path': str(module), 'sha256': digest})
        if system.digest(path) != before or system.digest(executable) != executable_hash:
            raise ValueError('Source or helper changed during evaluation')
        results.append(result)
        (evidence / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
        print(row['name'] + ': ' + str(result['passed']), flush=True)
    # Check output ownership and early refusal separately from codec fidelity.
    large = evidence / 'oversized.wma'
    with large.open('xb') as stream:
        stream.truncate(512 * 1024 * 1024 + 1)
    standard = next(row for row in rows if row['name'] == 'standard-44100-1-16-1')
    standard_path = corpus / standard['name'] / 'fixture.wma'
    if system.digest(standard_path) != standard['wmaSha256']:
        raise ValueError('Wrong-format control source changed')
    first = voices[0]
    collision = evidence / first['name']
    collision_hash = system.digest(collision / 'decoded.f32')
    guards = []
    for name, arguments, expected in (
        ('collision', [executable, corpus / first['name'] / 'fixture.wma'], 'Cannot create output'),
        ('wrong-format', [executable, standard_path], 'Input is outside the Voice evaluation profiles'),
        ('oversized', [executable, large], 'Source exceeds evaluation limit'),
        ('arguments', [executable], None)):
        directory = collision if name == 'collision' else evidence / ('guard-' + name)
        if name != 'collision':
            directory.mkdir()
        child = system.native(arguments, directory)
        (evidence / ('guard-' + name + '.log')).write_bytes(child.stdout + child.stderr)
        passed = child.returncode != 0 and (expected is None or expected in child.stderr.decode())
        passed &= (system.digest(collision / 'decoded.f32') == collision_hash if name == 'collision'
                   else not (directory / 'decoded.f32').exists())
        guards.append({'name': name, 'exitCode': child.returncode, 'passed': bool(passed)})
    if (system.digest(standard_path) != standard['wmaSha256']
            or system.digest(executable) != executable_hash):
        raise ValueError('Source or tool changed during guard checks')
    (evidence / 'guards.json').write_text(json.dumps(guards, indent=2) + '\n')
    (evidence / 'identity.json').write_text(json.dumps({'sourceSha256': system.digest(source),
        'executableSha256': executable_hash, 'scriptSha256': system.digest(pathlib.Path(__file__)),
        'windows': str(sys.getwindowsversion()), 'modules': module_hashes,
        'scope': 'Explicit Windows Voice decoder versus source-reader output using the same system codec. Not independent codec or production acceptance.'}, indent=2) + '\n')
    if not all(row['passed'] for row in results + guards):
        raise ValueError('Explicit Voice MFT comparison/refusal failed')
    print('36 exact Voice comparisons, eight malformed-input refusals and four ownership/input guards passed.')


if __name__ == '__main__':
    main()
