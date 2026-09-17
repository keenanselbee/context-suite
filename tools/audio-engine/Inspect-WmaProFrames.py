"""Read frame declarations from bounded ffprobe JSON for the WMA Pro corpus.

Diagnostic only: accepts the authored 48 kHz / 0x00e0 profile, does not decode
audio, change files, or establish a production trimming policy.
"""
import argparse
import json
import pathlib
import struct


class Bits:
    def __init__(self, value):
        self.value = value
        self.position = 0

    def take(self, count):
        if count < 0 or self.position + count > len(self.value):
            raise ValueError('Truncated frame header')
        value = self.value[self.position:self.position + count]
        self.position += count
        return int(value, 2) if count else 0


def unpack(value):
    result = bytearray()
    for line in value.strip().splitlines():
        offset, columns = line.split(': ', 1)
        if int(offset, 16) != len(result):
            raise ValueError('Noncontiguous packet dump')
        result.extend(bytes.fromhex(columns.split('  ', 1)[0]))
    return bytes(result)


def header(frame, width, channels):
    reader = Bits(frame)
    size = reader.take(width)
    if size != len(frame):
        raise ValueError('Frame length differs from its declaration')
    fixed = reader.take(1)
    tiles = [[] for _ in range(channels)]
    totals = [0] * channels
    for _ in range(channels * 16):
        offset = min(totals)
        if offset == 2048:
            break
        eligible = [channel for channel in range(channels) if totals[channel] == offset]
        members = [channel for channel in eligible
                   if fixed or len(eligible) == 1 or offset == 1920 or reader.take(1)]
        if offset == 1920:
            length = 128
        else:
            shift = 1 + reader.take(2) if reader.take(1) else 0
            length = 2048 >> shift
        if not members or length < 128:
            raise ValueError('Invalid subframe declaration')
        for channel in members:
            totals[channel] += length
            tiles[channel].append(length)
            if totals[channel] > 2048:
                raise ValueError('Subframe extends beyond its frame')
    if totals != [2048] * channels:
        raise ValueError('Incomplete channel tiling')
    if channels > 1 and reader.take(1) and reader.take(1):
        reader.take(channels * channels * 4)
    drc = reader.take(8)
    start = end = 0
    has_end = False
    if reader.take(1):
        if reader.take(1):
            start = reader.take(12)
        has_end = bool(reader.take(1))
        if has_end:
            end = reader.take(12)
    return {'bits': size, 'trimStart': start, 'trimEnd': end, 'hasTrimEnd': has_end,
            'drc': drc, 'tiles': tiles, 'moreInPacket': int(frame[-1])}


def inspect(document):
    streams, packets = document['streams'], document['packets']
    if len(streams) != 1 or not 1 <= len(packets) <= 256:
        raise ValueError('Expected one bounded audio stream')
    stream = streams[0]
    extra = unpack(stream['extradata'])
    channels = stream['channels']
    if (stream['codec_name'] != 'wmapro' or stream['sample_rate'] != '48000'
            or channels not in (2, 6) or len(extra) != 18
            or struct.unpack_from('<H', extra, 14)[0] != 0xe0):
        raise ValueError('Only the authored 48 kHz / 0x00e0 Pro profile is supported')
    packet_size = int(packets[0]['size'])
    if not 1 <= packet_size <= 32768:
        raise ValueError('Packet exceeds diagnostic bounds')
    width = packet_size.bit_length() + 3
    pending = ''
    frames = []
    previous = None
    for packet in packets:
        raw = unpack(packet['data'])
        if len(raw) != packet_size or len(raw) != int(packet['size']):
            raise ValueError('Packet length changes')
        bits = ''.join(f'{value:08b}' for value in raw)
        reader = Bits(bits)
        sequence = reader.take(4)
        if previous is not None and sequence != (previous + 1) % 16:
            raise ValueError('Codec packet sequence gap')
        previous = sequence
        reader.take(2)
        continuation = reader.take(width)
        if reader.position + continuation > len(bits):
            raise ValueError('Continuation exceeds packet')
        if continuation:
            combined = pending + bits[reader.position:reader.position + continuation]
            reader.position += continuation
            if len(combined) < width:
                raise ValueError('Missing frame length prefix')
            declared = int(combined[:width], 2)
            if declared > len(combined):
                pending = combined
                continue
            if declared != len(combined):
                raise ValueError('Continuation differs from frame length')
            frames.append(header(combined, width, channels))
        while len(bits) - reader.position > width:
            length = int(bits[reader.position:reader.position + width], 2)
            if not length or reader.position + length > len(bits):
                break
            frame = bits[reader.position:reader.position + length]
            reader.position += length
            frames.append(header(frame, width, channels))
            if frame[-1] == '0':
                break
        pending = bits[reader.position:]
        if len(frames) > 256:
            raise ValueError('Frame count exceeds diagnostic bounds')
    if not frames or not frames[-1]['hasTrimEnd']:
        raise ValueError('No terminal trim declaration in this diagnostic input')
    declared = 2048 * len(frames) - sum(frame['trimStart'] + frame['trimEnd'] for frame in frames)
    overlap = (2048 - max(channel[-1] for channel in frames[-1]['tiles'])) // 2
    return {'frameCount': len(frames), 'frames': frames,
            'samplesFromWholeFramesAndTrim': declared,
            'terminalOverlapHypothesis': overlap,
            'samplesWithTerminalOverlapHypothesis': declared + overlap,
            'productionPolicy': False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('packet_json', type=pathlib.Path)
    args = parser.parse_args()
    if args.packet_json.stat().st_size > 8 * 1024 * 1024:
        raise ValueError('Packet JSON exceeds diagnostic bounds')
    print(json.dumps(inspect(json.loads(args.packet_json.read_text())), indent=2))


if __name__ == '__main__':
    main()
