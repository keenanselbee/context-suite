"""Create a local manual listening pack through the current isolated worker and publication flow."""

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--audio-engine", type=Path, required=True, help="Pinned audio-engine folder from a retained production candidate.")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    engine = args.audio_engine.resolve(strict=True)
    engine.relative_to(root)
    pin = json.loads((root / "tools/audio-engine/curated-candidate.json").read_text())
    for item in pin["files"]:
        path = engine / item["path"]
        if path.stat().st_size != item["bytes"] or digest(path) != item["sha256"]:
            raise RuntimeError("Audio engine differs from the current pin.")
    stage = root / ".codex-temp/audio-review" / uuid.uuid4().hex
    pack = stage / "pack"
    (pack / "sources").mkdir(parents=True)
    print("Audio review evidence:", stage, flush=True)
    paths = {Path(__file__).resolve(), root / "tools/audio-engine/Create-ReviewSpeech.ps1",
             root / "tools/audio-engine/Write-AudioReviewPage.py", root / "tools/audio-engine/curated-candidate.json"}
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/ContextSuite.Worker", "src/Shared",
                      "proprietary/src/ContextSuite.Private", "tests/ContextSuite.Core.ContractTests", "tools/office-engine/Probe"):
        paths.update(path for path in (root / directory).rglob("*") if
                     path.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(path.parts))
    paths.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
                                         "Version.props", "global.json", "tools/Require-Private.targets") if (root / name).is_file())
    sources = {str(path.relative_to(root)): digest(path) for path in sorted(paths)}
    for label, project in (("worker", "src/ContextSuite.Worker/ContextSuite.Worker.csproj"),
                           ("contracts", "tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj")):
        run = subprocess.run(["dotnet", "build", str(root / project), "-c", "Release", "--no-restore", "--verbosity", "quiet"],
                             cwd=root, capture_output=True)
        (stage / (label + "-build.log")).write_bytes(run.stdout + run.stderr)
        if run.returncode:
            raise RuntimeError("Audio review build failed: " + label)
    worker = stage / "worker"
    worker.mkdir()
    for path in (root / "artifacts/managed/bin/ContextSuite.Worker/Release/net10.0-windows").iterdir():
        if path.is_file():
            shutil.copy2(path, worker / path.name)
    shutil.copytree(engine, worker / "audio-engine")
    original_engine = {str(path): digest(path) for path in engine.rglob("*") if path.is_file()}
    for name, expected in original_engine.items():
        if digest(worker / "audio-engine" / Path(name).relative_to(engine)) != expected:
            raise RuntimeError("Scratch audio engine copy differs.")
    speech = subprocess.run(["powershell", "-NoProfile", "-File", str(root / "tools/audio-engine/Create-ReviewSpeech.ps1"),
                             "-OutputPath", str(pack / "sources/speech.wav")], cwd=root, capture_output=True)
    (stage / "speech.log").write_bytes(speech.stdout + speech.stderr)
    if speech.returncode:
        raise RuntimeError("Local speech fixture generation failed; see speech.log.")
    host = root / "artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.exe"
    binaries = {str(path): digest(path) for path in host.parent.iterdir() if path.is_file()}
    binaries.update({str(path): digest(path) for path in worker.rglob("*") if path.is_file()})
    if any(digest(root / name) != expected for name, expected in sources.items()):
        raise RuntimeError("Source drift before audio review execution.")
    inputs = {"Sources": sources, "Binaries": binaries, "EngineSource": original_engine,
              "Speech": {str(path): digest(path) for path in (pack / "sources").iterdir() if path.is_file()}}
    (stage / "inputs.json").write_text(json.dumps(inputs, indent=2))
    with (stage / "stdout.log").open("wb") as output, (stage / "stderr.log").open("wb") as error:
        run = subprocess.run([str(host), "--audio-listening-pack", str(pack), str(worker / "ContextSuite.Worker.exe")],
                             cwd=root, stdout=output, stderr=error)
    unchanged = all(digest(root / name) == expected for name, expected in sources.items()) and all(
        digest(Path(name)) == expected for group in (binaries, original_engine, inputs["Speech"]) for name, expected in group.items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": run.returncode, "InputsUnchanged": unchanged}))
    print((stage / "stdout.log").read_text(errors="replace"), end="", flush=True)
    if run.returncode or not unchanged:
        raise RuntimeError("Audio review preparation failed; inspect retained evidence: " + str(stage))
    subprocess.run([sys.executable, "-B", str(root / "tools/audio-engine/Write-AudioReviewPage.py"), str(pack)],
                   cwd=root, check=True)
    print("Open the local review page:", pack / "review.html", flush=True)


if __name__ == "__main__":
    main()
