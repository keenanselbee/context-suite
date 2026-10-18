"""Generate original audio and verify installed Wwise's PCM/Vorbis external sources.

Creates a fresh project with the user's installed console; no Wwise template or
game audio is redistributed. Output must be a new directory in repository scratch.
"""

import argparse
import hashlib
import json
import math
import pathlib
import struct
import subprocess
import wave
import xml.etree.ElementTree as ET


def chunks(path):
    content = path.read_bytes()
    if content[:4] != b"RIFF" or content[8:12] != b"WAVE" or \
            struct.unpack_from("<I", content, 4)[0] + 8 != len(content):
        raise ValueError(f"Invalid Wwise output extent: {path.name}")
    result = {}
    offset = 12
    while offset < len(content):
        if offset + 8 > len(content):
            raise ValueError("Truncated Wwise chunk header")
        kind, size = struct.unpack_from("<4sI", content, offset)
        end = offset + 8 + size
        if end > len(content) or kind in result:
            raise ValueError("Invalid or duplicate Wwise chunk")
        result[kind] = content[offset + 8:end]
        offset = end if end == len(content) else end + (size & 1)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--wwise-console", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--loops", action="store_true", help="Include and verify one forward loop in each fixture")
    args = parser.parse_args()
    console = args.wwise_console.resolve(strict=True)
    if console.name.lower() != "wwiseconsole.exe" or not console.is_file():
        raise ValueError("Select the installed WwiseConsole.exe")
    root = args.output.resolve()
    scratch = pathlib.Path(__file__).resolve().parents[2] / ".codex-temp"
    if not root.is_relative_to(scratch) or root == scratch or root.exists():
        raise ValueError("Output must be a new directory under repository .codex-temp")
    root.mkdir(parents=True)
    inputs = root / "inputs"
    inputs.mkdir()
    fixtures = {}
    for rate in (44100, 48000):
        for channels in (1, 2):
            frames = rate // 4
            path = inputs / f"sine-{rate}-{channels}ch.wav"
            samples = bytearray()
            for frame in range(frames):
                for channel in range(channels):
                    value = round(0.2 * 32767 * math.sin(2 * math.pi * (440 + channel * 220) * frame / rate))
                    samples.extend(struct.pack("<h", value))
            with wave.open(str(path), "wb") as output:
                output.setnchannels(channels)
                output.setsampwidth(2)
                output.setframerate(rate)
                output.writeframes(samples)
            if args.loops:
                content = bytearray(path.read_bytes())
                smpl = struct.pack("<15I", 0, 0, round(1_000_000_000 / rate), 60, 0, 0, 0,
                                   1, 0, 0, 0, 100, frames - 101, 0, 0)
                content.extend(b"smpl" + struct.pack("<I", len(smpl)) + smpl)
                struct.pack_into("<I", content, 4, len(content) - 8)
                path.write_bytes(content)
            fixtures[path.stem] = (rate, channels, frames, bytes(samples))

    def run(label, arguments):
        process = subprocess.run([str(console), *map(str, arguments)], cwd=root,
                                 capture_output=True, timeout=120)
        (root / f"{label}.stdout.log").write_bytes(process.stdout)
        (root / f"{label}.stderr.log").write_bytes(process.stderr)
        if process.returncode != 0:
            raise RuntimeError(f"Wwise {label} failed ({process.returncode}); see scratch logs")

    project = root / "Probe" / "Probe.wproj"
    run("create", ["create-new-project", project, "--platform", "Windows"])
    if not project.is_file():
        raise RuntimeError("Wwise did not create the requested project")
    project_xml = ET.parse(project).getroot()
    results = []
    for preset, conversion in (("pcm", "PCM As Input"), ("vorbis", "Vorbis Quality High")):
        sources = ET.Element("ExternalSourcesList", SchemaVersion="1", Root=str(inputs))
        for stem in fixtures:
            ET.SubElement(sources, "Source", Path=stem + ".wav", Conversion=conversion)
        source_list = root / f"{preset}.wsources"
        ET.ElementTree(sources).write(source_list, encoding="utf-8", xml_declaration=True)
        output = root / f"output-{preset}"
        run(preset, ["convert-external-source", project, "--source-file", source_list,
                     "--platform", "Windows", "--output", output])
        if {path.stem for path in (output / "Windows").glob("*.wem")} != set(fixtures):
            raise RuntimeError("Wwise output files do not match the requested sources")
        for stem, (rate, channels, frames, reference) in fixtures.items():
            path = output / "Windows" / (stem + ".wem")
            media = chunks(path)
            fmt = media[b"fmt "]
            tag, actual_channels, actual_rate = struct.unpack_from("<HHI", fmt)
            if (tag, actual_channels, actual_rate) != \
                    (0xFFFE if preset == "pcm" else 0xFFFF, channels, rate):
                raise ValueError(f"Wwise changed codec, channels or rate: {path.name}")
            if preset == "pcm":
                if struct.unpack_from("<H", fmt, 14)[0] != 16 or media[b"data"] != reference:
                    raise ValueError("Wwise PCM did not preserve exact source samples")
            elif len(fmt) != 66 or struct.unpack_from("<I", fmt, 24)[0] != frames:
                raise ValueError("Wwise Vorbis did not preserve the declared sample count")
            if args.loops:
                smpl = media.get(b"smpl", b"")
                if len(smpl) < 52 or struct.unpack_from("<I", smpl, 28)[0] != 1 or \
                        struct.unpack_from("<III", smpl, 40) != (0, 100, frames - 101):
                    raise ValueError("Wwise did not preserve the source forward loop")
            results.append({"preset": preset, "file": str(path.relative_to(root)),
                            "sampleRate": rate, "channels": channels, "frames": frames,
                            "loopVerified": args.loops,
                            "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    manifest = {"console": str(console), "consoleSha256": hashlib.sha256(console.read_bytes()).hexdigest(),
                "projectAttributes": project_xml.attrib, "outputs": results}
    (root / "wwise-results.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Passed {len(results)} installed-Wwise PCM/Vorbis conversions; evidence: {root}")


if __name__ == "__main__":
    main()
