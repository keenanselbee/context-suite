"""Exercise vgmstream's Wwise RIFF loop parsing and WAV smpl export."""

import argparse
import json
import pathlib
import struct
import subprocess


def chunks(data):
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError("Expected RIFF/WAVE")
    cursor = 12
    while cursor + 8 <= len(data):
        size = struct.unpack_from("<I", data, cursor + 4)[0]
        end = cursor + 8 + size
        if end > len(data):
            raise ValueError("Invalid RIFF chunk length")
        yield data[cursor : cursor + 4], data[cursor + 8 : end], cursor
        cursor = end + (size & 1)
    if cursor not in (len(data), len(data) + 1):
        raise ValueError("Trailing RIFF bytes")


def smpl_chunk(start, end):
    fields = [0] * 15
    fields[7] = 1  # One forward loop.
    fields[11] = start
    fields[12] = end - 1  # RIFF smpl loop end is inclusive.
    return b"smpl" + struct.pack("<I15I", 60, *fields)


parser = argparse.ArgumentParser()
parser.add_argument("--decoder", type=pathlib.Path, required=True)
parser.add_argument("--fixtures", type=pathlib.Path, required=True)
parser.add_argument("--output", type=pathlib.Path, required=True)
args = parser.parse_args()
decoder = args.decoder.resolve(strict=True)
fixtures = args.fixtures.resolve(strict=True)
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=False)

for codec in ("pcm", "vorbis"):
    original = fixtures / f"output-{codec}" / "Windows" / "sine-48000-1ch.wem"
    wem = original.read_bytes()
    if struct.unpack_from("<I", wem, 4)[0] != len(wem) - 8:
        raise ValueError("Unexpected WEM RIFF length")
    source_chunks = list(chunks(wem))
    if any(name == b"smpl" for name, _, _ in source_chunks):
        raise ValueError("Fixture already contains smpl")
    loop_start, loop_end = 1000, 9000
    loop = smpl_chunk(loop_start, loop_end)
    looped_wem = output / f"loop-{codec}.wem"
    data_offset = next(offset for name, _, offset in source_chunks if name == b"data")
    looped_wem.write_bytes(wem[:4] + struct.pack("<I", len(wem) + len(loop) - 8)
                           + wem[8:data_offset] + loop + wem[data_offset:])

    info = subprocess.run([str(decoder), "-I", str(looped_wem)], capture_output=True,
                          text=True, timeout=30, check=True)
    facts = json.loads(info.stdout)
    if facts["loopingInfo"] != {"start": loop_start, "end": loop_end}:
        raise ValueError(f"WEM loop not recognized: {facts['loopingInfo']}")
    decoded = output / f"loop-{codec}.wav"
    sample_format = "1" if codec == "pcm" else "4"
    subprocess.run([str(decoder), "-i", "-L", "-W", sample_format, "-o", str(decoded),
                    str(looped_wem)], capture_output=True, text=True, timeout=30,
                   check=True)
    parts = {name: body for name, body, _ in chunks(decoded.read_bytes())}
    if b"smpl" not in parts:
        raise ValueError("Decoded WAV omitted smpl")
    exported = parts[b"smpl"]
    if (struct.unpack_from("<I", exported, 28)[0],
            struct.unpack_from("<I", exported, 44)[0],
            struct.unpack_from("<I", exported, 48)[0]) != (1, loop_start, loop_end - 1):
        raise ValueError("Decoded WAV changed loop points")
    if len(parts[b"data"]) != facts["numberOfSamples"] * facts["channels"] * (2 if codec == "pcm" else 4):
        raise ValueError("Decoded WAV did not contain exactly one playthrough")
    print(f"{codec}: loop [{loop_start}, {loop_end}) kept in WAV smpl; "
          f"{facts['numberOfSamples']} decoded frames")
