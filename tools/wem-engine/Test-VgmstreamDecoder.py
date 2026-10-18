"""Check a curated CLI against the synthetic Wwise PCM and Vorbis WEM set."""

import argparse
import array
import json
import math
import pathlib
import struct
import subprocess
import wave


def wave_chunks(path):
    data = path.read_bytes()
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError(f"Invalid WAV output: {path}")
    chunks = {}
    offset = 12
    while offset + 8 <= len(data):
        name = data[offset : offset + 4]
        size = struct.unpack_from("<I", data, offset + 4)[0]
        body = data[offset + 8 : offset + 8 + size]
        if len(body) != size or name in chunks:
            raise ValueError(f"Invalid WAV chunk: {path}")
        chunks[name] = body
        offset += 8 + size + (size & 1)
    return chunks


parser = argparse.ArgumentParser()
parser.add_argument("--decoder", type=pathlib.Path, required=True)
parser.add_argument("--fixtures", type=pathlib.Path, required=True)
parser.add_argument("--output", type=pathlib.Path, required=True)
args = parser.parse_args()
decoder = args.decoder.resolve(strict=True)
fixtures = args.fixtures.resolve(strict=True)
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=False)

for preset in ("pcm", "vorbis"):
    wem_files = sorted((fixtures / f"output-{preset}" / "Windows").glob("*.wem"))
    if len(wem_files) != 4:
        raise ValueError(f"Expected four {preset} WEM fixtures")
    for wem in wem_files:
        with wave.open(str(fixtures / "inputs" / wem.with_suffix(".wav").name)) as source:
            rate, channels, frames = source.getframerate(), source.getnchannels(), source.getnframes()
            reference_bytes = source.readframes(frames)
            reference = array.array("h", reference_bytes)
        source_chunks = wave_chunks(fixtures / "inputs" / wem.with_suffix(".wav").name)
        source_smpl = source_chunks.get(b"smpl")
        if source_smpl is not None:
            expected_loop = (struct.unpack_from("<I", source_smpl, 44)[0],
                             struct.unpack_from("<I", source_smpl, 48)[0] + 1)
        else:
            expected_loop = None
        info = subprocess.run([str(decoder), "-i", "-I", str(wem)], capture_output=True, text=True,
                              timeout=30, check=True)
        facts = json.loads(info.stdout)
        if (facts["sampleRate"], facts["channels"], facts["numberOfSamples"], facts["playSamples"]) != \
                (rate, channels, frames, frames):
            raise ValueError(f"Wrong decoded metadata: {wem}")
        if facts["encoding"] != ("16-bit Little Endian PCM" if preset == "pcm" else "Custom Vorbis"):
            raise ValueError(f"Wrong decoder or codec: {wem}")
        actual_loop = facts["loopingInfo"]
        if (None if actual_loop is None else (actual_loop["start"], actual_loop["end"])) != expected_loop:
            raise ValueError(f"Wrong WEM loop: {wem}: {actual_loop}")
        decoded_path = output / f"{preset}-{wem.stem}.wav"
        sample_format = "1" if preset == "pcm" else "4"
        subprocess.run([str(decoder), "-i", "-L", "-W", sample_format, "-o", str(decoded_path), str(wem)],
                       capture_output=True, text=True, timeout=30, check=True)
        chunks = wave_chunks(decoded_path)
        decoded_smpl = chunks.get(b"smpl")
        if (decoded_smpl is None) != (source_smpl is None):
            raise ValueError(f"Decoded WAV loop presence differs from source: {decoded_path}")
        if decoded_smpl is not None:
            loop = (struct.unpack_from("<I", decoded_smpl, 44)[0],
                    struct.unpack_from("<I", decoded_smpl, 48)[0] + 1)
            if loop != expected_loop:
                raise ValueError(f"Decoded WAV changed loop points: {decoded_path}")
        fmt = struct.unpack_from("<HHIIHH", chunks[b"fmt "])
        if fmt[1] != channels or fmt[2] != rate or (fmt[0], fmt[5]) != \
                ((1, 16) if preset == "pcm" else (3, 32)):
            raise ValueError(f"Wrong decoded WAV format: {decoded_path}")
        if preset == "pcm" and chunks[b"data"] != reference_bytes:
            raise ValueError(f"PCM samples differ from source: {decoded_path}")
        samples = array.array("h" if preset == "pcm" else "f", chunks[b"data"])
        if preset == "pcm":
            samples = [value / 32768 for value in samples]
        if len(samples) != frames * channels or not all(math.isfinite(value) for value in samples):
            raise ValueError(f"Wrong decoded sample count or nonfinite data: {decoded_path}")
        expected = [value / 32768 for value in reference]
        dot = sum(a * b for a, b in zip(samples, expected))
        expected_power = sum(value * value for value in expected)
        decoded_power = sum(value * value for value in samples)
        correlation = dot / math.sqrt(expected_power * decoded_power)
        max_error = max(abs(a - b) for a, b in zip(samples, expected))
        if preset == "vorbis" and correlation < 0.95:
            raise ValueError(f"Vorbis signal mismatch: {decoded_path}")
        print(f"{preset} {wem.name}: {rate} Hz, {channels} ch, {frames} frames, loop={expected_loop}, "
              f"correlation={correlation:.6f}, max_error={max_error:.6f}")
