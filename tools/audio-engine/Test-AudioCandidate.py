"""Smoke-check a local audio build against the pinned independent decoder."""
import hashlib
import importlib.util
import json
import math
import os
import pathlib
import re
import struct
import subprocess
import sys
import uuid
import wave
import zipfile
import zlib


TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("audio_build", TOOLS / "Build-AudioEngine.py")
build_tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(build_tool)


def run(arguments, directory):
    environment = dict(os.environ)
    environment.pop("FFREPORT", None)
    environment["AV_LOG_FORCE_NOCOLOR"] = "1"
    result = subprocess.run([str(value) for value in arguments], cwd=directory, env=environment,
                            timeout=20, capture_output=True, text=True)
    if result.returncode:
        raise ValueError(f"Native smoke command failed: {arguments}\n{result.stderr}")
    return result.stdout


def verify_evaluation(engine):
    pin = json.loads((TOOLS / "evaluation.json").read_text())
    archive_path = engine.parents[2] / "upstream.zip"
    if (engine != archive_path.parent / "unpacked" / pin["archiveDirectory"] / "bin"
            or build_tool.digest(archive_path) != pin["archiveSha256"].upper()):
        raise ValueError("Use the retained pinned independent evaluation engine")
    runtime = {}
    with zipfile.ZipFile(archive_path) as archive:
        for entry in archive.infolist():
            name = pathlib.PurePosixPath(entry.filename)
            if name.parent.as_posix() != pin["archiveDirectory"] + "/bin":
                continue
            if name.suffix.lower() != ".dll" and name.name not in ("ffmpeg.exe", "ffprobe.exe"):
                continue
            with archive.open(entry) as stream:
                expected = hashlib.file_digest(stream, "sha256").hexdigest().upper()
            path = build_tool.local_path(engine / name.name)
            if build_tool.digest(path) != expected:
                raise ValueError("Independent runtime changed: " + name.name)
            runtime[name.name] = expected
    if (not {"ffmpeg.exe", "ffprobe.exe"}.issubset(runtime)
            or {path.name for path in engine.glob("*.dll")} != {name for name in runtime if name.endswith(".dll")}):
        raise ValueError("Independent runtime membership differs")
    return runtime


def main():
    if len(sys.argv) != 3 or sys.version_info < (3, 14):
        raise ValueError("Use Python 3.14: Test-AudioCandidate.py <completed build> <evaluation bin>")
    candidate, independent = (build_tool.local_path(value) for value in sys.argv[1:])
    record = json.loads((candidate / "build-result.json").read_text())
    if (record["exitCode"] != 0 or record["status"] != "Unaccepted candidate build attempt"
            or record["scriptSha256"] != build_tool.digest(TOOLS / "Build-AudioEngine.py")
            or record["checkerSha256"] != build_tool.digest(TOOLS / "Check-AudioConfiguration.py")):
        raise ValueError("Use a completed build with the current recipes")
    binary = candidate / "bin"
    inventory = json.loads((candidate / "runtime-files.json").read_text())
    names = {"ffmpeg.exe", "ffprobe.exe", *(pathlib.PurePosixPath(name).name for name in build_tool.DLLS)}
    if len(inventory) != len(names) or {item["path"] for item in inventory} != names or {path.name for path in binary.iterdir()} != names:
        raise ValueError("Candidate runtime membership differs")
    for item in inventory:
        path = build_tool.local_path(binary / item["path"])
        if path.stat().st_size != item["bytes"] or build_tool.digest(path) != item["sha256"]:
            raise ValueError("Candidate runtime changed")
    independent_identity = verify_evaluation(independent)
    evidence = candidate / ("smoke-" + uuid.uuid4().hex)
    evidence.mkdir()
    print("Audio smoke evidence: " + str(evidence), flush=True)
    for name in ("ffmpeg", "ffprobe"):
        version = run([binary / (name + ".exe"), "-version"], evidence)
        if not version.startswith(name + " version 9.0.1-contextsuite-g9b0578816c6f "):
            raise ValueError("Candidate version differs")
        (evidence / (name + "-version.log")).write_text(version)
    for option in ("decoders", "encoders", "demuxers", "muxers", "protocols"):
        listing = run([binary / "ffmpeg.exe", "-hide_banner", "-" + option], evidence)
        (evidence / (option + ".log")).write_text(listing)
        if option == "protocols":
            actual = set(re.findall(r"^  (\w+)$", listing, re.MULTILINE))
            if actual != {"file", "pipe", "fd"}:
                raise ValueError("Runtime protocol list differs")
    source = evidence / "source.wav"
    samples = [int(amplitude * math.sin(2 * math.pi * frequency * frame / 48000))
               for frame in range(48000) for amplitude, frequency in ((12000, 440), (9000, 880))]
    with wave.open(str(source), "wb") as output:
        output.setparams((2, 2, 48000, 48000, "NONE", "not compressed"))
        output.writeframes(struct.pack("<" + "h" * len(samples), *samples))
    source_hash = build_tool.digest(source)
    settings = {
        "wav": ("pcm_s16le", []), "flac": ("flac", ["-compression_level", "8"]),
        "mp3": ("libmp3lame", ["-q:a", "2"]),
        "m4a": ("aac", ["-profile:a", "aac_low", "-b:a", "192k", "-movflags", "+faststart", "-f", "ipod"]),
        "ogg": ("libvorbis", ["-q:a", "5", "-page_duration", "1"]),
        "opus": ("libopus", ["-application", "audio", "-b:a", "160k", "-vbr", "on"]),
    }
    results = []
    for extension, (codec, options) in settings.items():
        encoded = evidence / ("encoded." + extension)
        run([binary / "ffmpeg.exe", "-v", "error", "-n", "-protocol_whitelist", "file,pipe", "-i", source,
             "-map", "0:a:0", "-threads", "1", "-c:a", codec, *options, encoded], evidence)
        encoded_hash = build_tool.digest(encoded)
        measurements = {}
        decoded_values = {}
        for label, engine in (("candidate", binary), ("independent", independent)):
            facts = json.loads(run([engine / "ffprobe.exe", "-v", "error", "-protocol_whitelist", "file",
                                    "-show_streams", "-of", "json", encoded], evidence))
            streams = facts["streams"]
            expected_codec = {"libmp3lame": "mp3", "libvorbis": "vorbis", "libopus": "opus"}.get(codec, codec)
            if (len(streams) != 1 or streams[0]["codec_name"] != expected_codec
                    or streams[0]["sample_rate"] != "48000" or streams[0]["channels"] != 2):
                raise ValueError("Encoded stream properties differ")
            decoded = evidence / (extension + "-" + label + ".f64")
            run([engine / "ffmpeg.exe", "-v", "error", "-n", "-protocol_whitelist", "file,pipe",
                 "-i", encoded, "-map", "0:a:0", "-t", "2", "-threads", "1", "-c:a", "pcm_f64le",
                 "-f", "f64le", decoded], evidence)
            if decoded.stat().st_size != len(samples) * 8:
                raise ValueError("Decoded frame count differs")
            values = [value[0] for value in struct.iter_unpack("<d", decoded.read_bytes())]
            errors = [abs(value - original / 32768) for value, original in zip(values, samples)]
            rms = math.sqrt(sum(error * error for error in errors) / len(errors))
            peak = max(errors)
            # The product's lossy signal admission uses RMS, not a peak-error
            # cap. Retain transient peaks for review, with a tighter RMS bound
            # for this simple tone and a separate cross-decoder comparison.
            if (not all(math.isfinite(error) for error in errors) or rms > 0.02
                    or extension in ("wav", "flac") and peak != 0):
                raise ValueError(f"Generated-tone signal check failed: {extension}, {label}, RMS={rms}, peak={peak}")
            decoded_values[label] = values
            measurements[label] = {"frames": 48000, "rmsError": rms, "peakError": peak,
                                   "decodedSha256": build_tool.digest(decoded)}
        if build_tool.digest(encoded) != encoded_hash:
            raise ValueError("Encoded input changed during decoding")
        difference = max(abs(left - right) for left, right in zip(decoded_values["candidate"], decoded_values["independent"]))
        if difference > 0.00001:
            raise ValueError(f"Candidate and independent decoder differ: {extension}, {difference}")
        results.append({"format": extension, "bytes": encoded.stat().st_size, "sha256": encoded_hash,
                        "maximumDecoderDifference": difference, "decoders": measurements})
    # An authored 1x1 RGB PNG in a FLAC PICTURE block exercises the actual PNG
    # decoder/inflate path without adding an image-file demuxer to the build.
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(b"\0\xff\x80\0")) + chunk(b"IEND", b""))
    picture = struct.pack(">II", 3, 9) + b"image/png" + struct.pack(">IIIIII", 0, 1, 1, 24, 0, len(png)) + png
    flac = (evidence / "encoded.flac").read_bytes()
    offset = 4
    blocks = []
    while True:
        header = flac[offset:offset + 4]
        length = int.from_bytes(header[1:], "big")
        blocks.append(bytes([header[0] & 127]) + header[1:] + flac[offset + 4:offset + 4 + length])
        offset += 4 + length
        if header[0] & 128:
            break
    artwork = evidence / "artwork.flac"
    artwork.write_bytes(b"fLaC" + b"".join(blocks) + b"\x86" + len(picture).to_bytes(3, "big") + picture + flac[offset:])
    artwork_hash = build_tool.digest(artwork)
    frames = json.loads(run([binary / "ffprobe.exe", "-v", "error", "-protocol_whitelist", "file",
                             "-select_streams", "v", "-show_frames", "-of", "json", artwork], evidence))["frames"]
    if len(frames) != 1 or any(frames[0][key] != value for key, value in (("width", 1), ("height", 1), ("pix_fmt", "rgb24"))):
        raise ValueError("Embedded PNG frame did not decode")
    if build_tool.digest(artwork) != artwork_hash or build_tool.digest(source) != source_hash:
        raise ValueError("Generated original changed")
    report = {"scope": "Six-format generated-tone and PNG decoding smoke only; not full adapter, worker or listening acceptance",
              "runtime": inventory, "independentRuntime": independent_identity, "sourceSha256": source_hash,
              "checkerSha256": build_tool.digest(pathlib.Path(__file__)), "formats": results, "pngFrames": frames}
    (evidence / "result.json").write_text(json.dumps(report, indent=2))
    print("Six targets passed candidate and independent decoding; embedded PNG decoded; originals unchanged.")


if __name__ == "__main__":
    main()
