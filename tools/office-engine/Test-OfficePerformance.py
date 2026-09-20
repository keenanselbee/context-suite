"""Measure the declared Office conversion budgets against an immutable combined candidate."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--production-stage", type=Path, required=True)
    parser.add_argument("--create-disposable-profiles", action="store_true")
    args = parser.parse_args()
    if not args.create_disposable_profiles:
        parser.error("Explicit disposable Office profile authorization is required.")
    root = Path(__file__).resolve().parents[2]
    candidate = args.production_stage.resolve(strict=True)
    if candidate.parent != root / "artifacts/production-staging" or uuid.UUID(candidate.name).hex != candidate.name:
        parser.error("Use a versioned isolated production candidate directory.")
    for name in ("ContextSuite.Worker.exe", "payload-inventory.json", "office-engine", "pdf-engine", "pdf-renderer"):
        if not (candidate / name).exists():
            parser.error("The candidate must contain the worker, inventory and Office and PDF validation components.")
    stage = root / ".codex-temp/office-performance" / uuid.uuid4().hex
    stage.mkdir(parents=True)
    print("Office performance evidence:", stage, flush=True)

    def run(label, command):
        result = subprocess.run(command, cwd=root, capture_output=True, timeout=600)
        (stage / (label + ".stdout.log")).write_bytes(result.stdout)
        (stage / (label + ".stderr.log")).write_bytes(result.stderr)
        (stage / (label + ".command.json")).write_text(json.dumps({"Command": command, "ExitCode": result.returncode}, indent=2))
        if result.returncode:
            raise RuntimeError("Office benchmark preparation/verification failed: " + label)
        return result.stdout

    inventory_command = ["python", "-B", str(root / "tools/audio-engine/Stage-AudioPayload.py"),
                         "--payload", str(candidate), "--inventory"]
    run("payload", ["powershell", "-NoProfile", "-File", str(root / "tools/curated-engine/Test-ProductionPayload.ps1"),
                    "-Payload", str(candidate), "-AllowAudioCandidate", "-AllowPdfCandidate", "-AllowOfficeCandidate"])
    run("inventory-before", inventory_command)
    paths = {Path(__file__).resolve()}
    for directory in ("src/ContextSuite.Core", "src/ContextSuite.Application", "src/Shared",
                      "tests/ContextSuite.Core.ContractTests", "tools/office-engine/Probe"):
        paths.update(path for path in (root / directory).rglob("*") if
                     path.suffix in (".cs", ".csproj") and not {"bin", "obj"}.intersection(path.parts))
    paths.update(root / name for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
        "Version.props", "global.json", "tools/Require-Private.targets", "tools/curated-engine/Test-ProductionPayload.ps1",
        "tools/audio-engine/Stage-AudioPayload.py", "tools/office-engine/Stage-OfficePayload.py") if (root / name).is_file())
    sources = {str(path.relative_to(root)): digest(path) for path in sorted(paths)}
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
    print("Starting fifteen fresh-worker Office conversions with disposable profiles.", flush=True)
    with (stage / "stdout.log").open("wb") as output, (stage / "stderr.log").open("wb") as error:
        result = subprocess.run([str(host), "--office-performance", "--create-disposable-profiles", str(stage / "runs"),
                                 str(candidate / "ContextSuite.Worker.exe")], cwd=root, stdout=output, stderr=error, timeout=1800)
    unchanged = all(digest(root / name) == expected for name, expected in sources.items()) and all(
        digest(Path(name)) == expected for name, expected in binaries.items())
    (stage / "exit.json").write_text(json.dumps({"ExitCode": result.returncode, "InputsUnchanged": unchanged}))
    print((stage / "stdout.log").read_text(errors="replace"), end="", flush=True)
    run("inventory-after", inventory_command)
    if result.returncode or not unchanged:
        raise RuntimeError("Office benchmark failed; retain the complete evidence: " + str(stage))
    if not json.loads((stage / "runs/results.json").read_text())["Passed"]:
        raise RuntimeError("Office performance result is not accepted.")
    print("Office performance results:", stage / "runs/results.json", flush=True)


if __name__ == "__main__":
    main()
