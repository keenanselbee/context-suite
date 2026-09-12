"""Compare authored Opus adapter outputs using native Opus and Xiph libopus decoding."""
import argparse
import array
import hashlib
import importlib.util
import json
import math
import os
import pathlib
import subprocess
import uuid
import wave

TOOLS = pathlib.Path(__file__).resolve().parent
ROOT = TOOLS.parent.parent
spec = importlib.util.spec_from_file_location('audio_candidate', TOOLS / 'Test-AudioCandidate.py')
candidate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(candidate)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def decode(engine, path, decoder, directory):
    environment = dict(os.environ)
    environment.pop('FFREPORT', None)
    command = [engine / 'ffmpeg.exe', '-hide_banner', '-nostdin', '-loglevel', 'error',
               '-protocol_whitelist', 'file,pipe', '-f', 'ogg', '-c:a', decoder,
               '-request_sample_fmt', 'flt' if decoder == 'libopus' else 'fltp',
               '-i', path, '-map', '0:a:0', '-threads', '1', '-c:a', 'pcm_f64le',
               '-f', 'f64le', 'pipe:1']
    result = subprocess.run([str(arg) for arg in command], cwd=directory, env=environment,
                            capture_output=True, timeout=20)
    if len(result.stdout) > 8 * 48000 * 8 or len(result.stderr) > 65536:
        raise ValueError('Authored decoder output exceeded its expected size')
    return result


def channel_errors(values, originals, channels):
    if len(values) != len(originals) or not all(math.isfinite(x) for x in values):
        raise ValueError('Incomplete or nonfinite decoded audio')
    frames = len(values) // channels
    errors = []
    for output_channel in range(channels):
        errors.append([math.sqrt(sum((values[frame * channels + output_channel] -
                                     originals[frame * channels + source_channel]) ** 2
                                    for frame in range(frames)) / frames)
                       for source_channel in range(channels)])
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--matrix', required=True)
    parser.add_argument('--decoder', required=True)
    args = parser.parse_args()
    matrix = candidate.build_tool.local_path(args.matrix)
    engine = candidate.build_tool.local_path(args.decoder)
    identity = candidate.verify_evaluation(engine)
    records = json.loads((matrix / 'matrix.json').read_text())
    selected = [item for item in records if item['target'] == 'Opus' and item['outcome'] == 'passed']
    rates = (8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 64000, 88200, 96000, 176400, 192000)
    expected = {(rate, layout) for rate in rates for layout in ('mono', 'stereo')}
    expected |= {(48000, layout) for layout in ('quad', '5.0', '5.1', '6.1', '7.1')}
    if len(selected) != 33 or {(x['Rate'], x['Layout']) for x in selected} != expected:
        raise ValueError('Incomplete authored Opus matrix')
    evidence = ROOT / '.codex-temp' / ('opus-decoder-' + uuid.uuid4().hex)
    evidence.mkdir()
    print('Decoder evidence: ' + str(evidence), flush=True)
    listing = candidate.run([engine / 'ffmpeg.exe', '-hide_banner', '-decoders'], evidence)
    if not all(any(line.split()[1:2] == [name] for line in listing.splitlines()) for name in ('opus', 'libopus')):
        raise ValueError('Both explicit decoder implementations are required')
    (evidence / 'decoders.log').write_text(listing)
    results = []
    differences = []
    channel_control = None
    for item in selected:
        name = str(item['Rate']) + '-' + item['Layout'].replace('(', '-').replace(')', '')
        if name != item['source']:
            raise ValueError('Unexpected authored source name')
        source = candidate.build_tool.local_path(matrix / (name + '.wav'))
        encoded = candidate.build_tool.local_path(matrix / (name + '-to-Opus.opus'))
        before = (digest(source), digest(encoded), source.stat().st_mtime_ns, encoded.stat().st_mtime_ns)
        if (before[0].upper() != item['sourceSha256'] or
                before[1].upper() != item['details']['outputSha256'] or encoded.stat().st_size > 2 * 1024 * 1024):
            raise ValueError('Authored source/output identity changed')
        decoded = {}
        diagnostics = {}
        for decoder in ('opus', 'libopus'):
            result = decode(engine, encoded, decoder, evidence)
            if result.returncode or len(result.stdout) != 48000 * item['Channels'] * 8:
                raise ValueError('Decoder failed or changed complete frame count: ' + name + ' ' + decoder)
            decoded[decoder] = array.array('d', result.stdout)
            if not all(math.isfinite(x) for x in decoded[decoder]):
                raise ValueError('Nonfinite decoder samples')
            diagnostics[decoder] = result.stderr.decode(errors='replace')
        difference = max(abs(a - b) for a, b in zip(decoded['opus'], decoded['libopus'], strict=True))
        if difference > .0001:
            differences.append((name, difference))
        errors = None
        native_errors = None
        if item['Rate'] == 48000:
            with wave.open(str(source), 'rb') as original:
                if original.getparams()[:4] != (item['Channels'], 2, 48000, 48000):
                    raise ValueError('Unexpected authored PCM format')
                pcm = array.array('h', original.readframes(48001))
            originals = [value / 32768 for value in pcm]
            errors = channel_errors(decoded['libopus'], originals, item['Channels'])
            native_errors = channel_errors(decoded['opus'], originals, item['Channels'])
            for implementation in (errors, native_errors):
                for channel, row in enumerate(implementation):
                    if min(range(len(row)), key=row.__getitem__) != channel or row[channel] > .1:
                        raise ValueError('Decoded speaker signal does not match its source: ' + name)
            if item['Layout'] == '5.0':
                swapped = originals.copy()
                for frame in range(48000):
                    swapped[frame * 5], swapped[frame * 5 + 1] = swapped[frame * 5 + 1], swapped[frame * 5]
                control = channel_errors(decoded['libopus'], swapped, 5)
                if min(range(5), key=control[0].__getitem__) == 0:
                    raise ValueError('Speaker-order negative control was not detected')
                channel_control = {'detected': True, 'swappedReferenceChannels': [0, 1], 'channelRmse': control}
        if before != (digest(source), digest(encoded), source.stat().st_mtime_ns, encoded.stat().st_mtime_ns):
            raise ValueError('Decoder changed an original or adapter output')
        results.append({'source': name, 'channels': item['Channels'], 'frames': 48000,
                        'sourceSha256': before[0], 'opusSha256': before[1], 'maximumDecoderDifference': difference,
                        'channelRmse': errors, 'nativeChannelRmse': native_errors, 'diagnostics': diagnostics})
        (evidence / 'observations.json').write_text(json.dumps(results, indent=2) + '\n')
        print('Observed complete native/libopus decode: ' + name, flush=True)
    if channel_control is None:
        raise ValueError('Missing speaker-order control')
    negative = []
    sample = matrix / '48000-5.0-to-Opus.opus'
    data = sample.read_bytes()
    truncated = evidence / 'truncated.opus'
    truncated.write_bytes(data[:len(data) // 2])
    result = decode(engine, truncated, 'libopus', evidence)
    if result.returncode == 0 and len(result.stdout) == 48000 * 5 * 8:
        raise ValueError('Truncated stream retained an unexplained full frame count')
    negative.append({'case': 'truncated', 'detected': True, 'exitCode': result.returncode,
                     'decodedBytes': len(result.stdout), 'diagnostics': result.stderr.decode(errors='replace')})
    if candidate.verify_evaluation(engine) != identity:
        raise ValueError('Independent decoder runtime changed')
    report = {'decoderRuntime': identity, 'matrixSha256': digest(matrix / 'matrix.json'),
              'results': results, 'speakerOrderControl': channel_control, 'negativeControls': negative,
              'decoderDifferenceReviewLimit': .0001, 'differencesRequiringReview': differences,
              'status': 'review-required' if differences else 'passed',
              'limits': 'Different audio decoders; shared FFmpeg container handling. No independent resampler or listening claim.'}
    (evidence / 'opus-decoder.json').write_text(json.dumps(report, indent=2) + '\n')
    if differences:
        raise ValueError('Decoder differences require review: ' + str(differences))
    print('Passed 33 cross-decoder cases, seven source-channel comparisons and two negative controls.', flush=True)


if __name__ == '__main__':
    main()
