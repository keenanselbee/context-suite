"""Opt-in typed Office worker exports using owned profiles and copied fixtures."""

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
    parser.add_argument("--create-disposable-profile", action="store_true")
    parser.add_argument("--copy-receipt", type=Path, required=True)
    parser.add_argument("--host-receipt", type=Path, required=True)
    parser.add_argument("--fixtures", type=Path, required=True)
    args = parser.parse_args()
    if not args.create_disposable_profile:
        parser.error("Explicit disposable profile authorization is required before preparation or execution.")
    root = Path(__file__).resolve().parents[2]
    scratch = root / ".codex-temp/office-worker" / uuid.uuid4().hex
    scratch.mkdir(parents=True)
    worker = scratch / "worker"
    package = worker / "office-engine"
    package.mkdir(parents=True)
    inventory = package / "runtime-files.txt"
    copied = json.loads(args.copy_receipt.read_text(encoding="utf-8"))
    host = json.loads(args.host_receipt.read_text(encoding="utf-8-sig"))
    inputs = [root / "src/ContextSuite.Application/Infrastructure/WorkerClient.cs",
              root / "src/ContextSuite.Application/Infrastructure/OfficeSandboxOwner.cs", root / "src/Shared/LocalPipe.cs",
              root / "src/ContextSuite.Core/Transport/Messages.cs", root / "src/ContextSuite.Worker/Program.cs",
              root / "proprietary/tests/ContextSuite.Pdf.ContractTests/OfficeWorkerContracts.cs",
              root / "proprietary/tests/ContextSuite.Pdf.ContractTests/Program.cs",
              root / "proprietary/tests/ContextSuite.Pdf.ContractTests/ContextSuite.Pdf.ContractTests.csproj",
              Path(__file__), root / "tools/office-engine/Write-OfficeRuntimeInventory.py"]
    inputs += list((root / "src/ContextSuite.Core/Office").glob("*.cs"))
    inputs += list((root / "proprietary/src/ContextSuite.Private/Office").glob("*.cs"))
    sources = {str(path.relative_to(root)): digest(path) for path in inputs}
    commands = [[sys.executable, str(root / "tools/office-engine/Write-OfficeRuntimeInventory.py"),
                 str(args.copy_receipt.resolve()), str(inventory)],
                ["dotnet", "build", str(root / "src/ContextSuite.Worker/ContextSuite.Worker.csproj"),
                 "-c", "Release", "--no-restore", "--verbosity", "quiet", "--output", str(worker)],
                ["dotnet", "build", str(root / "proprietary/tests/ContextSuite.Pdf.ContractTests/ContextSuite.Pdf.ContractTests.csproj"),
                 "-c", "Release", "--no-restore", "--verbosity", "quiet"]]
    for index, command in enumerate(commands):
        result = subprocess.run(command, cwd=root, capture_output=True)
        (scratch / f"prepare-{index}.log").write_bytes(result.stdout + result.stderr)
        if result.returncode:
            raise RuntimeError(f"Worker preparation failed before profile creation: {scratch}")
    runtime = package / "runtime"
    for relative in ["share/uno_packages/cache/uno_packages"]:
        (runtime / relative).mkdir(parents=True)
    # Copy only members of the pinned inventory; never run a source/reference executable.
    for index, line in enumerate(inventory.read_text(encoding="utf-8").splitlines(), 1):
        relative, size, expected = line.split("\t")
        source = Path(copied["destination"]) / relative
        target = runtime / relative
        if source.stat().st_size != int(size) or digest(source) != expected:
            raise RuntimeError(f"Retained runtime member differs from the inventory: {relative}")
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
        if index % 5000 == 0:
            print("Prepared pinned Office worker runtime files:", index, flush=True)
    executable = Path(host["executable"])
    if digest(executable) != host["sha256"]:
        raise RuntimeError("Host differs from its build receipt.")
    shutil.copyfile(executable, package / "ContextSuite.OfficeHost.exe")
    if any(digest(root / name) != value for name, value in sources.items()):
        raise RuntimeError("Sources changed during worker preparation; no profile was created.")
    managed = root / "artifacts/managed/bin/ContextSuite.Pdf.ContractTests/Release/net10.0/ContextSuite.Pdf.ContractTests.exe"
    receipt = {"source": sources, "host": host, "runtimeInventorySha256": digest(inventory),
               "copyReceiptSha256": digest(args.copy_receipt), "fixtures": str(args.fixtures.resolve()),
               "workerFiles": {path.name: digest(path) for path in worker.iterdir() if path.is_file()},
               "managedFiles": {path.name: digest(path) for path in managed.parent.iterdir() if path.is_file()}}
    (scratch / "build.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print("Office worker evidence:", scratch, flush=True)
    result = subprocess.run([str(managed), "--office-worker", str(worker / "ContextSuite.Worker.exe"),
                             str(args.fixtures.resolve()), str(scratch / "contracts")], cwd=root, capture_output=True)
    (scratch / "stdout.log").write_bytes(result.stdout)
    (scratch / "stderr.log").write_bytes(result.stderr)
    print((result.stdout + result.stderr).decode(errors="replace"), end="", flush=True)
    if result.returncode:
        raise RuntimeError(f"Office worker check failed; inspect profile cleanup before retrying: {scratch}")
    report = json.loads((scratch / "contracts/results.json").read_text(encoding="utf-8"))
    if not report["Passed"] or len(report["Reports"]) != 3:
        raise RuntimeError("Missing complete worker export evidence.")


if __name__ == "__main__":
    main()
