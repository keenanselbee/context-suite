"""Verify the raw-PCM timestamp diagnostic repair without changing samples."""
import array
import hashlib
import json
import pathlib
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
        raise ValueError('Use repository-owned fixtures and builds')
    return path


def main():
    if len(sys.argv) != 5:
        raise ValueError('Test-RawAudioTimestamps.py <old build> <new build> <95-case system corpus> <long Lossless fixture directory>')
    old, new, corpus, long = map(local, sys.argv[1:])
    for build in (old, new):
        rows = json.loads((build / 'runtime-files.json').read_text())
        if len(rows) != 7 or any(digest(build / 'bin' / row['path']) != row['sha256'] for row in rows):
            raise ValueError('Runtime identity changed')
    record = json.loads((new / 'build-result.json').read_text())
    if record['exitCode'] or record.get('rawAudioTimestampRepair') is not True:
        raise ValueError('Use a successful raw timestamp repair build')
    evidence = ROOT / '.codex-temp' / ('raw-audio-timestamps-' + uuid.uuid4().hex); evidence.mkdir()
    print('Raw audio timestamp evidence: ' + str(evidence), flush=True)
    checks = []; results = []

    def decode(build, source, name, target='f64le'):
        output = evidence / (name + '.' + target)
        args = [str(build / 'bin/ffmpeg.exe'), '-hide_banner', '-v', 'error', '-nostdin', '-xerror', '-err_detect', 'explode',
                '-i', str(source), '-map', '0:a:0', '-vn', '-sn', '-dn', '-threads', '1', '-c:a',
                'pcm_f64le' if target == 'f64le' else 'pcm_s24le', '-f', target, 'pipe:1']
        with output.open('xb') as stream:
            result = subprocess.run(args, cwd=evidence, stdout=stream, stderr=subprocess.PIPE, timeout=30)
        (evidence / (name + '.log')).write_bytes(result.stderr)
        results.append(dict(name=name, exit=result.returncode, outputBytes=output.stat().st_size,
                            outputSha256=digest(output), stderrBytes=len(result.stderr)))
        return result, output

    before, raw_before = decode(old, long / 'fixture.wma', 'long-before')
    after, raw_after = decode(new, long / 'fixture.wma', 'long-after')
    if before.returncode or after.returncode or len(before.stderr) <= 65536 or after.stderr or digest(raw_before) != digest(raw_after):
        raise ValueError('Raw repair must remove the transport-only overflow while preserving every sample byte')
    if any(b'non monotonically increasing dts' not in line for line in before.stderr.splitlines() if line.strip()):
        raise ValueError('Baseline includes another diagnostic; investigate it separately')
    checks.append('long raw PCM: previous diagnostic overflow, repaired zero stderr, byte-identical output')
    native = array.array('d'); native.frombytes(raw_after.read_bytes())
    windows = array.array('f'); windows.frombytes((long / 'windows.f32').read_bytes())
    source = (long / 'source.pcm').read_bytes()
    if len(native) != len(windows) or len(source) != len(native) * 3 or any(a != b for a, b in zip(native, windows)):
        raise ValueError('Long Lossless must match the complete Windows sample sequence')
    if any(sample != int.from_bytes(source[index * 3:index * 3 + 3], 'little', signed=True) / 8388608
           for index, sample in enumerate(native)):
        raise ValueError('Long Lossless changed authored integer samples')
    checks.append('long Lossless: exact authored PCM24 and independent Windows samples and extents')
    del native, windows, source
    for row in json.loads((corpus / 'results.json').read_text()):
        path = corpus / row['name'] / 'fixture.wma'
        if digest(path) != row['wmaSha256']:
            raise ValueError('Corpus source changed')
        result, output = decode(new, path, row['name'])
        previous = corpus / row['name'] / 'candidate.f64'
        if result.returncode or digest(output) != digest(previous):
            raise ValueError('Previously evaluated native samples changed: ' + row['name'])
        checks.append('previous sample bytes and extent retained: ' + row['name'])
    for build, name in ((old, 'old'), (new, 'new')):
        # WAV has timestamp semantics: the raw-format guard must not bypass it.
        result, output = decode(build, long / 'fixture.wma', name + '-wave', 'wav')
        if result.returncode == 0 or b'Non-monotonic DTS' not in result.stderr:
            raise ValueError('Timestamp-bearing WAV refusal unexpectedly changed')
        checks.append('timestamp-bearing direct WAV still refuses: ' + name)
        malformed = evidence / (name + '-invalid.wma'); malformed.write_bytes((long / 'fixture.wma').read_bytes()[:64])
        result, output = decode(build, malformed, name + '-malformed')
        if result.returncode == 0 or not result.stderr or output.stat().st_size:
            raise ValueError('Malformed-input diagnostic was suppressed')
        checks.append('malformed input still fails with diagnostics and no samples: ' + name)
    (evidence / 'results.json').write_text(json.dumps(dict(checks=checks, results=results), indent=2) + '\n')
    print(f'Passed {len(checks)} raw audio timestamp checks.', flush=True)


if __name__ == '__main__':
    main()
