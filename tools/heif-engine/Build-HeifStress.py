"""Generate reproducible native-interruption and oversized-NAL inputs in repository scratch."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--encoder-sdk", required=True, type=Path)
    parser.add_argument("--decoder-sdk", required=True, type=Path)
    parser.add_argument("--cmake", required=True, type=Path)
    parser.add_argument("--oversized-nal", action="store_true")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    encoder, decoder = args.encoder_sdk.resolve(strict=True), args.decoder_sdk.resolve(strict=True)
    for path in (encoder, decoder):
        path.relative_to(root / ".codex-temp")
    source = Path(__file__).parent / "Stress"
    inputs = {str(path): digest(path) for folder in (source, encoder) for path in folder.rglob("*") if path.is_file()}
    for path in (decoder / "bin/libde265.dll", Path(__file__).resolve(), args.cmake.resolve(strict=True)):
        inputs[str(path)] = digest(path)
    workspace = root / ".codex-temp/heif-stress" / uuid.uuid4().hex
    workspace.mkdir(parents=True)
    print("HEIF stress evidence:", workspace, flush=True)
    (workspace / "inputs.json").write_text(json.dumps(inputs, indent=2) + "\n")

    def run(name, command):
        command = [str(value) for value in command]
        child = subprocess.run(command, cwd=root, capture_output=True, timeout=240)
        (workspace / (name + ".stdout.log")).write_bytes(child.stdout)
        (workspace / (name + ".stderr.log")).write_bytes(child.stderr)
        (workspace / (name + ".command.json")).write_text(json.dumps({"command": command, "exit": child.returncode}, indent=2) + "\n")
        if child.returncode:
            print(child.stdout.decode(errors="replace"), child.stderr.decode(errors="replace"), flush=True)
            raise RuntimeError("Failed " + name + "; inspect retained evidence")

    run("configure", [args.cmake, "-S", source, "-B", workspace / "build", "-G", "Visual Studio 18 2026",
        "-A", "x64", "-DCMAKE_PREFIX_PATH=" + str(encoder)])
    run("build", [args.cmake, "--build", workspace / "build", "--config", "Release", "--parallel", "4"])
    shutil.copy2(encoder / "bin/heif.dll", workspace / "build/Release/heif.dll")
    shutil.copy2(decoder / "bin/libde265.dll", workspace / "build/Release/libde265.dll")
    run("generate", [workspace / "build/Release/ContextSuite.Heif.Stress.exe", workspace / "fixtures",
        *(["--oversized-nal"] if args.oversized_nal else [])])
    if any(digest(Path(path)) != expected for path, expected in inputs.items()):
        raise ValueError("Stress generator inputs changed")
    (workspace / "results.json").write_text(json.dumps({"Passed": True,
        "Dimension": 4096 if args.oversized_nal else 3072,
        "Fixtures": {str(path): digest(path) for path in (workspace / "fixtures").iterdir()}}, indent=2) + "\n")
    print("Generated stress fixtures:", workspace / "fixtures", flush=True)


if __name__ == "__main__":
    main()
