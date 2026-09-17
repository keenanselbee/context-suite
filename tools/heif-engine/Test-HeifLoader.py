"""Exercise optional HEIF loader paths in separate disposable processes."""
import argparse
import hashlib
import json
import os
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
    parser.add_argument("--fixture", required=True, type=Path)
    parser.add_argument("--scenario", action="append", choices=("valid", "preloaded", "junction", "hardlink", "extra", "missing"))
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    bridge = args.bridge.resolve(strict=True)
    fixture = args.fixture.resolve(strict=True)
    for path in (bridge, fixture):
        path.relative_to(root / ".codex-temp")
    evidence = root / ".codex-temp/heif-loader" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    print("HEIF loader evidence:", evidence, flush=True)
    inputs = {str(path): digest(path) for path in (bridge, fixture, bridge.parent / "heif.dll", bridge.parent / "libde265.dll", Path(__file__).resolve())}
    for folder in ("proprietary/src/ContextSuite.Private", "proprietary/tests/ContextSuite.Image.ContractTests"):
        inputs.update({str(path): digest(path) for path in (root / folder).rglob("*") if path.is_file() and path.suffix in (".cs", ".csproj", ".icc")})
    (evidence / "inputs.json").write_text(json.dumps(inputs, indent=2))

    def run(name, command):
        command = [str(value) for value in command]
        child = subprocess.run(command, cwd=root, capture_output=True, timeout=120)
        (evidence / (name + ".stdout.log")).write_bytes(child.stdout)
        (evidence / (name + ".stderr.log")).write_bytes(child.stderr)
        (evidence / (name + ".command.json")).write_text(json.dumps({"command": command, "exit": child.returncode}, indent=2))
        if child.returncode:
            print(child.stdout.decode(errors="replace"), child.stderr.decode(errors="replace"), flush=True)
            raise RuntimeError("Failed " + name + "; retained evidence: " + str(evidence))
        return child.stdout

    run("build", ["dotnet", "build", root / "proprietary/tests/ContextSuite.Image.ContractTests/ContextSuite.Image.ContractTests.csproj", "-c", "Release", "--nologo"])
    junction_script = evidence / "junction.ps1"
    junction_script.write_text("param([string]$Link, [string]$Target)\n$ErrorActionPreference = 'Stop'\nNew-Item -ItemType Junction -Path $Link -Target $Target | Out-Null\n")
    results = []
    for scenario in args.scenario or ("valid", "preloaded", "junction", "hardlink", "extra", "missing"):
        host = evidence / scenario / "host"
        shutil.copytree(root / "artifacts/managed/bin/ContextSuite.Image.ContractTests/Release/net10.0", host)
        engine = host / "heif"
        if engine.exists():
            raise ValueError("Base host unexpectedly contains a decoder")
        engine.mkdir()
        for name in ("ContextSuite.Heif.Native.dll", "heif.dll", "libde265.dll"):
            if scenario == "missing" and name == "heif.dll":
                continue
            if scenario == "hardlink" and name == "libde265.dll":
                shared = host / "shared"; shared.mkdir()
                shutil.copy2(bridge.parent / name, shared / name)
                os.link(shared / name, engine / name)
            else:
                shutil.copy2(bridge.parent / name, engine / name)
        if scenario == "preloaded":
            foreign = host / "foreign"; foreign.mkdir()
            shutil.copy2(bridge.parent / "libde265.dll", foreign / "libde265.dll")
        if scenario == "extra":
            (engine / "unexpected.dll").write_bytes(b"authored extra library")
        if scenario == "junction":
            target = host / "redirected-engine"
            engine.rename(target)
            run("junction", ["powershell", "-NoProfile", "-NonInteractive", "-File", junction_script, "-Link", engine, "-Target", target])
        payload = {str(path): digest(path) for path in host.rglob("*") if path.is_file()}
        (evidence / (scenario + ".payload.json")).write_text(json.dumps(payload, indent=2))
        print(run(scenario, [host / "ContextSuite.Image.ContractTests.exe", evidence / scenario / "results", "--heif-loader", fixture, scenario]).decode(), flush=True)
        if any(digest(Path(path)) != expected for path, expected in payload.items()):
            raise ValueError("Loader payload changed during verification")
        results.append(json.loads((evidence / scenario / "results/results.json").read_text()))
    if any(digest(Path(path)) != expected for path, expected in inputs.items()):
        raise ValueError("Loader sources or inputs changed")
    (evidence / "results.json").write_text(json.dumps({"Passed": True, "Scenarios": results}, indent=2))


if __name__ == "__main__":
    main()
