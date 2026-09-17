"""Author a finite five-minute Voice corpus from the owned fixture generator.

Uses Windows codecs, never customer media. Retains the exact adapted generator,
identity, logs and samples. This measures extent, not listening quality.
"""
import importlib.util
import json
import os
import pathlib
import sys
import time
import uuid
import wave

TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('wma_system', TOOLS / 'Test-WmaSystemCodecs.py')
system = importlib.util.module_from_spec(spec)
spec.loader.exec_module(system)


def main():
    speech = len(sys.argv) == 3 and sys.argv[1] == '--speech'
    if (len(sys.argv) != 1 and not speech) or os.name != 'nt':
        raise ValueError('Use Windows: Test-WmaVoiceDuration.py [--speech <expanded runtime bin>]')
    original = TOOLS / 'WmaFixtureWriter.cpp'
    if system.digest(original) != 'BE4037550098D70AB95EB569337E9D350B860B4269AC9B65C7AACDDD2D66E309':
        raise ValueError('Review the duration adaptation against the changed fixture writer')
    # Keep the established short-corpus generator and its identity unchanged.
    # Only enlarge its finite test bounds and suppress per-sample diagnostics.
    source = original.read_text()
    replacements = {
        'number > 1000000': 'number > 7000000',
        'frames > rate * 3': 'frames > rate * 300 + 1',
        'index < 4096; ++index)\n        {\n            DWORD flags': 'index < 131072; ++index)\n        {\n            DWORD flags',
        'total + length <= 16 * 1024 * 1024': 'total + length <= 64 * 1024 * 1024',
        'std::printf("Windows sample: time=%lld bytes=%lu\\n", time, static_cast<unsigned long>(length));': '(void)time;',
    }
    for before, after in replacements.items():
        if source.count(before) != 1:
            raise ValueError('Unexpected generator adaptation site: ' + before)
        source = source.replace(before, after)
    if speech:
        start = source.index('    const double scale =')
        end = source.index('    HANDLE raw =', start)
        source = source[:start] + '''    HANDLE fixture = CreateFileW(L"input.pcm", GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (fixture == INVALID_HANDLE_VALUE) throw std::runtime_error("Missing authored speech input");
    LARGE_INTEGER size{}; DWORD read = 0;
    bool complete = GetFileSizeEx(fixture, &size) && size.QuadPart == static_cast<LONGLONG>(pcm.size()) &&
        ReadFile(fixture, pcm.data(), static_cast<DWORD>(pcm.size()), &read, nullptr) && read == pcm.size();
    CloseHandle(fixture);
    if (!complete) throw std::runtime_error("Speech input differs from the finite extent");
''' + source[end:]
    evidence = system.ROOT / '.codex-temp' / ('wma-voice-duration-' + uuid.uuid4().hex)
    evidence.mkdir()
    print('Voice duration corpus: ' + str(evidence), flush=True)
    speech_samples = None
    speech_identity = None
    if speech:
        binary = system.smoke.build_tool.local_path(sys.argv[2])
        inventory = json.loads((binary.parent / 'runtime-files.json').read_text())
        # This is the already-reviewed isolated expanded runtime, not a tool
        # fetched by this test. Bind every selected native file to its inventory.
        expected = {'ffmpeg.exe', 'ffprobe.exe', *(pathlib.PurePosixPath(name).name for name in system.smoke.build_tool.DLLS)}
        if len(inventory) != len(expected) or {item['path'] for item in inventory} != expected or {path.name for path in binary.iterdir()} != expected:
            raise ValueError('Unexpected speech resampler runtime files')
        for item in inventory:
            path = binary / item['path']
            if path.parent != binary or path.stat().st_size != item['bytes'] or system.digest(path) != item['sha256']:
                raise ValueError('Speech resampler runtime identity differs')
        script = evidence / 'speech.ps1'
        script.write_text('''param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $voice = $synth.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -eq 'en-US' } | Select-Object -First 1
    if (-not $voice) { throw 'An installed English speech voice is required.' }
    $synth.SelectVoice($voice.VoiceInfo.Name)
    $synth.SetOutputToWaveFile($OutputPath)
    $synth.Speak('This is an authored local test recording for Context Suite. We are checking spoken words, pauses, timing, and the end of the recording. The original file must remain unchanged. One, two, three, four, five. A quiet room, a clear voice, and a short pause help us compare the complete result. No customer recording or network service is involved.')
    Write-Output $voice.VoiceInfo.Name
} finally { $synth.Dispose() }
''')
        spoken = system.native(['powershell.exe', '-NoProfile', '-NonInteractive', '-File', script,
                                '-OutputPath', evidence / 'speech.wav'], evidence, 120)
        (evidence / 'speech.log').write_bytes(spoken.stdout + spoken.stderr)
        if spoken.returncode:
            raise ValueError('Local speech fixture generation failed')
        speech_samples = {}
        for rate in (8000, 11025, 16000, 22050):
            output = evidence / f'speech-{rate}.wav'
            child = system.native([binary / 'ffmpeg.exe', '-v', 'error', '-nostdin', '-i', evidence / 'speech.wav',
                '-map_metadata', '-1', '-threads', '1', '-ar', rate, '-ac', '1', '-c:a', 'pcm_s16le', '-f', 'wav', output], evidence)
            if child.returncode:
                raise ValueError('Speech fixture resampling failed: ' + child.stderr.decode(errors='replace'))
            with wave.open(str(output), 'rb') as reader:
                if reader.getnchannels() != 1 or reader.getsampwidth() != 2 or reader.getframerate() != rate:
                    raise ValueError('Unexpected speech PCM profile')
                pcm = reader.readframes(reader.getnframes())
            if len(pcm) < rate * 2:
                raise ValueError('Speech fixture unexpectedly short')
            speech_samples[rate] = pcm
        speech_identity = {'waveSha256': system.digest(evidence / 'speech.wav'),
                           'voice': spoken.stdout.decode().strip(), 'runtime': inventory,
                           'policy': 'Repeat the authored local speech PCM to exactly 300 seconds plus one sample.'}
    adapted = evidence / 'WmaFixtureWriter.cpp'
    adapted.write_text(source)
    vswhere = pathlib.Path(os.environ['ProgramFiles(x86)']) / 'Microsoft Visual Studio/Installer/vswhere.exe'
    found = system.native([vswhere, '-latest', '-products', '*', '-requires',
        'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '-property', 'installationPath'], evidence)
    if found.returncode:
        raise ValueError('Cannot locate existing compiler')
    vcvars = pathlib.Path(found.stdout.decode().strip()) / 'VC/Auxiliary/Build/vcvars64.bat'
    if not vcvars.is_file() or any(c in str(vcvars) + str(evidence) for c in '%!&|^"'):
        raise ValueError('Unsupported build path')
    command = evidence / 'build.cmd'
    command.write_text('@echo off\ncall "' + str(vcvars) + '" 10.0.26100.0\n'
        'if errorlevel 1 exit /b 1\n'
        'cl.exe /nologo /W4 /WX /O2 /MT /EHsc /std:c++20 WmaFixtureWriter.cpp '
        '/link mfplat.lib mfreadwrite.lib mfuuid.lib mf.lib ole32.lib /DYNAMICBASE /NXCOMPAT\n'
        'exit /b %errorlevel%\n')
    built = system.native(['cmd.exe', '/d', '/c', command], evidence, 60)
    (evidence / 'build.log').write_bytes(built.stdout + built.stderr)
    if built.returncode:
        raise ValueError('Duration fixture build failed; see build.log')
    writer = evidence / 'WmaFixtureWriter.exe'
    identity = {'originalSourceSha256': system.digest(original), 'adaptedSourceSha256': system.digest(adapted),
                'writerSha256': system.digest(writer), 'scriptSha256': system.digest(pathlib.Path(__file__)),
                'windows': str(sys.getwindowsversion()), 'seconds': 300, 'tailFrames': 1,
                'speech': speech_identity,
                'scope': 'Authored local speech or tones and same-codec Windows reference; not independent codec or listening acceptance.'}
    (evidence / 'identity.json').write_text(json.dumps(identity, indent=2) + '\n')
    rows = []
    for rate in (8000, 11025, 16000, 22050):
        frames = rate * 300 + 1
        name = f'voice-{rate}-five-minute-tail'
        directory = evidence / name
        directory.mkdir()
        if speech_samples is not None:
            pcm = speech_samples[rate]
            (directory / 'input.pcm').write_bytes((pcm * ((frames * 2 + len(pcm) - 1) // len(pcm)))[:frames * 2])
        started = time.monotonic()
        child = system.native([writer, 'voice', rate, 1, 16, frames], directory, 120)
        (directory / 'run.log').write_bytes(child.stdout + child.stderr)
        reference = directory / 'windows.f32'
        passed = child.returncode == 0 and reference.exists() and reference.stat().st_size == frames * 4
        row = {'name': name, 'sampleRate': rate, 'sourceFrames': frames, 'exitCode': child.returncode,
               'elapsedSeconds': time.monotonic() - started, 'passed': passed}
        for filename in ('fixture.wma', 'source.pcm', 'windows.f32'):
            path = directory / filename
            if path.exists():
                row[filename] = {'bytes': path.stat().st_size, 'sha256': system.digest(path)}
        rows.append(row)
        (evidence / 'results.json').write_text(json.dumps(rows, indent=2) + '\n')
        print(name + ': ' + str(passed), flush=True)
        if not passed:
            raise ValueError('Windows duration oracle failed; do not trim or pad it')
    print('Passed four five-minute Voice reference extents. Evidence: ' + str(evidence), flush=True)


if __name__ == '__main__':
    main()
