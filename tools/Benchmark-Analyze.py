"""Benchmark only generated local Analyze fixtures; do not accept customer paths."""
import argparse
import ctypes
import hashlib
import json
import math
import pathlib
import statistics
import struct
import subprocess
import time
import zipfile
import zlib


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--scratch', required=True)
    args = parser.parse_args()
    repository = pathlib.Path(__file__).resolve().parent.parent
    scratch = pathlib.Path(args.scratch).resolve(strict=True)
    if scratch.parent != repository / '.codex-temp/analyze-benchmark' or not (scratch / 'machine.json').is_file():
        raise ValueError('Use a fresh benchmark directory prepared by Benchmark-Analyze.ps1')
    fixtures = scratch / 'fixtures'
    fixtures.mkdir()  # Refuse reuse of an earlier run.
    (fixtures / 'empty.txt').write_bytes(b'')
    block = bytes(range(256)) * 4096
    (fixtures / 'small.bin').write_bytes(block[:4096])
    (fixtures / 'locked.bin').write_bytes(block[:4096])
    (fixtures / 'small.json').write_text('{"fixture":[1,2,3],"label":"authored"}')
    with (fixtures / 'large.bin').open('xb') as stream:
        for _ in range(128):
            stream.write(block)
    with (fixtures / 'large.json').open('xb') as stream:
        stream.write(b'{"padding":"')
        for _ in range(8):
            stream.write(b'x' * (1024 * 1024))
        stream.write(b'"}')

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))

    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 16, 16, 8, 2, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress((b'\0' + b'\x10\x80\xf0' * 16) * 16)) + chunk(b'IEND', b'')
    (fixtures / 'small.png').write_bytes(png)
    with zipfile.ZipFile(fixtures / 'large.docx', 'x', compression=zipfile.ZIP_STORED) as package:
        package.writestr('[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
        package.writestr('_rels/.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="r1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
        package.writestr('word/document.xml', '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Authored benchmark</w:t></w:r></w:p></w:body></w:document>')
        with package.open('unrelated.bin', 'w') as stream:
            for _ in range(64):
                stream.write(block)
    expected = {'empty.txt': None, 'small.bin': None, 'small.png': 'png', 'small.json': 'json',
                'large.bin': None, 'large.json': 'json', 'large.docx': 'docx', 'locked.bin': None, 'missing.bin': None}
    originals = {p.name: {'bytes': p.stat().st_size, 'modifiedNs': p.stat().st_mtime_ns, 'sha256': digest(p)}
                 for p in fixtures.iterdir()}
    (scratch / 'fixtures.json').write_text(json.dumps(originals, indent=2))
    host = repository / 'artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.dll'
    cases = [(name, [name], False) for name in expected] + [('mixed-selection', [name for name in expected if name != 'missing.bin'], True)]
    results = []
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
    kernel.CreateFileW.restype = ctypes.c_void_p
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    kernel.CloseHandle.restype = ctypes.c_int
    locked = kernel.CreateFileW(str(fixtures / 'locked.bin'), 0x80000000, 0, None, 3, 0, None)
    if locked == ctypes.c_void_p(-1).value:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        for name, names, batch in cases:
            specification = scratch / (name + '.request.json')
            specification.write_text(json.dumps({'Paths': [str(fixtures / file) for file in names], 'Batch': batch, 'Iterations': 11}))
            runs = []
            for index in range(3):
                started = time.perf_counter()
                process = subprocess.run(['dotnet', str(host), '--benchmark-analyze', str(specification)],
                                         capture_output=True, text=True, timeout=30, cwd=repository)
                elapsed = (time.perf_counter() - started) * 1000
                if process.returncode or len(process.stdout) > 2 * 1024 * 1024:
                    raise RuntimeError(process.stderr[:4096] or process.stdout[:4096] or 'Benchmark child failed')
                result = json.loads(process.stdout)
                assert len(result['Samples']) == 11
                for sample in result['Samples']:
                    assert [item['Name'] for item in sample['Files']] == names
                    for item in sample['Files']:
                        file = item['Name']
                        if file in ('missing.bin', 'locked.bin'):
                            assert item['Error'] is not None and item['InspectedBytes'] is None
                        else:
                            assert item['Error'] is None and item['FormatId'] == expected[file], item
                            assert item['FileBytes'] == originals[file]['bytes']
                            maximum = 5 * 1024 * 1024 if file == 'large.docx' else min(originals[file]['bytes'], 65536)
                            assert 0 <= item['InspectedBytes'] <= maximum
                result['WholeProcessMilliseconds'] = elapsed
                (scratch / (name + f'.run-{index}.json')).write_text(json.dumps(result, indent=2))
                runs.append(result)
            cold = [run['Samples'][0]['Milliseconds'] for run in runs]
            warm = [sample for run in runs for sample in run['Samples'][1:]]
            times = sorted(sample['Milliseconds'] for sample in warm)
            summary = {'Case': name, 'FreshFirstCallMedianMs': statistics.median(cold),
                       'RepeatedMedianMs': statistics.median(times), 'RepeatedP95Ms': times[math.ceil(len(times) * .95) - 1],
                       'RepeatedAllocationMedianBytes': statistics.median(sample['ManagedAllocatedBytes'] for sample in warm),
                       'MaximumReportedInspectedBytes': max((file['InspectedBytes'] or 0 for run in runs for sample in run['Samples'] for file in sample['Files'])),
                       'WholeProcessMedianMs': statistics.median(run['WholeProcessMilliseconds'] for run in runs)}
            results.append(summary)
            print(f"{name}: first {summary['FreshFirstCallMedianMs']:.2f} ms; repeated median {summary['RepeatedMedianMs']:.2f} ms, p95 {summary['RepeatedP95Ms']:.2f} ms", flush=True)
    finally:
        if not kernel.CloseHandle(locked):
            raise ctypes.WinError(ctypes.get_last_error())
    for name, before in originals.items():
        path = fixtures / name
        assert {'bytes': path.stat().st_size, 'modifiedNs': path.stat().st_mtime_ns, 'sha256': digest(path)} == before
    (scratch / 'benchmark.json').write_text(json.dumps({'Results': results, 'OriginalsUnchanged': True,
        'HostSha256': digest(host), 'AnalyzerSourceSha256': digest(repository / 'src/ContextSuite.Application/Infrastructure/FileAnalysisReader.cs'),
        'Scope': 'Three fresh processes per case; first-call and 30 repeated samples. File caches not cleared; optional media workers, Explorer/UI and remote/cloud storage excluded.'}, indent=2))
    print('Verified source hashes/timestamps and bounded reported inspection. Evidence: ' + str(scratch))


if __name__ == '__main__':
    main()
