"""Verify the pinned Office candidate and read leases without executing Office."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import uuid


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--copy-receipt", type=Path, required=True)
    parser.add_argument("--host-receipt", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    evidence = root / ".codex-temp/office-runtime" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    inventory = evidence / "runtime-files.txt"
    copied = json.loads(args.copy_receipt.read_text(encoding="utf-8"))
    host = json.loads(args.host_receipt.read_text(encoding="utf-8-sig"))
    project = root / "proprietary/tests/ContextSuite.Pdf.ContractTests/ContextSuite.Pdf.ContractTests.csproj"
    names = ["proprietary/src/ContextSuite.Private/Office/OfficeRuntimeLease.cs",
             "proprietary/src/ContextSuite.Private/IO/MediaFiles.cs",
             "proprietary/tests/ContextSuite.Pdf.ContractTests/OfficeRuntimeContracts.cs",
             "proprietary/tests/ContextSuite.Pdf.ContractTests/Program.cs",
             "proprietary/tests/ContextSuite.Pdf.ContractTests/ContextSuite.Pdf.ContractTests.csproj",
             "tools/office-engine/Write-OfficeRuntimeInventory.py", "tools/office-engine/Test-OfficeRuntime.py"]
    sources = {name: digest(root / name) for name in names}
    commands = [[sys.executable, str(root / "tools/office-engine/Write-OfficeRuntimeInventory.py"),
                 str(args.copy_receipt.resolve()), str(inventory)],
                ["dotnet", "build", str(project), "-c", "Release", "--no-restore", "--verbosity", "quiet"]]
    for index, command in enumerate(commands):
        result = subprocess.run(command, cwd=root, capture_output=True)
        (evidence / f"prepare-{index}.log").write_bytes(result.stdout + result.stderr)
        if result.returncode:
            raise RuntimeError(f"Runtime contract preparation failed: {evidence}")
    if any(digest(root / name) != value for name, value in sources.items()):
        raise RuntimeError("Runtime verification sources changed during the build.")
    managed = root / "artifacts/managed/bin/ContextSuite.Pdf.ContractTests/Release/net10.0/ContextSuite.Pdf.ContractTests.exe"
    receipt = {"source": sources, "host": host, "copyReceiptSha256": digest(args.copy_receipt),
               "inventorySha256": digest(inventory), "runtime": copied["destination"],
               "managedFiles": {path.name: digest(path) for path in managed.parent.iterdir() if path.is_file()}}
    (evidence / "build.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print("Office runtime verification evidence:", evidence, flush=True)
    result = subprocess.run([str(managed), "--office-runtime", host["executable"], copied["destination"],
                             str(inventory), str(evidence / "contracts")], cwd=root, capture_output=True)
    (evidence / "stdout.log").write_bytes(result.stdout)
    (evidence / "stderr.log").write_bytes(result.stderr)
    print((result.stdout + result.stderr).decode(errors="replace"), end="", flush=True)
    if result.returncode:
        raise RuntimeError(f"Office runtime verification failed: {evidence}")
    report = json.loads((evidence / "contracts/results.json").read_text(encoding="utf-8"))
    if not report["Passed"] or report["RuntimeFiles"] != 19332:
        raise RuntimeError("Missing complete runtime verification evidence.")


if __name__ == "__main__":
    main()
