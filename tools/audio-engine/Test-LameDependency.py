"""Check authored LAME output with the separately pinned evaluation decoder."""
import hashlib
import json
import math
import pathlib
import struct
import subprocess
import sys
import uuid
import zipfile


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def run(arguments):
    return subprocess.run(arguments, check=True, timeout=20, capture_output=True, text=True).stdout


def main():
    if len(sys.argv) != 3:
        raise ValueError("Usage: python Test-LameDependency.py <dependency-build-directory> <evaluation-engine-bin>")
    if sys.version_info < (3, 14):
        raise ValueError("Use Python 3.14 or later, matching the source-retention tools")
    scratch = pathlib.Path(__file__).resolve().parents[2] / ".codex-temp"
    build, engine = (pathlib.Path(value).resolve() for value in sys.argv[1:])
    if not build.is_relative_to(scratch) or not engine.is_relative_to(scratch):
        raise ValueError("Both paths must remain in repository scratch")
    ffmpeg, ffprobe = engine / "ffmpeg.exe", engine / "ffprobe.exe"
    pin = json.loads(pathlib.Path(__file__).with_name("evaluation.json").read_text())
    archive_path = engine.parents[2] / "upstream.zip"
    if engine != archive_path.parent / "unpacked" / pin["archiveDirectory"] / "bin" or digest(archive_path) != pin["archiveSha256"].lower():
        raise ValueError("Use the retained, pinned independent evaluation engine")
    runtime = {}
    with zipfile.ZipFile(archive_path) as archive:
        for entry in archive.infolist():
            name = pathlib.PurePosixPath(entry.filename)
            if name.parent.as_posix() != pin["archiveDirectory"] + "/bin":
                continue
            if name.suffix.lower() != ".dll" and name.name not in ("ffmpeg.exe", "ffprobe.exe"):
                continue
            with archive.open(entry) as stream:
                expected_hash = hashlib.file_digest(stream, "sha256").hexdigest()
            path = engine / name.name
            if path.is_symlink() or path.is_junction() or digest(path) != expected_hash:
                raise ValueError("Evaluation runtime changed: " + name.name)
            runtime[name.name] = expected_hash
    if not {"ffmpeg.exe", "ffprobe.exe"}.issubset(runtime) or {path.name for path in engine.glob("*.dll")} != {name for name in runtime if name.endswith(".dll")}:
        raise ValueError("Evaluation runtime file membership changed")
    identity = json.loads((build / "dependency-build.json").read_text(encoding="utf-8-sig"))
    if identity["dependency"] not in ("Lame", "LameStable"):
        raise ValueError("Use a completed LAME dependency build")
    fixture = build / "build/lame-vbr2.mp3"
    expected = next(item for item in identity["artifacts"] if item["path"] == "lame-vbr2.mp3")
    if fixture.stat().st_size > 65536 or digest(fixture) != expected["sha256"].lower():
        raise ValueError("The authored fixture changed after its build")
    evidence = build / ("independent-check-" + uuid.uuid4().hex)
    evidence.mkdir()
    facts = json.loads(run([str(ffprobe), "-v", "error", "-protocol_whitelist", "file",
                           "-format_whitelist", "mp3", "-f", "mp3", "-show_streams", "-of", "json", str(fixture)]))
    streams = facts["streams"]
    if len(streams) != 1 or streams[0]["codec_name"] != "mp3" or streams[0]["sample_rate"] != "44100" or streams[0]["channels"] != 2:
        raise ValueError("Encoded stream properties differ from the authored input")
    decoded = evidence / "decoded.f64"
    run([str(ffmpeg), "-v", "error", "-n", "-protocol_whitelist", "file,pipe", "-format_whitelist", "mp3",
         "-f", "mp3", "-i", str(fixture), "-map", "0:a:0", "-t", "2", "-threads", "1",
         "-c:a", "pcm_f64le", "-f", "f64le", str(decoded)])
    if decoded.stat().st_size != 44100 * 2 * 8:
        raise ValueError("Gapless decoded sample count differs from the authored input")
    squared_error = 0.0
    peak_error = 0.0
    for index, (actual,) in enumerate(struct.iter_unpack("<d", decoded.read_bytes())):
        frame, channel = divmod(index, 2)
        amplitude, frequency = ((12000, 440), (9000, 880))[channel]
        original = int(amplitude * math.sin(6.283185307179586 * frequency * frame / 44100.0)) / 32768.0
        error = abs(actual - original)
        if not math.isfinite(error):
            raise ValueError("Decoded non-finite sample")
        squared_error += error * error
        peak_error = max(peak_error, error)
    rms_error = math.sqrt(squared_error / (44100 * 2))
    if rms_error > 0.01 or peak_error > 0.10:
        raise ValueError(f"Generated-tone error exceeds the smoke-test bound: {rms_error}, {peak_error}")
    if digest(fixture) != expected["sha256"].lower():
        raise ValueError("The source changed during verification")
    report = {"scope": "Generated one-second VBR2 stereo smoke check; not listening or full adapter acceptance",
              "frames": 44100, "channels": 2, "sampleRate": 44100, "rmsError": rms_error, "peakError": peak_error,
              "sourceSha256": digest(fixture), "decodedSha256": digest(decoded), "runtime": runtime,
              "archiveSha256": pin["archiveSha256"], "checkerSha256": digest(pathlib.Path(__file__)), "streams": streams}
    (evidence / "result.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"evidence": str(evidence), "frames": 44100, "rmsError": rms_error, "peakError": peak_error}))


if __name__ == "__main__":
    main()
