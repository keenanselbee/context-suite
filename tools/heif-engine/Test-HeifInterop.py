"""Verify pinned upstream HEIF inputs through the actual worker and publication path."""
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
    parser.add_argument("--inputs", required=True, type=Path)
    parser.add_argument("--bridge", required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    source = args.inputs.resolve(strict=True)
    bridge = args.bridge.resolve(strict=True)
    for path in (source, bridge):
        path.relative_to(root / ".codex-temp")
    manifest = Path(__file__).with_name("interop-inputs.json")
    corpus = json.loads(manifest.read_text())
    inputs = {str(manifest): digest(manifest), str(Path(__file__).resolve()): digest(Path(__file__).resolve())}
    for entry in corpus["files"]:
        path = source / entry["path"]
        if path.stat().st_size != entry["bytes"] or digest(path) != entry["sha256"]:
            raise ValueError("Upstream input differs: " + entry["path"])
        inputs[str(path)] = entry["sha256"]
    candidate_manifest = Path(__file__).with_name("distribution-candidate.json")
    inputs[str(candidate_manifest)] = digest(candidate_manifest)
    candidates = json.loads(candidate_manifest.read_text())
    for name, pin in candidates["files"].items():
        path = bridge.parent / name
        if path.stat().st_size != pin["bytes"] or digest(path) != pin["sha256"]:
            raise ValueError("Decoder differs from selected candidate")
        inputs[str(path)] = pin["sha256"]
    for directory in ("src", "tests/ContextSuite.Core.ContractTests", "proprietary/src"):
        inputs.update({str(path): digest(path) for path in (root / directory).rglob("*")
                       if path.is_file() and path.suffix in (".cs", ".csproj", ".icc")})
    for name in ("Directory.Build.props", "Directory.Build.targets", "Version.props"):
        if (root / name).is_file():
            inputs[str(root / name)] = digest(root / name)
    evidence = root / ".codex-temp/heif-interop" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    (evidence / "inputs.json").write_text(json.dumps(inputs, indent=2))
    print("HEIF interoperability evidence:", evidence, flush=True)

    def run(name, command):
        command = [str(value) for value in command]
        result = subprocess.run(command, cwd=root, capture_output=True, timeout=240)
        (evidence / (name + ".stdout.log")).write_bytes(result.stdout)
        (evidence / (name + ".stderr.log")).write_bytes(result.stderr)
        (evidence / (name + ".command.json")).write_text(json.dumps({"command": command, "exit": result.returncode}, indent=2))
        if result.returncode:
            print(result.stdout.decode(errors="replace"), result.stderr.decode(errors="replace"), flush=True)
            raise RuntimeError("Failed " + name + "; inspect retained evidence")
        return result.stdout.decode()

    # Shared Core outputs require sequential managed builds.
    run("worker-build", ["dotnet", "build", root / "src/ContextSuite.Worker/ContextSuite.Worker.csproj", "-c", "Release", "--nologo"])
    run("contracts-build", ["dotnet", "build", root / "tests/ContextSuite.Core.ContractTests/ContextSuite.Core.ContractTests.csproj", "-c", "Release", "--nologo"])
    for project, name in (("ContextSuite.Worker", "worker"), ("ContextSuite.Core.ContractTests", "host")):
        shutil.copytree(root / "artifacts/managed/bin" / project / "Release/net10.0-windows", evidence / name)
    engine = evidence / "worker/heif"
    engine.mkdir()
    for name in candidates["files"]:
        shutil.copy2(bridge.parent / name, engine / name)
    payloads = {str(path): digest(path) for name in ("worker", "host") for path in (evidence / name).rglob("*") if path.is_file()}
    (evidence / "payloads.json").write_text(json.dumps(payloads, indent=2))
    print(run("contracts", [evidence / "host/ContextSuite.Core.ContractTests.exe", "--heif-interop",
        evidence / "worker/ContextSuite.Worker.exe", source, manifest, evidence / "results"]), flush=True)
    if any(digest(Path(path)) != expected for path, expected in (inputs | payloads).items()):
        raise ValueError("Recorded input, source or payload changed")
    (evidence / "verified.json").write_text(json.dumps({"Passed": True, "InputsAndPayloadsUnchanged": True,
        "ResultSha256": digest(evidence / "results/results.json")}, indent=2))


if __name__ == "__main__":
    main()
