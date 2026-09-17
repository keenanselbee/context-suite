"""Evaluate new input decoding only; does not enable application capabilities."""
import importlib.util
import json
import math
import pathlib
import struct
import sys
import uuid
import wave


TOOLS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("audio_smoke", TOOLS / "Test-AudioCandidate.py")
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)
build = smoke.build_tool


def chunk(kind, data):
    return kind + len(data).to_bytes(4, "big") + data + bytes(len(data) % 2)


def aiff(samples, bits, channels, compression=None):
    # Authored from Apple's AIFF 1.3 / AIFF-C specifications. 48 kHz is exact
    # in extended precision: exponent 15, significand 48000 << 48.
    rate = struct.pack(">HQ", 16383 + 15, 48000 << 48)
    floating = compression in (b"fl32", b"fl64")
    if floating:
        data = b"".join(struct.pack(">f" if bits == 32 else ">d", value) for value in samples)
    else:
        order = "little" if compression == b"sowt" else "big"
        data = b"".join(value.to_bytes(bits // 8, order, signed=True) for value in samples)
    common = struct.pack(">HIH", channels, len(samples) // channels, bits) + rate
    if compression is not None:
        common += compression + b"\0\0"  # Empty, even-sized Pascal compression name.
    body = b"AIFF" if compression is None else b"AIFC" + chunk(b"FVER", bytes.fromhex("A2805140"))
    body += chunk(b"COMM", common) + chunk(b"SSND", bytes(8) + data)
    return chunk(b"FORM", body)


def main():
    if len(sys.argv) != 3 or sys.version_info < (3, 14):
        raise ValueError("Use Python 3.14: Test-AudioExpandedInputs.py <expanded build> <evaluation bin>")
    candidate, independent = (build.local_path(value) for value in sys.argv[1:])
    receipt = json.loads((candidate / "build-result.json").read_text())
    if (receipt["exitCode"] or receipt.get("expandedInputs") is not True
            or receipt["scriptSha256"] != build.digest(TOOLS / "Build-AudioEngine.py")
            or receipt["checkerSha256"] != build.digest(TOOLS / "Check-AudioConfiguration.py")):
        raise ValueError("Use a completed expanded-input build with the current recipes")
    inventory = json.loads((candidate / "runtime-files.json").read_text())
    binary = candidate / "bin"
    names = {"ffmpeg.exe", "ffprobe.exe", *(pathlib.PurePosixPath(name).name for name in build.DLLS)}
    if (len(inventory) != len(names) or {item["path"] for item in inventory} != names
            or {path.name for path in binary.iterdir()} != names):
        raise ValueError("Candidate runtime membership differs")
    for item in inventory:
        path = build.local_path(binary / item["path"])
        if path.stat().st_size != item["bytes"] or build.digest(path) != item["sha256"]:
            raise ValueError("Candidate runtime changed")
    independent_identity = smoke.verify_evaluation(independent)
    evidence = candidate / ("expanded-inputs-" + uuid.uuid4().hex)
    evidence.mkdir()
    print("Expanded-input evidence: " + str(evidence), flush=True)
    fixtures = []
    for channels in (1, 2):
        for bits in (8, 16, 24, 32):
            limit = 1 << (bits - 1)
            # Include extrema, alternating channels, zero and non-round values;
            # odd mono frame counts also exercise required AIFF chunk padding.
            cycle = [-limit, limit - 1, 0, 1, -1, limit // 3, -limit // 7]
            samples = [cycle[(frame + channel * 3) % len(cycle)]
                       for frame in range(257) for channel in range(channels)]
            for compression in (None, b"NONE"):
                name = f"{'aiff' if compression is None else 'aifc-none'}-{bits}-{channels}"
                path = evidence / (name + ".aif")
                path.write_bytes(aiff(samples, bits, channels, compression))
                codec = "pcm_s8" if bits == 8 else f"pcm_s{bits}be"
                fixtures.append((path, codec, channels, [value / limit for value in samples]))
        for bits in (32, 64):
            samples = [-1.0, 0.0, 1.0, 0.125, -0.5, 2 ** -30, -(2 ** -30)] * channels
            path = evidence / f"aifc-float{bits}-{channels}.aifc"
            path.write_bytes(aiff(samples, bits, channels, f"fl{bits}".encode()))
            fixtures.append((path, f"pcm_f{bits}be", channels, samples))
        samples = [0, -32768, 32767, 123, -17] * channels
        path = evidence / f"aifc-sowt-{channels}.aifc"
        path.write_bytes(aiff(samples, 16, channels, b"sowt"))
        fixtures.append((path, "pcm_s16le", channels, [value / 32768 for value in samples]))

    source = evidence / "tone.wav"
    tone = [int(amplitude * math.sin(2 * math.pi * frequency * frame / 48000))
            for frame in range(48000) for amplitude, frequency in ((12000, 440), (9000, 880))]
    with wave.open(str(source), "wb") as writer:
        writer.setparams((2, 2, 48000, 48000, "NONE", "not compressed"))
        writer.writeframes(struct.pack("<" + "h" * len(tone), *tone))
    source_hash = build.digest(source)
    for codec in ("pcm_alaw", "pcm_mulaw", "adpcm_ima_qt", "wmav1", "wmav2"):
        extension, container = ("wma", "asf") if codec.startswith("wma") else ("aifc", "aiff")
        path = evidence / (codec + "." + extension)
        options = ["-b:a", "192k"] if codec.startswith("wma") else []
        smoke.run([independent / "ffmpeg.exe", "-v", "error", "-n", "-protocol_whitelist", "file",
                   "-i", source, "-map", "0:a:0", "-threads", "1", "-c:a", codec, *options,
                   "-f", container, path], evidence)
        fixtures.append((path, codec, 2, None))

    observations = []
    for path, codec, channels, oracle in fixtures:
        original_hash = build.digest(path)
        decoded = {}
        facts = {}
        for label, engine in (("candidate", binary), ("independent", independent)):
            probe = smoke.run([engine / "ffprobe.exe", "-v", "error", "-protocol_whitelist", "file",
                               "-format_whitelist", "aiff,asf", "-show_streams", "-of", "json", path], evidence)
            (evidence / (path.stem + "-" + label + "-probe.json")).write_text(probe)
            streams = json.loads(probe)["streams"]
            if (len(streams) != 1 or streams[0]["codec_name"] != codec
                    or streams[0]["sample_rate"] != "48000" or streams[0]["channels"] != channels):
                raise ValueError("Input stream properties differ: " + path.name)
            output = evidence / (path.stem + "-" + label + ".f64")
            smoke.run([engine / "ffmpeg.exe", "-v", "error", "-n", "-protocol_whitelist", "file",
                       "-format_whitelist", "aiff,asf", "-i", path, "-map", "0:a:0", "-t", "2",
                       "-threads", "1", "-c:a", "pcm_f64le", "-f", "f64le", output], evidence)
            raw = output.read_bytes()
            if not raw or len(raw) % (8 * channels) or len(raw) > 48000 * 2 * channels * 8:
                raise ValueError("Invalid decoded sample size: " + path.name)
            decoded[label] = [value[0] for value in struct.iter_unpack("<d", raw)]
            if not all(math.isfinite(value) for value in decoded[label]):
                raise ValueError("Nonfinite decoded samples: " + path.name)
            if oracle is not None and decoded[label] != oracle:
                raise ValueError("Decoded samples differ from authored sample oracle: " + path.name)
            facts[label] = {"frames": len(raw) // (8 * channels), "sha256": build.digest(output)}
        if len(decoded["candidate"]) != len(decoded["independent"]):
            raise ValueError("Decoder frame counts differ: " + path.name)
        maximum = max(abs(a - b) for a, b in zip(decoded["candidate"], decoded["independent"]))
        if maximum > 0.00001:
            raise ValueError("Candidate and supplier decoding differ: " + path.name)
        if build.digest(path) != original_hash:
            raise ValueError("Source changed: " + path.name)
        observations.append({"fixture": path.name, "codec": codec, "channels": channels,
                             "sha256": original_hash, "exactAuthoredSamples": oracle is not None,
                             "maximumDecoderDifference": maximum, "decoders": facts})
    if build.digest(source) != source_hash:
        raise ValueError("Authored tone changed")
    report = {"scope": "Native input decoding only; no metadata, application/worker or listening acceptance",
              "runtime": inventory, "independentRuntime": independent_identity,
              "checkerSha256": build.digest(pathlib.Path(__file__)), "fixtures": observations,
              "unverified": ["WMA Pro", "WMA Lossless", "WMA Voice", "protected ASF", "metadata/artwork",
                             "surround layouts", "malformed inputs", "all six target conversions", "application/worker"]}
    (evidence / "result.json").write_text(json.dumps(report, indent=2))
    print(f"{len(observations)} input fixtures decoded; authored PCM/float samples exact; originals unchanged.")


if __name__ == "__main__":
    main()
