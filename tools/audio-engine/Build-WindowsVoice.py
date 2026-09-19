"""Rebuild the owned Windows Voice host locally; no installation or codec copying."""
import hashlib
import json
import os
import pathlib
import re
import shutil
import subprocess
import uuid

TOOLS = pathlib.Path(__file__).resolve().parent
ROOT = TOOLS.parents[1]


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()


def run(arguments, directory):
    return subprocess.run([str(value) for value in arguments], cwd=directory,
                          capture_output=True, timeout=120)


def main():
    if os.name != 'nt':
        raise ValueError('Use the existing Windows x64 C++ build environment')
    directory = ROOT / '.codex-temp' / ('windows-voice-build-' + uuid.uuid4().hex)
    directory.mkdir(parents=True)
    print('Windows Voice build: ' + str(directory), flush=True)
    source = TOOLS / 'WmaVoiceMftProbe.cpp'
    source_hash = digest(source)
    shutil.copyfile(source, directory / source.name)
    vswhere = pathlib.Path(os.environ['ProgramFiles(x86)']) / 'Microsoft Visual Studio/Installer/vswhere.exe'
    located = run([vswhere, '-latest', '-products', '*', '-requires',
                   'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '-property', 'installationPath'], directory)
    if located.returncode:
        raise ValueError('Cannot locate the existing C++ compiler')
    visual_studio = pathlib.Path(located.stdout.decode().strip())
    vcvars = visual_studio / 'VC/Auxiliary/Build/vcvars64.bat'
    if not vcvars.is_file() or any(value in str(vcvars) + str(directory) for value in '%!&|^"'):
        raise ValueError('Unsupported native build path')
    command = directory / 'build.cmd'
    command.write_text('@echo off\ncall "' + str(vcvars) + '" 10.0.26100.0\n'
        'if errorlevel 1 exit /b 1\n'
        'cl.exe /nologo /W4 /WX /O2 /MT /EHsc /std:c++20 /GS /guard:cf /guard:ehcont WmaVoiceMftProbe.cpp '
        '/link mfplat.lib mfreadwrite.lib mfuuid.lib mf.lib ole32.lib '
        '/guard:cf /guard:ehcont /CETCOMPAT /DYNAMICBASE /HIGHENTROPYVA /NXCOMPAT\n'
        'exit /b %errorlevel%\n')
    compiled = run(['cmd.exe', '/d', '/c', command], directory)
    (directory / 'build.log').write_bytes(compiled.stdout + compiled.stderr)
    if compiled.returncode:
        raise ValueError('Windows Voice build failed; see retained build.log')
    version = (visual_studio / 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt').read_text().strip()
    dumpbin = visual_studio / 'VC/Tools/MSVC' / version / 'bin/Hostx64/x64/dumpbin.exe'
    executable = directory / 'WmaVoiceMftProbe.exe'
    pe = run([dumpbin, '/headers', '/loadconfig', '/dependents', executable], directory)
    (directory / 'pe.txt').write_bytes(pe.stdout + pe.stderr)
    text = pe.stdout.decode()
    required = ('8664 machine (x64)', 'High Entropy Virtual Addresses', 'Dynamic base',
                'NX compatible', 'Control Flow Guard', 'CET compatible', 'CF instrumented', 'FID table present')
    if pe.returncode or any(value not in text for value in required):
        raise ValueError('Windows Voice native hardening is incomplete')
    for label in ('Security Cookie', 'Guard CF function table', 'Guard CF function count'):
        value = re.search(r'^\s+([0-9A-Fa-f]+) ' + re.escape(label) + r'\s*$', text, re.MULTILINE)
        if not value or not int(value[1], 16):
            raise ValueError('Missing nonzero PE field: ' + label)
    imports = {name.lower() for name in re.findall(r'^    ([^\s]+\.dll)\s*$', text, re.MULTILINE | re.IGNORECASE)}
    if imports != {'mfplat.dll', 'mfreadwrite.dll', 'ole32.dll', 'kernel32.dll'}:
        raise ValueError('Unexpected Windows Voice imports: ' + str(sorted(imports)))
    if digest(source) != source_hash or digest(directory / source.name) != source_hash:
        raise ValueError('Windows Voice source changed during build')
    record = dict(sourceSha256=source_hash, executableSha256=digest(executable), executableBytes=executable.stat().st_size,
                  recipeSha256=digest(pathlib.Path(__file__)), imports=sorted(imports),
                  compilerVersion=version, dumpbinSha256=digest(dumpbin), exitCode=compiled.returncode)
    (directory / 'build-result.json').write_text(json.dumps(record, indent=2) + '\n')
    print('Windows Voice source rebuild and native review passed: ' + str(directory))


if __name__ == '__main__':
    main()
