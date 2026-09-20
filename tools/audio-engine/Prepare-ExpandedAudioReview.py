"""Prepare five generated AIFF/WMA sources and ten local listening comparisons."""

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import uuid
import wave

ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / "tools/audio-engine"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def write_wave(path, samples, rate, channels, bits, floating=False):
    unit = channels * (bits // 8)
    if not samples or len(samples) % unit or len(samples) > 16 * 1024 * 1024:
        raise ValueError("Invalid bounded reference PCM")
    header = struct.pack("<4sI4s4sIHHIIHH4sI", b"RIFF", 36 + len(samples), b"WAVE", b"fmt ", 16,
                         3 if floating else 1, channels, rate, rate * unit, unit, bits, b"data", len(samples))
    path.write_bytes(header + samples)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--audio-engine", type=Path, required=True)
    parser.add_argument("--generated-pack", type=Path, required=True, help="Existing three-clip generated listening pack; never customer recordings.")
    args = parser.parse_args()
    engine = args.audio_engine.resolve(strict=True)
    generated = args.generated_pack.resolve(strict=True)
    spec = importlib.util.spec_from_file_location("audio_payload", TOOLS / "Stage-AudioPayload.py")
    staging = importlib.util.module_from_spec(spec); spec.loader.exec_module(staging)
    staging.local_path(engine)
    if engine.name != "audio-engine" or not generated.is_relative_to(ROOT / ".codex-temp/audio-review") or generated.name != "pack":
        raise ValueError("Use isolated engine and generated review pack directories")
    pin = staging.verify(engine.parent)
    if pin.get("profile") != "expanded" or not (engine / "voice/WmaVoiceMftProbe.exe").is_file():
        raise ValueError("The verified expanded runtime and Windows Voice provider are required")
    report = json.loads((generated / "results.json").read_text())
    if not report["AutomatedPassed"] or len(report["Sources"]) != 3 or len(report["Comparisons"]) != 15:
        raise ValueError("Use a complete original generated listening pack")
    references = {item["Name"]: item for item in report["Sources"]}
    if set(references) != {"music", "transients", "speech"}:
        raise ValueError("Unexpected generated references")
    for item in references.values():
        path = Path(item["Path"]).resolve(strict=True)
        if not path.is_relative_to(generated) or digest(path) != item["Sha256"]:
            raise ValueError("Generated reference identity changed")
    original = TOOLS / "WmaFixtureWriter.cpp"
    if digest(original) != "BE4037550098D70AB95EB569337E9D350B860B4269AC9B65C7AACDDD2D66E309":
        raise ValueError("Review adaptation against the changed authored WMA writer")
    stage = ROOT / ".codex-temp/expanded-audio-review" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    pack = stage / "pack"; pack.mkdir()
    print("Expanded listening evidence:", stage, flush=True)

    def run(label, command, cwd=stage, timeout=180):
        environment = dict(os.environ); environment.pop("FFREPORT", None)
        command = [str(value) for value in command]
        result = subprocess.run(command, cwd=cwd, env=environment, capture_output=True, timeout=timeout)
        (stage / (label + ".stdout.log")).write_bytes(result.stdout)
        (stage / (label + ".stderr.log")).write_bytes(result.stderr)
        (stage / (label + ".command.json")).write_text(json.dumps({"Command": command, "Cwd": str(cwd), "ExitCode": result.returncode}, indent=2))
        if result.returncode:
            raise RuntimeError("Expanded listening preparation failed: " + label)
        return result.stdout

    paths = {path for path in TOOLS.iterdir() if path.suffix in (".py", ".cpp", ".json")}
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/ContextSuite.Worker", "src/Shared",
                      "proprietary/src/ContextSuite.Private", "tests/ContextSuite.Core.ContractTests", "tools/office-engine/Probe"):
        paths.update(path for path in (ROOT / directory).rglob("*") if
                     path.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(path.parts))
    paths.update(ROOT / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
        "Version.props", "global.json", "tools/Require-Private.targets") if (ROOT / name).is_file())
    identities = {str(path): digest(path) for path in paths}
    identities.update({item["Path"]: item["Sha256"] for item in references.values()})
    identities.update({str(path): digest(path) for path in engine.rglob("*") if path.is_file()})
    identities[str(generated / "results.json")] = digest(generated / "results.json")

    # Adapt a fresh test-only source copy; preserve the established short-corpus writer.
    source = original.read_text()
    before = "frames > rate * 3"
    if source.count(before) != 1:
        raise ValueError("Unexpected writer duration guard")
    source = source.replace(before, "frames > rate * 30")
    start = source.index("    const double scale ="); end = source.index("    HANDLE raw =", start)
    source = source[:start] + '''    HANDLE fixture = CreateFileW(L"input.pcm", GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (fixture == INVALID_HANDLE_VALUE) throw std::runtime_error("Missing generated review PCM");
    LARGE_INTEGER size{}; DWORD read = 0;
    bool complete = GetFileSizeEx(fixture, &size) && size.QuadPart == static_cast<LONGLONG>(pcm.size()) &&
        ReadFile(fixture, pcm.data(), static_cast<DWORD>(pcm.size()), &read, nullptr) && read == pcm.size();
    CloseHandle(fixture);
    if (!complete) throw std::runtime_error("Review PCM has an unexpected extent");
''' + source[end:]
    build = stage / "writer"; build.mkdir()
    (build / "WmaFixtureWriter.cpp").write_text(source)
    vswhere = Path(os.environ["ProgramFiles(x86)"]) / "Microsoft Visual Studio/Installer/vswhere.exe"
    found = run("compiler", [vswhere, "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
                             "-property", "installationPath"])
    vcvars = Path(found.decode().strip()) / "VC/Auxiliary/Build/vcvars64.bat"
    if not vcvars.is_file() or any(char in str(vcvars) + str(build) for char in '%!&|^"'):
        raise ValueError("Unsupported compiler path")
    command = build / "build.cmd"
    command.write_text('@echo off\ncall "' + str(vcvars) + '" 10.0.26100.0\nif errorlevel 1 exit /b 1\n'
        'cl.exe /nologo /W4 /WX /O2 /MT /EHsc /std:c++20 WmaFixtureWriter.cpp '
        '/link mfplat.lib mfreadwrite.lib mfuuid.lib mf.lib ole32.lib /DYNAMICBASE /NXCOMPAT\nexit /b %errorlevel%\n')
    run("writer-build", ["cmd.exe", "/d", "/c", command], build)
    fixtures = []
    for name, rate, channels, bits, clip in (("aiff", 44100, 2, 24, "music"), ("standard", 48000, 2, 16, "transients"),
            ("pro", 48000, 2, 24, "transients"), ("lossless", 44100, 2, 24, "music"), ("voice", 16000, 1, 16, "speech")):
        directory = pack / "sources" / name; directory.mkdir(parents=True)
        pcm_path = directory / "input.pcm"
        prepared = directory / "prepared.wav"
        run(name + "-prepare", [engine / "ffmpeg.exe", "-v", "error", "-nostdin", "-i", references[clip]["Path"],
            "-map_metadata", "-1", "-threads", "1", "-ar", rate, "-ac", channels, "-c:a", f"pcm_s{bits}le",
            "-f", "wav", prepared])
        with wave.open(str(prepared), "rb") as reader:
            if reader.getframerate() != rate or reader.getnchannels() != channels or reader.getsampwidth() != bits // 8:
                raise ValueError("Unexpected prepared PCM profile")
            pcm = reader.readframes(reader.getnframes())
        pcm_path.write_bytes(pcm); unit = channels * (bits // 8)
        if name in ("aiff", "lossless"):
            with wave.open(references[clip]["Path"], "rb") as reader:
                if (reader.getframerate(), reader.getnchannels(), reader.getsampwidth()) != (rate, channels, bits // 8) or pcm != reader.readframes(reader.getnframes()):
                    raise ValueError("Lossless fixture preparation changed authored PCM")
        if not pcm or len(pcm) % unit:
            raise ValueError("Invalid generated review samples")
        frames = len(pcm) // unit
        if not rate * 10 <= frames <= rate * 30:
            raise ValueError("Use ten to thirty second review clips")
        if name == "aiff":
            # Fixed 44.1-kHz extended-precision rate; PCM24 samples change byte order only.
            comm = struct.pack(">hIh", channels, frames, bits) + bytes.fromhex("400eac44000000000000")
            data = b"".join(pcm[index:index + 3][::-1] for index in range(0, len(pcm), 3))
            chunks = b"COMM" + struct.pack(">I", len(comm)) + comm + b"SSND" + struct.pack(">I", len(data) + 8) + bytes(8) + data
            path = directory / "fixture.aif"; path.write_bytes(b"FORM" + struct.pack(">I", len(chunks) + 4) + b"AIFF" + chunks)
            reference = pcm_path; encoding = "s24"; codec = "pcm_s24be"
        else:
            run(name + "-fixture", [build / "WmaFixtureWriter.exe", name, rate, channels, bits, frames], directory)
            path = directory / "fixture.wma"
            reference = directory / ("source.pcm" if name == "lossless" else "windows.f32")
            encoding = f"s{bits}" if name == "lossless" else "f32"
            codec = {"standard": "wmav2", "pro": "wmapro", "lossless": "wmalossless", "voice": "wmavoice"}[name]
            if (directory / "source.pcm").read_bytes() != pcm:
                raise ValueError("Windows fixture writer changed its input PCM")
        expected = reference.read_bytes(); expected_bits = int(encoding[1:])
        if len(expected) % (channels * (expected_bits // 8)):
            raise ValueError("Independent reference has a partial frame")
        frames = len(expected) // (channels * (expected_bits // 8))
        write_wave(Path(str(reference) + ".wav"), expected, rate, channels, expected_bits, encoding[0] == "f")
        fixtures.append(dict(Name=name, Path=str(path), Sha256=digest(path), Codec=codec, Rate=rate, Channels=channels,
            Frames=frames, ReferencePath=str(reference), ReferenceHash=digest(reference), ReferenceEncoding=encoding,
            MaximumError=1e-6 if name in ("standard", "pro") else 0, RmsError=3e-7 if name in ("standard", "pro") else 0))
    manifest = stage / "fixtures.json"; manifest.write_text(json.dumps(fixtures, indent=2))
    for label, project in (("worker", "src/ContextSuite.Worker/ContextSuite.Worker.csproj"),
                           ("contracts", "tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj")):
        run(label + "-build", ["dotnet", "build", ROOT / project, "-c", "Release", "--no-restore", "--verbosity", "quiet"], ROOT)
    worker = stage / "worker"; worker.mkdir()
    for path in (ROOT / "artifacts/managed/bin/ContextSuite.Worker/Release/net10.0-windows").iterdir():
        if path.is_file(): shutil.copy2(path, worker / path.name)
    shutil.copytree(engine, worker / "audio-engine"); staging.verify(worker)
    host = ROOT / "artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.exe"
    identities.update({str(path): digest(path) for folder in (worker, build, pack / "sources") for path in folder.rglob("*") if path.is_file()})
    identities.update({str(path): digest(path) for path in host.parent.iterdir() if path.is_file()})
    identities[str(manifest)] = digest(manifest)
    (stage / "inputs.json").write_text(json.dumps({"Identities": identities, "AudioProfile": pin["profile"],
        "References": references, "FixturePolicy": "Fixed five generated inputs; PCM/Windows reference; ten WAV/Opus comparisons."}, indent=2))
    if any(digest(Path(path)) != expected for path, expected in identities.items()):
        raise RuntimeError("Listening inputs changed before execution")
    run("prepare", [host, "--expanded-audio-listening-pack", pack, worker / "ContextSuite.Worker.exe", manifest], ROOT, 600)
    print((stage / "prepare.stdout.log").read_text(errors="replace"), end="", flush=True)
    unchanged = all(digest(Path(path)) == expected for path, expected in identities.items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": 0, "InputsUnchanged": unchanged}))
    if not unchanged: raise RuntimeError("Listening inputs changed during execution")
    staging.verify(worker); staging.verify(engine.parent)
    run("worksheet", [sys.executable, "-B", TOOLS / "Write-AudioReviewPage.py", pack], ROOT)
    print("Open the local review page:", pack / "review.html", flush=True)


if __name__ == "__main__":
    main()
