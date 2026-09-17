"""Build and exercise real HEIF worker/application orchestration in disposable scratch.

No production stage, installation, Explorer registration or live licensing is used.
"""
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
    parser.add_argument("--bridge", required=True, type=Path)
    parser.add_argument("--fixture-evidence", required=True, action="append", type=Path)
    parser.add_argument("--grid-evidence", required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    scratch = root / ".codex-temp"
    bridge = args.bridge.resolve(strict=True)
    fixtures = [path.resolve(strict=True) for path in args.fixture_evidence]
    grid = args.grid_evidence.resolve(strict=True)
    for path in [bridge, grid, *fixtures]:
        path.relative_to(scratch)
    inputs = {}
    for folder in [grid, *fixtures]:
        result = json.loads((folder / "results.json").read_text())
        if not result["Passed"]:
            raise ValueError("Passing fixture/bridge evidence required")
        recorded = json.loads((folder / "inputs.json").read_text())
        recorded.update(result.get("Fixtures", {}))
        if any(digest(Path(path)) != expected for path, expected in recorded.items()):
            raise ValueError("Fixture/bridge inputs changed: " + str(folder))
        inputs.update(recorded)
        inputs[str(folder / "results.json")] = digest(folder / "results.json")
    for directory in ("src", "tests/ContextSuite.Core.ContractTests", "proprietary/src"):
        inputs.update({str(path): digest(path) for path in (root / directory).rglob("*")
                       if path.is_file() and path.suffix in (".cs", ".csproj", ".icc")})
    for path in [Path(__file__).resolve(), root / "Directory.Build.props", root / "Directory.Build.targets", root / "Version.props"]:
        if path.is_file():
            inputs[str(path)] = digest(path)
    evidence = scratch / "heif-worker" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    (evidence / "inputs.json").write_text(json.dumps(inputs, indent=2) + "\n")
    print("HEIF worker evidence:", evidence, flush=True)
    (scratch / "heif-worker-current.json").write_text(json.dumps({"workspace": str(evidence)}, indent=2))

    def run(name, command):
        command = [str(value) for value in command]
        child = subprocess.run(command, cwd=root, capture_output=True, timeout=240)
        (evidence / (name + ".stdout.log")).write_bytes(child.stdout)
        (evidence / (name + ".stderr.log")).write_bytes(child.stderr)
        (evidence / (name + ".command.json")).write_text(json.dumps({"command": command, "exit": child.returncode}, indent=2))
        if child.returncode:
            print(child.stdout.decode(errors="replace"), child.stderr.decode(errors="replace"), flush=True)
            raise RuntimeError("Failed " + name + "; inspect retained evidence")
        return child.stdout

    # These builds share Core output, so keep them sequential.
    run("worker-build", ["dotnet", "build", root / "src/ContextSuite.Worker/ContextSuite.Worker.csproj", "-c", "Release", "--nologo"])
    run("contracts-build", ["dotnet", "build", root / "tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj", "-c", "Release", "--nologo"])
    worker = evidence / "worker"
    host = evidence / "host"
    shutil.copytree(root / "artifacts/managed/bin/ContextSuite.Worker/Release/net10.0-windows", worker)
    if (worker / "heif").exists():
        raise ValueError("Base worker unexpectedly contains a HEIF engine")
    absent = evidence / "without-engine"
    shutil.copytree(worker, absent)
    shutil.copytree(root / "artifacts/managed/bin/ContextSuite.Core.ContractTests/Release/net10.0-windows", host)
    (worker / "heif").mkdir()
    for name in ("ContextSuite.Heif.Native.dll", "heif.dll", "libde265.dll"):
        shutil.copy2(bridge.parent / name, worker / "heif" / name)
    damaged = evidence / "damaged-engine"
    shutil.copytree(worker, damaged)
    with (damaged / "heif/ContextSuite.Heif.Native.dll").open("ab") as changed:
        changed.write(b"authored identity mismatch")
    payloads = {str(path): digest(path) for folder in (worker, absent, damaged, host) for path in folder.rglob("*") if path.is_file()}
    (evidence / "payloads.json").write_text(json.dumps(payloads, indent=2) + "\n")
    print(run("contracts", [host / "ContextSuite.Core.ContractTests.exe", "--heif-worker",
        worker / "ContextSuite.Worker.exe", absent / "ContextSuite.Worker.exe", damaged / "ContextSuite.Worker.exe",
        grid, evidence / "results", *fixtures]).decode(), flush=True)
    if any(digest(Path(path)) != expected for path, expected in (inputs | payloads).items()):
        raise ValueError("Source, evidence or executable payload changed during verification")
    (evidence / "verified.json").write_text(json.dumps({"Passed": True, "InputsAndPayloadsUnchanged": True,
        "ResultSha256": digest(evidence / "results/results.json")}, indent=2) + "\n")


if __name__ == "__main__":
    main()
