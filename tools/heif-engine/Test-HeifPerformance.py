"""Measure the declared HEIF batch budgets against an immutable combined candidate."""

import argparse
import hashlib
import json
import importlib.util
import shutil
from pathlib import Path
import subprocess
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", type=Path, required=True)
    parser.add_argument("--encoder-sdk", type=Path, required=True)
    parser.add_argument("--decoder-sdk", type=Path, required=True)
    parser.add_argument("--cmake", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    spec = importlib.util.spec_from_file_location("heif_payload", Path(__file__).with_name("Stage-HeifPayload.py"))
    deployment = importlib.util.module_from_spec(spec); spec.loader.exec_module(deployment)
    candidate = deployment.local(args.payload)
    deployment.verify(candidate)
    encoder, decoder = args.encoder_sdk.resolve(strict=True), args.decoder_sdk.resolve(strict=True)
    for folder in (encoder, decoder):
        folder.relative_to(root / ".codex-temp")
    cmake = args.cmake.resolve(strict=True)
    for name in ("ContextSuite.Worker.exe", "payload-inventory.json", "heif"):
        if not (candidate / name).exists():
            parser.error("The candidate must contain the worker, inventory and HEIF engine.")
    stage = root / ".codex-temp/heif-performance" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    print("HEIF performance evidence:", stage, flush=True)

    def run(label, command):
        result = subprocess.run(command, cwd=root, capture_output=True, timeout=600)
        (stage / (label + ".stdout.log")).write_bytes(result.stdout)
        (stage / (label + ".stderr.log")).write_bytes(result.stderr)
        (stage / (label + ".command.json")).write_text(json.dumps({"Command": [str(value) for value in command],
            "ExitCode": result.returncode}, indent=2))
        if result.returncode:
            raise RuntimeError("HEIF benchmark preparation/verification failed: " + label)
        return result.stdout

    inventory_command = ["python", "-B", str(root / "tools/audio-engine/Stage-AudioPayload.py"),
                         "--payload", str(candidate), "--inventory"]
    run("payload", ["powershell", "-NoProfile", "-File", str(root / "tools/curated-engine/Test-ProductionPayload.ps1"),
                    "-Payload", str(candidate), "-AllowAudioCandidate", "-AllowPdfCandidate", "-AllowOfficeCandidate", "-AllowHeifCandidate"])
    run("inventory-before", inventory_command)
    paths = {Path(__file__).resolve()}
    paths.update((root / "tools/heif-engine/Performance").iterdir())
    paths.update(path for path in (root / "tools/heif-engine").iterdir() if path.suffix in (".py", ".json"))
    fixture_inputs = {str(path): digest(path) for path in encoder.rglob("*") if path.is_file()}
    fixture_inputs.update({str(path): digest(path) for path in (decoder / "bin/libde265.dll", cmake)})
    (stage / "fixture-inputs.json").write_text(json.dumps(fixture_inputs, indent=2))
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/Shared",
                      "tests/ContextSuite.Core.ContractTests", "tools/office-engine/Probe"):
        paths.update(path for path in (root / directory).rglob("*") if
                     path.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(path.parts))
    paths.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
        "Version.props", "global.json", "tools/Require-Private.targets", "tools/curated-engine/Test-ProductionPayload.ps1",
        "tools/audio-engine/Stage-AudioPayload.py", "tools/office-engine/Stage-OfficePayload.py") if (root / name).is_file())
    sources = {str(path.relative_to(root)): digest(path) for path in sorted(paths)}
    run("fixture-configure", [str(cmake), "-S", str(root / "tools/heif-engine/Performance"), "-B", str(stage / "fixture-build"),
        "-G", "Visual Studio 18 2026", "-A", "x64", "-DCMAKE_PREFIX_PATH=" + str(encoder)])
    run("fixture-build", [str(cmake), "--build", str(stage / "fixture-build"), "--config", "Release", "--parallel", "4"])
    fixture_bin = stage / "fixture-build/Release"
    shutil.copy2(encoder / "bin/heif.dll", fixture_bin / "heif.dll")
    shutil.copy2(decoder / "bin/libde265.dll", fixture_bin / "libde265.dll")
    fixture_inputs.update({str(path): digest(path) for path in fixture_bin.iterdir() if path.is_file()})
    run("fixture-generate", [str(fixture_bin / "ContextSuite.Heif.Performance.exe"), str(stage / "fixtures")])
    fixture_inputs.update({str(path): digest(path) for path in (stage / "fixtures").iterdir()})
    (stage / "fixture-inputs.json").write_text(json.dumps(fixture_inputs, indent=2))
    run("contracts-build", ["dotnet", "build", str(root / "tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj"),
                            "-c", "Release", "--no-restore", "--verbosity", "quiet"])
    machine = run("machine", ["powershell", "-NoProfile", "-Command",
        "$cpu = Get-CimInstance Win32_Processor; $system = Get-CimInstance Win32_ComputerSystem; "
        "[ordered]@{Cpu = @($cpu.Name); PhysicalMemoryBytes = $system.TotalPhysicalMemory; "
        "LogicalProcessors = $system.NumberOfLogicalProcessors} | ConvertTo-Json"])
    (stage / "machine.json").write_text(json.dumps(json.loads(machine), indent=2))
    host = root / "artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows/ContextSuite.Core.ContractTests.exe"
    binaries = {str(path): digest(path) for path in host.parent.iterdir() if path.is_file()}
    binaries.update({str(path): digest(path) for path in candidate.rglob("*") if path.is_file()})
    inputs = {"Sources": sources, "Binaries": binaries, "ProductionStage": str(candidate),
              "ProductionInventorySha256": digest(candidate / "payload-inventory.json")}
    (stage / "inputs.json").write_text(json.dumps(inputs, indent=2))
    if any(digest(root / name) != expected for name, expected in sources.items()):
        raise RuntimeError("Source drift before benchmark execution.")
    print("Starting twenty fresh-worker HEIF direct conversions.", flush=True)
    with (stage / "stdout.log").open("wb") as output, (stage / "stderr.log").open("wb") as error:
        result = subprocess.run([str(host), "--heif-performance", str(stage / "runs"),
                                 str(candidate / "ContextSuite.Worker.exe"), str(stage / "fixtures")], cwd=root, stdout=output, stderr=error, timeout=900)
    unchanged = all(digest(root / name) == expected for name, expected in sources.items()) and all(
        digest(Path(name)) == expected for name, expected in (binaries | fixture_inputs).items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": result.returncode, "InputsUnchanged": unchanged}))
    print((stage / "stdout.log").read_text(errors="replace"), end="", flush=True)
    run("inventory-after", inventory_command)
    if result.returncode or not unchanged:
        raise RuntimeError("HEIF benchmark failed; retain the complete evidence: " + str(stage))
    if not json.loads((stage / "runs/results.json").read_text())["Passed"]:
        raise RuntimeError("HEIF performance result is not accepted.")
    print("HEIF performance results:", stage / "runs/results.json", flush=True)


if __name__ == "__main__":
    main()
